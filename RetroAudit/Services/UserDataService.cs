using System.IO;
using Microsoft.Data.Sqlite;
using RetroAudit.Models;

namespace RetroAudit.Services;

// Kullanıcının kendi ürettiği tüm veriyi (favori, gizli, çöp kutusu, playlist, düzenlenmiş
// metadata) tutan AYRI bir veritabanı. RetroAudit.db her Builder koşusunda tamamen silinip
// yeniden yazıldığı için (bkz. CatalogBuilder.WriteDatabase) kullanıcı verisi ORAYA asla
// yazılmaz — bu dosya RetroAudit.Catalog/Builder tarafından hiç bilinmez/dokunulmaz, sadece
// WPF uygulaması sahibidir. Oyunlar GameKeyHelper.Compute ile üretilen sabit anahtarla
// referans alınır (bkz. GameKeyHelper yorumu).
public static class UserDataService
{
    public static readonly string DbPath = Path.Combine(AppPaths.Metadata, "RetroAuditUserData.db");

    // Eski (taşınabilir düzenden ÖNCEki) konum — favori/gizli/playlist/override içeren bu dosya
    // küçük ve yeri doldurulamaz olduğu için (RetroAudit.db'nin aksine, o Builder ile yeniden
    // üretiliyor), tek seferlik bir kopya ile (taşıma değil) yeni konuma aktarılıyor.
    private static readonly string LegacyDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroAudit", "RetroAuditUserData.db");

    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS GameState (
            GameKey TEXT PRIMARY KEY,
            IsHidden INTEGER NOT NULL DEFAULT 0,
            IsDeleted INTEGER NOT NULL DEFAULT 0,
            DeletedAt TEXT,
            IsPermanentlyDeleted INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS MetadataOverrides (
            GameKey TEXT PRIMARY KEY,
            Title TEXT, Genre TEXT, Description TEXT, Notes TEXT, Publisher TEXT, Developer TEXT,
            VideoUrl TEXT, ReleaseYear INTEGER, Region TEXT, PreferredVersionRawName TEXT
        );

        CREATE TABLE IF NOT EXISTS FilePathOverrides (
            GameKey TEXT PRIMARY KEY,
            FilePath TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Playlists (
            PlaylistId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Color TEXT NOT NULL DEFAULT '#3A86FF',
            IsBuiltIn INTEGER NOT NULL DEFAULT 0,
            SortOrder INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS PlaylistGames (
            PlaylistId INTEGER NOT NULL REFERENCES Playlists(PlaylistId),
            GameKey TEXT NOT NULL,
            PRIMARY KEY (PlaylistId, GameKey)
        );

        CREATE TABLE IF NOT EXISTS CustomGames (
            GameKey TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            Platform TEXT NOT NULL,
            PlatformDisplayName TEXT NOT NULL
        );
        """;

    private static bool _initialized;

    private static SqliteConnection OpenConnection()
    {
        EnsureDatabase();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString);
        connection.Open();
        return connection;
    }

    // Şema + "Favorites" tohum satırı bir kere oluşturulur (uygulama ömrü boyunca). Builder'ın
    // "her koşu temiz" davranışının aksine bu dosya asla silinmez, sadece eksikse yaratılır.
    private static void EnsureDatabase()
    {
        if (_initialized)
            return;

        var directory = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Tek seferlik göç: yeni konumda henüz dosya yoksa ama eski (AppData) konumunda varsa,
        // kopyala (taşıma değil — eski dosyaya dokunmadan).
        if (!File.Exists(DbPath) && File.Exists(LegacyDbPath))
            File.Copy(LegacyDbPath, DbPath);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString);
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateTablesSql;
            cmd.ExecuteNonQuery();
        }

        EnsureColumnExists(connection, "MetadataOverrides", "VideoUrl", "TEXT");
        EnsureColumnExists(connection, "MetadataOverrides", "ReleaseYear", "INTEGER");
        EnsureColumnExists(connection, "MetadataOverrides", "Region", "TEXT");

        // Kullanıcı isteği: "manuel bağlanan kayıtlar ... Linked (Manual) olarak işaretlensin ...
        // hangi Game/GameVersion'a bağlandığı görülebilsin" — bkz. FilePathOverrideInfo.
        EnsureColumnExists(connection, "FilePathOverrides", "MatchMethod", "TEXT");
        EnsureColumnExists(connection, "FilePathOverrides", "TargetVersionRawName", "TEXT");

        // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan alıp
        // yazamıyormu buraya" — bkz. FilePathOverrideInfo yorumu.
        EnsureColumnExists(connection, "FilePathOverrides", "ZipEntryName", "TEXT");
        EnsureColumnExists(connection, "FilePathOverrides", "Crc32", "TEXT");

        // Kullanıcı geri bildirimi: "sormasın ayrı bir sürüm gibi sürümlerine eklesin" — gerçek
        // kullanımda yakalandı: GameKey PRIMARY KEY olduğu için bir oyuna İKİNCİ bir dosya (otomatik
        // ya da manuel) bağlanınca ÖNCEKİ (çalışan) bağlantı sessizce siliniyordu. Artık bir Game'in
        // BİRDEN FAZLA bağlı dosyası olabilir — biri "genel" (TargetVersionRawName NULL, BAŞLAT
        // butonunun kullandığı tek dosya) diğerleri belirli sürümlere özel (bkz.
        // MainViewModel.ResolveVersionFilePath, her GameVersion kendi bağlantısını bulur).
        EnsureFilePathOverridesAllowMultipleRows(connection);

        // Kullanıcı isteği: "rom unun olup olmaması fark etmemeli" — ROM'u olmayan bir oyun da
        // Bağla ile hedefin Sürümler listesine sahipsiz (kırmızı çarpı) bir kart olarak eklenebilsin
        // diye FilePath artık zorunlu değil (bkz. MainViewModel.LinkGameFileToGameAsync "dosyasız
        // birleştirme" dalı).
        EnsureFilePathOverridesAllowNullFilePath(connection);

        // Kullanıcı isteği: "merge'i kaldırınca tabloya geri gelmiyor" — dosyasız birleştirmede
        // (bkz. LinkGameFileToGameAsync) hangi oyunun gizlendiğini "Ayır" geri açabilsin diye.
        EnsureColumnExists(connection, "FilePathOverrides", "SourceGameKey", "TEXT");

        // Kullanıcı bulgusu: "Wanpaku Duck Yume Bouken" gerçekte resmi DuckTales iken LaunchBox'ın
        // kendi verisindeki BELİRSİZ bir alternatif isim eşleşmesi (aynı ad iki farklı LaunchBox
        // kaydında — biri gerçek oyun, biri bir ROM Hack) yüzünden yanlışlıkla Junk'a düşmüştü.
        // Kök neden Builder'ın eşleştirmesinde (kataloğu yeniden inşa etmeden düzeltilemez) — bunun
        // yerine kullanıcı istediği oyunu kapsüldeki tek-tuş toggle ile elle Junk<->Released
        // arasında geçirebilsin diye kalıcı bir kullanıcı override'ı: NULL (override yok, kataloğun
        // kendi sınıflandırması geçerli), 'Released' ya da 'Junk' (bkz. SetVersionOverride,
        // CatalogDatabaseService.ApplyUserData'daki uygulanışı).
        EnsureColumnExists(connection, "GameState", "VersionOverride", "TEXT");

        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM Playlists WHERE IsBuiltIn = 1 AND Name = 'Favorites'";
            var exists = System.Convert.ToInt64(checkCmd.ExecuteScalar()) > 0;
            if (!exists)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO Playlists (Name, Color, IsBuiltIn, SortOrder) VALUES ('Favorites', '#F5C518', 1, -1)";
                insertCmd.ExecuteNonQuery();
            }
        }

        _initialized = true;
    }

    // --- Toplu okuma (CatalogDatabaseService.GetGames() bunları 67 bin oyun için tek seferde
    // belleğe alıp overlay yapar; oyun başına ayrı sorgu atmaz). ---

    public static Dictionary<string, GameStateInfo> GetAllGameStates()
    {
        var result = new Dictionary<string, GameStateInfo>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, IsHidden, IsDeleted, IsPermanentlyDeleted, VersionOverride FROM GameState";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new GameStateInfo(
                reader.GetInt32(1) != 0,
                reader.GetInt32(2) != 0,
                reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        return result;
    }

    public static Dictionary<string, MetadataOverride> GetAllMetadataOverrides()
    {
        var result = new Dictionary<string, MetadataOverride>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, Title, Genre, Description, Notes, Publisher, Developer, VideoUrl, ReleaseYear, Region, PreferredVersionRawName FROM MetadataOverrides";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new MetadataOverride(
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));
        }
        return result;
    }

    // Kullanıcı geri bildirimi: "sormasın ayrı bir sürüm gibi sürümlerine eklesin" — bir oyunun
    // BİRDEN FAZLA bağlı dosyası olabilir (bkz. EnsureFilePathOverridesAllowMultipleRows), bu yüzden
    // artık GameKey -> TEK bir override değil, o oyuna bağlı TÜM override'ların listesi dönüyor.
    public static Dictionary<string, List<FilePathOverrideInfo>> GetAllFilePathOverrides()
    {
        var result = new Dictionary<string, List<FilePathOverrideInfo>>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, FilePath, MatchMethod, TargetVersionRawName, ZipEntryName, Crc32, SourceGameKey FROM FilePathOverrides";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var gameKey = reader.GetString(0);
            if (!result.TryGetValue(gameKey, out var list))
            {
                list = new List<FilePathOverrideInfo>();
                result[gameKey] = list;
            }
            list.Add(new FilePathOverrideInfo(
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result;
    }

    // matchMethod/targetVersionRawName — bkz. FilePathOverrideInfo. Otomatik ROM İçe Aktar akışı
    // (RomImportViewModel.ApplyImport, "Şu anki yoldan kullan") targetVersionRawName'i BİLEREK null
    // bırakır (Game seviyesi genel bağlantı); manuel bağlama (ManualLinkViewModel) ikisini de
    // doldurabilir. Kullanıcı geri bildirimi: bir oyuna İKİNCİ bir dosya bağlanınca ÖNCEKİ (farklı
    // sürüme ait) bağlantı ARTIK silinmiyor — sadece AYNI (GameKey, TargetVersionRawName) çiftine
    // sahip önceki kayıt (varsa) değiştiriliyor, böylece "genel" bağlantıdan biri, her sürümden de
    // birer tane olabiliyor.
    public static void SaveFilePathOverride(string gameKey, string? filePath, string? matchMethod = null, string? targetVersionRawName = null, string? zipEntryName = null, string? crc32 = null, string? sourceGameKey = null)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = targetVersionRawName is null
                ? "DELETE FROM FilePathOverrides WHERE GameKey = $gameKey AND TargetVersionRawName IS NULL"
                : "DELETE FROM FilePathOverrides WHERE GameKey = $gameKey AND TargetVersionRawName = $targetVersionRawName";
            deleteCmd.Parameters.AddWithValue("$gameKey", gameKey);
            if (targetVersionRawName is not null)
                deleteCmd.Parameters.AddWithValue("$targetVersionRawName", targetVersionRawName);
            deleteCmd.ExecuteNonQuery();
        }

        using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO FilePathOverrides (GameKey, FilePath, MatchMethod, TargetVersionRawName, ZipEntryName, Crc32, SourceGameKey)
                VALUES ($gameKey, $filePath, $matchMethod, $targetVersionRawName, $zipEntryName, $crc32, $sourceGameKey)
                """;
            insertCmd.Parameters.AddWithValue("$gameKey", gameKey);
            insertCmd.Parameters.AddWithValue("$filePath", (object?)filePath ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$matchMethod", (object?)matchMethod ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$targetVersionRawName", (object?)targetVersionRawName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$zipEntryName", (object?)zipEntryName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$crc32", (object?)crc32 ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$sourceGameKey", (object?)sourceGameKey ?? DBNull.Value);
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // Kullanıcı isteği: "unknown değilde manuel bağlamaya yönlendirebilirsin ... aynı yani" — bkz.
    // CustomGameInfo yorumu. RetroAudit.db'nin GetGames()'ine EK olarak MainViewModel._allGames'e
    // eklenir (bkz. MainViewModel.RegisterNewCustomGame), böylece Builder'ın "her koşu temiz"
    // davranışından (bu dosyanın aksine) etkilenmez.
    public static List<CustomGameInfo> GetAllCustomGames()
    {
        var result = new List<CustomGameInfo>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, Title, Platform, PlatformDisplayName FROM CustomGames";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new CustomGameInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return result;
    }

    // Kullanıcı geri bildirimi (ekran görüntüsü — aynı "Legend of Zelda, The" iki ayrı satırda):
    // "manuellerdede aynı isimdekileri tek bi yerde toplasın ayrı ayrı açmasın". Bundan SONRAKİ
    // bağlamalar için tekilleştirme MainViewModel.RegisterNewCustomGame'de yapılıyor — bu metot
    // SADECE o düzeltmeden ÖNCE yanlışlıkla oluşturulmuş, aynı başlık+platforma sahip AYRI
    // CustomGames satırlarını BİR KEZ birleştirir: hepsinin FilePathOverrides'ı TEK bir hayatta
    // kalan GameKey'e taşınır, fazlalık CustomGames satırları silinir. MainViewModel açılışta,
    // GetAllCustomGames'ten ÖNCE çağırır.
    public static void MergeDuplicateCustomGames()
    {
        var duplicateGroups = GetAllCustomGames()
            .GroupBy(c => $"{c.Title}|{c.PlatformDisplayName}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var group in duplicateGroups)
        {
            var ordered = group.ToList();
            var survivorKey = ordered[0].GameKey;
            foreach (var duplicate in ordered.Skip(1))
            {
                using (var updateCmd = connection.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = "UPDATE FilePathOverrides SET GameKey = $survivorKey WHERE GameKey = $duplicateKey";
                    updateCmd.Parameters.AddWithValue("$survivorKey", survivorKey);
                    updateCmd.Parameters.AddWithValue("$duplicateKey", duplicate.GameKey);
                    updateCmd.ExecuteNonQuery();
                }
                using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM CustomGames WHERE GameKey = $duplicateKey";
                    deleteCmd.Parameters.AddWithValue("$duplicateKey", duplicate.GameKey);
                    deleteCmd.ExecuteNonQuery();
                }
            }
        }

        // Kullanıcı geri bildirimi (ekran görüntüsü — açılır listede AYNI "Famicom Wars ...
        // Rev 0B" kartı iki kez): yukarıdaki GameKey birleştirmesi, iki "kopya" oyunun
        // YANLIŞLIKLA aynı dosyayı ayrı ayrı taşıdığı durumda (GameKey, TargetVersionRawName)
        // çiftini çakıştırabiliyor — bu da LoadSelectedGameVersions'ın sentetik kart üretiminde
        // AYNI kartın iki kez görünmesine yol açıyordu. Her (GameKey, TargetVersionRawName)
        // çifti için sadece EN bilgili satır (Crc32 dolu olan, yoksa en eski OverrideId) kalır.
        // Sadece yukarıdaki birleştirmenin bir yan etkisi değil, genel bir güvenlik ağı olarak da
        // her açılışta çalışır (ucuz — FilePathOverrides satır sayısı küçük).
        DeduplicateFilePathOverrides(connection, transaction);

        transaction.Commit();
    }

    // Kullanıcı geri bildirimi (ekran görüntüsü — gerçek katalog "Pinball" ile manuel "Pinball"
    // ayrı satırlarda): "bu 2 pinball'ı birleştirirsem kartlar tek bir yerde toplanacak dimi" —
    // MainViewModel.FoldCustomGamesIntoMatchingCatalogGames'in bir custom oyunu, aynı başlık+
    // platforma sahip GERÇEK bir katalog oyununa taşırken kullandığı iki adım. GameKey/GameKey ile
    // AYNI (GameKey, TargetVersionRawName) çakışması burada da olabileceğinden çağıran ardından
    // DeduplicateFilePathOverrides'ı da çağırmalı.
    public static void ReassignFilePathOverrides(string fromGameKey, string toGameKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE FilePathOverrides SET GameKey = $toGameKey WHERE GameKey = $fromGameKey";
        cmd.Parameters.AddWithValue("$toGameKey", toGameKey);
        cmd.Parameters.AddWithValue("$fromGameKey", fromGameKey);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteCustomGame(string gameKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM CustomGames WHERE GameKey = $gameKey";
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.ExecuteNonQuery();
    }

    // MainViewModel.LinkGameFileToGameAsync'in "tablodan bağla" akışı için — bir dosyayı bir
    // oyundan (kaynak) çıkarıp başka bir oyuna (hedef, bkz. SaveFilePathOverride) taşırken kaynağın
    // KENDİ override'ını siler.
    public static void RemoveFilePathOverride(string gameKey, string filePath)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM FilePathOverrides WHERE GameKey = $gameKey AND FilePath = $filePath";
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$filePath", filePath);
        cmd.ExecuteNonQuery();
    }

    // "Ayır" butonu (bkz. MainViewModel.RemoveVersionLink, MainWindow.xaml Sürümler kartı) — bir
    // sürüm kartının ARKASINDAKİ bağlantıyı kaldırır. FilePath'e göre değil TargetVersionRawName'e
    // göre siliniyor çünkü dosyasız (ROM'suz) birleştirmelerde (bkz. LinkGameFileToGameAsync)
    // FilePath NULL olabilir — TargetVersionRawName her sentetik/manuel-işaretli kartta MUTLAKA
    // dolu (kartın kimliği zaten bu).
    public static void RemoveFilePathOverrideByTargetVersion(string gameKey, string targetVersionRawName)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM FilePathOverrides WHERE GameKey = $gameKey AND TargetVersionRawName = $targetVersionRawName";
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$targetVersionRawName", targetVersionRawName);
        cmd.ExecuteNonQuery();
    }

    public static int CountFilePathOverrides(string gameKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM FilePathOverrides WHERE GameKey = $gameKey";
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void DeduplicateFilePathOverrides()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        DeduplicateFilePathOverrides(connection, transaction);
        transaction.Commit();
    }

    private static void DeduplicateFilePathOverrides(SqliteConnection connection, SqliteTransaction transaction)
    {
        var rows = new List<(long OverrideId, string GameKey, string? TargetVersionRawName, bool HasCrc32)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT OverrideId, GameKey, TargetVersionRawName, Crc32 FROM FilePathOverrides";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    !reader.IsDBNull(3) && !string.IsNullOrEmpty(reader.GetString(3))));
            }
        }

        var duplicateOverrideIds = rows
            .GroupBy(r => (r.GameKey, TargetVersionRawName: r.TargetVersionRawName?.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderByDescending(r => r.HasCrc32).ThenBy(r => r.OverrideId).Skip(1))
            .Select(r => r.OverrideId);

        foreach (var overrideId in duplicateOverrideIds)
        {
            using var deleteCmd = connection.CreateCommand();
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM FilePathOverrides WHERE OverrideId = $overrideId";
            deleteCmd.Parameters.AddWithValue("$overrideId", overrideId);
            deleteCmd.ExecuteNonQuery();
        }
    }

    public static void AddCustomGame(CustomGameInfo info)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CustomGames (GameKey, Title, Platform, PlatformDisplayName)
            VALUES ($gameKey, $title, $platform, $platformDisplayName)
            """;
        cmd.Parameters.AddWithValue("$gameKey", info.GameKey);
        cmd.Parameters.AddWithValue("$title", info.Title);
        cmd.Parameters.AddWithValue("$platform", info.Platform);
        cmd.Parameters.AddWithValue("$platformDisplayName", info.PlatformDisplayName);
        cmd.ExecuteNonQuery();
    }

    public static HashSet<string> GetFavoriteGameKeys()
    {
        var favoritesId = GetFavoritesPlaylistId();
        return GetPlaylistGameKeys(favoritesId);
    }

    // --- Playlists ---

    public static List<PlaylistRecord> GetPlaylists()
    {
        var result = new List<PlaylistRecord>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT PlaylistId, Name, Color, IsBuiltIn, SortOrder FROM Playlists ORDER BY SortOrder, Name COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PlaylistRecord(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3) != 0, reader.GetInt32(4)));
        }
        return result;
    }

    private static int GetFavoritesPlaylistId()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT PlaylistId FROM Playlists WHERE IsBuiltIn = 1 AND Name = 'Favorites'";
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static int CreatePlaylist(string name, string color = "#3A86FF")
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Playlists (Name, Color, IsBuiltIn, SortOrder) VALUES ($name, $color, 0, 0); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$color", color);
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void RenamePlaylist(int playlistId, string newName)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Playlists SET Name = $name WHERE PlaylistId = $id AND IsBuiltIn = 0";
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.Parameters.AddWithValue("$id", playlistId);
        cmd.ExecuteNonQuery();
    }

    public static void SetPlaylistColor(int playlistId, string color)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Playlists SET Color = $color WHERE PlaylistId = $id";
        cmd.Parameters.AddWithValue("$color", color);
        cmd.Parameters.AddWithValue("$id", playlistId);
        cmd.ExecuteNonQuery();
    }

    public static void DeletePlaylist(int playlistId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var deleteMembers = connection.CreateCommand())
        {
            deleteMembers.Transaction = transaction;
            deleteMembers.CommandText = "DELETE FROM PlaylistGames WHERE PlaylistId = $id";
            deleteMembers.Parameters.AddWithValue("$id", playlistId);
            deleteMembers.ExecuteNonQuery();
        }
        using (var deletePlaylist = connection.CreateCommand())
        {
            deletePlaylist.Transaction = transaction;
            deletePlaylist.CommandText = "DELETE FROM Playlists WHERE PlaylistId = $id AND IsBuiltIn = 0";
            deletePlaylist.Parameters.AddWithValue("$id", playlistId);
            deletePlaylist.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static HashSet<string> GetPlaylistGameKeys(int playlistId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey FROM PlaylistGames WHERE PlaylistId = $id";
        cmd.Parameters.AddWithValue("$id", playlistId);
        using var reader = cmd.ExecuteReader();
        var result = new HashSet<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    public static void AddToPlaylist(int playlistId, string gameKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO PlaylistGames (PlaylistId, GameKey) VALUES ($playlistId, $gameKey)";
        cmd.Parameters.AddWithValue("$playlistId", playlistId);
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.ExecuteNonQuery();
    }

    public static void RemoveFromPlaylist(int playlistId, string gameKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaylistGames WHERE PlaylistId = $playlistId AND GameKey = $gameKey";
        cmd.Parameters.AddWithValue("$playlistId", playlistId);
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.ExecuteNonQuery();
    }

    // Favorites, IsBuiltIn=1 olan sıradan bir playlist olduğu için favori işaretleme aslında
    // sadece o playlist'e ekleme/çıkarmadır — ayrı bir sütun/tablo yok.
    public static bool ToggleFavorite(string gameKey)
    {
        var favoritesId = GetFavoritesPlaylistId();
        var current = GetPlaylistGameKeys(favoritesId).Contains(gameKey);
        if (current)
            RemoveFromPlaylist(favoritesId, gameKey);
        else
            AddToPlaylist(favoritesId, gameKey);
        return !current;
    }

    // Toplu "Favoriye Ekle" eylemi için — ToggleFavorite'in aksine karışık seçimlerde (bazısı
    // zaten favori) tutarsız sonuç vermez, her zaman ekler.
    public static bool AddToFavorites(string gameKey)
    {
        AddToPlaylist(GetFavoritesPlaylistId(), gameKey);
        return true;
    }

    // --- GameState (Hide / Recycle Bin) ---

    private static void UpsertGameState(string gameKey, Action<SqliteCommand> configure)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO GameState (GameKey) VALUES ($gameKey)";
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.ExecuteNonQuery();

        using var updateCmd = connection.CreateCommand();
        configure(updateCmd);
        updateCmd.Parameters.AddWithValue("$gameKey", gameKey);
        updateCmd.ExecuteNonQuery();
    }

    public static void SetHidden(string gameKey, bool hidden) =>
        UpsertGameState(gameKey, cmd =>
        {
            cmd.CommandText = "UPDATE GameState SET IsHidden = $hidden WHERE GameKey = $gameKey";
            cmd.Parameters.AddWithValue("$hidden", hidden ? 1 : 0);
        });

    // Kullanıcı isteği: "istenilen oyunu junk'a atabilelim veya junktaki bi oyunu release'e
    // çekebilelim ... tek tuş toggle" — bkz. GameStateInfo.VersionOverride yorumu. version:
    // "Released" veya "Junk" (null geçilirse override kaldırılır, kataloğun kendi sınıflandırması
    // geçerli olur — şu an kullanılmıyor ama API olarak destekleniyor).
    public static void SetVersionOverride(string gameKey, string? version) =>
        UpsertGameState(gameKey, cmd =>
        {
            cmd.CommandText = "UPDATE GameState SET VersionOverride = $version WHERE GameKey = $gameKey";
            cmd.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        });

    public static void SoftDelete(string gameKey) =>
        UpsertGameState(gameKey, cmd =>
        {
            cmd.CommandText = "UPDATE GameState SET IsDeleted = 1, DeletedAt = $deletedAt WHERE GameKey = $gameKey";
            cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("O"));
        });

    public static void RestoreFromRecycleBin(string gameKey) =>
        UpsertGameState(gameKey, cmd =>
        {
            cmd.CommandText = "UPDATE GameState SET IsDeleted = 0, DeletedAt = NULL WHERE GameKey = $gameKey";
        });

    public static void PermanentlyDelete(string gameKey) =>
        UpsertGameState(gameKey, cmd =>
        {
            cmd.CommandText = "UPDATE GameState SET IsPermanentlyDeleted = 1 WHERE GameKey = $gameKey";
        });

    // --- Metadata overrides ---

    public static void SaveMetadataOverride(string gameKey, MetadataOverride overrideValue)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO MetadataOverrides (GameKey, Title, Genre, Description, Notes, Publisher, Developer, VideoUrl, ReleaseYear, Region, PreferredVersionRawName)
            VALUES ($gameKey, $title, $genre, $description, $notes, $publisher, $developer, $videoUrl, $releaseYear, $region, $preferredVersionRawName)
            ON CONFLICT(GameKey) DO UPDATE SET
                Title = $title, Genre = $genre, Description = $description, Notes = $notes,
                Publisher = $publisher, Developer = $developer, VideoUrl = $videoUrl,
                ReleaseYear = $releaseYear, Region = $region,
                PreferredVersionRawName = COALESCE($preferredVersionRawName, PreferredVersionRawName)
            """;
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$title", (object?)overrideValue.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$genre", (object?)overrideValue.Genre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)overrideValue.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)overrideValue.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$publisher", (object?)overrideValue.Publisher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$developer", (object?)overrideValue.Developer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$videoUrl", (object?)overrideValue.VideoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$releaseYear", (object?)overrideValue.ReleaseYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$region", (object?)overrideValue.Region ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$preferredVersionRawName", (object?)overrideValue.PreferredVersionRawName ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static void SavePreferredVersionOverride(string gameKey, string rawDatName)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO MetadataOverrides (GameKey, PreferredVersionRawName)
            VALUES ($gameKey, $rawDatName)
            ON CONFLICT(GameKey) DO UPDATE SET PreferredVersionRawName = $rawDatName
            """;
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$rawDatName", rawDatName);
        cmd.ExecuteNonQuery();
    }

    // VideoUrl alanını sadece güncellemek için (Edit Metadata penceresini açmadan)
    public static void SaveVideoUrlOverride(string gameKey, string videoUrl)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO MetadataOverrides (GameKey, VideoUrl)
            VALUES ($gameKey, $videoUrl)
            ON CONFLICT(GameKey) DO UPDATE SET VideoUrl = $videoUrl
            """;
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$videoUrl", videoUrl);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = checkCmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
        alterCmd.ExecuteNonQuery();
    }

    // Eski şemada GameKey PRIMARY KEY'di (oyun başına TEK satır) — SQLite'ta bir PRIMARY KEY'i
    // ALTER TABLE ile kaldırmak mümkün olmadığı için (kullanıcı verisi kaybı riski almadan) tabloyu
    // yeni şemayla YENİDEN oluşturup TÜM mevcut satırları kopyalıyoruz (idempotent — OverrideId
    // sütunu zaten varsa hiçbir şey yapmaz, bkz. EnsureColumnExists ile AYNI "bir kere kontrol et"
    // deseni).
    private static void EnsureFilePathOverridesAllowMultipleRows(SqliteConnection connection)
    {
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(FilePathOverrides)";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "OverrideId", StringComparison.OrdinalIgnoreCase))
                    return; // Zaten göç edilmiş.
            }
        }

        using var transaction = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                CREATE TABLE FilePathOverrides_New (
                    OverrideId INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameKey TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    MatchMethod TEXT,
                    TargetVersionRawName TEXT
                );
                INSERT INTO FilePathOverrides_New (GameKey, FilePath, MatchMethod, TargetVersionRawName)
                SELECT GameKey, FilePath, MatchMethod, TargetVersionRawName FROM FilePathOverrides;
                DROP TABLE FilePathOverrides;
                ALTER TABLE FilePathOverrides_New RENAME TO FilePathOverrides;
                """;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // EnsureFilePathOverridesAllowMultipleRows ile AYNI desen (SQLite'ta bir NOT NULL kısıtlamasını
    // ALTER TABLE ile kaldırmak mümkün değil) — tablo FilePath nullable olacak şekilde yeniden
    // oluşturulup TÜM satırlar (OverrideId dahil, sırası korunsun diye) kopyalanıyor. PRAGMA
    // table_info'nun notnull sütunu (index 3) zaten 0 ise (göç edilmiş) hiçbir şey yapılmaz.
    private static void EnsureFilePathOverridesAllowNullFilePath(SqliteConnection connection)
    {
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "PRAGMA table_info(FilePathOverrides)";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "FilePath", StringComparison.OrdinalIgnoreCase) && reader.GetInt32(3) == 0)
                    return;
            }
        }

        using var transaction = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                CREATE TABLE FilePathOverrides_New2 (
                    OverrideId INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameKey TEXT NOT NULL,
                    FilePath TEXT,
                    MatchMethod TEXT,
                    TargetVersionRawName TEXT,
                    ZipEntryName TEXT,
                    Crc32 TEXT
                );
                INSERT INTO FilePathOverrides_New2 (OverrideId, GameKey, FilePath, MatchMethod, TargetVersionRawName, ZipEntryName, Crc32)
                SELECT OverrideId, GameKey, FilePath, MatchMethod, TargetVersionRawName, ZipEntryName, Crc32 FROM FilePathOverrides;
                DROP TABLE FilePathOverrides;
                ALTER TABLE FilePathOverrides_New2 RENAME TO FilePathOverrides;
                """;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
