using Microsoft.Data.Sqlite;
using RetroAudit.Catalog.Dat;
using RetroAudit.Catalog.Grouping;
using RetroAudit.Catalog.Metadata;
using RetroAudit.Catalog.Schema;

namespace RetroAudit.Catalog;

// Tüm boru hattını (DAT tarama -> gruplama/tercih sürüm seçimi -> LaunchBox zenginleştirme ->
// RetroAudit.db yazımı) yöneten tek giriş noktası. RetroAudit.Builder konsol uygulaması sadece
// bu metodu çağırır; asıl mantık burada ve test edilebilir bir kütüphanede yaşıyor.
public static class CatalogBuilder
{
    public static BuildReport Run(BuildOptions options)
    {
        var report = new BuildReport();

        var scan = DatSourceScanner.Scan(options.DatRoot, options.SourceCategories, options.PlatformFilter);
        report.SkippedDatFiles.AddRange(scan.SkippedFiles);
        report.ExcludedPlatforms.AddRange(scan.ExcludedPlatforms);
        foreach (var resolution in scan.PlatformResolutions.Where(r => r.WasAmbiguous && !r.WasExplicitOverride))
            report.AmbiguousPlatformsDefaulted.Add((resolution.PlatformName, resolution.ChosenSource, resolution.AvailableSources));

        var games = VersionResolver.Group(scan.Entries);

        using (var metadataReader = new LaunchBoxMetadataReader(options.LaunchBoxDbPath))
        {
            foreach (var game in games)
            {
                if (game.Preferred is null)
                {
                    report.PreferredVersionMissing++;
                    continue;
                }

                // Platformun kendisi LaunchBox'ta hiç tanınamıyorsa (isim eşleşmesi yok), bunu her
                // oyun için ayrı ayrı "eşleşmedi" saymak yerine tek seferlik, ayrı bir uyarı olarak
                // raporluyoruz — "platform tamamen bilinmiyor" ile "bu tek oyun bulunamadı" çok
                // farklı ciddiyette durumlar.
                if (!metadataReader.IsPlatformKnown(game.PlatformName))
                {
                    if (!report.PlatformsNotInLaunchBox.Contains(game.PlatformName))
                        report.PlatformsNotInLaunchBox.Add(game.PlatformName);
                    report.MetadataUnmatched++;
                    continue;
                }

                var match = metadataReader.FindMatch(game.PlatformName, game.CompareTitle, game.Title);
                if (match is not null)
                {
                    game.Developer = match.Developer;
                    game.Publisher = match.Publisher;
                    game.ReleaseYear = match.ReleaseYear;
                    game.Overview = match.Overview;
                    game.MaxPlayers = match.MaxPlayers;
                    game.Genres.AddRange(match.Genres);
                    game.MatchedMetadata = true;
                    game.MatchMethod = match.MatchMethod;
                    game.MatchConfidence = match.Confidence;
                    game.NeedsReview = match.Confidence < LaunchBoxMetadataReader.FuzzyAcceptThreshold;
                    report.MetadataMatched++;

                    if (match.MatchMethod == "Fuzzy")
                        report.FuzzyMatched++;
                    if (game.NeedsReview)
                        report.NeedsReview++;
                }
                else
                {
                    // Platform biliniyor ama bu tek oyun LaunchBox'ta bulunamadı — No-Intro'nun
                    // homebrew/aftermarket/hack ağırlıklı yapısı düşünülürse beklenen bir durum,
                    // bu yüzden burada platform bazlı ayrı bir liste tutulmuyor; toplam sayı yeterli.
                    report.MetadataUnmatched++;
                }
            }
        }

        WriteDatabase(options.OutputDbPath, games, report);

        report.PlatformCount = games.Select(g => g.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        report.GameCount = games.Count;
        report.VersionCount = games.Sum(g => g.Versions.Count);
        report.HashCount = games.Sum(g => g.Versions.Sum(v => v.Roms.Count));

        return report;
    }

    private static void WriteDatabase(string outputPath, List<CatalogGame> games, BuildReport report)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(outputPath))
            File.Delete(outputPath); // her koşu temiz bir katalog üretir — kısmi/eski veri kalmaz

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = outputPath }.ConnectionString);
        connection.Open();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CatalogSchema.CreateTablesSql;
            schemaCmd.ExecuteNonQuery();
        }

        using (var seedSourceCmd = connection.CreateCommand())
        {
            seedSourceCmd.CommandText =
                "INSERT INTO MetadataSources (Name, Version, ImportedAt) VALUES ($name, $version, $importedAt)";
            seedSourceCmd.Parameters.AddWithValue("$name", "LaunchBox Metadata");
            seedSourceCmd.Parameters.AddWithValue("$version", DBNull.Value);
            seedSourceCmd.Parameters.AddWithValue("$importedAt", DateTime.UtcNow.ToString("O"));
            seedSourceCmd.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();

        var platformIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var developerIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var publisherIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var genreIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var regionIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Aynı CRC'nin farklı bir oyun altında tekrar görülmesini "olası çakışma" olarak işaretler.
        // Aynı sürümün başlıklı/başlıksız çifti gibi meşru durumlar farklı CRC'ye sahip olduğundan
        // bu, yalnızca gerçekten şüpheli tekrarları yakalar.
        var crcToGame = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Microsoft.Data.Sqlite, ADO.NET'in çoğu sağlayıcısının aksine SqliteConnection üzerinde
        // doğrudan bir LastInsertRowId özelliği sunmuyor; SQLite'ın kendi last_insert_rowid()
        // fonksiyonunu aynı bağlantı/transaction üzerinden sorgulamak gerekiyor.
        long GetLastInsertRowId()
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }

        long GetOrCreateLookup(Dictionary<string, long> cache, string tableName, string name)
        {
            if (cache.TryGetValue(name, out var cached))
                return cached;

            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = $"SELECT rowid FROM {tableName} WHERE Name = $name";
                select.Parameters.AddWithValue("$name", name);
                if (select.ExecuteScalar() is long existing)
                {
                    cache[name] = existing;
                    return existing;
                }
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {tableName} (Name) VALUES ($name)";
            insert.Parameters.AddWithValue("$name", name);
            insert.ExecuteNonQuery();

            var newId = GetLastInsertRowId();
            cache[name] = newId;
            return newId;
        }

        foreach (var game in games)
        {
            var platformId = GetOrCreateLookup(platformIds, "Platforms", game.PlatformName);
            var developerId = game.Developer is { Length: > 0 } dev ? GetOrCreateLookup(developerIds, "Developers", dev) : (long?)null;
            var publisherId = game.Publisher is { Length: > 0 } pub ? GetOrCreateLookup(publisherIds, "Publishers", pub) : (long?)null;

            long gameId;
            using (var insertGame = connection.CreateCommand())
            {
                insertGame.Transaction = transaction;
                insertGame.CommandText = """
                    INSERT INTO Games (PlatformId, Title, CompareTitle, DeveloperId, PublisherId, ReleaseYear, Overview, MaxPlayers, MatchedMetadata, MatchMethod, MatchConfidence, NeedsReview)
                    VALUES ($platformId, $title, $compareTitle, $developerId, $publisherId, $releaseYear, $overview, $maxPlayers, $matchedMetadata, $matchMethod, $matchConfidence, $needsReview)
                    """;
                insertGame.Parameters.AddWithValue("$platformId", platformId);
                insertGame.Parameters.AddWithValue("$title", game.Title);
                insertGame.Parameters.AddWithValue("$compareTitle", game.CompareTitle);
                insertGame.Parameters.AddWithValue("$developerId", (object?)developerId ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$publisherId", (object?)publisherId ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$overview", (object?)game.Overview ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$maxPlayers", (object?)game.MaxPlayers ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$matchedMetadata", game.MatchedMetadata ? 1 : 0);
                insertGame.Parameters.AddWithValue("$matchMethod", (object?)game.MatchMethod ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$matchConfidence", (object?)game.MatchConfidence ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$needsReview", game.NeedsReview ? 1 : 0);
                insertGame.ExecuteNonQuery();
                gameId = GetLastInsertRowId();
            }

            foreach (var genreName in game.Genres)
            {
                var genreId = GetOrCreateLookup(genreIds, "Genres", genreName);
                using var insertGenre = connection.CreateCommand();
                insertGenre.Transaction = transaction;
                insertGenre.CommandText = "INSERT OR IGNORE INTO GameGenres (GameId, GenreId) VALUES ($gameId, $genreId)";
                insertGenre.Parameters.AddWithValue("$gameId", gameId);
                insertGenre.Parameters.AddWithValue("$genreId", genreId);
                insertGenre.ExecuteNonQuery();
            }

            long? preferredVersionId = null;

            foreach (var version in game.Versions)
            {
                var primaryRegion = version.Regions.FirstOrDefault();
                var regionId = primaryRegion is { Length: > 0 } ? GetOrCreateLookup(regionIds, "Regions", primaryRegion) : (long?)null;

                using (var insertVersion = connection.CreateCommand())
                {
                    insertVersion.Transaction = transaction;
                    insertVersion.CommandText = """
                        INSERT INTO GameVersions (GameId, RegionId, AllRegionsRaw, VersionLabel, IsPreferred, RawDatName, SourceDat)
                        VALUES ($gameId, $regionId, $allRegionsRaw, $versionLabel, $isPreferred, $rawDatName, $sourceDat)
                        """;
                    insertVersion.Parameters.AddWithValue("$gameId", gameId);
                    insertVersion.Parameters.AddWithValue("$regionId", (object?)regionId ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$allRegionsRaw", version.Regions.Length > 0 ? string.Join(",", version.Regions) : DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$versionLabel", (object?)version.VersionLabel ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$isPreferred", version.IsPreferred ? 1 : 0);
                    insertVersion.Parameters.AddWithValue("$rawDatName", version.RawDatName);
                    insertVersion.Parameters.AddWithValue("$sourceDat", version.SourceDat);
                    insertVersion.ExecuteNonQuery();
                }

                var versionId = GetLastInsertRowId();
                if (version.IsPreferred)
                    preferredVersionId = versionId;

                foreach (var rom in version.Roms)
                {
                    using var insertHash = connection.CreateCommand();
                    insertHash.Transaction = transaction;
                    insertHash.CommandText = """
                        INSERT INTO GameHashes (GameVersionId, FileName, Size, Crc32, Md5, Sha1)
                        VALUES ($versionId, $fileName, $size, $crc32, $md5, $sha1)
                        """;
                    insertHash.Parameters.AddWithValue("$versionId", versionId);
                    insertHash.Parameters.AddWithValue("$fileName", rom.FileName);
                    insertHash.Parameters.AddWithValue("$size", rom.Size);
                    insertHash.Parameters.AddWithValue("$crc32", (object?)(rom.Crc32.Length > 0 ? rom.Crc32 : null) ?? DBNull.Value);
                    insertHash.Parameters.AddWithValue("$md5", (object?)rom.Md5 ?? DBNull.Value);
                    insertHash.Parameters.AddWithValue("$sha1", (object?)rom.Sha1 ?? DBNull.Value);
                    insertHash.ExecuteNonQuery();

                    if (rom.Crc32.Length > 0)
                    {
                        if (crcToGame.TryGetValue(rom.Crc32, out var firstGameId) && firstGameId != gameId)
                            report.DuplicateHashCollisions++;
                        else
                            crcToGame[rom.Crc32] = gameId;
                    }
                }
            }

            if (preferredVersionId is not null)
            {
                using var updateGame = connection.CreateCommand();
                updateGame.Transaction = transaction;
                updateGame.CommandText = "UPDATE Games SET PreferredVersionId = $preferredVersionId WHERE GameId = $gameId";
                updateGame.Parameters.AddWithValue("$preferredVersionId", preferredVersionId.Value);
                updateGame.Parameters.AddWithValue("$gameId", gameId);
                updateGame.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }
}
