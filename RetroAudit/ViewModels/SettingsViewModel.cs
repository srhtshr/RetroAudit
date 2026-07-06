using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Admin/Ayarlar penceresinin ViewModel'i. Projenin "ana omurgası" olarak tasarlandı:
// ileride eklenecek her dinamik parametre (emülatör yolları, klasör yolları, bölge tercihleri vb.)
// buraya bir alan olarak eklenip Export/Import Config akışına otomatik dahil olacak.
public partial class SettingsViewModel : ObservableObject
{
    // Romlar/görseller/veritabanları artık kullanıcı tarafından seçilemiyor — her zaman uygulamanın
    // kendi klasörünün yanında (bkz. AppPaths). Ayarlar > Genel bu kökü salt-okunur gösterir.
    public string DataRootPath => AppPaths.Root;

    // DataGrid'de düzenlenen, seçili emülatör satırı (Sil/Gözat komutları bunu hedef alabilir).
    [ObservableProperty]
    private EmulatorConfig? selectedEmulator;

    // Region Priority listesinde seçili bölge (Yukarı/Aşağı taşıma komutları için).
    [ObservableProperty]
    private string? selectedRegion;

    // --- Arayüz sekmesi ---
    [ObservableProperty]
    private ContextMenuDisplayMode contextMenuDisplayMode = ContextMenuDisplayMode.IconAndText;

    [ObservableProperty]
    private string launchBoxDbPath = string.Empty;

    // DataGrid satır yüksekliği — önceden ana penceredeki araç çubuğunda bir kaydırıcıydı,
    // kalıcı olmadan her açılışta sıfırlanıyordu. Buraya taşınıp diğer Arayüz alanları gibi
    // otomatik kaydediliyor (bkz. SaveInterfaceSettings), MainWindow kapanınca
    // MainViewModel.ReloadAppSettings ile geri okunuyor.
    [ObservableProperty]
    private double rowHeight = 30;

    // Detay panelindeki "Sürümler (Region)" gösterim tercihi (bkz. Ayarlar > Arayüz, kullanıcı
    // isteği: "ister bu şekilde açılır liste isterse full açık olarak gösterebilme ayarı olsun").
    [ObservableProperty]
    private bool showVersionsAsSingleCard = true;

    // Sol paneldeki platform listesinin gruplama/görünüm tercihleri (bkz. Ayarlar > Arayüz >
    // Platform Listesi) — MainViewModel.RebuildPlatformListItems bunları okuyup uygular.
    [ObservableProperty]
    private bool groupPlatformsByCategory = true;

    [ObservableProperty]
    private PlatformListDisplayMode platformListDisplayMode = PlatformListDisplayMode.Text;

    [ObservableProperty]
    private RegionColumnDisplayMode regionColumnDisplayMode = RegionColumnDisplayMode.FlagAndText;

    // "Görsel Getir" indirmelerinin küçültüleceği maksimum boyut (bkz. Ayarlar > Genel).
    [ObservableProperty]
    private ArtworkMaxDimension artworkMaxDimension = ArtworkMaxDimension.Px600;

    // Her kategori için görünür/gizli tercihi — MainViewModel.CategoryConsoles/Handhelds/... ile
    // aynı sabit anahtarları kullanır (Key), ColumnVisibilityOption'ın Sütunlar seçicisindeki
    // rolüyle aynı deseni burada da tekrar kullanıyor.
    public ObservableCollection<ColumnVisibilityOption> CategoryOptions { get; } = new();

    private static readonly (string Key, string Header)[] CategoryDefinitions =
    {
        (MainViewModel.CategoryConsoles, "Konsollar"),
        (MainViewModel.CategoryHandhelds, "El Konsolları"),
        (MainViewModel.CategoryArcade, "Arcade"),
        (MainViewModel.CategoryComputers, "Bilgisayarlar"),
        (MainViewModel.CategoryClassic, "Klasik"),
        (MainViewModel.CategoryOthers, "Diğer"),
    };

    public SettingsViewModel()
    {
        var settings = ConfigService.LoadDefault();
        contextMenuDisplayMode = settings.ContextMenuDisplayMode;
        launchBoxDbPath = settings.LaunchBoxDbPath;
        rowHeight = settings.RowHeight;
        showVersionsAsSingleCard = settings.ShowVersionsAsSingleCard;
        groupPlatformsByCategory = settings.GroupPlatformsByCategory;
        platformListDisplayMode = settings.PlatformListDisplayMode;
        regionColumnDisplayMode = settings.RegionColumnDisplayMode;
        artworkMaxDimension = settings.ArtworkMaxDimension;
        BuildCategoryOptions(settings);
    }

    private void BuildCategoryOptions(AppSettings settings)
    {
        CategoryOptions.Clear();
        foreach (var (key, header) in CategoryDefinitions)
        {
            CategoryOptions.Add(new ColumnVisibilityOption
            {
                Key = key,
                Header = header,
                IsVisible = settings.CategoryVisibility.GetValueOrDefault(key, true),
            });
        }
    }

    // Ayarlar artık her değişiklikte değil, sadece "Kaydet" butonuna (bkz. SaveSettings) veya
    // Export Config'e basıldığında yazılıyor (kullanıcı kararı: görünür/kasıtlı bir kaydetme
    // eylemi istendi, sessiz oto-kaydetmenin yerine). ConfigService.LoadDefault ile önce diskteki
    // güncel hali okunuyor ki MainViewModel'in ayrıca yönettiği alanlar (ColumnWidths, PinnedLeft/
    // RightColumns, ColumnOrder, DetailPanelWidth, PlatformOrder, ColumnVisibility) ezilmesin —
    // sadece bu ViewModel'in sahip olduğu alanlar üzerine yazılıyor.
    [RelayCommand]
    private void SaveSettings()
    {
        ConfigService.SaveDefault(ToAppSettings());
        RequestShowMessage?.Invoke("Ayarlar kaydedildi.");
    }

    [RelayCommand]
    private void BrowseLaunchBoxDb()
    {
        var dialog = new OpenFileDialog
        {
            Title = "LaunchBox.Metadata.db Seçin",
            Filter = "SQLite veritabanı (*.db)|*.db|Tüm dosyalar (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
            LaunchBoxDbPath = dialog.FileName;
    }

    // Platform başına emülatör kayıtları; DataGrid'e doğrudan bağlanır.
    // NOT: PlatformName değerleri burada kısa/küratörlü isimler (ör. "Nintendo Entertainment
    // System") — RetroAudit.db'deki Platforms.Name ise ham DAT adı (ör. "Nintendo - Nintendo
    // Entertainment System"). "Seçili oyunun platformu -> bu tablodaki satır" eşlemesi (BAŞLAT
    // butonu, Stage C) kurulacağı zaman bu isim farkı ele alınmalı. ExecutablePath bilinçli
    // olarak boş bırakıldı: bu, kullanıcının kendi makinesindeki kurulum yoluna bağlı, mock veri değil.
    // Arcade tarafı (CPS1-3, genel arcade) ayrı platform açılmadan MAME satırının altına, FBNeo alternatif
    // core olarak toplandı.
    public ObservableCollection<EmulatorConfig> Emulators { get; } = new()
    {
        // --- Favori platformlar ---
        new() { PlatformName = "MAME", PreferredCore = "MAME", AlternativeCore = "FBNeo" },
        new() { PlatformName = "NeoGeo", PreferredCore = "MAME", AlternativeCore = "RetroArch Alpha" },
        new() { PlatformName = "Nintendo", PreferredCore = "Mesen", AlternativeCore = "Snes9x" },
        new() { PlatformName = "Super Nintendo", PreferredCore = "bsnes", AlternativeCore = "Snes9x" },
        new() { PlatformName = "PlayStation", PreferredCore = "SwanStation", AlternativeCore = "DuckStation" },
        new() { PlatformName = "PlayStation 2", PreferredCore = "PCSX2" },
        new() { PlatformName = "Sega Genesis", PreferredCore = "Genesis Plus GX" },
        new() { PlatformName = "Master System", PreferredCore = "Genesis Plus GX" },
        new() { PlatformName = "PSP", PreferredCore = "PPSSPP" },
        new() { PlatformName = "PlayStation 3", PreferredCore = "RPCS3" },
        new() { PlatformName = "Xbox", PreferredCore = "Xemu" },
        new() { PlatformName = "Xbox 360", PreferredCore = "Xenia" },
        new() { PlatformName = "Dreamcast", PreferredCore = "Flycast" },
        new() { PlatformName = "Game Gear", PreferredCore = "Genesis Plus GX", AlternativeCore = "SMS Plus" },
        new() { PlatformName = "Commodore 64", PreferredCore = "VICE x64" },
        new() { PlatformName = "Amiga", PreferredCore = "WinUAE" },
        new() { PlatformName = "Atari", PreferredCore = "Stella" },

        // --- Ek platformlar (varsayılan gizli; "+" popup'ından açılabilir) ---
        new() { PlatformName = "Nintendo 64", PreferredCore = "Mupen64Plus-Next", AlternativeCore = "Project64" },
        new() { PlatformName = "Nintendo GameCube", PreferredCore = "Dolphin" },
        new() { PlatformName = "Nintendo Wii", PreferredCore = "Dolphin" },
        new() { PlatformName = "Nintendo Wii U", PreferredCore = "Cemu" },
        new() { PlatformName = "Nintendo Switch", PreferredCore = "Yuzu", AlternativeCore = "Ryujinx" },
        new() { PlatformName = "Game Boy", PreferredCore = "SameBoy", AlternativeCore = "Gambatte" },
        new() { PlatformName = "Game Boy Color", PreferredCore = "SameBoy" },
        new() { PlatformName = "Game Boy Advance", PreferredCore = "mGBA" },
        new() { PlatformName = "Nintendo DS", PreferredCore = "melonDS" },
        new() { PlatformName = "Nintendo 3DS", PreferredCore = "Citra" },
        new() { PlatformName = "Sega CD", PreferredCore = "Genesis Plus GX" },
        new() { PlatformName = "Sega 32X", PreferredCore = "Kega Fusion", AlternativeCore = "PicoDrive" },
        new() { PlatformName = "Sega Saturn", PreferredCore = "Beetle Saturn", AlternativeCore = "Mednafen" },
        new() { PlatformName = "Sony PlayStation Vita", PreferredCore = "Vita3K" },
        new() { PlatformName = "Microsoft Xbox One", PreferredCore = "Xemu", AlternativeCore = "Bağımsız emülatör altyapısı" },
        new() { PlatformName = "Atari 5200", PreferredCore = "Atari800" },
        new() { PlatformName = "Atari 7800", PreferredCore = "ProSystem" },
        new() { PlatformName = "Atari Jaguar", PreferredCore = "Virtual Jaguar" },
        new() { PlatformName = "Atari Lynx", PreferredCore = "Beetle Lynx", AlternativeCore = "Handy" },
        new() { PlatformName = "NEC PC Engine / TurboGrafx-16", PreferredCore = "Mednafen", AlternativeCore = "Beetle PCE" },
        new() { PlatformName = "NEC PC Engine CD", PreferredCore = "Mednafen" },
        new() { PlatformName = "SNK Neo Geo CD", PreferredCore = "NeoCD" },
        new() { PlatformName = "SNK Neo Geo Pocket", PreferredCore = "Mednafen" },
        new() { PlatformName = "SNK Neo Geo Pocket Color", PreferredCore = "Mednafen" },
        new() { PlatformName = "Bilgisayar (MS-DOS)", PreferredCore = "DOSBox-Staging" },
        new() { PlatformName = "Bilgisayar (Windows)", PreferredCore = "Doğrudan PC Executable (.exe)" },
    };

    // Bölge önceliği sırası: USA > EU > JP gibi. Liste başı = en yüksek öncelik.
    public ObservableCollection<string> RegionPriority { get; } = new() { "USA", "EU", "JP" };

    // Üst araç çubuğundaki komutların açıklaması + (varsa) parametresi. "Komutlar" sekmesinde
    // Category alanına göre gruplanarak gösterilir (bkz. SettingsWindow.xaml CollectionViewSource).
    // Komutların gerçek mantığı MainViewModel'de henüz stub olduğu için buradaki Parameter alanları
    // şimdilik gerçek bir davranışı değiştirmiyor — ancak ileride commandlar buradan okuyacak şekilde
    // bağlanacak, böylece davranış değişikliği için kod değil sadece bu panel yeterli olacak.
    public ObservableCollection<CommandSetting> Commands { get; } = new()
    {
        new() { CommandName = "Import", Category = "Veri Yönetimi", Description = "Dışarıdan oyun/rom verisi içe aktarır.", Parameter = "" },
        new() { CommandName = "Rescan", Category = "Veri Yönetimi", Description = "Seçili platformun rom klasörünü yeniden tarar.", Parameter = "" },
        new() { CommandName = "Temizle", Category = "Veri Yönetimi", Description = "Arama, platform ve filtre seçimlerini varsayılana döndürür.", Parameter = "" },
        new() { CommandName = "Refresh Media", Category = "Medya", Description = "Eksik kutu/arkaplan/ekran görüntüsü medyasını yeniler.", Parameter = "Kaynak: TheGamesDB" },
        new() { CommandName = "Metadata Yenile", Category = "Medya", Description = "Oyun bilgilerini (açıklama, geliştirici, tür vb.) günceller.", Parameter = "Kaynak: RetroAudit Data" },
        new() { CommandName = "Kütüphaneye Taşı", Category = "Organizasyon", Description = "Seçili oyunu RetroAudit kütüphane klasör yapısına taşır/kopyalar.", Parameter = "Hedef: %RetroAuditData%\\Games" },
        new() { CommandName = "Apply Resolver", Category = "Organizasyon", Description = "Belirsiz/çakışan girişleri kural bazlı otomatik çözümler.", Parameter = "Kural: EnYeniSürüm" },
        new() { CommandName = "BAŞLAT", Category = "Oynatma", Description = "Seçili oyunu, Emülatörler sekmesindeki ilgili kayıtla başlatır.", Parameter = "" },
    };

    // Export/Import sonrası kullanıcıya kısa bir bilgi mesajı göstermek için View'a bırakılan olay.
    // ViewModel MessageBox gibi View-katmanı tiplerine doğrudan bağımlı olmasın diye event üzerinden iletilir.
    public event Action<string>? RequestShowMessage;

    // Ayarlar > Genel'deki "Klasörü Aç" düğmesi — Games/Images/Metadata/Emulation'ın hepsinin
    // yaşadığı kökü Explorer'da açar (bkz. MainViewModel.OpenFileLocation ile aynı desen).
    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    // Boş bir emülatör satırı ekler; kullanıcı sonra platform adı/exe yolunu doldurur.
    [RelayCommand]
    private void AddEmulator() => Emulators.Add(new EmulatorConfig { PlatformName = "Yeni Platform" });

    // Seçili (veya parametre olarak verilen) emülatör satırını listeden kaldırır.
    [RelayCommand]
    private void RemoveEmulator(EmulatorConfig? emulator)
    {
        if (emulator is not null)
            Emulators.Remove(emulator);
    }

    // İlgili emülatörün .exe dosyasını seçmek için dosya tarayıcısı açar.
    [RelayCommand]
    private void BrowseEmulatorPath(EmulatorConfig? emulator)
    {
        if (emulator is null)
            return;

        // Kullanıcı kendi emülatörünü nereye kurduysa oradan seçebilir — Emulation\ sadece bir
        // öneri, dialog başlangıçta orayı açıyor ama zorunlu değil.
        var dialog = new OpenFileDialog
        {
            Title = "Emülatör Çalıştırılabilir Dosyası",
            Filter = "Çalıştırılabilir dosyalar (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*",
            InitialDirectory = AppPaths.Emulation,
        };

        if (dialog.ShowDialog() == true)
            emulator.ExecutablePath = dialog.FileName;
    }

    // Verilen bölgeyi öncelik sırasında bir üste taşır (daha yüksek öncelik verir).
    [RelayCommand]
    private void MoveRegionUp(string? region)
    {
        if (region is null)
            return;

        var index = RegionPriority.IndexOf(region);
        if (index > 0)
            RegionPriority.Move(index, index - 1);
    }

    // Verilen bölgeyi öncelik sırasında bir alta taşır (daha düşük öncelik verir).
    [RelayCommand]
    private void MoveRegionDown(string? region)
    {
        if (region is null)
            return;

        var index = RegionPriority.IndexOf(region);
        if (index >= 0 && index < RegionPriority.Count - 1)
            RegionPriority.Move(index, index + 1);
    }

    // Mevcut tüm ayarları kullanıcının seçtiği bir .json dosyasına yazar.
    [RelayCommand]
    private void ExportConfig()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Config Dışa Aktar",
            Filter = "JSON dosyası (*.json)|*.json",
            FileName = "retroaudit.settings.json",
        };

        if (dialog.ShowDialog() != true)
            return;

        ConfigService.Export(ToAppSettings(), dialog.FileName);
        RequestShowMessage?.Invoke($"Config dışa aktarıldı: {dialog.FileName}");
    }

    // Kullanıcının seçtiği bir .json dosyasından ayarları okuyup ekrana uygular.
    [RelayCommand]
    private void ImportConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Config İçe Aktar",
            Filter = "JSON dosyası (*.json)|*.json",
        };

        if (dialog.ShowDialog() != true)
            return;

        LoadFromAppSettings(ConfigService.Import(dialog.FileName));
        RequestShowMessage?.Invoke($"Config içe aktarıldı: {dialog.FileName}");
    }

    // Ekrandaki tüm alan/koleksiyonları JSON'a yazılabilecek bir AppSettings POCO'suna dönüştürür.
    // Önce diskteki güncel hal okunuyor (ConfigService.LoadDefault) — MainViewModel'in ayrıca
    // yönettiği alanlar (ColumnWidths, PinnedLeft/RightColumns, ColumnOrder, DetailPanelWidth,
    // PlatformOrder, ColumnVisibility) bu ViewModel'de hiç yok, o yüzden sıfırdan bir AppSettings
    // oluşturmak onları silerdi.
    private AppSettings ToAppSettings()
    {
        var settings = ConfigService.LoadDefault();
        settings.ContextMenuDisplayMode = ContextMenuDisplayMode;
        settings.LaunchBoxDbPath = LaunchBoxDbPath;
        settings.RowHeight = RowHeight;
        settings.ShowVersionsAsSingleCard = ShowVersionsAsSingleCard;
        settings.GroupPlatformsByCategory = GroupPlatformsByCategory;
        settings.PlatformListDisplayMode = PlatformListDisplayMode;
        settings.RegionColumnDisplayMode = RegionColumnDisplayMode;
        settings.ArtworkMaxDimension = ArtworkMaxDimension;
        settings.CategoryVisibility = CategoryOptions.ToDictionary(o => o.Key, o => o.IsVisible);
        settings.Emulators = Emulators.ToList();
        settings.RegionPriority = RegionPriority.ToList();
        settings.Commands = Commands.ToList();
        return settings;
    }

    // İçe aktarılan AppSettings verisini ekrandaki alanlara/ObservableCollection'lara uygular.
    private void LoadFromAppSettings(AppSettings settings)
    {
        ContextMenuDisplayMode = settings.ContextMenuDisplayMode;
        LaunchBoxDbPath = settings.LaunchBoxDbPath;
        RowHeight = settings.RowHeight;
        ShowVersionsAsSingleCard = settings.ShowVersionsAsSingleCard;
        GroupPlatformsByCategory = settings.GroupPlatformsByCategory;
        PlatformListDisplayMode = settings.PlatformListDisplayMode;
        RegionColumnDisplayMode = settings.RegionColumnDisplayMode;
        ArtworkMaxDimension = settings.ArtworkMaxDimension;
        BuildCategoryOptions(settings);

        Emulators.Clear();
        foreach (var emulator in settings.Emulators)
            Emulators.Add(emulator);

        RegionPriority.Clear();
        foreach (var region in settings.RegionPriority)
            RegionPriority.Add(region);

        // Boş/eksik bir JSON'dan içe aktarılırsa (ör. eski bir export dosyası) Komutlar listesi
        // kaybolmasın diye, dosyada hiç komut yoksa mevcut varsayılan tanımlar korunuyor.
        if (settings.Commands.Count > 0)
        {
            Commands.Clear();
            foreach (var command in settings.Commands)
                Commands.Add(command);
        }
    }
}
