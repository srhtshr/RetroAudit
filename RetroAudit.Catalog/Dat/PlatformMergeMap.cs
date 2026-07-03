namespace RetroAudit.Catalog.Dat;

// Bazı DAT dosyaları, aslında aynı fiziksel platformun farklı dağıtım biçimlerini (dijital
// mağaza / özel donanım revizyonu / indirilebilir demo kanalı) ayrı bir "platform" gibi
// isimlendirir (ör. "Nintendo - Nintendo 3DS (Digital)"). Kullanıcı kararı: bunlar RetroAudit.db'de
// ayrı bir Platforms satırı OLUŞTURMASIN — kaynak dosyaları hâlâ ayrı ayrı taranır (veri kaybı yok),
// ama üretilen oyunlar taban platformun altında tek bir katalogda toplanır. Haritada olmayan her
// platform adı kendisiyle eşlenir (değişmeden kalır) — DatSourceScanner.Scan bunu entry.PlatformName
// ataması sırasında uygular.
public static class PlatformMergeMap
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nintendo - Nintendo 3DS (Digital)"] = "Nintendo - Nintendo 3DS",
        ["Nintendo - New Nintendo 3DS"] = "Nintendo - Nintendo 3DS",
        ["Nintendo - New Nintendo 3DS (Digital)"] = "Nintendo - Nintendo 3DS",
        ["Nintendo - Nintendo DS (Download Play)"] = "Nintendo - Nintendo DS",
        ["Nintendo - Wii (Digital)"] = "Nintendo - Wii",
        ["Sony - PlayStation 3 (PSN)"] = "Sony - PlayStation 3",
        ["Sony - PlayStation Portable (PSN)"] = "Sony - PlayStation Portable",
        ["Sony - PlayStation Vita (PSN)"] = "Sony - PlayStation Vita",
    };

    public static string Resolve(string rawPlatformName) =>
        Aliases.TryGetValue(rawPlatformName, out var canonical) ? canonical : rawPlatformName;
}
