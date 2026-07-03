namespace RetroAudit.Models;

// Sol paneldeki platform listesinde gösterilen tek bir satırı temsil eder
// (ör. "Nintendo", "PlayStation 2"). "All Platforms" satırı da bu tipten,
// IsAllPlatforms bayrağıyla ayırt edilir.
public class Platform
{
    public string Name { get; set; } = string.Empty;

    // Gerçek logo görseli yerine kullanılan kısa metin placeholder'ı (ör. "NES", "PS2").
    // İleride gerçek platform logosu eklendiğinde bu alanın yerini bir ImageSource alacak.
    public string IconGlyph { get; set; } = string.Empty;

    // Listede platform adının yanında gösterilen oyun sayısı rozeti.
    public int GameCount { get; set; }

    // Favori yıldızı gösterilsin mi (şu an sadece görsel; favorileme mantığı henüz yok).
    public bool IsFavorite { get; set; }

    // "All Platforms" satırını normal platformlardan ayırt eder; true ise
    // MainViewModel.ApplyFilter platform bazlı filtrelemeyi atlar.
    public bool IsAllPlatforms { get; set; }
}
