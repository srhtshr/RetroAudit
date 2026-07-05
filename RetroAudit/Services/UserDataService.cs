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
    public static readonly string DbPath = Path.Combine(
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
            PreferredVersionRawName TEXT
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

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString);
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateTablesSql;
            cmd.ExecuteNonQuery();
        }

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
        cmd.CommandText = "SELECT GameKey, IsHidden, IsDeleted, IsPermanentlyDeleted FROM GameState";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new GameStateInfo(
                reader.GetInt32(1) != 0,
                reader.GetInt32(2) != 0,
                reader.GetInt32(3) != 0);
        }
        return result;
    }

    public static Dictionary<string, MetadataOverride> GetAllMetadataOverrides()
    {
        var result = new Dictionary<string, MetadataOverride>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, Title, Genre, Description, Notes, Publisher, Developer, PreferredVersionRawName FROM MetadataOverrides";
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
                reader.IsDBNull(7) ? null : reader.GetString(7));
        }
        return result;
    }

    public static Dictionary<string, string> GetAllFilePathOverrides()
    {
        var result = new Dictionary<string, string>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT GameKey, FilePath FROM FilePathOverrides";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public static void SaveFilePathOverride(string gameKey, string filePath)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FilePathOverrides (GameKey, FilePath)
            VALUES ($gameKey, $filePath)
            ON CONFLICT(GameKey) DO UPDATE SET FilePath = $filePath
            """;
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$filePath", filePath);
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
            INSERT INTO MetadataOverrides (GameKey, Title, Genre, Description, Notes, Publisher, Developer, PreferredVersionRawName)
            VALUES ($gameKey, $title, $genre, $description, $notes, $publisher, $developer, $preferredVersionRawName)
            ON CONFLICT(GameKey) DO UPDATE SET
                Title = $title, Genre = $genre, Description = $description, Notes = $notes,
                Publisher = $publisher, Developer = $developer,
                PreferredVersionRawName = COALESCE($preferredVersionRawName, PreferredVersionRawName)
            """;
        cmd.Parameters.AddWithValue("$gameKey", gameKey);
        cmd.Parameters.AddWithValue("$title", (object?)overrideValue.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$genre", (object?)overrideValue.Genre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)overrideValue.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)overrideValue.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$publisher", (object?)overrideValue.Publisher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$developer", (object?)overrideValue.Developer ?? DBNull.Value);
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
}
