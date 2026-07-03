namespace RetroAudit.Catalog.Metadata;

// DAT dosya adından türeyen platform adını (ör. "Nintendo - Nintendo Entertainment System")
// LaunchBox.Metadata.db'nin Platforms.Name sütunundaki gerçek isimle eşleştirmeye çalışır.
// LaunchBox'ın kendi adlandırması tutarlı değil: bazı üretici öneklerini düşürüyor
// ("Nintendo Entertainment System") bazılarını koruyor ("Microsoft Xbox 360", "Sony Playstation").
// Bu yüzden birkaç aday sırayla denenir (LaunchBoxMetadataReader.ResolvePlatform); hiçbiri
// LaunchBox'ta yoksa eşleşme yapılmaz — yanlış platformdan veri çekmektense hiç doldurmamak tercih edildi.
public static class PlatformNameMap
{
    // Genel algoritmanın (önek düşürme / tire->boşluk) yetmediği bilinen istisnalar.
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sony - PlayStation"] = "Sony Playstation",
        ["Sony - PlayStation 2"] = "Sony Playstation 2",
        ["Sony - PlayStation 3"] = "Sony Playstation 3",
        ["Sony - PlayStation Portable"] = "Sony PSP",
        ["Sony - PlayStation Vita"] = "Sony Playstation Vita",
    };

    public static IReadOnlyList<string> BuildCandidates(string datPlatformName)
    {
        var candidates = new List<string>();

        if (Overrides.TryGetValue(datPlatformName, out var overridden))
            candidates.Add(overridden);

        var dashIndex = datPlatformName.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            candidates.Add(datPlatformName[(dashIndex + 3)..]); // "Nintendo - Nintendo Entertainment System" -> "Nintendo Entertainment System"
            candidates.Add(datPlatformName.Replace(" - ", " ")); // "Microsoft - Xbox 360" -> "Microsoft Xbox 360"
        }

        candidates.Add(datPlatformName);

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
