namespace RetroAudit.Models;

// Oyun satırına sağ tıklandığında açılan kapsül menünün görünüm modu — Ayarlar > Arayüz'den
// değiştirilir (bkz. SettingsViewModel, GameContextMenu.xaml).
public enum ContextMenuDisplayMode
{
    IconOnly,
    IconAndText,
}

// Sol paneldeki platform listesinin satır görünümü — Ayarlar > Arayüz > Platform Listesi'nden
// değiştirilir. Logo, Platform.IconGlyph kısa metin rozetini (ör. "NES") gösterir, tam adı gizler.
public enum PlatformListDisplayMode
{
    Text,
    Logo,
}

// "Görsel Getir" ile indirilen Box/BG/SS görsellerinin en uzun kenarının küçültüleceği maksimum
// piksel boyutu (bkz. ArtworkService.ResizeAndEncode) — Ayarlar > Genel'den değiştirilir.
// Original: hiç küçültme yapılmaz, kaynak dosya boyutu aynen korunur.
public enum ArtworkMaxDimension
{
    Px600,
    Px800,
    Original,
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

    // DataGrid satır yüksekliği (bkz. Ayarlar > Arayüz > Tablo Görünümü). Önceden ana penceredeki
    // araç çubuğunda bir kaydırıcıydı ve hiç kalıcı değildi; kullanıcı isteğiyle Ayarlar'a taşındı.
    public double RowHeight { get; set; } = 30;

    // Her sütunun son kullanıcı tarafından sürüklenerek ayarlanmış genişliği (Key -> piksel).
    // Kayıtlı değeri olmayan sütunlar MainWindow.xaml'deki sabit Width'i kullanmaya devam eder
    // (bkz. MainWindow.xaml.cs WireColumnWidths).
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    // Sütun başlığına sağ tıklayıp "Sola Sabitle"/"Sağa Sabitle" ile sabitlenen sütunların Key'leri,
    // pinlenme sırasıyla (bkz. MainWindow.xaml.cs ApplyColumnPinning). Sola sabitlenenler
    // DataGrid.FrozenColumnCount ile gerçekten yatay kaydırmadan bağışık tutulur; WPF DataGrid'in
    // sağdan dondurma desteği olmadığı için sağa sabitleme sadece sütunu en sona TAŞIR (gerçek bir
    // "sticky" davranış değildir) — bu bilinçli bir sınırlama.
    public List<string> PinnedLeftColumns { get; set; } = new();
    public List<string> PinnedRightColumns { get; set; } = new();

    // Kullanıcının sütun başlığını sürükleyip bıraktığı (pinleme dışı, düz sıra değiştirme) sonucu
    // oluşan tam sütun sırası (bkz. MainWindow.xaml.cs GamesGrid_ColumnReordered). Bu olmadan
    // ApplyColumnPinningPositions her açılışta sırayı ColumnDefinitions'ın SABİT koddaki sırasına
    // döndürüyordu — kullanıcının manuel sürükleyerek yaptığı sıralama hiç kalıcı olmuyordu.
    // Kayıtlı küme mevcut sütun anahtarlarıyla tam eşleşmezse (ör. bir güncellemede sütun eklenip
    // çıkarıldıysa) yok sayılıp koddaki varsayılan sıraya dönülür.
    public List<string> ColumnOrder { get; set; } = new();

    // Sağ detay panelinin GridSplitter ile ayarlanmış genişliği (bkz. MainWindow.xaml GridSplitter,
    // MainViewModel.DetailColumnWidth). Panel kapatılıp açıldığında bu genişliğe geri döner.
    public double DetailPanelWidth { get; set; } = 340;

    // Sol paneldeki platform listesi kategorilere (Konsollar, El Konsolları, ...) göre mi
    // gruplanacak, yoksa düz bir liste mi olacak (bkz. Ayarlar > Arayüz > Platform Listesi).
    public bool GroupPlatformsByCategory { get; set; } = true;

    // Platform satırlarının Yazı mı Logo mu gösterileceği (bkz. PlatformListDisplayMode).
    public PlatformListDisplayMode PlatformListDisplayMode { get; set; } = PlatformListDisplayMode.Text;

    // "Görsel Getir" indirmelerinin küçültüleceği maksimum boyut (bkz. Ayarlar > Genel).
    public ArtworkMaxDimension ArtworkMaxDimension { get; set; } = ArtworkMaxDimension.Px600;

    // Hangi kategorilerin sol panelde görünür olduğu (Key -> IsVisible). Kayıtlı değeri olmayan
    // bir kategori varsayılan olarak görünür sayılır.
    public Dictionary<string, bool> CategoryVisibility { get; set; } = new();

    // Kullanıcının platform listesinde sürükle-bırak ile ayarladığı özel sıra (Platform.Name,
    // ham DAT adı). Listede olmayan platformlar doğal sıralarıyla sona eklenir (bkz.
    // MainViewModel.OrderPlatforms). Kategoriler açıkken sıralama sadece aynı kategori içinde
    // yapılabilir (bkz. MainViewModel.ReorderPlatform).
    public List<string> PlatformOrder { get; set; } = new();
}
