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

    public int PreferredVersionMissing { get; set; }
    public int DuplicateHashCollisions { get; set; }
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

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== RetroAudit Catalog Build Report ===");
        sb.AppendLine($"Platforms:                  {PlatformCount}");
        sb.AppendLine($"Games:                      {GameCount}");
        sb.AppendLine($"Versions:                   {VersionCount}");
        sb.AppendLine($"Hash rows:                  {HashCount}");
        sb.AppendLine($"LaunchBox matched:          {MetadataMatched}  (exact: {MetadataMatched - FuzzyMatched}, fuzzy: {FuzzyMatched})");
        sb.AppendLine($"  of which Needs Review:    {NeedsReview}");
        sb.AppendLine($"LaunchBox unmatched:        {MetadataUnmatched}");
        sb.AppendLine($"Preferred version missing:  {PreferredVersionMissing}");
        sb.AppendLine($"Duplicate hash collisions:  {DuplicateHashCollisions}");
        sb.AppendLine($"Skipped DAT files:          {SkippedDatFiles.Count}");
        foreach (var file in SkippedDatFiles)
            sb.AppendLine($"  - {file}");
        sb.AppendLine($"Excluded platforms:         {ExcludedPlatforms.Count}");
        foreach (var platform in ExcludedPlatforms)
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
