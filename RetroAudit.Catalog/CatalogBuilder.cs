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
    // BuildInfo tablosuna yazılan sabitler. SchemaVersion, Games/GameVersions/Platforms tablo
    // yapısı değiştikçe (ör. bu turda Games.HiddenByDefault eklendi) artırılır; WPF tarafı
    // (Stage B) ileride uyumsuz bir RetroAudit.db'yi bu alana bakarak erkenden reddedebilir.
    public const string SchemaVersion = "1.4";
    public const string BuilderVersion = "1.4.0";

    // Ana listede varsayılan olarak gizlenecek (ama SİLİNMEYECEK) LaunchBox tür etiketleri —
    // kullanıcı kararı: gerçek video oyunu sayılmayan Casino/Gambling/Mahjong/Pachinko/Pachislot/
    // Quiz/masa oyunu/eğitim yazılımı türleri veri kaybı olmadan HiddenByDefault=1 ile işaretlenir.
    // Contains ile kontrol edildiği için "Board Game"/"Board Games", "Educational"/"Education"
    // gibi tekil/çoğul ya da sıfat/isim varyasyonlarının hepsini yakalar.
    private static readonly string[] HiddenGenreKeywords =
    {
        "Casino", "Gambling", "Mahjong", "Pachinko", "Pachislot", "Pachi-Slot",
        "Quiz", "Board Game", "Tabletop", "Card Game", "Educational", "Education",
    };

    private static bool IsHiddenByGenre(IEnumerable<string> genres) =>
        genres.Any(g => HiddenGenreKeywords.Any(k => g.Contains(k, StringComparison.OrdinalIgnoreCase)));

    public static BuildReport Run(BuildOptions options)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var report = new BuildReport();

        var scan = DatSourceScanner.Scan(options.DatRoot, options.SourceCategories, options.PlatformFilter);
        report.SkippedDatFiles.AddRange(scan.SkippedFiles);
        report.ExcludedPlatforms.AddRange(scan.ExcludedPlatforms);
        report.OutOfScopePlatforms.AddRange(scan.OutOfScopePlatforms);
        foreach (var resolution in scan.PlatformResolutions.Where(r => r.WasAmbiguous && !r.WasExplicitOverride))
            report.AmbiguousPlatformsDefaulted.Add((resolution.PlatformName, resolution.ChosenSource, resolution.AvailableSources));


        var games = VersionResolver.Group(scan.Entries);
        report.FilteredRecordCount = scan.Entries.Count - games.Sum(g => g.Versions.Count);
        report.UnknownRegionVersionCount = games.Sum(g => g.Versions.Count(v => v.Regions.Contains("Unknown", StringComparer.OrdinalIgnoreCase)));

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
                    game.ReleaseDate = match.ReleaseDate;
                    game.CommunityRating = match.CommunityRating;
                    game.VideoUrl = match.VideoUrl;
                    game.WikipediaUrl = match.WikipediaUrl;
                    game.SteamAppId = match.SteamAppId;
                    game.Cooperative = match.Cooperative;
                    game.AlternateNames.AddRange(match.AlternateNames);
                    game.MetadataSourceId = match.MetadataSourceId;
                    game.BoxImageFileName = match.BoxImageFileName;
                    game.BackgroundImageFileName = match.BackgroundImageFileName;
                    game.ScreenshotImageFileName = match.ScreenshotImageFileName;
                    game.ClearLogoImageFileName = match.ClearLogoImageFileName;
                    game.MatchedMetadata = true;
                    game.MatchMethod = match.MatchMethod;
                    game.MatchConfidence = match.Confidence;
                    game.NeedsReview = match.Confidence < LaunchBoxMetadataReader.FuzzyAcceptThreshold;
                    report.MetadataMatched++;

                    if (match.MatchMethod == "Fuzzy")
                        report.FuzzyMatched++;
                    if (game.NeedsReview)
                        report.NeedsReview++;

                    if (IsHiddenByGenre(game.Genres))
                    {
                        game.HiddenByDefault = true;
                        report.HiddenByDefaultCount++;
                    }
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

        WriteDatabase(options, games, report);

        report.PlatformCount = games.Select(g => g.PlatformName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        report.GameCount = games.Count;
        report.VersionCount = games.Sum(g => g.Versions.Count);
        report.HashCount = games.Sum(g => g.Versions.Sum(v => v.Roms.Count));

        foreach (var group in games.GroupBy(g => g.PlatformName, StringComparer.OrdinalIgnoreCase))
            report.GamesByPlatform[group.Key] = group.Count();

        // Kaynak, platformun kendisinden değil oyunun TERCİH EDİLEN sürümünden okunuyor —
        // PlatformMergeMap birden fazla dat dosyasını (ör. fiziksel + dijital) tek bir platform
        // altında topladığından, bir platformun "tek kaynağı" artık her zaman doğru olmayabilir.
        foreach (var game in games)
        {
            var source = game.Preferred?.SourceDat ?? "unknown";
            report.GamesBySource[source] = report.GamesBySource.GetValueOrDefault(source) + 1;
        }

        stopwatch.Stop();
        report.BuildDuration = stopwatch.Elapsed;

        return report;
    }

    private static void WriteDatabase(BuildOptions options, List<CatalogGame> games, BuildReport report)
    {
        var outputPath = options.OutputDbPath;
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

        using (var buildInfoCmd = connection.CreateCommand())
        {
            buildInfoCmd.CommandText = """
                INSERT INTO BuildInfo (SchemaVersion, CatalogVersion, BuildDate, BuilderVersion, SourceSummary)
                VALUES ($schemaVersion, $catalogVersion, $buildDate, $builderVersion, $sourceSummary)
                """;
            buildInfoCmd.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
            buildInfoCmd.Parameters.AddWithValue("$catalogVersion", DateTime.UtcNow.ToString("yyyy.MM.dd"));
            buildInfoCmd.Parameters.AddWithValue("$buildDate", DateTime.UtcNow.ToString("O"));
            buildInfoCmd.Parameters.AddWithValue("$builderVersion", BuilderVersion);
            buildInfoCmd.Parameters.AddWithValue("$sourceSummary", string.Join(",", options.SourceCategories));
            buildInfoCmd.ExecuteNonQuery();
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

        // Platforms tablosu Name dışında Category da tuttuğu için genel GetOrCreateLookup yerine
        // ayrı bir yol izliyor (bkz. PlatformCategoryMap — UI taksonomisi, Builder hiçbir platformu
        // bu yüzden atmaz, haritada olmayanlar "OTHERS" olarak yazılır).
        long GetOrCreatePlatformId(string platformName)
        {
            if (platformIds.TryGetValue(platformName, out var cached))
                return cached;

            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT rowid FROM Platforms WHERE Name = $name";
                select.Parameters.AddWithValue("$name", platformName);
                if (select.ExecuteScalar() is long existing)
                {
                    platformIds[platformName] = existing;
                    return existing;
                }
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Platforms (Name, Category) VALUES ($name, $category)";
            insert.Parameters.AddWithValue("$name", platformName);
            insert.Parameters.AddWithValue("$category", PlatformCategoryMap.Resolve(platformName));
            insert.ExecuteNonQuery();

            var newId = GetLastInsertRowId();
            platformIds[platformName] = newId;
            return newId;
        }

        foreach (var game in games)
        {
            var platformId = GetOrCreatePlatformId(game.PlatformName);
            var developerId = game.Developer is { Length: > 0 } dev ? GetOrCreateLookup(developerIds, "Developers", dev) : (long?)null;
            var publisherId = game.Publisher is { Length: > 0 } pub ? GetOrCreateLookup(publisherIds, "Publishers", pub) : (long?)null;

            long gameId;
            using (var insertGame = connection.CreateCommand())
            {
                insertGame.Transaction = transaction;
                insertGame.CommandText = """
                    INSERT INTO Games (PlatformId, Title, CompareTitle, DeveloperId, PublisherId, ReleaseYear, Overview, MaxPlayers, ReleaseDate, CommunityRating, VideoUrl, WikipediaUrl, SteamAppId, Cooperative, MatchedMetadata, MatchMethod, MatchConfidence, NeedsReview, HiddenByDefault, MetadataSourceId)
                    VALUES ($platformId, $title, $compareTitle, $developerId, $publisherId, $releaseYear, $overview, $maxPlayers, $releaseDate, $communityRating, $videoUrl, $wikipediaUrl, $steamAppId, $cooperative, $matchedMetadata, $matchMethod, $matchConfidence, $needsReview, $hiddenByDefault, $metadataSourceId)
                    """;
                insertGame.Parameters.AddWithValue("$platformId", platformId);
                insertGame.Parameters.AddWithValue("$title", game.Title);
                insertGame.Parameters.AddWithValue("$compareTitle", game.CompareTitle);
                insertGame.Parameters.AddWithValue("$developerId", (object?)developerId ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$publisherId", (object?)publisherId ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$overview", (object?)game.Overview ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$maxPlayers", (object?)game.MaxPlayers ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$releaseDate", (object?)game.ReleaseDate?.ToString("O") ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$communityRating", (object?)game.CommunityRating ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$videoUrl", (object?)game.VideoUrl ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$wikipediaUrl", (object?)game.WikipediaUrl ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$steamAppId", (object?)game.SteamAppId ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$cooperative", game.Cooperative is { } coop ? (coop ? 1 : 0) : DBNull.Value);
                insertGame.Parameters.AddWithValue("$matchedMetadata", game.MatchedMetadata ? 1 : 0);
                insertGame.Parameters.AddWithValue("$matchMethod", (object?)game.MatchMethod ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$matchConfidence", (object?)game.MatchConfidence ?? DBNull.Value);
                insertGame.Parameters.AddWithValue("$needsReview", game.NeedsReview ? 1 : 0);
                insertGame.Parameters.AddWithValue("$hiddenByDefault", game.HiddenByDefault ? 1 : 0);
                insertGame.Parameters.AddWithValue("$metadataSourceId", (object?)game.MetadataSourceId ?? DBNull.Value);
                insertGame.ExecuteNonQuery();
                gameId = GetLastInsertRowId();
            }

            foreach (var alternateName in game.AlternateNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var insertAlternateName = connection.CreateCommand();
                insertAlternateName.Transaction = transaction;
                insertAlternateName.CommandText = "INSERT INTO AlternateNames (GameId, Name, Source) VALUES ($gameId, $name, 'LaunchBox')";
                insertAlternateName.Parameters.AddWithValue("$gameId", gameId);
                insertAlternateName.Parameters.AddWithValue("$name", alternateName);
                insertAlternateName.ExecuteNonQuery();
            }

            var artworkByType = new (string Type, string? FileName)[]
            {
                ("Box", game.BoxImageFileName),
                ("Background", game.BackgroundImageFileName),
                ("Screenshot", game.ScreenshotImageFileName),
                ("ClearLogo", game.ClearLogoImageFileName),
            };
            foreach (var (type, fileName) in artworkByType)
            {
                if (string.IsNullOrEmpty(fileName))
                    continue;

                using var insertArtwork = connection.CreateCommand();
                insertArtwork.Transaction = transaction;
                insertArtwork.CommandText = "INSERT INTO ArtworkAssets (GameId, Type, FileName) VALUES ($gameId, $type, $fileName)";
                insertArtwork.Parameters.AddWithValue("$gameId", gameId);
                insertArtwork.Parameters.AddWithValue("$type", type);
                insertArtwork.Parameters.AddWithValue("$fileName", fileName);
                insertArtwork.ExecuteNonQuery();
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
