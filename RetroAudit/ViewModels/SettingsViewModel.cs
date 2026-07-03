using System.Collections.ObjectModel;
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
    // RetroAudit'in kendi veri kök dizini (romlar, medya ve RetroAudit.db bu dizin altında olacak).
    [ObservableProperty]
    private string retroAuditDataPath = string.Empty;

    // DataGrid'de düzenlenen, seçili emülatör satırı (Sil/Gözat komutları bunu hedef alabilir).
    [ObservableProperty]
    private EmulatorConfig? selectedEmulator;

    // Region Priority listesinde seçili bölge (Yukarı/Aşağı taşıma komutları için).
    [ObservableProperty]
    private string? selectedRegion;

    // --- Arayüz sekmesi ---
    // Bu ikisi diğer alanların aksine (RetroAuditDataPath/Emulators/... sadece Export/Import ile
    // taşınır) her değiştiğinde otomatik olarak ConfigService'in sabit varsayılan dosyasına
    // kaydedilir (bkz. SaveInterfaceSettings) — aksi halde MainWindow bu tercihi bir daha hiç
    // okuyamazdı, çünkü Ayarlar penceresi elle "Kaydet" düğmesi olmayan, Export/Import'a dayanan
    // bir tasarımda.
    [ObservableProperty]
    private ContextMenuDisplayMode contextMenuDisplayMode = ContextMenuDisplayMode.IconAndText;

    [ObservableProperty]
    private string launchBoxDbPath = string.Empty;

    public SettingsViewModel()
    {
        var settings = ConfigService.LoadDefault();
        contextMenuDisplayMode = settings.ContextMenuDisplayMode;
        launchBoxDbPath = settings.LaunchBoxDbPath;
    }

    partial void OnContextMenuDisplayModeChanged(ContextMenuDisplayMode value) => SaveInterfaceSettings();

    partial void OnLaunchBoxDbPathChanged(string value) => SaveInterfaceSettings();

    private void SaveInterfaceSettings()
    {
        var settings = ConfigService.LoadDefault();
        settings.ContextMenuDisplayMode = ContextMenuDisplayMode;
        settings.LaunchBoxDbPath = LaunchBoxDbPath;
        ConfigService.SaveDefault(settings);
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

    // RetroAudit veri kök dizinini seçmek için klasör tarayıcısı açar.
    [RelayCommand]
    private void BrowseDataPath()
    {
        var dialog = new OpenFolderDialog { Title = "RetroAudit Data Dizinini Seçin" };
        if (dialog.ShowDialog() == true)
            RetroAuditDataPath = dialog.FolderName;
    }

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

        var dialog = new OpenFileDialog
        {
            Title = "Emülatör Çalıştırılabilir Dosyası",
            Filter = "Çalıştırılabilir dosyalar (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*",
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

    // Ekrandaki ObservableCollection'ları JSON'a yazılabilecek sade bir AppSettings POCO'suna dönüştürür.
    private AppSettings ToAppSettings() => new()
    {
        RetroAuditDataPath = RetroAuditDataPath,
        Emulators = Emulators.ToList(),
        RegionPriority = RegionPriority.ToList(),
        Commands = Commands.ToList(),
    };

    // İçe aktarılan AppSettings verisini ekrandaki ObservableCollection'lara ve alanlara uygular.
    private void LoadFromAppSettings(AppSettings settings)
    {
        RetroAuditDataPath = settings.RetroAuditDataPath;

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
