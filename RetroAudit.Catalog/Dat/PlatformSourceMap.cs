namespace RetroAudit.Catalog.Dat;

// Her platformun RetroAudit.db'de TEK bir referans kaynağı olur — aynı platform iki farklı
// kaynaktan (ör. hem No-Intro hem Redump) asla birleştirilmez (merge edilmez). Bu sözlük sadece
// birden fazla kaynakta AYNI dosya adıyla bulunan (yani gerçekten çakışan) platformlar için
// gereklidir; DatSourceScanner çakışmayan platformları otomatik olarak bulundukları tek kaynağa
// atar. Çakışan ama burada tanımlı olmayan bir platformla karşılaşılırsa (ör. ileride yeni bir
// DAT seti eklenirse), Scanner sabit bir öncelik sırasına (no-intro > redump > tosec > mame >
// fbneo) düşer ve bunu BuildReport'ta "belirsiz, otomatik seçildi" olarak açıkça loglar — asla
// sessizce iki kaynağı karıştırmaz.
public static class PlatformSourceMap
{
    public static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Optik disk tabanlı: Redump tercih edilir (No-Intro'da da varsa bile) ---
        ["Microsoft - Xbox 360"] = "redump",
        ["Microsoft - Xbox"] = "redump",
        ["Sony - PlayStation Portable"] = "redump",

        // --- Ev bilgisayarları: TOSEC tercih edilir ---
        ["Commodore - Amiga"] = "tosec",
        ["Atari - ST"] = "tosec",
        ["Atari - 8-bit Family"] = "tosec",
        ["Sharp - X1"] = "tosec",
        ["Sharp - X68000"] = "tosec",
        ["NEC - PC-98"] = "tosec",

        // --- Konsol/kartuş: TOSEC'te de bulunsa No-Intro tercih edilir (daha kapsamlı/güncel) ---
        ["Nintendo - Nintendo Entertainment System"] = "no-intro",
        ["Nintendo - Super Nintendo Entertainment System"] = "no-intro",
        ["Nintendo - Game Boy"] = "no-intro",
        ["Nintendo - Game Boy Color"] = "no-intro",
        ["Nintendo - Game Boy Advance"] = "no-intro",
        ["Sega - Mega Drive - Genesis"] = "no-intro",
        ["Sega - Master System - Mark III"] = "no-intro",
        ["Sega - Game Gear"] = "no-intro",
        ["Sega - 32X"] = "no-intro",
        ["Sega - SG-1000"] = "no-intro",
        ["Sega - PICO"] = "no-intro",
        ["Mattel - Intellivision"] = "no-intro",
        ["Atari - 2600"] = "no-intro",
        ["Atari - 5200"] = "no-intro",
        ["Atari - 7800"] = "no-intro",

        // NOT: Kullanıcı "Commodore 64 -> TOSEC" bekliyordu ama bu makinedeki TOSEC seti Commodore 64
        // dat'ı içermiyor (sadece Amiga ve PET var) — tek seçenek No-Intro olduğu için burada override
        // yok, çakışmasız otomatik tespit zaten No-Intro'yu seçecek. Gerçek bir TOSEC C64 dat'ı eklenirse
        // bu davranışı değiştirmek için buraya ["Commodore - 64"] = "tosec" eklemek yeterli olur.
    };

    // Birden fazla kaynakta bulunan ama Overrides'ta tanımsız bir platformla karşılaşılırsa
    // kullanılacak sabit öncelik sırası. Sessiz bir varsayım değil — Scanner bunu her zaman loglar.
    public static readonly string[] FallbackPriorityOrder =
    {
        "no-intro", "redump", "tosec", "mame", "fbneo-split", "fbneo-member",
    };
}
