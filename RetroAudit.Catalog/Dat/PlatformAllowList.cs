namespace RetroAudit.Catalog.Dat;

// Kullanıcı kararı: Builder artık sadece WPF sol panelindeki curated platform listesiyle
// (MockDataService.GetPlatforms — CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC + OTHERS altında
// sabit olarak listelenen platformlar) eşleşen DAT dosyalarını tarar. Bu, önceki "Builder her
// zaman zengin/eksiksiz katalog üretsin, UI karar versin" kararının kasıtlı olarak tersine
// çevrilmesidir — kullanıcı ileride yeni platform eklemeyi planlamadığı için taramayı sadece
// gösterilecek platformlarla sınırlamak build süresini kısaltıyor ve kapsamı sade tutuyor.
// Buradaki isimler PlatformMergeMap'in CANONICAL (birleştirme sonrası) adlarıdır — bir dağıtım
// varyantı (ör. "Nintendo - Nintendo 3DS (Digital)") kendi canonical'ı izin listesindeyse otomatik
// olarak taranır, DatSourceScanner bu kontrolü PlatformMergeMap.Resolve sonrasında yapar.
public static class PlatformAllowList
{
    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- CONSOLES ---
        "Nintendo - Nintendo Entertainment System",
        "Nintendo - Super Nintendo Entertainment System",
        "Nintendo - Nintendo 64",
        "Nintendo - GameCube",
        "Nintendo - Wii",
        "Sega - Master System - Mark III",
        "Sega - Mega Drive - Genesis",
        "Sega - Saturn",
        "Sega - Dreamcast",
        "Sony - PlayStation",
        "Sony - PlayStation 2",
        "Sony - PlayStation 3",
        "Microsoft - Xbox",
        "Microsoft - Xbox 360",

        // --- HANDHELDS ---
        "Nintendo - Game Boy",
        "Nintendo - Game Boy Color",
        "Nintendo - Game Boy Advance",
        "Nintendo - Nintendo DS",
        "Nintendo - Nintendo 3DS",
        "Sony - PlayStation Portable",

        // --- ARCADE --- (mame/fbneo kaynak kategorileri şu an varsayılan taramaya dahil değil;
        // bu isimler ileride --sources'a mame/fbneo eklenirse otomatik devreye girsin diye burada
        // tutuluyor, bugün hiçbir DAT dosyasıyla eşleşmiyorlar.)
        "MAME",
        "FBNeo",
        "SNK - Neo Geo",

        // --- COMPUTERS ---
        "Commodore - 64",
        "Commodore - Amiga",

        // --- CLASSIC ---
        "Atari - 2600",

        // --- OTHERS (curated listede var, varsayılan kapalı kategori ama Builder'da taranır) ---
        "Sega - Game Gear",
        "Sega - Mega-CD - Sega CD",
        "Sega - 32X",
        "Atari - 7800",
        "Atari - Jaguar",
        "Atari - Lynx",
        "Bandai - WonderSwan",
        "Bandai - WonderSwan Color",
        "SNK - Neo Geo Pocket",
        "SNK - Neo Geo Pocket Color",
        "NEC - PC Engine - TurboGrafx 16",
        "NEC - PC-FX",
        "The 3DO Company - 3DO",
        "Philips - CD-i",
        "Coleco - ColecoVision",
        "Mattel - Intellivision",
        "Amstrad - CPC",
        "Microsoft - MSX",
        "Microsoft - MSX2",

        // ZX Spectrum kasıtlı olarak burada YOK — kullanıcı kararıyla (17.381 oyunluk şişme,
        // %53 bilinmeyen bölge) programdan tamamen dışlandı, curated mock listede görünmesine
        // rağmen Builder'a hiç dahil edilmiyor.

        // "NEC - PC Engine CD - TurboGrafx-CD" de aynı şekilde kasıtlı olarak burada YOK —
        // kullanıcı kararıyla kaldırıldı (temel "PC Engine - TurboGrafx-16" ile karıştırılıyordu).
        // Zaten üretilmiş RetroAudit.db'lerdeki karşılığı ayrıca CatalogDatabaseService.
        // RemovedPlatformName ile okuma sırasında filtreleniyor.
    };

    public static bool IsAllowed(string canonicalPlatformName) => Allowed.Contains(canonicalPlatformName);
}
