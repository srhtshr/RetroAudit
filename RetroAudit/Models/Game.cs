namespace RetroAudit.Models;

// Kütüphanedeki tek bir oyunu temsil eden veri modeli.
// Bu aşamada gerçek bir veritabanından değil, Services/MockDataService içindeki
// placeholder verilerden geliyor; alan adları ileride XML/SQLite entegrasyonunda
// birebir aynı kalacak şekilde tasarlandı.
public class Game
{
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Version { get; set; } = "Released"; // "Released" veya "Junk" — toolbar'daki filtre butonlarıyla eşleşir
    public string Genres { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty; // Rom dosyasının adı (yol değil, sadece dosya adı)

    // Orta paneldeki DataGrid'de mor tik / kırmızı çarpı ikonunu belirleyen genel durum bayrağı.
    public bool StatusOk { get; set; }

    // Box/BG/SS kolonlarındaki nokta göstergelerini besleyen medya varlık bayrakları.
    public bool HasBox { get; set; }
    public bool HasBackground { get; set; }
    public bool HasScreenshot { get; set; }

    public string CoverImagePath { get; set; } = string.Empty;
    public string ScreenshotImagePath { get; set; } = string.Empty;

    // Sağ detay panelinde gösterilen ek bilgiler.
    public int ReleaseYear { get; set; }
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Single Player";
    public int MaxPlayers { get; set; } = 1;
    public string Description { get; set; } = string.Empty;
}
