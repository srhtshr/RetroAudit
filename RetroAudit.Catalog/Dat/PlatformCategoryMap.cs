namespace RetroAudit.Catalog.Dat;

// RetroAudit.db'ye yazılan her platformu, WPF sol panelinde kullanılan kategori taksonomisiyle
// (CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC/OTHERS) etiketler. Bu, bir UI/sunum bilgisidir —
// Builder hiçbir platformu bu yüzden atmaz; haritada olmayan her platform otomatik olarak
// "OTHERS" kategorisine düşer. Bir platformu popüler bir kategoriye taşımak (ya da OTHERS'a
// geri almak) sadece buradaki tek satırı değiştirmekle olur.
public static class PlatformCategoryMap
{
    public const string Consoles = "CONSOLES";
    public const string Handhelds = "HANDHELDS";
    public const string Arcade = "ARCADE";
    public const string Computers = "COMPUTERS";
    public const string Classic = "CLASSIC";
    public const string Others = "OTHERS";

    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- CONSOLES ---
        ["Nintendo - Nintendo Entertainment System"] = Consoles,
        ["Nintendo - Super Nintendo Entertainment System"] = Consoles,
        ["Nintendo - Nintendo 64"] = Consoles,
        ["Nintendo - GameCube"] = Consoles,
        ["Nintendo - Wii"] = Consoles,
        ["Sega - Master System - Mark III"] = Consoles,
        ["Sega - Mega Drive - Genesis"] = Consoles,
        ["Sega - Saturn"] = Consoles,
        ["Sega - Dreamcast"] = Consoles,
        ["Sony - PlayStation"] = Consoles,
        ["Sony - PlayStation 2"] = Consoles,
        ["Sony - PlayStation 3"] = Consoles,
        ["Microsoft - Xbox"] = Consoles,
        ["Microsoft - Xbox 360"] = Consoles,

        // --- HANDHELDS ---
        ["Nintendo - Game Boy"] = Handhelds,
        ["Nintendo - Game Boy Color"] = Handhelds,
        ["Nintendo - Game Boy Advance"] = Handhelds,
        ["Nintendo - Nintendo DS"] = Handhelds,
        ["Nintendo - Nintendo 3DS"] = Handhelds,
        ["Sony - PlayStation Portable"] = Handhelds,

        // --- ARCADE --- (MAME/FBNeo kaynakları varsayılan olarak henüz açık değil; Neo Geo
        // ileride bu kataloglardan besleneceği için burada ayrı bir DAT platformu olmayabilir,
        // ama isim eşleşirse doğru kategoriye düşsün diye kayıt tutuluyor.)
        ["MAME"] = Arcade,
        ["FBNeo"] = Arcade,
        ["SNK - Neo Geo"] = Arcade,
        ["Neo Geo"] = Arcade,

        // --- COMPUTERS ---
        ["Commodore - 64"] = Computers,
        ["Commodore - Amiga"] = Computers,

        // --- CLASSIC ---
        ["Atari - 2600"] = Classic,
    };

    public static string Resolve(string platformName) =>
        Categories.TryGetValue(platformName, out var category) ? category : Others;
}
