namespace RetroAudit.Models;

// RetroAudit.db'deki ham platform adını, o platformun arşivleme standardına karşılık gelen
// arama etiketine çevirir (ör. "Nintendo - Nintendo Entertainment System" -> "No-Intro").
// Sadece arama sorgusunu daha isabetli hale getirmek için — RomSearchWindow'daki Google
// aramasına "+ No-Intro" gibi bir ek terim ekler, başka hiçbir işlevi yok.
public static class RomSearchTagMap
{
    private static readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Kartuş/HuCard tabanlı (No-Intro) ---
        ["Nintendo - Nintendo Entertainment System"] = "No-Intro",
        ["Nintendo - Super Nintendo Entertainment System"] = "No-Intro",
        ["Nintendo - Nintendo 64"] = "No-Intro",
        ["Nintendo - Game Boy"] = "No-Intro",
        ["Nintendo - Game Boy Color"] = "No-Intro",
        ["Nintendo - Game Boy Advance"] = "No-Intro",
        ["Nintendo - Nintendo DS"] = "No-Intro",
        ["Nintendo - Nintendo 3DS"] = "No-Intro",
        ["Sega - Master System - Mark III"] = "No-Intro",
        ["Sega - Mega Drive - Genesis"] = "No-Intro",
        ["Sega - Game Gear"] = "No-Intro",
        ["Sega - 32X"] = "No-Intro",
        ["Atari - 2600"] = "No-Intro",
        ["Atari - 7800"] = "No-Intro",
        ["Atari - Jaguar"] = "No-Intro",
        ["Atari - Lynx"] = "No-Intro",
        ["Bandai - WonderSwan"] = "No-Intro",
        ["Bandai - WonderSwan Color"] = "No-Intro",
        ["SNK - Neo Geo Pocket"] = "No-Intro",
        ["SNK - Neo Geo Pocket Color"] = "No-Intro",
        ["NEC - PC Engine - TurboGrafx 16"] = "No-Intro",
        ["Coleco - ColecoVision"] = "No-Intro",
        ["Mattel - Intellivision"] = "No-Intro",

        // --- Optik disk tabanlı (Redump) ---
        ["Nintendo - GameCube"] = "Redump",
        ["Nintendo - Wii"] = "Redump",
        ["Sega - Saturn"] = "Redump",
        ["Sega - Dreamcast"] = "Redump",
        ["Sega - Mega-CD - Sega CD"] = "Redump",
        ["Sony - PlayStation"] = "Redump",
        ["Sony - PlayStation 2"] = "Redump",
        ["Sony - PlayStation 3"] = "Redump",
        ["Sony - PlayStation Portable"] = "Redump",
        ["Microsoft - Xbox"] = "Redump",
        ["Microsoft - Xbox 360"] = "Redump",
        ["NEC - PC-FX"] = "Redump",
        ["The 3DO Company - 3DO"] = "Redump",
        ["Philips - CD-i"] = "Redump",

        // --- Arcade (MAME) ---
        ["MAME"] = "MAME",
        ["FBNeo"] = "MAME",
        ["SNK - Neo Geo"] = "MAME",

        // --- Eski bilgisayarlar (TOSEC) ---
        ["Commodore - 64"] = "TOSEC",
        ["Commodore - Amiga"] = "TOSEC",
        ["Amstrad - CPC"] = "TOSEC",
        ["Microsoft - MSX"] = "TOSEC",
        ["Microsoft - MSX2"] = "TOSEC",
    };

    // Haritada olmayan bir platform için boş döner — arama sorgusuna hiçbir ek etiket eklenmez,
    // uydurma bir varsayılan (ör. hep "No-Intro") göstermek yanıltıcı olurdu.
    public static string Resolve(string rawPlatformName) => Tags.GetValueOrDefault(rawPlatformName, string.Empty);
}
