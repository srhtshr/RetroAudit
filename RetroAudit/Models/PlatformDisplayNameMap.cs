namespace RetroAudit.Models;

// RetroAudit.db'deki DAT kaynaklı platform adları ("Nintendo - Nintendo 64",
// "NEC - PC Engine - TurboGrafx 16" gibi) üretici/parantez gürültüsünden arındırılmış, sol panelde
// gösterilecek sade isme çevrilir. Sadece sunum amaçlı — Games.Platform ve filtreleme hep
// Platform.Name (ham DAT adı) üzerinden yapılır, bu harita hiçbir eşleşme/join'de kullanılmaz.
// Anahtarlar RetroAudit.Catalog/Dat/PlatformAllowList.cs'teki canonical adlarla birebir aynı.
public static class PlatformDisplayNameMap
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nintendo - Nintendo Entertainment System"] = "Nintendo Entertainment System",
        ["Nintendo - Super Nintendo Entertainment System"] = "Super Nintendo Entertainment System",
        ["Nintendo - Nintendo 64"] = "Nintendo 64",
        ["Nintendo - GameCube"] = "GameCube",
        ["Nintendo - Wii"] = "Wii",
        ["Sega - Master System - Mark III"] = "Master System",
        ["Sega - Mega Drive - Genesis"] = "Genesis",
        ["Sega - Saturn"] = "Saturn",
        ["Sega - Dreamcast"] = "Dreamcast",
        ["Sony - PlayStation"] = "PlayStation",
        ["Sony - PlayStation 2"] = "PlayStation 2",
        ["Sony - PlayStation 3"] = "PlayStation 3",
        ["Microsoft - Xbox"] = "Xbox",
        ["Microsoft - Xbox 360"] = "Xbox 360",

        ["Nintendo - Game Boy"] = "Game Boy",
        ["Nintendo - Game Boy Color"] = "Game Boy Color",
        ["Nintendo - Game Boy Advance"] = "Game Boy Advance",
        ["Nintendo - Nintendo DS"] = "Nintendo DS",
        ["Nintendo - Nintendo 3DS"] = "Nintendo 3DS",
        ["Sony - PlayStation Portable"] = "PlayStation Portable",

        ["Commodore - 64"] = "Commodore 64",
        ["Commodore - Amiga"] = "Amiga",

        ["Atari - 2600"] = "Atari 2600",

        ["Sega - Game Gear"] = "Game Gear",
        ["Sega - Mega-CD - Sega CD"] = "Mega-CD - Sega CD",
        ["Sega - 32X"] = "32X",
        ["Atari - 7800"] = "Atari 7800",
        ["Atari - Jaguar"] = "Jaguar",
        ["Atari - Lynx"] = "Lynx",
        ["Bandai - WonderSwan"] = "WonderSwan",
        ["Bandai - WonderSwan Color"] = "WonderSwan Color",
        ["SNK - Neo Geo Pocket"] = "Neo Geo Pocket",
        ["SNK - Neo Geo Pocket Color"] = "Neo Geo Pocket Color",
        ["NEC - PC Engine - TurboGrafx 16"] = "PC Engine - TurboGrafx-16",
        ["NEC - PC-FX"] = "PC-FX",
        ["The 3DO Company - 3DO"] = "3DO",
        ["Philips - CD-i"] = "CD-i",
        ["Coleco - ColecoVision"] = "ColecoVision",
        ["Mattel - Intellivision"] = "Intellivision",
        ["Amstrad - CPC"] = "Amstrad CPC",
        ["Microsoft - MSX"] = "MSX",
        ["Microsoft - MSX2"] = "MSX2",
    };

    public static string Resolve(string rawPlatformName) =>
        Names.TryGetValue(rawPlatformName, out var displayName) ? displayName : rawPlatformName;
}
