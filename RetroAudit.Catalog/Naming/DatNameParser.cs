using System.Text.RegularExpressions;

namespace RetroAudit.Catalog.Naming;

// Bir DAT "name" alanından hem No-Intro parantez etiketleme kuralına göre (ör.
// "Super Mario World (USA) (Rev A)") hem de TOSEC'in köşeli parantez dump/crack-grup kuralına
// göre (ör. "'Allo 'Allo! (Alternative) (Disk 1)[cr CSL][b doscopy]") temiz başlığı, bölge(leri)
// ve sürüm etiketini çıkarır; ayrıca bu kaydın RetroAudit.db'ye HİÇ dahil edilip edilmeyeceğine
// karar verir (ShouldExclude).
//
// Kural (kullanıcı talebiyle netleşti): RetroAudit sadece gerçek, resmi, oynanabilir oyun
// sürümlerini içerir. Beta/Prototype/Demo/Sample/Preview/Kiosk/BadDump/Overdump/Alternate/
// Cracked/Trainer/Fixed/Pirate/Hack/BIOS/Utility/Application/Documentation/Magazine/Music/
// Coverdisk/SDK/Update/TestROM gibi her şey — hem No-Intro'nun "(...)" hem TOSEC'in "[...]"
// etiketleme biçiminde — GameVersions'a bile girmeden tamamen atılır (önceki "alt sürüm olarak
// sakla ama ana listede gösterme" davranışının yerini aldı). Sadece USA/Europe/Japan/World gibi
// bölge etiketleri ve Rev A/B/C gibi resmi revizyonlar hayatta kalır.
public static partial class DatNameParser
{
    private static readonly HashSet<string> KnownRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "USA", "Europe", "Japan", "World", "Asia", "Australia", "Brazil", "Canada", "China",
        "Denmark", "Finland", "France", "Germany", "Greece", "Hong Kong", "Ireland", "Italy",
        "Korea", "Netherlands", "Norway", "Poland", "Portugal", "Russia", "Spain", "Sweden",
        "Switzerland", "Taiwan", "UK", "United Kingdom", "Scandinavia", "Latin America",
    };

    // No-Intro tarzı "(...)" etiketlerinde bu kelimelerden biriyle BAŞLAYAN bir grup, kaydın
    // tamamen dışlanmasına (ShouldExclude=true) neden olur.
    private static readonly string[] ExcludedParenKeywords =
    {
        "Beta", "Proto", "Prototype", "Demo", "Sample", "Preview", "Kiosk",
        "Bad Dump", "Overdump", "Alternate", "Alternative", "Crack", "Cracked", "Trainer", "Fixed",
        "Pirate", "Hack", "Aftermarket", "Unl", "Unlicensed",
        "BIOS", "Utility", "Utilities", "Application", "Applications",
        "Documentation", "Docs", "Magazine", "Music", "Coverdisk", "Cover Disk",
        "SDK", "Update", "Test",
    };

    // TOSEC köşeli parantez tek-harf dump/crack-grup kodları — hepsi tam dışlama nedeni.
    // cr=cracked, h=hack, t=trainer, f=fixed, b=bad dump, a=alternate, o=overdump, m=modified,
    // p=pirate.
    private static readonly HashSet<string> ExcludedBracketCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cr", "h", "t", "f", "b", "a", "o", "m", "p",
    };

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex ParenGroupRegex();

    [GeneratedRegex(@"\[([^\[\]]*)\]")]
    private static partial Regex BracketGroupRegex();

    [GeneratedRegex(@"^Rev\s*[A-Za-z0-9]+$|^v[\d.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionLabelRegex();

    public static ParsedDatName Parse(string rawName)
    {
        var regions = new List<string>();
        string? versionLabel = null;
        var shouldExclude = false;

        foreach (Match match in ParenGroupRegex().Matches(rawName))
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

            if (ExcludedParenKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                shouldExclude = true;
            }

            // Diğer parantez içerikleri (disk numarası, dil kodu vb.) gruplama mantığını
            // etkilemediği için burada göz ardı ediliyor.
        }

        // TOSEC köşeli parantez grupları: "[cr CSL]" -> kod "cr". Kod, dışlama listesindeyse
        // (cr/h/t/f/b/a/o/m/p) bu kayıt tamamen atılır.
        foreach (Match match in BracketGroupRegex().Matches(rawName))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length == 0)
                continue;

            var code = content.Split(' ', 2)[0];
            if (ExcludedBracketCodes.Contains(code))
                shouldExclude = true;
        }

        var cleanTitle = ParenGroupRegex().Replace(rawName, string.Empty);
        cleanTitle = BracketGroupRegex().Replace(cleanTitle, string.Empty);
        cleanTitle = Regex.Replace(cleanTitle, @"\s{2,}", " ").Trim();

        return new ParsedDatName(cleanTitle, regions.ToArray(), versionLabel, shouldExclude);
    }

    // Parse(), sadece DAT'ın "game" seviyesindeki isme (rawName) bakıyor — ama bazı No-Intro
    // kayıtlarında (ör. "Forehead Block Guy") oyun adı temiz görünürken, o oyunun ROM dosya
    // adı(ları) "(Aftermarket) (Unl)" gibi ek etiketler taşıyabiliyor (oyun adı ile rom adı
    // BİREBİR aynı olmak zorunda değil). Bu yüzden VersionResolver, her kaydın rom dosya
    // adlarını da AYRICA bu metotla kontrol ediyor — game adı "temiz" görünse bile dosya
    // adında dışlanan bir etiket varsa kayıt yine tamamen elenir (kullanıcı geri bildirimi:
    // "üstte released yazıyor ama sürüm adında Unl yazıyor").
    public static bool ContainsExcludedTag(string text)
    {
        foreach (Match match in ParenGroupRegex().Matches(text))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length > 0 && ExcludedParenKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        foreach (Match match in BracketGroupRegex().Matches(text))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length == 0)
                continue;

            var code = content.Split(' ', 2)[0];
            if (ExcludedBracketCodes.Contains(code))
                return true;
        }

        return false;
    }
}

// ShouldExclude=true olan bir kayıt VersionResolver tarafından tamamen atlanır — GameVersions'a
// bile girmez. Hayatta kalan her kayıt tanım gereği "resmi/temiz" sayılır: sadece bölge ve
// (varsa) Rev etiketi taşır.
public record ParsedDatName(string CleanTitle, string[] Regions, string? VersionLabel, bool ShouldExclude);
