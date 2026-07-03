using System.Text;

namespace RetroAudit.Catalog;

// Bir Builder koşusunun sonunda üretilen özet rapor. Konsola yazdırılır; "sessizce hata"
// olmaması için her koşuda bu rapor gösterilir (kullanıcı isteği: Builder her zaman rapor üretmeli).
public class BuildReport
{
    public int PlatformCount { get; set; }
    public int GameCount { get; set; }
    public int VersionCount { get; set; }
    public int HashCount { get; set; }
    public int MetadataMatched { get; set; }
    public int MetadataUnmatched { get; set; }

    // MetadataMatched'in alt kümeleri: kaçı fuzzy (benzerlik) eşleştirmeyle bulundu, kaçı
    // FuzzyAcceptThreshold'un altında kalıp "Needs Review" işaretlendi.
    public int FuzzyMatched { get; set; }
    public int NeedsReview { get; set; }

    // Filtreyi geçen ama başlığında net bir bölge etiketi (USA/Europe/Japan/World) bulunamayan
    // sürüm sayısı — "Unknown" olarak işaretlenip GameVersions'da tutulanlar.
    public int UnknownRegionVersionCount { get; set; }

    // Beta/Proto/Demo/Pirate/Cracked/BIOS/Utility/... ya da TOSEC [cr][h][t][f][b][a][o][m][p]
    // etiketleri yüzünden DatNameParser.ShouldExclude tarafından tamamen elenen ham DAT kaydı sayısı.
    public int FilteredRecordCount { get; set; }

    public int PreferredVersionMissing { get; set; }
    public int DuplicateHashCollisions { get; set; }

    // Casino/Gambling/Mahjong/Pachinko/Pachislot/Quiz/Board Game/Tabletop/Card Game/Educational
    // türlerinden biriyle eşleşen ve bu yüzden Games.HiddenByDefault=1 yazılan oyun sayısı —
    // satırlar silinmiyor, sadece WPF'in varsayılan ana listesinde gizleniyor.
    public int HiddenByDefaultCount { get; set; }
    public List<string> SkippedDatFiles { get; } = new();

    // Platformun TAMAMI LaunchBox'ta hiç tanınamadı (isim eşleşmesi yok) — tek tek oyunların
    // bulunamamasından çok daha ciddi bir durum, bu yüzden ayrı listeleniyor.
    public List<string> PlatformsNotInLaunchBox { get; } = new();

    // Birden fazla kaynakta (ör. hem No-Intro hem Redump) bulunan ama PlatformSourceMap'te açık
    // bir kural olmadığı için sabit öncelik sırasına düşülen platformlar — hiçbir zaman iki kaynak
    // birleştirilmez, ama bu liste "burada bir karar verildi, gözden geçir" sinyali verir.
    public List<(string Platform, string ChosenSource, List<string> AvailableSources)> AmbiguousPlatformsDefaulted { get; } = new();

    // Varsayılan olarak katalog dışı bırakılan platformlar (ör. Xbox 360 Digital/DLC varyantları).
    public List<string> ExcludedPlatforms { get; } = new();

    // Curated UI listesinde karşılığı olmadığı için hiç taranmayan platformlar (PlatformAllowList).
    public List<string> OutOfScopePlatforms { get; } = new();

    // Kaynak (no-intro/redump/tosec/...) ve platform bazında oyun sayıları — her platform tek bir
    // kaynaktan geldiği için (PlatformSourceMap) bu iki kırılım tutarlıdır: bir platformun tüm
    // oyunları aynı kaynak grubunun altında toplanır.
    public Dictionary<string, int> GamesBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> GamesByPlatform { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan BuildDuration { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== RetroAudit Catalog Build Report ===");
        sb.AppendLine($"Build duration:             {BuildDuration:mm\\:ss\\.fff}");
        sb.AppendLine($"Platforms:                  {PlatformCount}");
        sb.AppendLine($"Games:                      {GameCount}");
        sb.AppendLine($"Versions:                   {VersionCount}");
        sb.AppendLine($"Hash rows:                  {HashCount}");
        sb.AppendLine($"Filtered records (excluded before grouping): {FilteredRecordCount}");
        sb.AppendLine($"Unknown-region versions:    {UnknownRegionVersionCount}");
        sb.AppendLine($"RetroAudit Matched:         {MetadataMatched}  (exact: {MetadataMatched - FuzzyMatched}, fuzzy: {FuzzyMatched})");
        sb.AppendLine($"  of which Needs Review:    {NeedsReview}");
        sb.AppendLine($"Missing Metadata:           {MetadataUnmatched}");
        sb.AppendLine($"Preferred version missing:  {PreferredVersionMissing}");
        sb.AppendLine($"Duplicate hash collisions:  {DuplicateHashCollisions}");
        sb.AppendLine($"Hidden by default (genre):  {HiddenByDefaultCount}");

        sb.AppendLine("Games by source:");
        foreach (var (source, count) in GamesBySource.OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {source,-12} {count}");

        sb.AppendLine("Games by platform (top 20):");
        foreach (var (platform, count) in GamesByPlatform.OrderByDescending(kv => kv.Value).Take(20))
            sb.AppendLine($"  {platform,-45} {count}");

        sb.AppendLine($"Skipped DAT files:          {SkippedDatFiles.Count}");
        foreach (var file in SkippedDatFiles)
            sb.AppendLine($"  - {file}");
        sb.AppendLine($"Excluded platforms:         {ExcludedPlatforms.Count}");
        foreach (var platform in ExcludedPlatforms)
            sb.AppendLine($"  - {platform}");
        sb.AppendLine($"Out of scope (not in curated list): {OutOfScopePlatforms.Count}");
        foreach (var platform in OutOfScopePlatforms)
            sb.AppendLine($"  - {platform}");
        sb.AppendLine($"Ambiguous platforms (defaulted via priority order): {AmbiguousPlatformsDefaulted.Count}");
        foreach (var (platform, chosen, available) in AmbiguousPlatformsDefaulted)
            sb.AppendLine($"  - {platform} -> {chosen} (available: {string.Join(", ", available)})");
        sb.AppendLine($"Platforms not in LaunchBox:  {PlatformsNotInLaunchBox.Count}");
        foreach (var platform in PlatformsNotInLaunchBox)
            sb.AppendLine($"  - {platform}");
        return sb.ToString();
    }
}
