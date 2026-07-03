using RetroAudit.Models;

namespace RetroAudit.Services;

// UI prototipi aşamasında gerçek bir veritabanı/XML tarama olmadığı için tüm platform ve
// oyun verisi burada elle tanımlanır. İleride bu sınıfın yerini gerçek bir tarama/okuma
// servisi alacak, ancak döndürdüğü Platform/Game tipleri aynı kalacağı için ViewModel'lerde
// değişiklik gerekmeyecek.
public static class MockDataService
{
    // Sol paneldeki platform listesini doldurur.
    // İlk 18 kayıt (All Platforms hariç) "favori" platformlar: kullanıcının işe başlarken
    // kullandığı asıl set, varsayılan olarak görünür ve favori yıldızlı. Onları izleyen kayıtlar
    // kullanıcının verdiği geniş platform listesinden gelen ek platformlar — varsayılan olarak
    // gizli (IsVisible=false), sol paneldeki "+" popup'ından açılabilirler. Oyun sayıları bu
    // ek platformlar için henüz mock veri girilmediğinden 0.
    public static List<Platform> GetPlatforms()
    {
        var platforms = new List<Platform>
        {
            new() { Name = "All Platforms", IconGlyph = "ALL", GameCount = 24803, IsAllPlatforms = true },
            new() { Name = "MAME", IconGlyph = "MAME", GameCount = 1406, IsFavorite = true },
            new() { Name = "NeoGeo", IconGlyph = "NEO", GameCount = 148, IsFavorite = true },
            new() { Name = "Nintendo", IconGlyph = "NES", GameCount = 1406, IsFavorite = true },
            new() { Name = "Super Nintendo", IconGlyph = "SNES", GameCount = 2426, IsFavorite = true },
            new() { Name = "PlayStation", IconGlyph = "PS1", GameCount = 4717, IsFavorite = true },
            new() { Name = "PlayStation 2", IconGlyph = "PS2", GameCount = 1575, IsFavorite = true },
            new() { Name = "Sega Genesis", IconGlyph = "GEN", GameCount = 901, IsFavorite = true },
            new() { Name = "Master System", IconGlyph = "SMS", GameCount = 331, IsFavorite = true },
            new() { Name = "PSP", IconGlyph = "PSP", GameCount = 1101, IsFavorite = true },
            new() { Name = "PlayStation 3", IconGlyph = "PS3", GameCount = 2696, IsFavorite = true },
            new() { Name = "Xbox", IconGlyph = "XB", GameCount = 987, IsFavorite = true },
            new() { Name = "Xbox 360", IconGlyph = "X360", GameCount = 1503, IsFavorite = true },
            new() { Name = "Dreamcast", IconGlyph = "DC", GameCount = 612, IsFavorite = true },
            new() { Name = "Game Gear", IconGlyph = "GG", GameCount = 388, IsFavorite = true },
            new() { Name = "Commodore 64", IconGlyph = "C64", GameCount = 2210, IsFavorite = true },
            new() { Name = "Amiga", IconGlyph = "AMI", GameCount = 1740, IsFavorite = true },
            new() { Name = "Atari", IconGlyph = "ATR", GameCount = 655, IsFavorite = true },
        };

        // Kullanıcının verdiği geniş platform listesi (arcade tarafı MAME/FBNeo altında toplandığı,
        // CPS1-3/Naomi ayrı kategori açılmadığı için burada tekrarlanmıyor).
        (string Name, string Glyph)[] extraPlatforms =
        {
            ("Nintendo 64", "N64"),
            ("Nintendo GameCube", "NGC"),
            ("Nintendo Wii", "WII"),
            ("Nintendo Wii U", "WIIU"),
            ("Nintendo Switch", "SWCH"),
            ("Game Boy", "GB"),
            ("Game Boy Color", "GBC"),
            ("Game Boy Advance", "GBA"),
            ("Nintendo DS", "NDS"),
            ("Nintendo 3DS", "3DS"),
            ("Sega CD", "SCD"),
            ("Sega 32X", "32X"),
            ("Sega Saturn", "SAT"),
            ("Sony PlayStation Vita", "VITA"),
            ("Microsoft Xbox One", "XB1"),
            ("Atari 5200", "5200"),
            ("Atari 7800", "7800"),
            ("Atari Jaguar", "JAG"),
            ("Atari Lynx", "LYNX"),
            ("NEC PC Engine / TurboGrafx-16", "PCE"),
            ("NEC PC Engine CD", "PCECD"),
            ("SNK Neo Geo CD", "NGCD"),
            ("SNK Neo Geo Pocket", "NGP"),
            ("SNK Neo Geo Pocket Color", "NGPC"),
            ("Bilgisayar (MS-DOS)", "DOS"),
            ("Bilgisayar (Windows)", "WIN"),
            ("ZX Spectrum", "ZXS"),
        };

        foreach (var (name, glyph) in extraPlatforms)
            platforms.Add(new Platform { Name = name, IconGlyph = glyph, GameCount = 0, IsVisible = false });

        return platforms;
    }

    // Orta paneldeki DataGrid'i doldurur. Nintendo (NES) listesi referans ekran görüntüsündeki
    // "K" harfiyle başlayan başlıklarla birebir eşleşecek şekilde elle girildi; diğer platformlar
    // için ise sadece çeşitlilik göstermesi amacıyla birkaç tanınmış başlık eklendi.
    public static List<Game> GetGames()
    {
        var games = new List<Game>();

        (string Title, string Genres, bool Ok, bool Box, bool Bg, bool Ss)[] nes =
        {
            ("Kid Niki: Radical Ninja", "Action, Platform", false, false, true, true),
            ("Kirby's Son in Fantasia", "Action, Platform", false, false, true, true),
            ("Kidou Senshi Z Gundam: Hot Scra...", "Shooter", false, false, true, true),
            ("Kidou Senshi Z Gundam: Hot Scra... (Final)", "Shooter", false, false, true, true),
            ("King Kong 2: Ikari no Megaton Punch", "Action", false, false, true, true),
            ("King Nothing", "Puzzle", false, false, true, true),
            ("King of Kings", "Strategy", true, false, true, true),
            ("King's Knight", "Shooter", true, false, true, true),
            ("Kings of the Beach: Professional...", "Sports", true, false, true, true),
            ("King's Quest V", "Adventure", true, false, true, true),
            ("Kirby's Adventure", "Platform", true, false, true, true),
            ("Klonoa Dolphaka", "Action, Platform", true, false, true, true),
            ("Kiwi Kraze: A Bird-Brained Adventure", "Platform", true, false, true, true),
            ("Klash Ball", "Sports", true, false, true, true),
            ("Klax", "Puzzle", true, false, true, true),
            ("Knight Rider", "Racing, Shooter", true, false, true, true),
            ("Konami Hyper Soccer", "Sports", true, false, true, true),
            ("Konami Wai Wai World", "Action, Platform", true, false, true, true),
            ("Kouryuu Densetsu Villgust Gaiden", "Role-Playing", true, false, true, true),
            ("Knuty's Fun House", "Platform", true, false, true, true),
            ("Kajaku Oo", "Adventure", true, false, true, true),
            ("Kajaku Oo II", "Adventure", true, false, true, true),
            ("Kung Fu", "Beat 'em Up", true, false, true, true),
            ("Kung-Fu Heroes", "Beat 'em Up", true, false, true, true),
            ("Kunio-kun no Nekketsu Soccer Le...", "Fighting Sports", true, false, true, true),
            ("Kurogane Hiroshi no Yosou Daisuki...", "Sports", true, false, true, true),
            ("Kyattp Nyden Teyandee", "Action, Platform", true, false, true, true),
            ("Konjishiro II", "Action Adventure", true, false, true, true),
        };

        foreach (var g in nes)
        {
            games.Add(new Game
            {
                Title = g.Title,
                Platform = "Nintendo",
                Version = "Released",
                Genres = g.Genres,
                File = g.Title.ToLowerInvariant(),
                StatusOk = g.Ok,
                HasBox = g.Box,
                HasBackground = g.Bg,
                HasScreenshot = g.Ss,
                ReleaseYear = 1989,
                Developer = "Various",
                Publisher = "Various",
                GameMode = "Single Player",
                MaxPlayers = 1,
                Description = "A classic title from the NES library, catalogued as part of the Nintendo platform sweep.",
            });
        }

        // Referans ekran görüntüsünde varsayılan seçili oyun buydu; MainViewModel de açılışta
        // bunu SelectedGame olarak seçiyor, böylece sağ detay paneli baştan dolu görünüyor.
        games.Add(new Game
        {
            Title = "A Week of Garfield",
            Platform = "Nintendo",
            Version = "Released",
            Genres = "Action, Platform",
            File = "a week of garfield",
            StatusOk = true,
            HasBox = true,
            HasBackground = true,
            HasScreenshot = true,
            CoverImagePath = "",
            ScreenshotImagePath = "",
            ReleaseYear = 1989,
            Developer = "MARS",
            Publisher = "Towa Chiki",
            GameMode = "Single Player",
            MaxPlayers = 1,
            Description = "Garfield, the lovable fat orange cat, has gotten himself into a spot of trouble. His pal and center of abuse, the yellow bird Nermal, has been kidnapped.",
        });

        // Diğer platformlar için sadece filtreleme/görsel çeşitlilik amaçlı birkaç örnek başlık.
        (string Title, string Platform, string Genres)[] others =
        {
            ("Chrono Trigger", "Super Nintendo", "Role-Playing"),
            ("Gran Turismo", "PlayStation", "Racing"),
            ("Metal Gear Solid", "PlayStation", "Action, Stealth"),
            ("God of War II", "PlayStation 2", "Action Adventure"),
            ("Shadow of the Colossus", "PlayStation 2", "Action Adventure"),
            ("Sonic the Hedgehog 2", "Sega Genesis", "Platform"),
            ("Streets of Rage 2", "Sega Genesis", "Beat 'em Up"),
            ("Alex Kidd in Miracle World", "Master System", "Platform"),
            ("Patapon", "PSP", "Rhythm, Strategy"),
            ("The Last of Us", "PlayStation 3", "Action Adventure"),
            ("Halo: Combat Evolved", "Xbox", "Shooter"),
            ("Gears of War", "Xbox 360", "Shooter"),
            ("Shenmue", "Dreamcast", "Action Adventure"),
            ("Sonic Chaos", "Game Gear", "Platform"),
            ("The Last Ninja", "Commodore 64", "Action Adventure"),
            ("Cannon Fodder", "Amiga", "Strategy"),
            ("Missile Command", "Atari", "Shooter"),
        };

        foreach (var g in others)
        {
            games.Add(new Game
            {
                Title = g.Title,
                Platform = g.Platform,
                Version = "Released",
                Genres = g.Genres,
                File = g.Title.ToLowerInvariant(),
                StatusOk = true,
                HasBox = true,
                HasBackground = true,
                HasScreenshot = true,
                ReleaseYear = 1995,
                Developer = "Various",
                Publisher = "Various",
                GameMode = "Single Player",
                MaxPlayers = 1,
                Description = $"Placeholder catalogue entry for {g.Title} on {g.Platform}.",
            });
        }

        return games;
    }
}
