using System.IO;
using RetroAudit.Services;

namespace RetroAudit.Models;

public class PlatformMetadataAuditSummary
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
    public int MissingGenresCount { get; init; }
    public int MissingPublisherCount { get; init; }
    public int MissingDescriptionCount { get; init; }
    public int MissingYearCount { get; init; }
    public int MissingVersionCount { get; init; }
    public int ManuallyLinkedCount { get; init; }
    public int HealthPercent { get; init; }
}
