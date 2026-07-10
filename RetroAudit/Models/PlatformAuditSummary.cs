using System.IO;
using RetroAudit.Services;

namespace RetroAudit.Models;

// Media Provider'daki platform audit listesinin tek satırı — seçilebilir UI satırı ile
// gerçek platform filtresi arasında küçük bir "view model-ish" özet taşıyıcısı.
public class PlatformAuditSummary
{
    public required Platform Platform { get; init; }

    public string PlatformDisplayName => Platform.DisplayName;
    public string PlatformLogoPath
    {
        get
        {
            if (Platform.IsAllPlatforms)
                return AppPaths.NoImageLogo;

            if (PlatformDisplayName == "PC-FX")
            {
                var pcFxPath = Path.Combine(AppPaths.Images, "Platforms", "Pc-FX.png");
                return File.Exists(pcFxPath) ? pcFxPath : string.Empty;
            }

            var path = Path.Combine(AppPaths.Images, "Platforms", $"{PlatformDisplayName}.png");
            return File.Exists(path) ? path : string.Empty;
        }
    }

    public bool HasPlatformLogo => !string.IsNullOrWhiteSpace(PlatformLogoPath);
    public int TotalGames { get; init; }
    public int MatchedCount { get; init; }
    public int UnmatchedCount => TotalGames - MatchedCount;
    public int MissingLogoCount { get; init; }
    public int MissingBoxCount { get; init; }
    public int MissingScreenshotCount { get; init; }
    public int MissingVideoCount { get; init; }
    public int MissingWikipediaCount { get; init; }
    public int HealthPercent { get; init; }
}
