using Microsoft.Data.Sqlite;

namespace RetroAudit.Catalog.Metadata;

public record MetadataMatch(
    string Title,
    string? Developer,
    string? Publisher,
    int? ReleaseYear,
    string? Overview,
    int? MaxPlayers,
    string[] Genres);

// LaunchBox.Metadata.db'ye salt-okunur (Mode=ReadOnly) erişir. Bu, RetroAudit'in tek seferlik
// zenginleştirme kaynağıdır — Builder bu dosyayı asla değiştirmez ve çalışma zamanındaki RetroAudit
// hiçbir zaman buna bağımlı olmaz. Platform eşleşmesi olmadan (yani sadece CompareName ile) global
// arama YAPILMAZ; kullanıcının istediği "Platform + CompareName zorunlu" kuralı burada uygulanıyor.
public class LaunchBoxMetadataReader : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, string?> _platformResolutionCache = new(StringComparer.OrdinalIgnoreCase);

    public LaunchBoxMetadataReader(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    // Platform adı LaunchBox'ta hiç tanınamadıysa true döner — CatalogBuilder bunu, "bu platformun
    // TÜMÜ LaunchBox'ta yok" durumunu (her oyunun ayrı ayrı eşleşmemesinden farklı, çok daha ciddi
    // bir durum) rapora doğru yansıtmak için kullanır.
    public bool IsPlatformKnown(string datPlatformName) => ResolvePlatform(datPlatformName) is not null;

    public MetadataMatch? FindMatch(string datPlatformName, string compareTitle, string exactTitle)
    {
        var resolvedPlatform = ResolvePlatform(datPlatformName);
        if (resolvedPlatform is null)
            return null; // Bu DAT platformu LaunchBox'ta tanınamadı — enrichment atlanır, yanlış eşleşme riske edilmez.

        using (var byCompareName = _connection.CreateCommand())
        {
            byCompareName.CommandText =
                "SELECT Name, Developer, Publisher, ReleaseYear, Overview, MaxPlayers, Genres " +
                "FROM Games WHERE Platform = $platform AND CompareName = $compareName LIMIT 1";
            byCompareName.Parameters.AddWithValue("$platform", resolvedPlatform);
            byCompareName.Parameters.AddWithValue("$compareName", compareTitle);

            using var reader = byCompareName.ExecuteReader();
            if (reader.Read())
                return ReadMatch(reader);
        }

        // CompareName normalizasyon farkı yüzünden kaçırılmış olabilir; aynı platform içinde
        // (platform şartı burada da geçerli) tam başlık eşleşmesi ikinci deneme olarak yapılır.
        using (var byExactName = _connection.CreateCommand())
        {
            byExactName.CommandText =
                "SELECT Name, Developer, Publisher, ReleaseYear, Overview, MaxPlayers, Genres " +
                "FROM Games WHERE Platform = $platform AND Name = $name LIMIT 1";
            byExactName.Parameters.AddWithValue("$platform", resolvedPlatform);
            byExactName.Parameters.AddWithValue("$name", exactTitle);

            using var reader2 = byExactName.ExecuteReader();
            if (reader2.Read())
                return ReadMatch(reader2);
        }

        // Son çare: LaunchBox'ın kendi alternatif isim/lakap tablosu (GameAlternateTitles) üzerinden
        // eşleştirme — platform şartı burada da korunur, global bir arama hiçbir zaman yapılmaz.
        using var byAlternateName = _connection.CreateCommand();
        byAlternateName.CommandText = """
            SELECT g.Name, g.Developer, g.Publisher, g.ReleaseYear, g.Overview, g.MaxPlayers, g.Genres
            FROM GameAlternateTitles a
            JOIN Games g ON g.DatabaseID = a.DatabaseID
            WHERE g.Platform = $platform AND a.AltNameCompareValue = $compareName
            LIMIT 1
            """;
        byAlternateName.Parameters.AddWithValue("$platform", resolvedPlatform);
        byAlternateName.Parameters.AddWithValue("$compareName", compareTitle);

        using var reader3 = byAlternateName.ExecuteReader();
        return reader3.Read() ? ReadMatch(reader3) : null;
    }

    private string? ResolvePlatform(string datPlatformName)
    {
        if (_platformResolutionCache.TryGetValue(datPlatformName, out var cached))
            return cached;

        string? resolved = null;

        foreach (var candidate in PlatformNameMap.BuildCandidates(datPlatformName))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Platforms WHERE Name = $name LIMIT 1";
            cmd.Parameters.AddWithValue("$name", candidate);

            if (cmd.ExecuteScalar() is string matchedName)
            {
                resolved = matchedName;
                break;
            }
        }

        _platformResolutionCache[datPlatformName] = resolved;
        return resolved;
    }

    private static MetadataMatch ReadMatch(SqliteDataReader reader)
    {
        var genresRaw = reader.IsDBNull(6) ? null : reader.GetString(6);
        var genres = genresRaw?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     ?? Array.Empty<string>();

        return new MetadataMatch(
            Title: reader.GetString(0),
            Developer: reader.IsDBNull(1) ? null : reader.GetString(1),
            Publisher: reader.IsDBNull(2) ? null : reader.GetString(2),
            ReleaseYear: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Overview: reader.IsDBNull(4) ? null : reader.GetString(4),
            MaxPlayers: reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Genres: genres);
    }

    public void Dispose() => _connection.Dispose();
}
