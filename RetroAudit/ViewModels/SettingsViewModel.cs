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
    // LaunchBox kurulumunun kök dizini (romlar ve medya bu dizin altında aranacak).
    [ObservableProperty]
    private string launchBoxRootPath = string.Empty;

    // DataGrid'de düzenlenen, seçili emülatör satırı (Sil/Gözat komutları bunu hedef alabilir).
    [ObservableProperty]
    private EmulatorConfig? selectedEmulator;

    // Region Priority listesinde seçili bölge (Yukarı/Aşağı taşıma komutları için).
    [ObservableProperty]
    private string? selectedRegion;

    // Platform başına emülatör kayıtları; DataGrid'e doğrudan bağlanır.
    public ObservableCollection<EmulatorConfig> Emulators { get; } = new();

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
        new() { CommandName = "Metadata Yenile", Category = "Medya", Description = "Oyun bilgilerini (açıklama, geliştirici, tür vb.) günceller.", Parameter = "Kaynak: LaunchBox Metadata" },
        new() { CommandName = "LB Taşı", Category = "Organizasyon", Description = "Seçili oyunu LaunchBox klasör yapısına taşır/kopyalar.", Parameter = "Hedef: %LaunchBoxRoot%\\Games" },
        new() { CommandName = "Apply Resolver", Category = "Organizasyon", Description = "Belirsiz/çakışan girişleri kural bazlı otomatik çözümler.", Parameter = "Kural: EnYeniSürüm" },
        new() { CommandName = "BAŞLAT", Category = "Oynatma", Description = "Seçili oyunu, Emülatörler sekmesindeki ilgili kayıtla başlatır.", Parameter = "" },
    };

    // Export/Import sonrası kullanıcıya kısa bir bilgi mesajı göstermek için View'a bırakılan olay.
    // ViewModel MessageBox gibi View-katmanı tiplerine doğrudan bağımlı olmasın diye event üzerinden iletilir.
    public event Action<string>? RequestShowMessage;

    // LaunchBox kök dizinini seçmek için klasör tarayıcısı açar.
    [RelayCommand]
    private void BrowseLaunchBoxPath()
    {
        var dialog = new OpenFolderDialog { Title = "LaunchBox Ana Dizinini Seçin" };
        if (dialog.ShowDialog() == true)
            LaunchBoxRootPath = dialog.FolderName;
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
        LaunchBoxRootPath = LaunchBoxRootPath,
        Emulators = Emulators.ToList(),
        RegionPriority = RegionPriority.ToList(),
        Commands = Commands.ToList(),
    };

    // İçe aktarılan AppSettings verisini ekrandaki ObservableCollection'lara ve alanlara uygular.
    private void LoadFromAppSettings(AppSettings settings)
    {
        LaunchBoxRootPath = settings.LaunchBoxRootPath;

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
