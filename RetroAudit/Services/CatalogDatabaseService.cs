using System.IO;
using Microsoft.Data.Sqlite;
using RetroAudit.Catalog.Metadata;
using RetroAudit.Models;

namespace RetroAudit.Services;

// RetroAudit.Builder'ın ürettiği RetroAudit.db'yi okuyan tek servis (Stage B). Bu aşamada
// sadece okuma var (yazma/ROM içe aktarma Stage C'ye kalıyor).
public static class CatalogDatabaseService
{
    // Builder'ın varsayılan çıktı yolu (RetroAudit.Builder/Program.cs ile aynı) — taşınabilir veri
    // kökü altında (bkz. AppPaths.Metadata).
    public static readonly string DbPath = Path.Combine(AppPaths.Metadata, "RetroAudit.db");

    public static bool DatabaseExists => File.Exists(DbPath);

    // Images/Platforms/ altındaki logo paketi (kullanıcı tarafından eklendi, üçüncü taraf isimleri
    // elle DisplayName'e göre yeniden adlandırıldı) — bkz. Platform.LogoPath.
    private static readonly string PlatformLogoDirectory = Path.Combine(AppPaths.Images, "Platforms");

    private static string ResolveLogoPath(string displayName)
    {
        var path = Path.Combine(PlatformLogoDirectory, $"{displayName}.png");
        return File.Exists(path) ? path : string.Empty;
    }

    // MainViewModel.RegisterNewCustomGame'in (kataloktan gelmeyen, elle oluşturulan bir oyun için)
    // AYNI logo çözümlemesini kullanabilmesi için ResolveLogoPath'in genel erişilebilir hali.
    public static string GetPlatformLogoPath(string displayName) => ResolveLogoPath(displayName);

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
            {
                // Kullanıcı isteği: "Construction and Management Simulation gözüküyor hala" —
                // RetroAudit.db ÖNCEDEN üretilmiş bir dosya olduğu için (bkz. GenreDisplayNameMap
                // yorumu) katalog yeniden derlenene kadar eski (uzun) isim veritabanında kalır; bu
                // yüzden AYNI haritayı burada da (okuma anında) uyguluyoruz — Builder'ı yeniden
                // çalıştırınca zaten kaynağında kısa gelecek, bu satır o ana kadarki köprü.
                var mapped = string.Join(", ", reader.GetString(1).Split(", ").Select(GenreDisplayNameMap.Resolve));
                genresByGame[reader.GetInt32(0)] = mapped;
            }
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

        // Bir oyunun TÜM sürümlerinin bölgesi — sadece tercih edilenin değil. Toolbar'daki USA/EU/
        // Japan bayrak filtreleri (bkz. MainViewModel) bir oyunun görünür kalıp kalmayacağına ve
        // hangi sürümün "gösterilecek" bilgi olarak seçileceğine bunun üzerinden karar veriyor —
        // ör. tercih edilen sürüm USA olsa bile oyunun ayrıca bir Japan sürümü varsa (özellikle
        // MergeRegionVariants'ın birleştirdiği, farklı isimli bölge varyantlarında) bu bilgi
        // olmadan sadece USA'nın var olduğu bilinirdi.
        var allVersionsByGame = new Dictionary<int, List<GameVersionSummary>>();
        using (var versionsCmd = connection.CreateCommand())
        {
            versionsCmd.CommandText = """
                SELECT gv.GameId, r.Name, gv.SourceDat, gh.FileName, MIN(gh.GameHashId)
                FROM GameVersions gv
                JOIN GameHashes gh ON gh.GameVersionId = gv.GameVersionId
                LEFT JOIN Regions r ON r.RegionId = gv.RegionId
                GROUP BY gv.GameVersionId
                """;
            using var reader = versionsCmd.ExecuteReader();
            while (reader.Read())
            {
                var gid = reader.GetInt32(0);
                if (!allVersionsByGame.TryGetValue(gid, out var list))
                {
                    list = new List<GameVersionSummary>();
                    allVersionsByGame[gid] = list;
                }
                list.Add(new GameVersionSummary(
                    reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT g.GameId, g.Title, g.CompareTitle, p.Name, g.ReleaseYear, d.Name, pub.Name,
                   g.Overview, g.MaxPlayers, g.MatchedMetadata, g.HiddenByDefault,
                   g.MatchMethod, g.NeedsReview, g.ReleaseDate, g.CommunityRating, g.CommunityRatingCount,
                   g.VideoUrl, g.WikipediaUrl, g.SteamAppId, g.Cooperative, g.MetadataSourceId
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
            var cooperative = gameReader.IsDBNull(19) ? (bool?)null : gameReader.GetInt32(19) != 0;

            // LaunchBox'ta ReleaseYear ve ReleaseDate BİRBİRİNDEN BAĞIMSIZ nullable alanlar —
            // bir oyunda ReleaseDate dolu olup ReleaseYear NULL olabilir (ör. Friday the 13th,
            // NES). Bu yüzden "Yıl" sütunu/filtresi sadece ham ReleaseYear'a bakarsa yanlışlıkla
            // 0 gösterir; ReleaseDate varsa ondan çıkarılan yıl devreye giriyor.
            var releaseDate = gameReader.IsDBNull(13) ? (DateTime?)null : DateTime.Parse(gameReader.GetString(13));
            var releaseYear = gameReader.IsDBNull(4) ? (releaseDate?.Year ?? 0) : gameReader.GetInt32(4);

            var platformDisplayName = PlatformDisplayNameMap.Resolve(platformName);
            games.Add(new Game
            {
                GameId = gameId,
                GameKey = GameKeyHelper.Compute(platformName, compareTitle),
                Title = gameReader.GetString(1),
                Platform = platformName,
                PlatformDisplayName = platformDisplayName,
                PlatformLogoPath = ResolveLogoPath(platformDisplayName),
                ReleaseYear = releaseYear,
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
                ReleaseDate = releaseDate,
                CommunityRating = gameReader.IsDBNull(14) ? null : gameReader.GetDouble(14),
                CommunityRatingCount = gameReader.IsDBNull(15) ? null : gameReader.GetInt32(15),
                VideoUrl = gameReader.IsDBNull(16) ? string.Empty : gameReader.GetString(16),
                WikipediaUrl = gameReader.IsDBNull(17) ? string.Empty : gameReader.GetString(17),
                SteamAppId = gameReader.IsDBNull(18) ? null : gameReader.GetInt64(18),
                Cooperative = cooperative,
                MetadataSourceId = gameReader.IsDBNull(20) ? null : gameReader.GetInt32(20),
                // GameMode: LaunchBox'ın kesin bir "oyun modu" alanı yok, sadece Cooperative
                // (co-op) bilgisi var — bu yüzden burada tam bir mod adı değil, sadece bu tek
                // boyutu yansıtıyoruz. Bilinmiyorsa (null) uydurma bir varsayılan göstermiyoruz.
                GameMode = cooperative switch
                {
                    true => "Kooperatif",
                    false => "Kooperatif Değil",
                    null => string.Empty,
                },
                AllVersions = allVersionsByGame.GetValueOrDefault(gameId, new List<GameVersionSummary>()),
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
                if (ov.VideoUrl is { Length: > 0 }) game.VideoUrl = ov.VideoUrl;
                if (ov.ReleaseYear is { } releaseYear) game.ReleaseYear = releaseYear;
                if (ov.Region is { Length: > 0 }) game.Region = ov.Region;
            }
        }
    }

    // "Görsel Getir" komutu için: bir oyunun Builder'da önceden çözülmüş görsel varlık dosya
    // adları (bkz. ArtworkAssets, MasterMetadataReader.GetArtwork). Type -> FileName;
    // MainViewModel bu FileName'i bir indirme URL'sine çevirip diske kaydeder.
    // GameId -> o oyunun TÜM sürüm/dump varyantlarının (FileName, Crc32) çiftleri (sadece tercih
    // edilen sürümün değil — bkz. RomImportService.ScanFolder). Kullanıcı geri bildirimi 1: "İçe
    // Aktar, sadece USA/tercih edilen sürümün dosya adını tanıyor, Europe/Japan dump'ları
    // eşleşmiyor" — Games.File (bkz. GetGames) sadece TEK bir (tercih edilen) dosya adı taşıdığı
    // için, toplu eşleştirme öncesi TÜM GameHashes satırlarını tek seferde (67 binin üzerinde oyun
    // için ayrı ayrı sorgu atmadan) çekmek için bu bulk metot eklendi. Kullanıcı geri bildirimi 2:
    // dosya adı DAT revizyonları arasında değişebiliyor (ör. "Rev A" -> "Rev 1") ama İÇERİK
    // (CRC32) aynı kalıyor — Crc32 de aynı sorguda döndürülüyor ki RomImportService dosya adı
    // eşleşmezse İÇERİĞE göre de bir deneme yapabilsin (bkz. RomImportService.BuildCandidatesByFile).
    public static Dictionary<int, List<(string FileName, string Crc32)>> GetAllVersionRecordsByGame()
    {
        var result = new Dictionary<int, List<(string FileName, string Crc32)>>();
        if (!DatabaseExists)
            return result;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT gv.GameId, gh.FileName, gh.Crc32
            FROM GameVersions gv
            JOIN GameHashes gh ON gh.GameVersionId = gv.GameVersionId
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var gameId = reader.GetInt32(0);
            if (!result.TryGetValue(gameId, out var list))
            {
                list = new List<(string, string)>();
                result[gameId] = list;
            }
            list.Add((reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    // BEŞİNCİ eşleştirme katmanı (bkz. RomImportService.ResolveCandidate "Tier 5") için: LaunchBox'ın
    // AlternateNames'i (bölgesel/yeniden isimlendirilmiş sürümler — ör. "Kage" (Japan) = "Shadow of
    // the Ninja" (USA) — DAT bunları AYRI Game kayıtları olarak tutar, sadece LaunchBox bu bağlantıyı
    // bilir). Gerçek veriyle doğrulandı: doğru katalogda (proje kökü RetroAudit.db) 138 "başlık hiç
    // tutmuyor" dosyasından 81'i TEK bir Game'e, 70'i sürüm seviyesinde de kesin eşleşiyor.
    public static Dictionary<int, List<string>> GetAllAlternateNames()
    {
        var result = new Dictionary<int, List<string>>();
        if (!DatabaseExists)
            return result;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameId, Name FROM AlternateNames";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var gameId = reader.GetInt32(0);
            if (!result.TryGetValue(gameId, out var list))
            {
                list = new List<string>();
                result[gameId] = list;
            }
            list.Add(reader.GetString(1));
        }
        return result;
    }

    // Kullanıcı geri bildirimi: "eşleşmeyenlerden 447'si aslında normal oyun, sadece CRC/dosya adı
    // tutmuyor" (gerçek CSV verisiyle doğrulandı: %55'i başlık+bölge+revizyon eşleşiyor ama farklı
    // dump nesli) — RomImportService'in ÜÇÜNCÜ eşleştirme katmanı (bkz. RomImportService.
    // ResolveCandidate "Tier 3") için, her GameVersion'ın bölge(leri)/revizyon etiketi VE o
    // sürüme ait TÜM dosya adları tek seferde (67 bin oyun için ayrı sorgu atmadan) çekiliyor.
    // GetAllVersionRecordsByGame'den (Tier 2, CRC32) BİLEREK ayrı — o metodun dönüş şekli
    // (FileName, Crc32) çiftleri, bunun ihtiyacı (bölge/etiket) farklı, birleştirmek gereksiz
    // karmaşıklık katardı.
    public static Dictionary<int, List<CatalogVersionRecord>> GetAllVersionDetailsByGame()
    {
        var result = new Dictionary<int, List<CatalogVersionRecord>>();
        if (!DatabaseExists)
            return result;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT gv.GameId, gv.GameVersionId, gv.AllRegionsRaw, gv.VersionLabel, gv.RawDatName, gh.FileName
            FROM GameVersions gv
            JOIN GameHashes gh ON gh.GameVersionId = gv.GameVersionId
            """;
        using var reader = cmd.ExecuteReader();
        var recordsByVersionId = new Dictionary<int, CatalogVersionRecord>();
        while (reader.Read())
        {
            var gameId = reader.GetInt32(0);
            var versionId = reader.GetInt32(1);
            var allRegionsRaw = reader.IsDBNull(2) ? null : reader.GetString(2);
            var versionLabel = reader.IsDBNull(3) ? null : reader.GetString(3);
            var rawDatName = reader.GetString(4);
            var fileName = reader.GetString(5);

            if (!recordsByVersionId.TryGetValue(versionId, out var record))
            {
                record = new CatalogVersionRecord(allRegionsRaw, versionLabel, rawDatName, new List<string>());
                recordsByVersionId[versionId] = record;
                if (!result.TryGetValue(gameId, out var list))
                {
                    list = new List<CatalogVersionRecord>();
                    result[gameId] = list;
                }
                list.Add(record);
            }
            record.FileNames.Add(fileName);
        }
        return result;
    }

    public static Dictionary<string, string> GetArtworkAssets(int gameId)
    {
        var result = new Dictionary<string, string>();
        if (!DatabaseExists)
            return result;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Type, FileName FROM ArtworkAssets WHERE GameId = $gameId";
        cmd.Parameters.AddWithValue("$gameId", gameId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    // Edit Metadata'daki Region seçimi için (kullanıcı isteği: "manuel girilenlerde mevcut kayıtlı
    // regionlardan olsun, bi nevi hazır listeden") — kataloktaki TÜM bölgelerin tam listesi (bu
    // oyunun kendi sürümleriyle sınırlı değil, ör. "Brazil", "Korea", "Taiwan" gibi daha az yaygın
    // olanlar da dahil).
    public static List<string> GetAllRegionNames()
    {
        var names = new List<string>();
        if (!DatabaseExists)
            return names;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Regions ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
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

        if (string.IsNullOrWhiteSpace(gameKey))
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

    // Sağ paneldeki başlığa tıklanınca açılan menü için: LaunchBox'ın GameAlternateTitles
    // tablosundan Builder tarafından kopyalanan alternatif isimler (bkz. CatalogBuilder.cs
    // AlternateNames INSERT, Schema/CatalogSchema.cs AlternateNames tablosu). SelectedGame
    // değiştiğinde değil, sadece menü açıldığında talep üzerine sorgulanır — Versions listesiyle
    // aynı "talep üzerine" prensibi.
    public static List<GameAlternateName> GetAlternateNames(int gameId)
    {
        var names = new List<GameAlternateName>();
        if (!DatabaseExists)
            return names;

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, Region FROM AlternateNames WHERE GameId = $gameId ORDER BY Name";
        cmd.Parameters.AddWithValue("$gameId", gameId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(new GameAlternateName(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        return names;
    }
}
