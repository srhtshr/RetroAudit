using System.IO;

namespace RetroAudit.Services;

// Bir bölge adını (ör. "USA", "Japan", "UK") Images/Flags altındaki gerçek dosya adına çözer —
// LaunchBox'ın kendi dosya adlandırması çoğu zaman bizim Region metnimizle birebir aynı ama bazı
// kısaltmalar/eş anlamlılar için (USA/UK/Netherlands/Korea varyantları) bir eşleme tablosu
// gerekiyor. Dosya yoksa (ör. "Unknown" ya da hâlâ eksik bir bayrak) null döner — çağıran taraf
// bunu "bayrak gösterme" olarak yorumlamalı.
public static class FlagResolver
{
    // Region metni -> Images/Flags altındaki gerçek dosya adı (uzantısız). Sadece dosya adı
    // BİZİM Region string'imizle birebir eşleşmediği durumlar burada; diğer her şey (Region adı
    // == dosya adı, boşluklar tireye çevrilerek) aşağıdaki Resolve içinde otomatik hallediliyor.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USA"] = "United-States",
        ["UK"] = "United-Kingdom",
        ["Netherlands"] = "The-Netherlands",
        ["South Korea"] = "Korea",
        ["North Korea"] = "Korea",
    };

    public static string? Resolve(string? region)
    {
        if (string.IsNullOrWhiteSpace(region) || region.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return null;

        var fileBaseName = Aliases.TryGetValue(region, out var alias) ? alias : region.Replace(' ', '-');
        var path = Path.Combine(AppPaths.Flags, $"{fileBaseName}.png");
        return File.Exists(path) ? path : null;
    }
}
