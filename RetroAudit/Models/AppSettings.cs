namespace RetroAudit.Models;

// Oyun satırına sağ tıklandığında açılan kapsül menünün görünüm modu — Ayarlar > Arayüz'den
// değiştirilir (bkz. SettingsViewModel, GameContextMenu.xaml).
public enum ContextMenuDisplayMode
{
    IconOnly,
    IconAndText,
}

// Tek bir platform için emülatör başlatma ayarları.
// PreferredCore/AlternativeCore, hangi emülatör(ler)in önerildiğini kaydeder (ör. "Mesen" / "Snes9x");
// ExecutablePath ise kullanıcının kendi makinesindeki gerçek .exe yolu — core adı ile karışmasın diye
// ayrı tutuldu, çünkü core seçimi platform bazlı sabit bir öneri, exe yolu ise kişiye/kuruluma özel.
// "%ROM%" gibi bir yer tutucu, gerçek başlatma mantığı yazıldığında rom dosya yolu ile değiştirilecek.
public class EmulatorConfig
{
    public string PlatformName { get; set; } = string.Empty;
    public string PreferredCore { get; set; } = string.Empty;
    public string AlternativeCore { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Parameters { get; set; } = "%ROM%";
}

// Üst araç çubuğundaki bir komutun (ör. "Rescan", "Apply Resolver") ayarlar panelindeki karşılığı.
// Amaç: komutların ne yaptığını kod okumadan anlamak ve (varsa) parametrelerini kod değiştirmeden
// düzenleyebilmek. Category alanı, panelde komutları tek uzun liste yerine gruplu göstermek için var.
public class CommandSetting
{
    public string CommandName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // ör. "Veri Yönetimi", "Medya", "Organizasyon", "Oynatma"
    public string Description { get; set; } = string.Empty; // "bu komut ne yapar" açıklaması
    public string Parameter { get; set; } = string.Empty; // komutun kullanacağı serbest metin parametresi
}

// Uygulamanın tüm kalıcı ayarlarını tutan kök nesne.
// Export/Import Config butonları bu sınıfı doğrudan JSON'a serileştirip geri okur.
public class AppSettings
{
    // RetroAudit'in kendi veri kök dizini (ROM/medya taraması ve RetroAudit.db burada yaşayacak).
    public string RetroAuditDataPath { get; set; } = string.Empty;

    // Platform başına bir emülatör kaydı.
    public List<EmulatorConfig> Emulators { get; set; } = new();

    // Aynı oyunun birden fazla bölge sürümü bulunduğunda hangisinin tercih edileceğini belirleyen sıra.
    // Liste başındaki bölge en yüksek önceliğe sahiptir (varsayılan: USA > EU > JP).
    public List<string> RegionPriority { get; set; } = new() { "USA", "EU", "JP" };

    // Toolbar komutlarının açıklamaları ve parametreleri (bkz. CommandSetting).
    public List<CommandSetting> Commands { get; set; } = new();

    // Oyun satırı sağ tık menüsünün görünüm modu (bkz. Ayarlar > Arayüz).
    public ContextMenuDisplayMode ContextMenuDisplayMode { get; set; } = ContextMenuDisplayMode.IconAndText;

    // Re-match Metadata komutu için — Builder'ın kullandığı LaunchBox.Metadata.db yolu, WPF
    // tarafında da bilinmesi gerekiyor (bkz. plan: RetroAudit.Catalog referansı).
    public string LaunchBoxDbPath { get; set; } = string.Empty;

    // "Sütunlar" seçicisindeki her sütunun son görünürlük durumu (Key -> IsVisible). Kullanıcı
    // bir sütunu açıp/kapatınca MainViewModel.SaveColumnVisibility burayı güncelleyip diske yazar;
    // uygulama açılışında bu değerler ColumnOptions'ın sabit varsayılanlarının üzerine uygulanır.
    public Dictionary<string, bool> ColumnVisibility { get; set; } = new();
}
