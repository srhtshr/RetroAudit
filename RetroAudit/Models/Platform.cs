namespace RetroAudit.Models;

// Sol paneldeki platform listesinde gösterilen tek bir satırı temsil eder
// (ör. "Nintendo Entertainment System", "PlayStation 2"). "All Platforms" satırı da bu tipten,
// IsAllPlatforms bayrağıyla ayırt edilir.
public class Platform
{
    // RetroAudit.db'deki gerçek (DAT kaynaklı) platform adı — "Nintendo - Nintendo 64" gibi.
    // Games.Platform ile eşleştirme/filtreleme burada, ASLA DisplayName ile yapılmaz.
    public string Name { get; set; } = string.Empty;

    // Sol panelde gösterilen sade isim (ör. "Nintendo 64") — bkz. PlatformDisplayNameMap.
    // Eşlemede yoksa Name'e düşer (CatalogDatabaseService.GetPlatforms bunu ayarlar).
    public string DisplayName { get; set; } = string.Empty;

    // Gerçek logo görseli yerine kullanılan kısa metin placeholder'ı (ör. "NES", "PS2"). Sol
    // paneldeki platform listesinde hâlâ bu kullanılıyor — gerçek logolar bunun yerine DataGrid'in
    // Platform sütununda gösteriliyor (bkz. Game.PlatformLogoPath, kullanıcı kararı: sol panelde
    // logo çirkin duruyordu).
    public string IconGlyph { get; set; } = string.Empty;

    // Listede platform adının yanında gösterilen oyun sayısı rozeti.
    public int GameCount { get; set; }

    // "All Platforms" satırını normal platformlardan ayırt eder; true ise
    // MainViewModel.ApplyFilter platform bazlı filtrelemeyi atlar.
    public bool IsAllPlatforms { get; set; }

    // Sol paneldeki kategori başlığı (ör. "CONSOLES", "HANDHELDS", "OTHERS"). Bir platformu bir
    // kategoriden diğerine taşımak (ör. "OTHERS" -> "CONSOLES") sadece bu alanı değiştirmekle
    // olur — MainViewModel.RebuildPlatformListItems sıralamayı/gruplamayı bu alana göre kurar.
    public string Category { get; set; } = string.Empty;
}
