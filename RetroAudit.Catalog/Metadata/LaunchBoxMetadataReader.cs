using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace RetroAudit.Catalog.Metadata;

public record MetadataMatch(
    string Title,
    string? Developer,
    string? Publisher,
    int? ReleaseYear,
    string? Overview,
    int? MaxPlayers,
    string[] Genres,
    double Confidence,
    string MatchMethod,
    DateTime? ReleaseDate,
    double? CommunityRating,
    int? CommunityRatingCount,
    string? VideoUrl,
    string? WikipediaUrl,
    long? SteamAppId,
    bool? Cooperative,
    string[] AlternateNames,
    int MetadataSourceId,
    string? BoxImageFileName,
    string? BackgroundImageFileName,
    string? ScreenshotImageFileName,
    string? ClearLogoImageFileName);

// LaunchBox.Metadata.db'ye salt-okunur (Mode=ReadOnly) erişir. Bu, RetroAudit'in tek seferlik
// zenginleştirme kaynağıdır — Builder bu dosyayı asla değiştirmez ve çalışma zamanındaki RetroAudit
// hiçbir zaman buna bağımlı olmaz. Platform eşleşmesi olmadan (yani sadece CompareName ile) global
// arama YAPILMAZ; kullanıcının istediği "Platform + CompareName zorunlu" kuralı burada uygulanıyor.
//
// Eşleştirme dört kademeli: CompareName -> tam isim -> LaunchBox'ın alternatif isim tablosu ->
// (hiçbiri tutmazsa) fuzzy benzerlik. İlk üç kademe Confidence=1.0 ile "kesin" kabul edilir;
// fuzzy kademe ise Confidence puanına göre CatalogBuilder tarafından "onaylı" veya "Needs Review"
// olarak işaretlenir (bkz. FuzzyAcceptThreshold/FuzzyReviewFloor).
public class LaunchBoxMetadataReader : IDisposable
{
    // Bu eşiğin üzeri: yeterince güvenli, doğrudan onaylı eşleşme sayılır.
    public const double FuzzyAcceptThreshold = 0.92;

    // Bu eşiğin altı: yanlış eşleşme riski çok yüksek, hiç önerilmez (boş bırakmak daha güvenli).
    // Arada kalanlar (FuzzyReviewFloor <= puan < FuzzyAcceptThreshold) eşleşme olarak kaydedilir
    // ama "Needs Review" bayrağıyla — kullanıcı gözden geçirene kadar şüpheli kabul edilir.
    public const double FuzzyReviewFloor = 0.80;

    private const int PrefixLength = 3;

    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, string?> _platformResolutionCache = new(StringComparer.OrdinalIgnoreCase);

    // platform -> (CompareName'in ilk PrefixLength karakteri) -> o kovaya düşen adaylar.
    // Büyük platformlarda (ör. TOSEC Amiga, binlerce LaunchBox kaydı) her fuzzy denemede TÜM
    // aday listesini taramak yerine O(1) kova erişimiyle sadece ilgili alt kümeyi kontrol eder.
    private readonly Dictionary<string, Dictionary<string, List<(string Name, string CompareName)>>> _platformGameNameBuckets = new(StringComparer.OrdinalIgnoreCase);

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
                "SELECT DatabaseID, Name, Developer, Publisher, ReleaseYear, Overview, MaxPlayers, Genres, " +
                "ReleaseDate, CommunityRating, CommunityRatingCount, VideoURL, WikipediaURL, SteamAppId, Cooperative " +
                "FROM Games WHERE Platform = $platform AND CompareName = $compareName LIMIT 1";
            byCompareName.Parameters.AddWithValue("$platform", resolvedPlatform);
            byCompareName.Parameters.AddWithValue("$compareName", compareTitle);

            using var reader = byCompareName.ExecuteReader();
            if (reader.Read())
                return ReadMatch(reader, confidence: 1.0, matchMethod: "CompareName");
        }

        // CompareName normalizasyon farkı yüzünden kaçırılmış olabilir; aynı platform içinde
        // (platform şartı burada da geçerli) tam başlık eşleşmesi ikinci deneme olarak yapılır.
        using (var byExactName = _connection.CreateCommand())
        {
            byExactName.CommandText =
                "SELECT DatabaseID, Name, Developer, Publisher, ReleaseYear, Overview, MaxPlayers, Genres, " +
                "ReleaseDate, CommunityRating, CommunityRatingCount, VideoURL, WikipediaURL, SteamAppId, Cooperative " +
                "FROM Games WHERE Platform = $platform AND Name = $name LIMIT 1";
            byExactName.Parameters.AddWithValue("$platform", resolvedPlatform);
            byExactName.Parameters.AddWithValue("$name", exactTitle);

            using var reader2 = byExactName.ExecuteReader();
            if (reader2.Read())
                return ReadMatch(reader2, confidence: 1.0, matchMethod: "ExactName");
        }

        // LaunchBox'ın kendi alternatif isim/lakap tablosu (GameAlternateTitles) üzerinden
        // eşleştirme — platform şartı burada da korunur, global bir arama hiçbir zaman yapılmaz.
        using (var byAlternateName = _connection.CreateCommand())
        {
            byAlternateName.CommandText = """
                SELECT g.DatabaseID, g.Name, g.Developer, g.Publisher, g.ReleaseYear, g.Overview, g.MaxPlayers, g.Genres,
                       g.ReleaseDate, g.CommunityRating, g.CommunityRatingCount, g.VideoURL, g.WikipediaURL, g.SteamAppId, g.Cooperative
                FROM GameAlternateTitles a
                JOIN Games g ON g.DatabaseID = a.DatabaseID
                WHERE g.Platform = $platform AND a.AltNameCompareValue = $compareName
                LIMIT 1
                """;
            byAlternateName.Parameters.AddWithValue("$platform", resolvedPlatform);
            byAlternateName.Parameters.AddWithValue("$compareName", compareTitle);

            using var reader3 = byAlternateName.ExecuteReader();
            if (reader3.Read())
                return ReadMatch(reader3, confidence: 1.0, matchMethod: "AlternateName");
        }

        // Son çare: fuzzy (Levenshtein benzerlik) eşleştirme. FuzzyReviewFloor'un altındaki sonuçlar
        // hiç döndürülmez; arada kalanlar CatalogBuilder tarafından "Needs Review" işaretlenecek.
        return FindFuzzyMatch(resolvedPlatform, compareTitle);
    }

    private MetadataMatch? FindFuzzyMatch(string resolvedPlatform, string compareTitle)
    {
        if (compareTitle.Length < PrefixLength)
            return null; // çok kısa başlıklarda benzerlik oranı anlamsızlaşır, yanlış eşleşme riski yüksek

        if (!_platformGameNameBuckets.TryGetValue(resolvedPlatform, out var buckets))
        {
            buckets = new Dictionary<string, List<(string Name, string CompareName)>>(StringComparer.Ordinal);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Name, CompareName FROM Games WHERE Platform = $platform";
            cmd.Parameters.AddWithValue("$platform", resolvedPlatform);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var cmpName = reader.IsDBNull(1) ? name : reader.GetString(1);
                if (cmpName.Length < PrefixLength)
                    continue;

                var bucketKey = cmpName[..PrefixLength];
                if (!buckets.TryGetValue(bucketKey, out var bucket))
                {
                    bucket = new List<(string, string)>();
                    buckets[bucketKey] = bucket;
                }
                bucket.Add((name, cmpName));
            }

            _platformGameNameBuckets[resolvedPlatform] = buckets;
        }

        var bestRatio = 0.0;
        string? bestName = null;

        // O(1) kova erişimi: aynı platformdaki binlerce/on binlerce kaydın tamamını taramak yerine
        // sadece CompareName'in ilk PrefixLength karakteri eşleşen adaylar kontrol edilir.
        if (buckets.TryGetValue(compareTitle[..PrefixLength], out var candidates))
        {
            foreach (var (name, cmpName) in candidates)
            {
                // Güvenlik freni: "Super Mario Bros 16" ile "Super Mario Bros 6" gibi isimler tek
                // rakam farkıyla neredeyse birebir Levenshtein oranı üretir (bir NES pirate kartı
                // gerçek testte bir ROM hack'ine yanlışlıkla yüksek güvenle eşleşti) — ama farklı
                // sayı, genelde farklı bir devam/sürüm/hack anlamına gelir. İki başlıkta da rakam
                // varsa ve rakam kümeleri farklıysa, oran ne olursa olsun bu aday tamamen elenir.
                if (HasConflictingNumerals(compareTitle, cmpName))
                    continue;

                var ratio = StringSimilarity.Ratio(compareTitle, cmpName);
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestName = name;
                    if (bestRatio >= 0.999)
                        break;
                }
            }
        }

        if (bestName is null || bestRatio < FuzzyReviewFloor)
            return null;

        using var detail = _connection.CreateCommand();
        detail.CommandText =
            "SELECT DatabaseID, Name, Developer, Publisher, ReleaseYear, Overview, MaxPlayers, Genres, " +
            "ReleaseDate, CommunityRating, CommunityRatingCount, VideoURL, WikipediaURL, SteamAppId, Cooperative " +
            "FROM Games WHERE Platform = $platform AND Name = $name LIMIT 1";
        detail.Parameters.AddWithValue("$platform", resolvedPlatform);
        detail.Parameters.AddWithValue("$name", bestName);

        using var detailReader = detail.ExecuteReader();
        return detailReader.Read() ? ReadMatch(detailReader, bestRatio, "Fuzzy") : null;
    }

    private static readonly Regex DigitSequenceRegex = new(@"\d+", RegexOptions.Compiled);

    // İki başlıkta da en az bir rakam dizisi varsa VE bu rakam kümeleri birebir aynı değilse true
    // döner. Sadece rakam FARKI değil, rakam VARLIĞI da iki tarafta olmalı — "Mega Man 2" ile
    // "Mega Man II" gibi rakam/Roma rakamı karışık durumlarda (biri rakamsız) veto uygulanmaz,
    // çünkü bu daha az riskli bir belirsizlik.
    private static bool HasConflictingNumerals(string a, string b)
    {
        var numbersA = DigitSequenceRegex.Matches(a).Select(m => m.Value).ToHashSet();
        var numbersB = DigitSequenceRegex.Matches(b).Select(m => m.Value).ToHashSet();

        if (numbersA.Count == 0 || numbersB.Count == 0)
            return false;

        return !numbersA.SetEquals(numbersB);
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

    // Artık statik değil: eşleşen kaydın DatabaseID'siyle GameAlternateTitles'tan lakap listesini
    // ayrıca sorgulaması gerekiyor (bkz. GetAlternateNames) — eşleştirmede kullanılan
    // AltNameCompareValue'dan farklı olarak burada GÖRÜNTÜLENEBİLİR AlternateName alınıyor.
    private MetadataMatch ReadMatch(SqliteDataReader reader, double confidence, string matchMethod)
    {
        var databaseId = reader.GetInt32(0);

        var genresRaw = reader.IsDBNull(7) ? null : reader.GetString(7);
        var genres = genresRaw?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     ?? Array.Empty<string>();

        // LaunchBox ReleaseDate "yyyy-MM-dd HH:mm:ss" biçiminde düz metin — bazı kayıtlarda hiç yok
        // (NULL) ama ReleaseYear dolu olabilir, bu yüzden ayrı ayrı nullable tutuluyor.
        var releaseDateRaw = reader.IsDBNull(8) ? null : reader.GetString(8);
        var releaseDate = releaseDateRaw is not null && DateTime.TryParse(releaseDateRaw, out var parsedDate)
            ? parsedDate
            : (DateTime?)null;

        var artwork = GetArtwork(databaseId);

        return new MetadataMatch(
            Title: reader.GetString(1),
            Developer: reader.IsDBNull(2) ? null : reader.GetString(2),
            Publisher: reader.IsDBNull(3) ? null : reader.GetString(3),
            ReleaseYear: reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Overview: reader.IsDBNull(5) ? null : reader.GetString(5),
            MaxPlayers: reader.IsDBNull(6) ? null : reader.GetInt32(6),
            Genres: genres,
            Confidence: confidence,
            MatchMethod: matchMethod,
            ReleaseDate: releaseDate,
            CommunityRating: reader.IsDBNull(9) ? null : reader.GetDouble(9),
            CommunityRatingCount: reader.IsDBNull(10) ? null : reader.GetInt32(10),
            VideoUrl: reader.IsDBNull(11) ? null : reader.GetString(11),
            WikipediaUrl: reader.IsDBNull(12) ? null : reader.GetString(12),
            SteamAppId: reader.IsDBNull(13) ? null : reader.GetInt64(13),
            Cooperative: reader.IsDBNull(14) ? null : reader.GetInt32(14) != 0,
            AlternateNames: GetAlternateNames(databaseId),
            MetadataSourceId: databaseId,
            BoxImageFileName: artwork.Box,
            BackgroundImageFileName: artwork.Background,
            ScreenshotImageFileName: artwork.Screenshot,
            ClearLogoImageFileName: artwork.ClearLogo);
    }

    // Görsel türü başına (Box/Background/Screenshot/ClearLogo) en fazla bir dosya adı döner —
    // bölge önceliği ROM sürüm seçiminde kullanılanla birebir aynı (bkz. VersionResolver.RegionRank:
    // USA > Europe > World > Japan > diğer). Hiç bölge bilgisi olmayan bir görsel de kabul edilir
    // (kişisel koleksiyon aracı için "hiç yok"tan iyi). Sonuç doğrudan RetroAudit.db'ye yazılıyor,
    // çalışma zamanında bu kaynağa bir daha hiç erişilmiyor.
    private (string? Box, string? Background, string? Screenshot, string? ClearLogo) GetArtwork(int databaseId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT FileName, Type, Region FROM GameImages WHERE DatabaseId = $databaseId " +
                           "AND Type IN ('Box - Front', 'Fanart - Background', 'Screenshot - Gameplay', 'Clear Logo')";
        cmd.Parameters.AddWithValue("$databaseId", databaseId);

        string? box = null, background = null, screenshot = null, clearLogo = null;
        var bestRank = new Dictionary<string, int>(StringComparer.Ordinal);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var fileName = reader.GetString(0);
            var type = reader.GetString(1);
            var region = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var rank = RegionRank(region);

            if (bestRank.TryGetValue(type, out var currentRank) && currentRank <= rank)
                continue;
            bestRank[type] = rank;

            switch (type)
            {
                case "Box - Front": box = fileName; break;
                case "Fanart - Background": background = fileName; break;
                case "Screenshot - Gameplay": screenshot = fileName; break;
                case "Clear Logo": clearLogo = fileName; break;
            }
        }

        return (box, background, screenshot, clearLogo);
    }

    private static int RegionRank(string region)
    {
        if (region.Equals("USA", StringComparison.OrdinalIgnoreCase)) return 0;
        if (region.Equals("Europe", StringComparison.OrdinalIgnoreCase)) return 1;
        if (region.Equals("World", StringComparison.OrdinalIgnoreCase)) return 2;
        if (region.Equals("Japan", StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    private string[] GetAlternateNames(int databaseId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT AlternateName FROM GameAlternateTitles WHERE DatabaseID = $databaseId";
        cmd.Parameters.AddWithValue("$databaseId", databaseId);

        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                names.Add(reader.GetString(0));
        }
        return names.ToArray();
    }

    public void Dispose() => _connection.Dispose();
}
