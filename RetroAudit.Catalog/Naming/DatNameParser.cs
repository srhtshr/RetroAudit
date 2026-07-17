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

    [GeneratedRegex(@"^Rev\s*([A-Za-z0-9]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionTokenRegex();

    // Amiga sahne DAT'ları No-Intro'nun "(...)" etiketleme kuralını kullanmıyor — sürüm bilgisi
    // başlığın SONUNA çıplak (parantezsiz) bir token olarak ekleniyor (ör. "Ambermoon v1.01",
    // "Dread r1A08"). ParenGroupRegex bunları hiç yakalamadığı için (parantez yok) ayrı bir
    // kontrol gerekiyor. Kullanıcı + araştırma ile netleşen kapsam SADECE gerçek sürüm kalıpları:
    // v0.09/v1.1/v2.0, r3/r57/r1905/r1A08 (hex rev kodu), rev1/rev2, release 2, alpha v6, beta 1,
    // rc4, wip1. AGA/CD32/CDTV/ECS/OCS/WHDLoad/Trainer/Cracked/Fixed gibi dağıtım etiketlerine
    // kasten dokunulmuyor (aynı oyun mu farklı dağıtım mı belirsiz, ayrı bir karar konusu).
    //
    // ÖNEMLİ — sadece Amiga'ya scope'lu (bkz. Parse'ın platformName parametresi): ilk denemede bu
    // kontrol TÜM platformlara uygulanmıştı ve gerçek veriyle test edilince NES "Family Basic
    // V2.0/V2.1/V3" (üç AYRI ürün), Xbox "Wireless Adapter Setup v1.0", Xbox 360 "Experience Demo
    // v7.1/v7.2/v7.5" gibi başlıklarda "v-sayı" ifadesinin oyunun GERÇEK adının bir PARÇASI olduğu,
    // sürüm eki OLMADIĞI ortaya çıktı — bunlar yanlışlıkla tek karta birleştiriliyordu ve daha önce
    // işlenmiş 16 platformdaki override'ların GameKey'ini kaydırıp kopuk bırakıyordu. Bu yüzden bu
    // regex SADECE Commodore - Amiga DAT girdilerinde çalışır.
    [GeneratedRegex(@"(?<=\s)(v\d+(?:\.\d+)*|r[0-9a-fA-F]{1,6}|rev\d+|release\s\d+|alpha\s?v?\d+|beta\s\d+|rc\d+|wip\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BareVersionSuffixRegex();

    private const string AmigaPlatformName = "Commodore - Amiga";

    public static ParsedDatName Parse(string rawName, string? platformName = null)
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

        // Parantez içi bir sürüm etiketi zaten bulunduysa (No-Intro "(Rev 1)" vb.) çıplak sondaki
        // token'a bakılmıyor — iki mekanizma aynı kayıtta asla birlikte tetiklenmiyor.
        if (versionLabel is null && string.Equals(platformName, AmigaPlatformName, StringComparison.Ordinal))
        {
            var suffixMatch = BareVersionSuffixRegex().Match(cleanTitle);
            if (suffixMatch.Success)
            {
                versionLabel = suffixMatch.Value.Trim();
                cleanTitle = cleanTitle[..suffixMatch.Index].TrimEnd();
            }
        }

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

    // ContainsExcludedTag ile AYNI mantık ama HANGİ etiketin eşleştiğini de döndürür (null = hiçbiri
    // eşleşmedi) — RetroAudit (WPF)'nin ROM İçe Aktar penceresindeki "Eşleşmeyenler" listesini
    // kategoriye göre (Beta/Unl/Proto vb.) gruplayıp toplu seçebilmesi için (bkz.
    // RomImportService.ScanFolder, RomImportViewModel.ExcludedTagGroups) — kullanıcı isteği: "adam
    // belki betaları tutmak isticek diğerlerini silmek isticek", yani TEK bir "kasıtlı dışlanan"
    // bayrağı yetmiyor, hangi etiket olduğu ayrı ayrı seçilebilmeli.
    public static string? TryGetExcludedTag(string text)
    {
        foreach (Match match in ParenGroupRegex().Matches(text))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length == 0)
                continue;
            var found = ExcludedParenKeywords.FirstOrDefault(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }

        foreach (Match match in BracketGroupRegex().Matches(text))
        {
            var content = match.Groups[1].Value.Trim();
            if (content.Length == 0)
                continue;

            var code = content.Split(' ', 2)[0];
            if (ExcludedBracketCodes.Contains(code))
                return code.ToUpperInvariant();
        }

        return null;
    }

    // Gerçek kullanıcı verisiyle doğrulandı (bkz. RomImportService.ResolveCandidate "Tier 3"):
    // No-Intro bir dönem revizyonları HARFLE ("Rev A", "Rev B") etiketlerken sonradan SAYIYLA
    // ("Rev 1", "Rev 2") etiketlemeye geçti — aynı revizyon, farklı isim. Eski bir ROM setinden
    // gelen "Rev A" ile kataloğun kullandığı "Rev 1" karşılaştırılabilsin diye ikisi de bu
    // metotla aynı kanonik forma indirgeniyor (A/B/C -> 1/2/3). Harf DEĞİLSE (zaten "Rev 1" gibi
    // sayısal ya da tanınmayan bir biçimse) olduğu gibi döner.
    public static string? NormalizeVersionLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var match = RevisionTokenRegex().Match(label.Trim());
        if (!match.Success)
            return label.Trim();

        var token = match.Groups[1].Value;
        if (token.Length == 1 && char.IsAsciiLetterUpper(char.ToUpperInvariant(token[0])))
            return $"Rev {char.ToUpperInvariant(token[0]) - 'A' + 1}";

        return $"Rev {token}";
    }
}

// ShouldExclude=true olan bir kayıt VersionResolver tarafından tamamen atlanır — GameVersions'a
// bile girmez. Hayatta kalan her kayıt tanım gereği "resmi/temiz" sayılır: sadece bölge ve
// (varsa) Rev etiketi taşır.
public record ParsedDatName(string CleanTitle, string[] Regions, string? VersionLabel, bool ShouldExclude);
