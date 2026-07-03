using System.Text.RegularExpressions;

namespace RetroAudit.Catalog.Metadata;

// DAT dosya adından türeyen platform adını (ör. "Nintendo - Nintendo Entertainment System")
// LaunchBox.Metadata.db'nin Platforms.Name sütunundaki gerçek isimle eşleştirmeye çalışır.
// LaunchBox'ın kendi adlandırması tutarlı değil: bazı üretici öneklerini düşürüyor
// ("Nintendo Entertainment System") bazılarını koruyor ("Microsoft Xbox 360", "Sony Playstation").
// Bu yüzden birkaç aday sırayla denenir (LaunchBoxMetadataReader.ResolvePlatform); hiçbiri
// LaunchBox'ta yoksa eşleşme yapılmaz — yanlış platformdan veri çekmektense hiç doldurmamak tercih edildi.
//
// v0.10 doğruluk analizinde bulunan gerçek hata: "Sega - Mega Drive - Genesis" gibi BİRDEN FAZLA
// " - " içeren isimlerde eski algoritma sadece ilk tireden sonrasını alıyordu ("Mega Drive -
// Genesis" -> LaunchBox'ta yok), oysa LaunchBox'ta "Sega Genesis" olarak kayıtlı. Aynı şekilde
// "(Digital)"/"(PSN)"/"(Title Updates)" gibi mağaza varyantı ekleri de eşleşmeyi tamamen
// engelliyordu (LaunchBox bunları ayrı platform saymıyor). Bu iki durum düzeltildi.
public static partial class PlatformNameMap
{
    // Genel algoritmanın yetmediği bilinen istisnalar (ör. LaunchBox'ın kullandığı isim tire
    // temizliğiyle bile üretilemeyecek kadar farklı: "Family Computer" -> "Famicom").
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sony - PlayStation"] = "Sony Playstation",
        ["Sony - PlayStation 2"] = "Sony Playstation 2",
        ["Sony - PlayStation 3"] = "Sony Playstation 3",
        ["Sony - PlayStation Portable"] = "Sony PSP",
        ["Sony - PlayStation Vita"] = "Sony Playstation Vita",
        ["NEC - PC Engine - TurboGrafx 16"] = "NEC TurboGrafx-16",
        ["NEC - PC Engine CD - TurboGrafx-CD"] = "NEC TurboGrafx-CD",
        ["Nintendo - Family Computer Disk System"] = "Nintendo Famicom Disk System",
    };

    // Mağaza/dağıtım varyantı ekleri: LaunchBox bunları ayrı bir platform olarak saymıyor,
    // hepsi ana donanımın platform kaydına dahil ediliyor.
    [GeneratedRegex(@"\s*\((Digital|PSN|Title Updates|UMD Video|UMD Music|PSX2PSP|eShop|Download Play)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StoreVariantSuffixRegex();

    public static IReadOnlyList<string> BuildCandidates(string datPlatformName)
    {
        var candidates = new List<string>();

        if (Overrides.TryGetValue(datPlatformName, out var overridden))
            candidates.Add(overridden);

        var withoutSuffix = StoreVariantSuffixRegex().Replace(datPlatformName, string.Empty);
        if (!string.Equals(withoutSuffix, datPlatformName, StringComparison.Ordinal))
        {
            if (Overrides.TryGetValue(withoutSuffix, out var overriddenBase))
                candidates.Add(overriddenBase);
            AddDashVariants(candidates, withoutSuffix);
            candidates.Add(withoutSuffix);
        }

        AddDashVariants(candidates, datPlatformName);
        candidates.Add(datPlatformName);

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // "A - B - C" biçimindeki (birden fazla tireli) isimlerde hangi parçanın LaunchBox'ın
    // beklediği isim olduğu önceden bilinemez: "Sega - Mega Drive - Genesis" için LaunchBox
    // sadece "Genesis" (üretici + son parça = "Sega Genesis"), "Sega - Master System - Mark III"
    // için ise "Master System" (üretici + orta parça = "Sega Master System") istiyor. Bu yüzden
    // tek bir sabit kural yerine, üretici adını her bir ara/son parçayla birleştiren tüm
    // makul kombinasyonlar aday olarak üretiliyor; hangisi LaunchBox'ta gerçekten varsa o kullanılır.
    private static void AddDashVariants(List<string> candidates, string name)
    {
        var segments = name.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return;

        var manufacturer = segments[0];

        candidates.Add(string.Join(' ', segments.Skip(1))); // "Manufacturer - A - B" -> "A B"
        candidates.Add(name.Replace(" - ", " ")); // tüm isim, tire -> boşluk

        for (var i = 1; i < segments.Length; i++)
        {
            candidates.Add(segments[i]); // tek başına bir ara/son parça (ör. "Genesis", "Master System")
            candidates.Add($"{manufacturer} {segments[i]}"); // ör. "Sega Genesis", "Sega Master System"
        }
    }
}
