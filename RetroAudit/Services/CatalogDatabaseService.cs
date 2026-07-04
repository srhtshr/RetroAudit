using System.IO;
using Microsoft.Data.Sqlite;
using RetroAudit.Models;

namespace RetroAudit.Services;

// RetroAudit.Builder'ın ürettiği RetroAudit.db'yi okuyan tek servis (Stage B). Bu aşamada
// sadece okuma var (yazma/ROM içe aktarma Stage C'ye kalıyor).
public static class CatalogDatabaseService
{
    // Builder'ın varsayılan çıktı yolu (RetroAudit.Builder/Program.cs ile aynı). İleride
    // AppSettings.RetroAuditDataPath uygulama açılışında otomatik yükleniyor olsaydı buradan
    // override edilebilirdi — o kablolama henüz yok (bkz. ConfigService yorumu), bu yüzden şimdilik
    // sabit varsayılan kullanılıyor.
    public static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroAudit", "RetroAudit.db");

    public static bool DatabaseExists => File.Exists(DbPath);

    // "PC Engine CD - TurboGrafx-CD" kullanıcı kararıyla programdan tamamen çıkarıldı (temel
    // "PC Engine - TurboGrafx-16" yeterli kabul edildi, ikisi kafa karıştırıyordu). RetroAudit.db
    // önceden üretilmiş bir dosya olduğu için (bkz. sınıf yorumu) Builder'ı yeniden çalıştırmadan
    // bu platformu veritabanından gerçekten silemeyiz — burada, okuma sırasında filtreleniyor.
    // Builder tarafındaki karşılığı: RetroAudit.Catalog/Dat/PlatformAllowList.cs.
    private const string RemovedPlatformName = "NEC - PC Engine CD - TurboGrafx-CD";

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString);
        connection.Open();
        return connection;
    }

    // Platform adından kısa bir rozet metni türetir (ör. "Nintendo - Nintendo Entertainment System"
    // -> "NES"). Gerçek DAT platform isimleri MockDataService'teki gibi elle küratörlü kısa isimler
    // taşımadığı için, üretici önekini (" - "'dan önceki kısım) atıp kalan kelimelerin baş
    // harflerini alıyoruz. Kozmetik bir yardımcı — tasarım/özellik değil, boş rozet yerine geçiyor.
    private static string DeriveGlyph(string platformName)
    {
        var dashIndex = platformName.IndexOf(" - ", StringComparison.Ordinal);
        var relevant = dashIndex >= 0 ? platformName[(dashIndex + 3)..] : platformName;
        var words = relevant.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Where(w => char.IsLetterOrDigit(w[0])).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length > 4 ? initials[..4] : initials;
    }

    public static List<Platform> GetPlatforms()
    {
        var platforms = new List<Platform> { new() { Name = "All Platforms", DisplayName = "All Platforms", IconGlyph = "ALL", IsAllPlatforms = true } };

        if (!DatabaseExists)
            return platforms;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, Category FROM Platforms ORDER BY Name COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (name == RemovedPlatformName)
                continue;

            platforms.Add(new Platform
            {
                Name = name,
                DisplayName = PlatformDisplayNameMap.Resolve(name),
                IconGlyph = DeriveGlyph(name),
                Category = reader.GetString(1),
            });
        }

        return platforms;
    }

    public static List<Game> GetGames()
    {
        var games = new List<Game>();
        if (!DatabaseExists)
            return games;

        using var connection = OpenConnection();

        // Bir oyunun birden fazla türü olabilir (GameGenres 1-e-çok) — DataGrid/detay panelinde
        // tek bir metin olarak göstermek için GameId -> "Tür1, Tür2" sözlüğü önceden kuruluyor.
        var genresByGame = new Dictionary<int, string>();
        using (var genreCmd = connection.CreateCommand())
        {
            genreCmd.CommandText = """
                SELECT gg.GameId, GROUP_CONCAT(gr.Name, ', ')
                FROM GameGenres gg JOIN Genres gr ON gr.GenreId = gg.GenreId
                GROUP BY gg.GameId
                """;
            using var reader = genreCmd.ExecuteReader();
            while (reader.Read())
                genresByGame[reader.GetInt32(0)] = reader.GetString(1);
        }

        // Tercih edilen sürümün Region/Kaynak'ı + ilk dosya adı — SQLite'ın "bare column"
        // davranışı sayesinde MIN(GameHashId) satırındaki FileName seçiliyor (headered/headerless
        // ikilisinden ilki). DataGrid'de tüm oyunlar için tek bakışta bölge/kaynak gösterebilmek
        // için GameVersion listesini (sağ panel, talep üzerine) beklemeden burada toplanıyor.
        var preferredByGame = new Dictionary<int, (string Region, string SourceDat, string FileName)>();
        using (var preferredCmd = connection.CreateCommand())
        {
            preferredCmd.CommandText = """
                SELECT gv.GameId, r.Name, gv.SourceDat, gh.FileName, MIN(gh.GameHashId)
                FROM GameVersions gv
                JOIN GameHashes gh ON gh.GameVersionId = gv.GameVersionId
                LEFT JOIN Regions r ON r.RegionId = gv.RegionId
                WHERE gv.IsPreferred = 1
                GROUP BY gv.GameId
                """;
            using var reader = preferredCmd.ExecuteReader();
            while (reader.Read())
            {
                preferredByGame[reader.GetInt32(0)] = (
                    reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3));
            }
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT g.GameId, g.Title, g.CompareTitle, p.Name, g.ReleaseYear, d.Name, pub.Name,
                   g.Overview, g.MaxPlayers, g.MatchedMetadata, g.HiddenByDefault,
                   g.MatchMethod, g.NeedsReview
            FROM Games g
            JOIN Platforms p ON p.PlatformId = g.PlatformId
            LEFT JOIN Developers d ON d.DeveloperId = g.DeveloperId
            LEFT JOIN Publishers pub ON pub.PublisherId = g.PublisherId
            WHERE p.Name <> $removedPlatform
            """;
        cmd.Parameters.AddWithValue("$removedPlatform", RemovedPlatformName);
        using var gameReader = cmd.ExecuteReader();
        while (gameReader.Read())
        {
            var gameId = gameReader.GetInt32(0);
            var platformName = gameReader.GetString(3);
            var compareTitle = gameReader.GetString(2);
            var hidden = !gameReader.IsDBNull(10) && gameReader.GetInt32(10) != 0;
            var preferred = preferredByGame.GetValueOrDefault(gameId, (Region: string.Empty, SourceDat: string.Empty, FileName: string.Empty));

            games.Add(new Game
            {
                GameId = gameId,
                GameKey = GameKeyHelper.Compute(platformName, compareTitle),
                Title = gameReader.GetString(1),
                Platform = platformName,
                PlatformDisplayName = PlatformDisplayNameMap.Resolve(platformName),
                ReleaseYear = gameReader.IsDBNull(4) ? 0 : gameReader.GetInt32(4),
                Developer = gameReader.IsDBNull(5) ? string.Empty : gameReader.GetString(5),
                Publisher = gameReader.IsDBNull(6) ? string.Empty : gameReader.GetString(6),
                Description = gameReader.IsDBNull(7) ? string.Empty : gameReader.GetString(7),
                MaxPlayers = gameReader.IsDBNull(8) ? 0 : gameReader.GetInt32(8),
                StatusOk = !gameReader.IsDBNull(9) && gameReader.GetInt32(9) != 0,
                Version = hidden ? "Junk" : "Released",
                Genres = genresByGame.GetValueOrDefault(gameId, string.Empty),
                File = preferred.FileName,
                Region = preferred.Region,
                SourceDat = preferred.SourceDat,
                MatchMethod = gameReader.IsDBNull(11) ? string.Empty : gameReader.GetString(11),
                NeedsReview = !gameReader.IsDBNull(12) && gameReader.GetInt32(12) != 0,
                // GameMode: RetroAudit.db şemasında henüz karşılığı yok (LaunchBox'ın
                // Cooperative/ReleaseType alanları Builder'a taşınmadı) — uydurma bir varsayılan
                // göstermemek için boş bırakılıyor.
                GameMode = string.Empty,
            });
        }

        ApplyUserData(games);
        return games;
    }

    // RetroAudit.db'den okunan katalog verisinin üzerine RetroAuditUserData.db'deki (Builder'ın
    // hiç bilmediği, ayrı) kullanıcı verisini bindirir: favori/gizli/çöp kutusu durumu ve elle
    // düzenlenmiş metadata alanları. Kalıcı silinmiş (IsPermanentlyDeleted) oyunlar listeden
    // tamamen çıkarılır — bkz. plan: "veritabanından tamamen silinsin" burada karşılığını buluyor,
    // çünkü RetroAudit.db'nin kendisinden gerçek bir satır silmek Builder'ın bir sonraki koşusunda
    // anlamsızlaşırdı (bkz. UserDataService üstündeki yorum).
    private static void ApplyUserData(List<Game> games)
    {
        var states = UserDataService.GetAllGameStates();
        var overrides = UserDataService.GetAllMetadataOverrides();
        var favorites = UserDataService.GetFavoriteGameKeys();

        games.RemoveAll(g => states.TryGetValue(g.GameKey, out var state) && state.IsPermanentlyDeleted);

        foreach (var game in games)
        {
            if (states.TryGetValue(game.GameKey, out var state))
            {
                game.IsHidden = state.IsHidden;
                game.IsDeleted = state.IsDeleted;
            }

            game.IsFavorite = favorites.Contains(game.GameKey);

            if (overrides.TryGetValue(game.GameKey, out var ov))
            {
                if (ov.Title is { Length: > 0 }) game.Title = ov.Title;
                if (ov.Genre is { Length: > 0 }) game.Genres = ov.Genre;
                if (ov.Description is { Length: > 0 }) game.Description = ov.Description;
                if (ov.Notes is { Length: > 0 }) game.Notes = ov.Notes;
                if (ov.Publisher is { Length: > 0 }) game.Publisher = ov.Publisher;
                if (ov.Developer is { Length: > 0 }) game.Developer = ov.Developer;
            }
        }
    }

    // Sağ paneldeki Versions listesi için: bir oyunun tüm GameVersions + her sürümün tüm
    // GameHashes kayıtları. SelectedGame değiştiğinde tek oyun için çağrılır (67 bin oyunun
    // tamamının sürüm/hash verisini baştan belleğe yüklemek yerine talep üzerine sorgu). gameKey,
    // kullanıcının "Preferred yap" ile seçtiği bir override varsa (RetroAuditUserData.db) onu
    // Builder'ın varsayılan IsPreferred seçiminin üzerine uygulamak için kullanılır.
    public static List<GameVersion> GetVersions(int gameId, string gameKey)
    {
        var versions = new List<GameVersion>();
        if (!DatabaseExists)
            return versions;

        using var connection = OpenConnection();

        var versionIds = new List<long>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT gv.GameVersionId, r.Name, gv.AllRegionsRaw, gv.VersionLabel, gv.IsPreferred, gv.SourceDat, gv.RawDatName
                FROM GameVersions gv
                LEFT JOIN Regions r ON r.RegionId = gv.RegionId
                WHERE gv.GameId = $gameId
                ORDER BY gv.IsPreferred DESC, gv.AllRegionsRaw
                """;
            cmd.Parameters.AddWithValue("$gameId", gameId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                versionIds.Add(reader.GetInt64(0));
                versions.Add(new GameVersion
                {
                    Region = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                    AllRegionsRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    VersionLabel = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    IsPreferred = reader.GetInt32(4) != 0,
                    SourceDat = reader.GetString(5),
                    RawDatName = reader.GetString(6),
                });
            }
        }

        if (versionIds.Count == 0)
            return versions;

        var hashesByVersion = new Dictionary<long, List<GameHash>>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT GameVersionId, FileName, Size, Crc32, Md5, Sha1
                FROM GameHashes
                WHERE GameVersionId IN ({string.Join(",", versionIds)})
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var versionId = reader.GetInt64(0);
                if (!hashesByVersion.TryGetValue(versionId, out var list))
                {
                    list = new List<GameHash>();
                    hashesByVersion[versionId] = list;
                }

                list.Add(new GameHash
                {
                    FileName = reader.GetString(1),
                    Size = reader.GetInt64(2),
                    Crc32 = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Md5 = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Sha1 = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                });
            }
        }

        for (var i = 0; i < versions.Count; i++)
            versions[i].Hashes = hashesByVersion.GetValueOrDefault(versionIds[i], new List<GameHash>());

        // Kullanıcı Versions panelinden "Preferred yap" dediyse (RetroAuditUserData.db), bu
        // Builder'ın varsayılan seçiminin üzerine geçer.
        var overrides = UserDataService.GetAllMetadataOverrides();
        if (overrides.TryGetValue(gameKey, out var ov) && ov.PreferredVersionRawName is { Length: > 0 } preferredRawName)
        {
            foreach (var version in versions)
                version.IsPreferred = version.RawDatName == preferredRawName;
        }

        return versions;
    }
}
