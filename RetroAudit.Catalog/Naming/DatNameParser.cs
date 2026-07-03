using System.Text.RegularExpressions;

namespace RetroAudit.Catalog.Naming;

// Bir DAT "name" alanından (No-Intro/TOSEC parantez etiketleme kuralına göre, ör.
// "Super Mario World (USA) (Rev A)" veya "!Clik! (World) (Proto 1) (Aftermarket) (Unl)")
// temiz başlığı, bölge(leri), sürüm etiketini ve "alt sürüm" bayraklarını çıkarır.
public static partial class DatNameParser
{
    private static readonly HashSet<string> KnownRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "USA", "Europe", "Japan", "World", "Asia", "Australia", "Brazil", "Canada", "China",
        "Denmark", "Finland", "France", "Germany", "Greece", "Hong Kong", "Ireland", "Italy",
        "Korea", "Netherlands", "Norway", "Poland", "Portugal", "Russia", "Spain", "Sweden",
        "Switzerland", "Taiwan", "UK", "United Kingdom", "Scandinavia", "Latin America",
    };

    // Bunlardan biriyle başlayan bir parantez grubu, oyunun "normal" sürümü olarak sayılmaz
    // (VersionResolver'ın ana liste satırını şişirmeme mantığı bu listeye dayanır).
    private static readonly string[] AltVersionFlagPrefixes =
    {
        "Beta", "Proto", "Prototype", "Demo", "Sample", "Test", "Pirate", "Hack", "Aftermarket", "Unl", "Unlicensed", "Alpha",
    };

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex TagGroupRegex();

    [GeneratedRegex(@"^Rev\s*[A-Za-z0-9]+$|^v[\d.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionLabelRegex();

    public static ParsedDatName Parse(string rawName)
    {
        var regions = new List<string>();
        string? versionLabel = null;
        var flags = new List<string>();

        foreach (Match match in TagGroupRegex().Matches(rawName))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length == 0)
                continue;

            var parts = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0 && parts.All(p => KnownRegions.Contains(p)))
            {
                regions.AddRange(parts);
                continue;
            }

            if (VersionLabelRegex().IsMatch(content))
            {
                versionLabel = content;
                continue;
            }

            var altFlag = AltVersionFlagPrefixes.FirstOrDefault(prefix =>
                content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (altFlag is not null)
            {
                flags.Add(content);
            }

            // Diğer parantez içerikleri (disk numarası, dil kodu vb.) gruplama mantığını
            // etkilemediği için burada göz ardı ediliyor.
        }

        var cleanTitle = TagGroupRegex().Replace(rawName, string.Empty);
        cleanTitle = Regex.Replace(cleanTitle, @"\s{2,}", " ").Trim();

        return new ParsedDatName(cleanTitle, regions.ToArray(), versionLabel, flags.ToArray());
    }
}

public record ParsedDatName(string CleanTitle, string[] Regions, string? VersionLabel, string[] Flags)
{
    // Beta/Proto/Demo/Test/Pirate/Hack/Aftermarket/Unlicensed gibi en az bir bayrağı varsa,
    // bu kayıt "normal" değil "alt sürüm" sayılır (VersionResolver'ın önceliklendirmesinde kullanılır).
    public bool IsAltVersion => Flags.Length > 0;
}
