using RetroAudit.Models;

namespace RetroAudit.Services;

// UI prototipi aşamasında gerçek bir veritabanı/XML tarama olmadığı için tüm platform ve
// oyun verisi burada elle tanımlanır. İleride bu sınıfın yerini gerçek bir tarama/okuma
// servisi alacak, ancak döndürdüğü Platform/Game tipleri aynı kalacağı için ViewModel'lerde
// değişiklik gerekmeyecek.
public static class MockDataService
{
    // Kategori sabitleri — MainViewModel.RebuildPlatformListItems de aynı adları kullanır.
    public const string CategoryConsoles = "CONSOLES";
    public const string CategoryHandhelds = "HANDHELDS";
    public const string CategoryArcade = "ARCADE";
    public const string CategoryComputers = "COMPUTERS";
    public const string CategoryClassic = "CLASSIC";
    public const string CategoryOthers = "OTHERS";

    // Sol paneldeki platform listesini doldurur. Kullanıcının verdiği kategorize edilmiş
    // taksonomiye birebir uyar: CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC "popüler" kategoriler
    // olarak varsayılan açık gelir, OTHERS ise MainViewModel'de varsayılan kapalı bir kategori
    // olarak ele alınır. Bu, Builder'ın ürettiği RetroAudit.db'deki platform sayısından bağımsız
    // bir UI organizasyonudur — Builder/veritabanı tarafında hiçbir platform silinmiyor.
    public static List<Platform> GetPlatforms()
    {
        var platforms = new List<Platform>
        {
            new() { Name = "All Platforms", IconGlyph = "ALL", GameCount = 24803, IsAllPlatforms = true },
        };

        (string Name, string Glyph, int Count, string Category)[] catalog =
        {
            // --- CONSOLES ---
            ("Nintendo Entertainment System", "NES", 1406, CategoryConsoles),
            ("Super Nintendo Entertainment System", "SNES", 2426, CategoryConsoles),
            ("Nintendo 64", "N64", 601, CategoryConsoles),
            ("Nintendo GameCube", "NGC", 1076, CategoryConsoles),
            ("Nintendo Wii", "WII", 2153, CategoryConsoles),

            ("Sega Master System", "SMS", 331, CategoryConsoles),
            ("Sega Genesis / Mega Drive", "GEN", 901, CategoryConsoles),
            ("Sega Saturn", "SAT", 1377, CategoryConsoles),
            ("Sega Dreamcast", "DC", 612, CategoryConsoles),

            ("PlayStation", "PS1", 6358, CategoryConsoles),
            ("PlayStation 2", "PS2", 6619, CategoryConsoles),
            ("PlayStation 3", "PS3", 2696, CategoryConsoles),

            ("Xbox", "XB", 987, CategoryConsoles),
            ("Xbox 360", "X360", 1959, CategoryConsoles),

            // --- HANDHELDS ---
            ("Game Boy", "GB", 1659, CategoryHandhelds),
            ("Game Boy Color", "GBC", 1837, CategoryHandhelds),
            ("Game Boy Advance", "GBA", 2315, CategoryHandhelds),
            ("Nintendo DS", "NDS", 5272, CategoryHandhelds),
            ("Nintendo 3DS", "3DS", 1275, CategoryHandhelds),
            ("PlayStation Portable (PSP)", "PSP", 2250, CategoryHandhelds),

            // --- ARCADE ---
            ("MAME", "MAME", 1406, CategoryArcade),
            ("FBNeo", "FBN", 850, CategoryArcade),
            // Neo Geo kullanıcıya ayrı bir platform olarak görünür, ama gerçek veri katmanında
            // (Stage B) MAME/FBNeo kataloğundan besleneceği için burada ayrı bir DAT/Builder
            // platformu değil, sadece bir sunum/filtre satırıdır.
            ("Neo Geo", "NEO", 148, CategoryArcade),

            // --- COMPUTERS ---
            ("Commodore 64", "C64", 2706, CategoryComputers),
            ("Amiga", "AMI", 6705, CategoryComputers),

            // --- CLASSIC ---
            ("Atari 2600", "A2600", 663, CategoryClassic),

            // --- OTHERS (varsayılan kapalı) ---
            ("Sega Game Gear", "GG", 498, CategoryOthers),
            ("Sega CD", "SCD", 214, CategoryOthers),
            ("Sega 32X", "32X", 49, CategoryOthers),

            ("Atari 7800", "A7800", 140, CategoryOthers),
            ("Atari Jaguar", "JAG", 187, CategoryOthers),
            ("Atari Lynx", "LYNX", 236, CategoryOthers),

            ("WonderSwan", "WS", 202, CategoryOthers),
            ("WonderSwan Color", "WSC", 202, CategoryOthers),

            ("Neo Geo Pocket", "NGP", 10, CategoryOthers),
            ("Neo Geo Pocket Color", "NGPC", 85, CategoryOthers),

            ("PC Engine / TurboGrafx-16", "PCE", 361, CategoryOthers),
            ("PC Engine CD", "PCECD", 200, CategoryOthers),
            ("PC-FX", "PCFX", 60, CategoryOthers),

            ("3DO", "3DO", 220, CategoryOthers),
            ("CD-i", "CDI", 190, CategoryOthers),

            ("ColecoVision", "COL", 165, CategoryOthers),
            ("Intellivision", "INTV", 230, CategoryOthers),

            ("Amstrad CPC", "CPC", 900, CategoryOthers),

            ("MSX", "MSX", 576, CategoryOthers),
            ("MSX2", "MSX2", 154, CategoryOthers),
        };

        platforms.AddRange(catalog.Select(p => new Platform
        {
            Name = p.Name,
            IconGlyph = p.Glyph,
            GameCount = p.Count,
            Category = p.Category,
        }));

        return platforms;
    }

    // Orta paneldeki DataGrid'i besler. İlk UI iskeleti fazında burada elle girilmiş placeholder
    // oyunlar vardı; RetroAudit.db entegrasyonuna (Stage B) geçmeden önce kasıtlı olarak boşaltıldı
    // — oyun listesi artık gerçek veri bağlanana kadar boş gelecek. Platform listesi (GetPlatforms)
    // bundan etkilenmiyor, mock platform taksonomisi olduğu gibi kalıyor.
    public static List<Game> GetGames() => new();
}
