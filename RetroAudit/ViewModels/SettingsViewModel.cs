using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Emülatörler tablosundaki "Mod" rozet-ComboBox'ının (bkz. SettingsWindow.xaml LauncherTypePillComboBox
// stili) ItemsSource'undaki tek bir öğe — CoreChoiceOption ile AYNI desen: DisplayMemberPath="Label" +
// SelectedValuePath="Value" kullanan, uygulamanın ZATEN çalışan varsayılan ComboBox şablonuyla (bkz.
// ObsidianDark.xaml) uyumlu, PROVEN bir teknik. Önceki deneme ComboBox'ın TÜM ControlTemplate'ini
// (ToggleButton+Popup+ItemsPresenter) elden geçiriyordu — kullanıcı geri bildirimi: "seçiyorum ama
// değişmiyor, tıklama hiçbir şey yapmıyor" (seçim tamamen kırılmıştı). Bu sürüm şablona HİÇ dokunmuyor,
// sadece Background/Foreground gibi ZATEN şablonun okuduğu özellikleri DataTrigger ile renklendiriyor.
public sealed class LauncherTypeOption
{
    public LauncherType Value { get; }
    public string Label { get; }

    public LauncherTypeOption(LauncherType value, string label)
    {
        Value = value;
        Label = label;
    }

    public override string ToString() => Label;
}

// Emülatörler sekmesindeki "İndir & Kur" satırlarından biri (bkz. SettingsViewModel.StandaloneEmulatorInstalls,
// StandaloneEmulatorInstallerService) — RetroArch'ın tekil Is/Progress/Status alan üçlüsünün, birden
// fazla standalone emülatör için TEKRARLANMADAN (IsInstallingPcsx2/IsInstallingXemu/... gibi ayrı ayrı
// alanlar yerine) kullanılabilmesi için küçük bir gözlemlenebilir durum nesnesi.
public partial class StandaloneEmulatorInstallState : ObservableObject
{
    public string EmulatorId { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private double progress;

    // "ilerleme çubuğu oynamıyor yapay gecikme ekleme yüzde göstersin" (kullanıcı isteği) — arşiv
    // açma aşamasında GERÇEK bir yüzde bilgisi yok (bkz. StandaloneEmulatorInstallerService), sahte
    // bir sayı uydurmak yerine ProgressBar bu süre boyunca "IsIndeterminate" moduna geçiyor.
    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    private string status = string.Empty;

    // "inenlerde buton kalksın işarete dönsün" (kullanıcı isteği) — ThirdParty\{EmulatorId}\ klasörüne
    // daha önce kurulup kurulmadığını tutar (bkz. StandaloneEmulatorInstallerService.IsInstalled),
    // pencere açılışında ve her indirme/kaldırma sonrası SettingsViewModel tarafından güncellenir.
    [ObservableProperty]
    private bool isInstalled;

    public StandaloneEmulatorInstallState(string emulatorId, string displayName)
    {
        EmulatorId = emulatorId;
        DisplayName = displayName;
    }
}

// Ayarlar > Komutlar sekmesindeki "Emülatör İndirme Kaynakları" listesindeki TEK satır (bkz.
// SettingsViewModel.EmulatorDownloadSettings) — kullanıcı geri bildirimi: önceden Manuel URL ve
// Varsayılan Kaynak İKİ AYRI liste olarak (aynı 10 emülatör tekrar tekrar) gösteriliyordu ("neden öyle
// yaptın"), sonra TEK satıra indirildi ama hâlâ 2 alan vardı — kullanıcı son talimatı: "API kısmında
// API linki, API yoksa Direct URL, manuel kısmını tekrar eklemene gerek yok tek sütun yeterli" — ARTIK
// SADECE SourceType (GitHubReleases/DirectUrl) + Source: API varsa GitHubReleases + "owner/repo";
// yoksa (Dolphin/Ryujinx gibi) DirectUrl + admin'in bulduğu gerçek indirme linki. RPCS3 için Source
// BOŞ bırakılırsa kendi özel JSON güncelleme API'si kullanılır (bkz. StandaloneEmulatorInstallerService.
// ResolveSourceAsync), doldurulursa o değer ONU geçersiz kılar — bu yüzden RPCS3 satırı da AYNI iki
// alanla, devre dışı bırakılmadan gösterilir. "Kaynak değiştirilirse program yeniden derlenmeden
// kullanılabilsin" (görev talimatı) — bu yüzden [ObservableProperty], AppSettings.EmulatorDownloadSources'e
// kaydedilip her indirmede DİSKTEN OKUNUYOR, hiçbir yerde koda gömülü değil.
public partial class EmulatorDownloadSettingsEntry : ObservableObject
{
    public string EmulatorId { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private DownloadSourceType sourceType;

    [ObservableProperty]
    private string source = string.Empty;

    public EmulatorDownloadSettingsEntry(string emulatorId, string displayName, DownloadSourceType sourceType, string source)
    {
        EmulatorId = emulatorId;
        DisplayName = displayName;
        this.sourceType = sourceType;
        this.source = source;
    }
}

// DownloadSourceType ComboBox'ının ItemsSource'undaki tek öğe — LauncherTypeOption ile AYNI PROVEN
// desen (bkz. o sınıfın yorumu).
public sealed class DownloadSourceTypeOption
{
    public DownloadSourceType Value { get; }
    public string Label { get; }

    public DownloadSourceTypeOption(DownloadSourceType value, string label)
    {
        Value = value;
        Label = label;
    }

    public override string ToString() => Label;
}

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

    // SettingsWindow.xaml.cs'in Emülatörler DataGrid'inde başlangıçta uygulayacağı kayıtlı sütun
    // genişlikleri/sırası (kullanıcı isteği: "sütunları ayarlıyom kapatınca ayar bozuluyor genişlik
    // ve konum") — MainViewModel.ColumnWidths/ColumnOrder ile AYNI desen, ama bu ViewModel MainViewModel
    // gibi uzun ömürlü olmadığı (her pencere açılışında yeniden kuruluyor) için kalıcı bir _appSettings
    // alanı tutmuyor; Save* metotları her çağrıda diskten TAZE okuyup sadece bu iki alanı günceller.
    public IReadOnlyDictionary<string, double> EmulatorGridColumnWidths { get; }
    public IReadOnlyList<string> EmulatorGridColumnOrder { get; }

    public void SaveEmulatorGridColumnWidths(Dictionary<string, double> widths)
    {
        var settings = ConfigService.LoadDefault();
        settings.EmulatorGridColumnWidths = widths;
        ConfigService.SaveDefault(settings);
    }

    public void SaveEmulatorGridColumnOrder(List<string> order)
    {
        var settings = ConfigService.LoadDefault();
        settings.EmulatorGridColumnOrder = order;
        ConfigService.SaveDefault(settings);
    }

    // "RetroArch İndir & Kur" düğmesinin durumu (bkz. InstallRetroArch, RetroArchInstallerService).
    [ObservableProperty]
    private bool isInstallingRetroArch;

    [ObservableProperty]
    private double retroArchInstallProgress;

    // "ilerleme çubuğu oynamıyor yapay gecikme ekleme yüzde göstersin" (kullanıcı isteği) — arşiv
    // açma aşamalarında (özellikle ~258 MB'lık RetroArch_cores.7z) gerçek bir yüzde yok, bkz.
    // StandaloneEmulatorInstallState.IsIndeterminate ile aynı gerekçe.
    [ObservableProperty]
    private bool isInstallingRetroArchIndeterminate;

    [ObservableProperty]
    private string retroArchInstallStatus = string.Empty;

    // "inenlerde buton kalksın işarete dönsün" (kullanıcı isteği) — RetroArch'ın kendi ThirdParty\
    // RetroArch\ klasörümüze daha önce kurulup kurulmadığını tutar (bkz. RetroArchInstallerService.
    // IsInstalled), constructor'da ve her indirme/kaldırma sonrası güncellenir.
    [ObservableProperty]
    private bool isRetroArchInstalled;

    // Standalone emülatörler için "İndir & Kur" satırları (bkz. InstallStandaloneEmulator,
    // StandaloneEmulatorInstallerService) — TÜM standalone emülatörler için TEK, PAYLAŞILAN akış
    // (görev talimatı: "generic hale getir ... emulator bazlı kopya kod yazılmasın"). Her satırın
    // gerçek indirme kaynağı artık EmulatorDownloadSettings'ten (bkz. o listenin yorumu) DİSKTEN
    // OKUNUYOR, burada hiçbir emülatöre özel kod YOK.
    public ObservableCollection<StandaloneEmulatorInstallState> StandaloneEmulatorInstalls { get; } = new()
    {
        new("PCSX2", "PCSX2 (PlayStation 2)"),
        new("RPCS3", "RPCS3 (PlayStation 3)"),
        new("Xemu", "Xemu (Xbox)"),
        new("Cemu", "Cemu (Wii U)"),
        new("melonDS", "melonDS (Nintendo DS)"),
        new("Vita3K", "Vita3K (PS Vita)"),
        new("Xenia", "Xenia Canary (Xbox 360)"),
        new("Dolphin", "Dolphin (GameCube/Wii)"),
        new("Ryujinx", "Ryujinx (Nintendo Switch)"),
        new("Azahar", "Azahar (Nintendo 3DS)"),
    };

    // Ayarlar > Komutlar sekmesindeki "Emülatör İndirme Kaynakları" — TÜM standalone emülatörler için
    // TEK satır/TEK liste (bkz. EmulatorDownloadSettingsEntry yorumu — StandaloneEmulatorInstalls'tan
    // türetiliyor, ayrı elle tutulmuyor; C# alan başlatıcıları başka bir instance alanına başvuramadığı
    // için bkz. constructor, ORADA dolduruluyor). Sadece indirme KAYNAĞIYLA ilgili — audit/versiyon
    // karşılaştırma/güncelleme kontrolü bu fazda YOK.
    public ObservableCollection<EmulatorDownloadSettingsEntry> EmulatorDownloadSettings { get; }

    // "Core Adı" Mod rozeti (LauncherTypeOptions) ile AYNI PROVEN desen — DisplayMemberPath="Label" +
    // SelectedValuePath="Value" (bkz. LauncherTypeOption yorumu).
    public IReadOnlyList<DownloadSourceTypeOption> DownloadSourceTypeOptions { get; } = new[]
    {
        new DownloadSourceTypeOption(DownloadSourceType.GitHubReleases, "GitHub Releases API"),
        new DownloadSourceTypeOption(DownloadSourceType.DirectUrl, "Direct URL"),
        new DownloadSourceTypeOption(DownloadSourceType.BuiltInApi, "Yerleşik API (özel)"),
    };


    // Region Priority listesinde seçili bölge (Yukarı/Aşağı taşıma komutları için).
    [ObservableProperty]
    private string? selectedRegion;

    // --- Arayüz sekmesi ---
    [ObservableProperty]
    private ContextMenuDisplayMode contextMenuDisplayMode = ContextMenuDisplayMode.IconAndText;

    [ObservableProperty]
    private string masterMetadataDbPath = string.Empty;

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

    [ObservableProperty]
    private ProviderDesignMode providerDesignMode = ProviderDesignMode.Classic;

    // "seçenek koy bide oraya önerilen ve alternatifleri göster veya hepsini göster diye seçilen
    // ayara göre göstersin" (kullanıcı isteği) — bkz. Ayarlar > Emülatörler'deki seçici,
    // EmulatorConfig.AvailableChoices/DisplayMode (static, TÜM satırları etkiler).
    [ObservableProperty]
    private CoreChoiceDisplayMode coreChoiceDisplayMode = CoreChoiceDisplayMode.ShowAll;

    partial void OnCoreChoiceDisplayModeChanged(CoreChoiceDisplayMode value)
    {
        EmulatorConfig.DisplayMode = value;
        foreach (var emulator in Emulators)
            emulator.RefreshAvailableChoices();
        ConfigService.SaveDefault(ToAppSettings());
    }

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

        // C# alan başlatıcıları başka bir instance alanına (StandaloneEmulatorInstalls) başvuramadığı
        // için (CS0236) bu liste BURADA, constructor gövdesinde türetiliyor.
        EmulatorDownloadSettings = new ObservableCollection<EmulatorDownloadSettingsEntry>(
            StandaloneEmulatorInstalls.Select(s =>
            {
                var defaults = StandaloneEmulatorInstallerService.DefaultDownloadSources.TryGetValue(s.EmulatorId, out var def)
                    ? def
                    : new EmulatorDownloadSourceSetting { SourceType = DownloadSourceType.GitHubReleases, Source = string.Empty };
                return new EmulatorDownloadSettingsEntry(s.EmulatorId, s.DisplayName, defaults.SourceType, defaults.Source);
            }));

        contextMenuDisplayMode = settings.ContextMenuDisplayMode;
        masterMetadataDbPath = settings.MasterMetadataDbPath;
        rowHeight = settings.RowHeight;
        showVersionsAsSingleCard = settings.ShowVersionsAsSingleCard;
        groupPlatformsByCategory = settings.GroupPlatformsByCategory;
        platformListDisplayMode = settings.PlatformListDisplayMode;
        regionColumnDisplayMode = settings.RegionColumnDisplayMode;
        providerDesignMode = settings.ProviderDesignMode;
        artworkMaxDimension = settings.ArtworkMaxDimension;
        coreChoiceDisplayMode = settings.CoreChoiceDisplayMode;
        EmulatorConfig.DisplayMode = coreChoiceDisplayMode;
        BuildCategoryOptions(settings);

        // BUG (bu oturumda bulundu): Emulators/RegionPriority/Commands koleksiyonları field
        // initializer'daki SABİT varsayılan listelerle başlıyordu ve constructor bunları HİÇBİR ZAMAN
        // diskteki settings.Emulators/RegionPriority/Commands ile DEĞİŞTİRMİYORDU (sadece Import Config
        // akışındaki LoadFromAppSettings bunu yapıyordu) — yani "Kaydet"e basılsa bile, Ayarlar
        // penceresi her yeniden açıldığında (özellikle programı kapatıp açtıktan sonra) emülatör
        // yolları sıfırlanıyordu. Kullanıcı belirtisi: "İndir & Kur" ile atanan ExecutablePath diske
        // yazılıyordu ama pencere yeniden açılınca "Download" görünmeye devam ediyordu. Boşsa (ilk
        // çalıştırma / henüz hiç kaydedilmemiş) sabit varsayılanlar korunuyor.
        if (settings.Emulators.Count > 0)
        {
            Emulators.Clear();
            foreach (var emulator in settings.Emulators)
                Emulators.Add(emulator);
        }
        if (settings.RegionPriority.Count > 0)
        {
            RegionPriority.Clear();
            foreach (var region in settings.RegionPriority)
                RegionPriority.Add(region);
        }
        if (settings.Commands.Count > 0)
        {
            Commands.Clear();
            foreach (var command in settings.Commands)
                Commands.Add(command);
        }

        // "emülatör eşleşmelerini kontrol et" (kullanıcı isteği) — diskten YÜKLENMİŞ (yukarıdaki
        // if bloğu) satırlar, PlatformDisplayNameMap ile eşleşmeyen ESKİ/YANLIŞ PlatformName
        // değerlerini hâlâ taşıyor olabilir (ör. "Sega Genesis" ≠ gerçek "Genesis") — bu satırlarda
        // BAŞLAT'ın hiç çalışmamış olması gerekiyordu. ExecutablePath/CorePath (zaten indirilmiş,
        // yeniden kazanılması zor) KORUNARAK sadece isim/etiket alanları düzeltilir.
        MigrateLegacyPlatformNames();

        // "Core Adı" ComboBox'ının SelectedItem'ı yazılabilir bir alana (SelectedCoreOrEmulatorName)
        // bağlı olduğu için — hiç seçim yapılmamış satırlarda (yeni satır, veya bu özellikten önce
        // kaydedilmiş eski bir kayıt) varsayılan olarak PreferredCore'a düşer.
        foreach (var emulator in Emulators)
        {
            if (string.IsNullOrWhiteSpace(emulator.SelectedCoreOrEmulatorName))
                emulator.SelectedCoreOrEmulatorName = emulator.PreferredCore;
            emulator.RefreshAvailableChoices();
        }

        EmulatorGridColumnWidths = settings.EmulatorGridColumnWidths;
        EmulatorGridColumnOrder = settings.EmulatorGridColumnOrder;

        foreach (var entry in EmulatorDownloadSettings)
        {
            if (settings.EmulatorDownloadSources.TryGetValue(entry.EmulatorId, out var saved))
            {
                entry.SourceType = saved.SourceType;
                entry.Source = saved.Source;
            }
        }

        // "inenlerde buton kalksın işarete dönsün" (kullanıcı isteği) — pencere her açıldığında
        // ThirdParty\ klasörüne bakıp hangi emülatörlerin zaten kurulu olduğunu belirler.
        IsRetroArchInstalled = RetroArchInstallerService.IsInstalled();
        foreach (var state in StandaloneEmulatorInstalls)
            state.IsInstalled = StandaloneEmulatorInstallerService.IsInstalled(state.EmulatorId);

        // Kullanıcı isteği: "cores in içinde her platform için ayrı klasör oluştur hepsi kendi
        // klasöründen okusun" — bu değişiklikten ÖNCE indirilmiş çekirdekler hâlâ eski (düz cores\
        // kökü / cores\RetroArch-Win64\cores\) konumlarında duruyor olabilir; ReconcileInstalledEmulatorPaths
        // ile AYNI mantıkla ama platform klasörlerine göç ettirir — ondan ÖNCE çalışması gerekiyor.
        MigrateCoreFilesToPlatformFolders();

        // Kullanıcı isteği: "standalonelar içinde Emulation içine platformların listesini klasörlerini
        // ekle bütün platformlar olsun gene içinde" — cores\RetroAudit\{Platform}\ ile AYNI fikir,
        // StandaloneEXE tarafı için: her platformun kendi Emulation\{Platform}\ klasörü olsun,
        // Gözat/"+" varsayılan olarak buraya baksın (bkz. BrowseEmulatorPath, AddCustomCore).
        EnsureEmulationPlatformFolders();

        // Bugünkü kurulumlar bu düzeltmeden ÖNCE yapıldığı ve kaydedilmediği için (yukarıdaki bug),
        // kullanıcının zaten indirdiği ama satırlara atanamayan emülatörleri YENİDEN İNDİRMEDEN
        // düzeltir — ThirdParty\ klasöründe zaten kurulu olan ama ExecutablePath'i hâlâ boş olan
        // satırlara, mevcut kurulumun yolunu bulup atar.
        ReconcileInstalledEmulatorPaths();

        CollectionViewSource.GetDefaultView(Emulators).Filter = obj =>
            obj is not EmulatorConfig emulator
            || string.IsNullOrWhiteSpace(EmulatorSearchText)
            || emulator.PlatformName.Contains(EmulatorSearchText, StringComparison.OrdinalIgnoreCase);
    }

    // Emülatörler tablosunun üstündeki arama kutusu — platform adına göre listeyi daraltır (bkz.
    // yukarısı, CollectionViewSource.GetDefaultView(Emulators).Filter).
    [ObservableProperty]
    private string emulatorSearchText = string.Empty;

    partial void OnEmulatorSearchTextChanged(string value) =>
        CollectionViewSource.GetDefaultView(Emulators).Refresh();

    // Eski (PlatformDisplayNameMap ile eşleşmeyen) PlatformName -> DOĞRU isim. Sadece yeniden
    // adlandırma — asıl güncel Tercih Edilen/Alternatif/Mod/Parametreler CanonicalEmulatorDefinitions'ta
    // (bkz. aşağısı), DOĞRU isimle anahtarlı tek bir kaynak.
    private static readonly Dictionary<string, string> LegacyPlatformNameRenames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sega Genesis"] = "Genesis",
        ["PSP"] = "PlayStation Portable",
        ["Nintendo GameCube"] = "GameCube",
        ["Nintendo Wii"] = "Wii",
        ["Sega CD"] = "Mega-CD - Sega CD",
        ["Sega 32X"] = "32X",
        ["Sega Saturn"] = "Saturn",
        ["Atari"] = "Atari 2600",
        ["Atari Jaguar"] = "Jaguar",
        ["Atari Lynx"] = "Lynx",
        ["NEC PC Engine / TurboGrafx-16"] = "PC Engine - TurboGrafx-16",
        ["SNK Neo Geo Pocket"] = "Neo Geo Pocket",
        ["SNK Neo Geo Pocket Color"] = "Neo Geo Pocket Color",
        ["Sony PlayStation Vita"] = "PlayStation Vita",
    };

    // "core adında seçili mesela ... nintendoda snes9x seçili ... bu arada snes9x olmaması lazım
    // nintendoda" (kullanıcı geri bildirimi) — kök neden: PlatformName ZATEN doğruysa (ör. "Nintendo"
    // hiç yeniden adlandırılmadı) yukarıdaki yeniden adlandırma tablosu o satıra HİÇ dokunmuyordu, bu
    // yüzden kullanıcının ESKİ kaydedilmiş (bu tablo düzeltilmeden ÖNCEki) PreferredCore/AlternativeCore
    // değerleri (ör. Nintendo: Mesen/Snes9x) hiç senkronize edilmiyordu. Şimdi SyncEmulatorsWithCanonicalTable
    // (bkz. aşağısı) İSİMDEN BAĞIMSIZ, HER satırı (doğru isme yeniden adlandırıldıktan SONRA) bu tabloyla
    // karşılaştırıp senkronize ediyor. Emulators koleksiyonunun varsayılan tohumlaması da AYNI kaynaktan.
    private static readonly (string PlatformName, string PreferredCore, string AlternativeCore, LauncherType LauncherType, string Parameters)[] CanonicalEmulatorDefinitions =
    {
        ("MAME", "MAME", "FBNeo", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("NeoGeo", "FBNeo", "MAME", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Nintendo", "Mesen", "Nestopia", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Super Nintendo", "bsnes", "Snes9x", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PlayStation", "SwanStation", "Beetle PSX HW", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PlayStation 2", "PCSX2", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Genesis", "Genesis Plus GX", "PicoDrive", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Master System", "Genesis Plus GX", "SMS Plus", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PlayStation Portable", "PPSSPP", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PlayStation 3", "RPCS3", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Xbox", "Xemu", "", LauncherType.StandaloneEXE, "\"-dvd_path\" \"%ROM%\""),
        ("Xbox 360", "Xenia", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Dreamcast", "Flycast", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Game Gear", "Genesis Plus GX", "SMS Plus", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Commodore 64", "VICE x64sc", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Amiga", "PUAE", "UAE4ARM", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Atari 2600", "Stella", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Nintendo 64", "Mupen64Plus-Next", "Parallel N64", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("GameCube", "Dolphin", "", LauncherType.StandaloneEXE, "\"-b\" \"-e\" \"%ROM%\""),
        ("Wii", "Dolphin", "", LauncherType.StandaloneEXE, "\"-b\" \"-e\" \"%ROM%\""),
        ("Nintendo Wii U", "Cemu", "", LauncherType.StandaloneEXE, "\"-g\" \"%ROM%\""),
        ("Nintendo Switch", "Ryujinx", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Game Boy", "Gambatte", "SameBoy", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Game Boy Color", "Gambatte", "SameBoy", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Game Boy Advance", "mGBA", "VBA-M", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Nintendo DS", "melonDS", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        // Kullanıcı kararı: GitHub'da Lime3DS/Lime3DS deposu azahar-emu/azahar'a 301 yönlendiriyor
        // (proje yeniden adlandı) — UI'da artık "Azahar" gösteriliyor (bkz. EmulatorNameResolver'daki
        // geriye dönük "Lime3DS" eşlemesi, MigrateLegacyPlatformNames'teki tek seferlik isim göçü).
        ("Nintendo 3DS", "Azahar", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Mega-CD - Sega CD", "Genesis Plus GX", "PicoDrive", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("32X", "PicoDrive", "Genesis Plus GX", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Saturn", "Beetle Saturn", "Kronos", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PlayStation Vita", "Vita3K", "", LauncherType.StandaloneEXE, "\"%ROM%\""),
        ("Atari 5200", "Atari800", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Atari 7800", "ProSystem", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Jaguar", "Virtual Jaguar", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Lynx", "Beetle Lynx", "Handy", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("PC Engine - TurboGrafx-16", "Beetle PCE", "Beetle PCE Fast", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("NEC PC Engine CD", "Beetle PCE Fast", "Mednafen", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("SNK Neo Geo CD", "NeoCD", "", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Neo Geo Pocket", "Beetle NeoPop", "Race", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
        ("Neo Geo Pocket Color", "Beetle NeoPop", "Race", LauncherType.RetroArchCore, "-L \"%CORE%\" \"%ROM%\""),
    };

    // Rename + senkronizasyon TEK adımda: önce eski isim varsa doğrusuna çevirir, SONRA (yeniden
    // adlandırılsın ya da adlandırılmasın) HER satırı CanonicalEmulatorDefinitions'taki güncel
    // Tercih Edilen/Alternatif/Mod/Parametreler ile karşılaştırıp farklıysa günceller. ExecutablePath/
    // CorePath (kullanıcının zaten indirdiği, yeniden kazanılması zor veriler) HİÇ dokunulmaz;
    // SelectedCoreOrEmulatorName sadece artık geçersiz bir isme (ör. eski Alternatif) işaret ediyorsa
    // temizlenir, aşağıdaki seed adımı PreferredCore'a düşürür.
    private static readonly Dictionary<string, (string PreferredCore, string AlternativeCore, LauncherType LauncherType, string Parameters)> CanonicalEmulatorsByPlatform =
        CanonicalEmulatorDefinitions.ToDictionary(
            d => d.PlatformName,
            d => (d.PreferredCore, d.AlternativeCore, d.LauncherType, d.Parameters),
            StringComparer.OrdinalIgnoreCase);

    // Kullanıcı isteği: "Bilgisayar MSDOS ve Windows'u kaldır direk onları kullanmıcaz" — bu iki
    // platform CanonicalEmulatorDefinitions'tan da çıkarıldı; eski settings.json'dan yüklenmiş
    // satırları da burada ayrıca temizleniyor. "Yeni Platform" ise artık kaldırılan "+ Emülatör Ekle"
    // butonunun (bkz. AddEmulatorCommand — silindi) bıraktığı, yanlışlıkla eklenmiş boş satırların adı.
    private static readonly HashSet<string> RemovedPlatformNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bilgisayar (MS-DOS)",
        "Bilgisayar (Windows)",
        "Yeni Platform",
        // Kullanıcı isteği: "xbox one platformlarımızda yok zaten onu sil direk platformlarımızda
        // olmayanlar olmasın emulatör listesinde" — hem katalogda (No-Intro/Redump) karşılığı yok
        // hem de artık desteklenen bir emülatörü yok, satır tamamen kaldırıldı.
        "Xbox One",
        "Microsoft Xbox One",
    };

    private void MigrateLegacyPlatformNames()
    {
        var changed = false;

        foreach (var emulator in Emulators.Where(e => RemovedPlatformNames.Contains(e.PlatformName)).ToList())
        {
            Emulators.Remove(emulator);
            changed = true;
        }

        foreach (var emulator in Emulators)
        {
            if (LegacyPlatformNameRenames.TryGetValue(emulator.PlatformName, out var newName))
            {
                emulator.PlatformName = newName;
                changed = true;
            }

            if (!CanonicalEmulatorsByPlatform.TryGetValue(emulator.PlatformName, out var canonical))
                continue;

            // Kullanıcı kararı: Nintendo 3DS'te "Lime3DS" yerine artık "Azahar" gösteriliyor (bkz.
            // CanonicalEmulatorDefinitions, EmulatorNameResolver) — daha önce elle "Lime3DS" seçilmiş
            // satırlar aşağıdaki geçerlilik kontrolünde "artık geçersiz" sayılıp BOŞA düşmesin diye,
            // ExecutablePath/CorePath (kullanıcının zaten kurduğu, yeniden kazanılması zor) KORUNARAK
            // sadece seçim adı yeni isme taşınıyor (görev talimatı: "mevcut Lime3DS path/settings
            // değerlerini Azahar için migration/fallback olarak kullanabilirsin").
            if (string.Equals(emulator.SelectedCoreOrEmulatorName, "Lime3DS", StringComparison.OrdinalIgnoreCase))
            {
                emulator.SelectedCoreOrEmulatorName = "Azahar";
                changed = true;
            }

            var wasSelectedNameStillValid = emulator.SelectedCoreOrEmulatorName == canonical.PreferredCore
                || emulator.SelectedCoreOrEmulatorName == canonical.AlternativeCore;

            if (emulator.PreferredCore != canonical.PreferredCore)
            {
                emulator.PreferredCore = canonical.PreferredCore;
                changed = true;
            }
            if (emulator.AlternativeCore != canonical.AlternativeCore)
            {
                emulator.AlternativeCore = canonical.AlternativeCore;
                changed = true;
            }
            if (emulator.LauncherType != canonical.LauncherType)
            {
                emulator.LauncherType = canonical.LauncherType;
                changed = true;
            }
            if (emulator.Parameters != canonical.Parameters)
            {
                emulator.Parameters = canonical.Parameters;
                changed = true;
            }

            if (!wasSelectedNameStillValid && !string.IsNullOrWhiteSpace(emulator.SelectedCoreOrEmulatorName))
            {
                emulator.SelectedCoreOrEmulatorName = string.Empty;
                changed = true;
            }
        }

        if (changed)
            ConfigService.SaveDefault(ToAppSettings());
    }

    // Kullanıcı isteği: "cores in içinde her platform için ayrı klasör oluştur hepsi kendi klasöründen
    // okusun" — bu değişiklikten ÖNCE indirilmiş/eklenmiş çekirdekler eski (düz cores\ kökü, cores\
    // RetroArch-Win64\cores\, veya CustomChoices'ta eski bir "+" ekleme yolu) konumlarında kalmış
    // olabilir; her RetroArchCore satırının Tercih Edilen/Alternatif/özel çekirdeklerini KOPYALAYARAK
    // (taşımaz — aynı dosya birden fazla platformda paylaşılıyor olabilir, ör. Genesis Plus GX) kendi
    // cores\RetroAudit\{Platform}\ klasörüne göç ettirir.
    private void MigrateCoreFilesToPlatformFolders()
    {
        var changed = false;

        foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.RetroArchCore))
        {
            foreach (var name in new[] { emulator.PreferredCore, emulator.AlternativeCore })
            {
                if (EmulatorNameResolver.TryGetCoreFileName(name) is { } coreFileName)
                    EnsureCoreInPlatformFolder(emulator.PlatformName, coreFileName);
            }

            var platformFolder = RetroArchInstallerService.PlatformCoresFolder(emulator.PlatformName);
            foreach (var custom in emulator.CustomChoices)
            {
                if (!File.Exists(custom.FilePath)
                    || string.Equals(Path.GetDirectoryName(custom.FilePath), platformFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(platformFolder);
                var destination = Path.Combine(platformFolder, Path.GetFileName(custom.FilePath));
                if (!File.Exists(destination))
                    File.Copy(custom.FilePath, destination);
                custom.FilePath = destination;
                changed = true;
            }
        }

        if (changed)
            ConfigService.SaveDefault(ToAppSettings());
    }

    // Kullanıcı isteği: "standalonelar içinde Emulation içine platformların listesini klasörlerini
    // ekle bütün platformlar olsun gene içinde" — sadece klasörleri oluşturur, dosya taşımaz (bir
    // standalone .exe'sini elle taşımak yanındaki DLL'leri/kaynakları bozabilir, bkz. AddCustomCore
    // yorumu) — kullanıcı Gözat/"+" ile buraya kendi kurulumunu koyar. NOT: "platform bazlı olmasın
    // o zaman onlar sadece emulation klasörüne indirip ordan algılasın" (kullanıcı isteği) — otomatik
    // kurulumu OLAN isimler (PCSX2/RPCS3/Xemu) burada ATLANIYOR, onlar kendi paylaşılan emulatorId
    // klasörünü kullanıyor (bkz. StandaloneEmulatorInstallerService.InstallRootFor), platform
    // klasörüne ihtiyaçları yok.
    private void EnsureEmulationPlatformFolders()
    {
        foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.StandaloneEXE
            && EmulatorNameResolver.TryGetStandaloneId(e.PreferredCore) is null))
            Directory.CreateDirectory(Path.Combine(AppPaths.Emulation, AppPaths.SanitizeFolderName(emulator.PlatformName)));
    }

    // MigrateCoreFilesToPlatformFolders/ReconcileInstalledEmulatorPaths/InstallRetroArch'ın ORTAK
    // yardımcısı: önce bu platformun KENDİ klasörüne bakar, yoksa cores\ altındaki HERHANGİ bir yerde
    // (bulk kurulumun ham çıktısı dahil) arar ve bulursa platform klasörüne KOPYALAR.
    private static string? EnsureCoreInPlatformFolder(string platformName, string coreFileName)
    {
        if (RetroArchInstallerService.FindCoreFile(platformName, coreFileName) is { } alreadyThere)
            return alreadyThere;

        if (RetroArchInstallerService.FindCoreFileAnywhere(coreFileName) is not { } legacyPath)
            return null;

        var platformFolder = RetroArchInstallerService.PlatformCoresFolder(platformName);
        Directory.CreateDirectory(platformFolder);
        var destination = Path.Combine(platformFolder, coreFileName);
        File.Copy(legacyPath, destination, overwrite: true);
        return destination;
    }

    // Bkz. constructor'daki BUG notu — kurulu ama satıra atanmamış emülatörleri düzeltir. Artık
    // CorePath'i bare bir varsayılan dosya adından DEĞİL, satırın SEÇİLİ ismi + EmulatorNameResolver'dan
    // çözüyor (bkz. EmulatorConfig.EffectiveCoreOrEmulatorName) — kullanıcı Alternatif'i seçmişse o
    // çözülür, Tercih Edilen değil.
    private void ReconcileInstalledEmulatorPaths()
    {
        var changed = false;

        if (IsRetroArchInstalled && RetroArchInstallerService.GetInstalledExecutablePath() is { } retroArchExe)
        {
            // NOT: ExecutablePath boş olsun ya da olmasın HER RetroArchCore satırı taranıyor —
            // CorePath çözümlemesi (aşağıda) ExecutablePath'ten BAĞIMSIZ, "Missing Core" durumu tam
            // olarak ExecutablePath zaten dolu ama CorePath'in hâlâ çözülmemiş olduğu satırlarda oluşuyordu.
            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.RetroArchCore))
            {
                if (string.IsNullOrWhiteSpace(emulator.ExecutablePath) || !File.Exists(emulator.ExecutablePath))
                {
                    emulator.ExecutablePath = retroArchExe;
                    changed = true;
                }

                if (!File.Exists(emulator.CorePath)
                    && EmulatorNameResolver.TryGetCoreFileName(emulator.EffectiveCoreOrEmulatorName) is { } coreFileName
                    && EnsureCoreInPlatformFolder(emulator.PlatformName, coreFileName) is { } resolvedCorePath)
                {
                    emulator.CorePath = resolvedCorePath;
                    changed = true;
                }

                emulator.RefreshAvailableChoices();
            }
        }

        foreach (var state in StandaloneEmulatorInstalls)
        {
            if (!state.IsInstalled || StandaloneEmulatorInstallerService.GetInstalledExecutablePath(state.EmulatorId) is not { } exePath)
                continue;

            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.StandaloneEXE
                && EmulatorNameResolver.TryGetStandaloneId(e.EffectiveCoreOrEmulatorName) == state.EmulatorId
                && string.IsNullOrWhiteSpace(e.ExecutablePath)))
            {
                emulator.ExecutablePath = exePath;
                changed = true;
                emulator.RefreshAvailableChoices();
            }
        }

        if (changed)
            ConfigService.SaveDefault(ToAppSettings());
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
    private void BrowseMasterMetadataDb()
    {
        var dialog = new OpenFileDialog
        {
            Title = "MasterMetadata.db Seçin",
            Filter = "SQLite veritabanı (*.db)|*.db|Tüm dosyalar (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
            MasterMetadataDbPath = dialog.FileName;
    }

    // "Core Adı" sütunundaki Mod rozet-ComboBox'ının ItemsSource'u (bkz. LauncherTypeOption yorumu) —
    // sadece 2 sabit seçenek, satırdan bağımsız, bu yüzden tek bir paylaşılan liste yeterli.
    public IReadOnlyList<LauncherTypeOption> LauncherTypeOptions { get; } = new[]
    {
        new LauncherTypeOption(LauncherType.RetroArchCore, "RetroArch"),
        new LauncherTypeOption(LauncherType.StandaloneEXE, "Standalone"),
    };

    // Platform başına emülatör kayıtları; DataGrid'e doğrudan bağlanır.
    // "emülatör eşleşmelerini kontrol et" (kullanıcı isteği, tam tablo verildi) — bu geçişte AYRICA
    // ÖNEMLİ bir düzeltme yapıldı: PlatformName değerlerinin çoğu (ör. "Sega Genesis", "Nintendo
    // GameCube", "PSP", "Sega Saturn") RetroAudit.db'deki GERÇEK PlatformDisplayName (bkz.
    // PlatformDisplayNameMap.Resolve) ile EŞLEŞMİYORDU — LaunchWithEmulator (MainViewModel) satırı
    // game.PlatformDisplayName'e göre ARADIĞI için, bu satırlar için BAŞLAT'ın hiçbir zaman
    // çalışmamış olması gerekiyordu (ör. "Sega Genesis" ≠ gerçek "Genesis"). Şimdi PlatformName'ler
    // PlatformDisplayNameMap çıktısıyla BİREBİR — ör. "Genesis", "GameCube", "Wii", "PlayStation
    // Portable", "Saturn", "32X", "Mega-CD - Sega CD", "Jaguar", "Lynx", "Neo Geo Pocket (Color)",
    // "PC Engine - TurboGrafx-16" (bkz. PlatformDisplayNameMap.cs). Katalogda (No-Intro/Redump
    // tabanlı) HİÇ karşılığı olmayan platformlar (MAME, NeoGeo arcade, Wii U, Switch, Vita, Xbox One,
    // Neo Geo CD, PC Engine CD) kullanıcının isteği üzerine yine de tabloda —
    // eşleşen oyun olmayacağı için BAŞLAT'ları hiç tetiklenmez, ileride bir katalog kaynağı eklenirse
    // hazır dururlar. CorePath artık burada SABİTLENMİYOR — SelectedCoreOrEmulatorName (varsayılan:
    // PreferredCore, bkz. EmulatorConfig.EffectiveCoreOrEmulatorName) üzerinden DownloadCoreForEmulator/
    // ReconcileInstalledEmulatorPaths ile dinamik çözülüyor (bkz. EmulatorNameResolver).
    public ObservableCollection<EmulatorConfig> Emulators { get; } = new(
        CanonicalEmulatorDefinitions.Select(d => new EmulatorConfig
        {
            PlatformName = d.PlatformName,
            PreferredCore = d.PreferredCore,
            AlternativeCore = d.AlternativeCore,
            LauncherType = d.LauncherType,
            Parameters = d.Parameters,
        }));

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

    // İlgili emülatörün .exe dosyasını seçmek için dosya tarayıcısı açar. Kullanıcı sorusu: "onun
    // gözat ı mod zaten doğrumu?" — ÖNCEDEN her zaman AppPaths.Emulation açılıyordu, Mod'dan (RetroArchCore/
    // StandaloneEXE) BAĞIMSIZ, yani hayır, doğru değildi — şimdi düzeltildi: RetroArchCore ise kendi
    // indirdiğimiz ThirdParty\RetroArch\, StandaloneEXE ise (PreferredCore bir otomatik kurulum id'siyle
    // eşleşiyorsa) o emülatörün kendi ThirdParty\{id}\ klasörü öncelikli; hiçbiri yoksa Emulation'a düşer.
    [RelayCommand]
    private void BrowseEmulatorPath(EmulatorConfig? emulator)
    {
        if (emulator is null)
            return;

        var initialDirectory = AppPaths.Emulation;
        if (emulator.LauncherType == LauncherType.RetroArchCore)
        {
            if (Directory.Exists(RetroArchInstallerService.InstallRoot))
                initialDirectory = RetroArchInstallerService.InstallRoot;
        }
        else
        {
            // Otomatik kurulumu olan (PCSX2/RPCS3/Xemu) satırlarda kendi ThirdParty\{id}\ klasörü
            // öncelikli (gerçekten oraya kuruldu); diğerlerinde bu platformun kendi Emulation\{Platform}\
            // klasörü (bkz. EnsureEmulationPlatformFolders, kullanıcı isteği: "her platform için ayrı
            // klasör ... standalonelar içinde de gene içinde olsun").
            var standaloneInstallRoot = StandaloneEmulatorInstallerService.InstallRootFor(emulator.PreferredCore);
            var platformEmulationFolder = Path.Combine(AppPaths.Emulation, AppPaths.SanitizeFolderName(emulator.PlatformName));
            if (Directory.Exists(standaloneInstallRoot))
                initialDirectory = standaloneInstallRoot;
            else if (Directory.Exists(platformEmulationFolder))
                initialDirectory = platformEmulationFolder;
        }

        // Kullanıcı kendi emülatörünü başka bir yere kurduysa oradan da seçebilir — yukarıdaki
        // sadece bir başlangıç önerisi, zorunlu değil.
        var dialog = new OpenFileDialog
        {
            Title = "Emülatör Çalıştırılabilir Dosyası",
            Filter = "Çalıştırılabilir dosyalar (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*",
            InitialDirectory = initialDirectory,
        };

        if (dialog.ShowDialog() == true)
        {
            emulator.ExecutablePath = dialog.FileName;
            emulator.RefreshAvailableChoices();
        }
    }

    // "core adındaki mame yazan açılır liste ... onun içine + add butonu ekle ordan tıkladıklarında
    // seçip ekleyebilsinler ekledikleri de listeye eklensin" (kullanıcı isteği) — Gözat'tan (satırın
    // ExecutablePath'ini SABİTLER) FARKLI: bu, Core Adı dropdown'ına PreferredCore/AlternativeCore
    // DIŞINDA YENİ bir seçenek ekler (bkz. EmulatorConfig.AddCustomChoice) — RetroArchCore satırlarında
    // bir çekirdek .dll'i, StandaloneEXE satırlarında bir .exe seçtirir.
    [RelayCommand]
    private void AddCustomCore(EmulatorConfig? emulator)
    {
        if (emulator is null)
            return;

        var isRetroArchCore = emulator.LauncherType == LauncherType.RetroArchCore;

        // Kullanıcı isteği: "+'ya basınca burayı açsın retroarch da ... cores in içinde her platform
        // için ayrı klasör" — RetroArchCore satırlarında doğrudan bu platformun KENDİ klasörü
        // (cores\RetroAudit\{Platform}\) açılır; henüz yoksa genel cores\ köküne, o da yoksa
        // Emulation'a düşer.
        var platformCoresFolder = RetroArchInstallerService.PlatformCoresFolder(emulator.PlatformName);
        var platformEmulationFolder = Path.Combine(AppPaths.Emulation, AppPaths.SanitizeFolderName(emulator.PlatformName));
        var initialDirectory = isRetroArchCore
            ? (Directory.Exists(platformCoresFolder) ? platformCoresFolder
                : Directory.Exists(RetroArchInstallerService.CoresFolder) ? RetroArchInstallerService.CoresFolder
                : AppPaths.Emulation)
            : (Directory.Exists(platformEmulationFolder) ? platformEmulationFolder : AppPaths.Emulation);

        var dialog = new OpenFileDialog
        {
            Title = isRetroArchCore ? "Çekirdek Dosyası Seç" : "Emülatör Çalıştırılabilir Dosyası Seç",
            Filter = isRetroArchCore
                ? "RetroArch çekirdeği (*.dll)|*.dll"
                : "Çalıştırılabilir dosya (*.exe)|*.exe",
            InitialDirectory = initialDirectory,
        };

        if (dialog.ShowDialog() != true)
            return;

        var name = Path.GetFileNameWithoutExtension(dialog.FileName);
        var filePath = dialog.FileName;

        // Kullanıcı isteği: "seçilen dll'i oraya taşısın" — RetroArchCore'da seçilen çekirdek, bu
        // platformun KENDİ klasörüne (cores\RetroAudit\{Platform}\) taşınır (kullanıcı zaten o
        // klasörü açıp seçtiği için genelde no-op, ama başka bir yerden seçtiyse tutarlı kalsın diye).
        // StandaloneEXE'ye dokunulmuyor — bir emülatörün .exe'si genelde yanındaki DLL'lere bağımlı,
        // taşımak kurulumu bozabilir.
        if (isRetroArchCore)
        {
            Directory.CreateDirectory(platformCoresFolder);
            var destinationPath = Path.Combine(platformCoresFolder, Path.GetFileName(dialog.FileName));
            if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(dialog.FileName, destinationPath);
                filePath = destinationPath;
            }
        }

        emulator.AddCustomChoice(name, filePath);
        ConfigService.SaveDefault(ToAppSettings());
    }

    // "ayarla bozulunca önerilenleri seçmek ayarlamak için buton ekle default ayarladığımız önerilen
    // düzene çevirsin" (kullanıcı isteği) — kullanıcı dropdown'dan yanlışlıkla Alternatif/özel bir
    // çekirdek seçmiş olsa bile, TÜM satırların Core Adı seçimini tek tıkla PreferredCore'a (bkz.
    // CanonicalEmulatorDefinitions) geri döndürür. PreferredCore/AlternativeCore/Mod/Parametreler'in
    // kendisine dokunulmuyor — onlar zaten UI'dan değiştirilemiyor, bozulabilecek TEK şey seçim.
    [RelayCommand]
    private void ResetToRecommendedCores()
    {
        foreach (var emulator in Emulators)
            emulator.SelectedCoreOrEmulatorName = emulator.PreferredCore;

        ConfigService.SaveDefault(ToAppSettings());
        RequestShowMessage?.Invoke("Tüm platformlar önerilen çekirdek/emülatöre sıfırlandı.");
    }

    // "core adının yanındaki download butonu core adında seçili emulatörü indirecek" (kullanıcı
    // isteği) — "Durum" sütunundaki İndir'in AKSİNE (o her zaman TÜM RetroArch çekirdeklerini/sabit
    // bir standalone'u kurar) bu buton SADECE "Core Adı" dropdown'ında O AN SEÇİLİ olan tek ismi
    // hedefler: RetroArchCore satırında o çekirdeği (bkz. RetroArchInstallerService.DownloadCoreAsync,
    // tüm cores.7z'yi yeniden indirmeden), StandaloneEXE satırında (eşleşen bir otomatik kurulum
    // varsa, bkz. EmulatorNameResolver.TryGetStandaloneId) o emülatörü indirir; otomatik kurulumu
    // olmayan bir standalone seçiliyse (ör. Dolphin, Cemu, Ryujinx — "daha sonra hepsinin tek tek
    // bağlarız" kullanıcı kararı) Gözat diyaloğuna düşer.
    [RelayCommand]
    private async Task DownloadCoreForEmulator(EmulatorConfig emulator)
    {
        var selectedName = emulator.EffectiveCoreOrEmulatorName;
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            RequestShowMessage?.Invoke("Önce bu satır için Tercih Edilen Core/Emülatör alanını doldurun.");
            return;
        }

        if (emulator.LauncherType == LauncherType.RetroArchCore)
        {
            var coreFileName = EmulatorNameResolver.TryGetCoreFileName(selectedName);
            if (coreFileName is null)
            {
                RequestShowMessage?.Invoke($"\"{selectedName}\" için bilinen bir RetroArch çekirdeği eşlemesi yok.");
                return;
            }

            // Tercih Edilen/Alternatif ise bizim TEMİZ platform klasörümüze, katalogdaki bir isimse
            // (kullanıcı isteği: "bizim klasörde karışıklık olmasın") RetroArch'ın kendi ham cores\
            // köküne iner.
            var isPreferredOrAlternative = string.Equals(selectedName, emulator.PreferredCore, StringComparison.OrdinalIgnoreCase)
                || string.Equals(selectedName, emulator.AlternativeCore, StringComparison.OrdinalIgnoreCase);

            try
            {
                var corePath = isPreferredOrAlternative
                    ? await RetroArchInstallerService.DownloadCoreAsync(emulator.PlatformName, coreFileName, CancellationToken.None)
                    : await RetroArchInstallerService.DownloadCoreAsync(coreFileName, CancellationToken.None);
                emulator.CorePath = corePath;
                emulator.RefreshAvailableChoices();
                ConfigService.SaveDefault(ToAppSettings());
                RequestShowMessage?.Invoke($"{coreFileName} indirildi.");
            }
            catch (Exception ex)
            {
                RequestShowMessage?.Invoke($"{coreFileName} indirilemedi: {ex.Message}");
            }
            return;
        }

        var standaloneId = EmulatorNameResolver.TryGetStandaloneId(selectedName);
        var state = standaloneId is not null ? StandaloneEmulatorInstalls.FirstOrDefault(s => s.EmulatorId == standaloneId) : null;
        if (state is not null)
        {
            await InstallStandaloneEmulator(state);
            return;
        }

        RequestShowMessage?.Invoke($"\"{selectedName}\" için henüz otomatik indirme yok — Emülatör Yolu sütunundaki Gözat ile kendi kurulumunuzu gösterin.");
        BrowseEmulatorPath(emulator);
    }

    // "RetroArch İndir & Kur" (kullanıcı isteği) — resmi buildbot'tan RetroArch + tüm çekirdekleri
    // indirip AppPaths.ThirdParty\RetroArch\'a açar, ardından RetroArchCore modundaki HER satırın
    // ExecutablePath'ini (RetroArch.exe) ve CorePath'ini (o satırın zaten önerilen .dll dosya adını
    // yeni indirilen cores klasöründe arayıp bulursa) otomatik doldurur — kullanıcı hiçbir yol
    // yazmadan RetroArch tarafını tek tıkla kurmuş olur. Sonunda ConfigService.SaveDefault ile HEMEN
    // diske yazılıyor — normalde ayarlar sadece "Kaydet"e basınca kaydediliyor (bkz. SaveSettings
    // yorumu), ama burada kaydetmeyi unutmak, "Durum" sütununun (ThirdParty\ klasörüne bakan üstteki
    // tik/kaldır düğmesinin AKSİNE) programı kapatıp açtıktan sonra hâlâ "Download" göstermesine yol
    // açıyordu — kurulum kendi başına tamamlanmış bir eylem, "kaydedilmeyi bekleyen bir düzenleme" değil.
    [RelayCommand]
    private async Task InstallRetroArch()
    {
        IsInstallingRetroArch = true;
        RetroArchInstallProgress = 0;
        IsInstallingRetroArchIndeterminate = false;
        RetroArchInstallStatus = "Başlatılıyor...";
        try
        {
            var progress = new Progress<(string Message, double Percent, bool IsIndeterminate)>(p =>
            {
                RetroArchInstallStatus = p.Message;
                RetroArchInstallProgress = p.Percent;
                IsInstallingRetroArchIndeterminate = p.IsIndeterminate;
            });

            var result = await RetroArchInstallerService.DownloadAndInstallAsync(progress, CancellationToken.None);

            // NOT: İndirme/kurulum ZATEN başarıyla tamamlandı (yukarıdaki await patlamadıysa) — toolbar'daki
            // tik işareti bunu HEMEN yansıtmalı. Aşağıdaki döngü (~40 satır, disk kopyalama dahil) bir
            // satırda beklenmedik bir istisna fırlatırsa, bu satır döngüden SONRA olsaydı toolbar yanlışlıkla
            // "kurulu değil" göstermeye devam ederdi (kullanıcı testiyle bulunan gerçek bir belirti — bkz.
            // InstallStandaloneEmulator'daki AYNI düzeltme).
            IsRetroArchInstalled = true;

            var updatedCount = 0;
            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.RetroArchCore))
            {
                emulator.ExecutablePath = result.ExecutablePath;

                // Bulk arşiv (RetroArch_cores.7z) kendi içinde bir üst klasörle geliyor (.../cores/
                // RetroArch-Win64/cores/*.dll) — EnsureCoreInPlatformFolder bunu HAM haliyle (recursive)
                // arayıp bu satırın KENDİ cores\RetroAudit\{Platform}\ klasörüne kopyalar (kullanıcı
                // isteği: "her platform için ayrı klasör ... hepsi kendi klasöründen okusun"). Hangi
                // çekirdek aranacağı satırın SEÇİLİ ismine göre belirleniyor (bkz. EmulatorNameResolver).
                if (EmulatorNameResolver.TryGetCoreFileName(emulator.EffectiveCoreOrEmulatorName) is { } coreFileName
                    && EnsureCoreInPlatformFolder(emulator.PlatformName, coreFileName) is { } resolvedCorePath)
                {
                    emulator.CorePath = resolvedCorePath;
                }

                emulator.RefreshAvailableChoices();
                updatedCount++;
            }

            ConfigService.SaveDefault(ToAppSettings());
            RequestShowMessage?.Invoke($"RetroArch kuruldu ve {updatedCount} platforma otomatik atandı.");
        }
        catch (Exception ex)
        {
            RequestShowMessage?.Invoke($"RetroArch kurulamadı: {ex.Message}");
        }
        finally
        {
            IsInstallingRetroArch = false;
        }
    }

    // "kaldırma butonu ekle" (kullanıcı isteği) — InstallRetroArch'ın tersi: ThirdParty\RetroArch\
    // klasörünü siler. Sadece BİZİM bu klasöre işaret eden ExecutablePath/CorePath değerlerini
    // temizler (StartsWith kontrolü) — kullanıcının elle "Gözat" ile başka bir yerdeki RetroArch'ı
    // göstermiş olabileceği satırlara dokunmaz.
    [RelayCommand]
    private void UninstallRetroArch()
    {
        try
        {
            RetroArchInstallerService.Uninstall();
            IsRetroArchInstalled = false;

            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.RetroArchCore))
            {
                if (emulator.ExecutablePath.StartsWith(RetroArchInstallerService.InstallRoot, StringComparison.OrdinalIgnoreCase))
                    emulator.ExecutablePath = string.Empty;
                if (emulator.CorePath.StartsWith(RetroArchInstallerService.InstallRoot, StringComparison.OrdinalIgnoreCase))
                    emulator.CorePath = string.Empty;
                emulator.RefreshAvailableChoices();
            }

            ConfigService.SaveDefault(ToAppSettings());
            RequestShowMessage?.Invoke("RetroArch kaldırıldı.");
        }
        catch (Exception ex)
        {
            RequestShowMessage?.Invoke($"RetroArch kaldırılamadı: {ex.Message}");
        }
    }

    // "Standalonelar içinde indirme koyacaz ayrıca" (kullanıcı isteği) — InstallRetroArch'ın
    // StandaloneEXE karşılığı. EmulatorNameResolver.TryGetStandaloneId, satırın SEÇİLİ ismini (bkz.
    // EffectiveCoreOrEmulatorName — Tercih Edilen VEYA kullanıcının seçtiği Alternatif) bu state'in
    // EmulatorId'siyle eşleştiriyor — indirilen .exe, eşleşen HER satıra otomatik atanıyor
    // (RetroArch'ın "tüm RetroArchCore satırlarına ata" mantığının aynısı).
    [RelayCommand]
    private async Task InstallStandaloneEmulator(StandaloneEmulatorInstallState state)
    {
        state.IsInstalling = true;
        state.Progress = 0;
        state.IsIndeterminate = false;
        state.Status = "Başlatılıyor...";
        try
        {
            var progress = new Progress<(string Message, double Percent, bool IsIndeterminate)>(p =>
            {
                state.Status = p.Message;
                state.Progress = p.Percent;
                state.IsIndeterminate = p.IsIndeterminate;
            });

            var settingsEntry = EmulatorDownloadSettings.FirstOrDefault(e => e.EmulatorId == state.EmulatorId);
            var source = settingsEntry is null ? null : new EmulatorDownloadSourceSetting { SourceType = settingsEntry.SourceType, Source = settingsEntry.Source };
            var result = await StandaloneEmulatorInstallerService.DownloadAndInstallAsync(state.EmulatorId, source, progress, CancellationToken.None);

            // NOT (kullanıcı testiyle bulunan gerçek belirti): "indirme bitti core adı listesine geldi
            // ama üstteki (toolbar) tik gözükmedi, pencereyi kapatıp açınca gözüktü" — indirme ZATEN
            // başarıyla tamamlandı (yukarıdaki await patlamadıysa), toolbar'daki tik bunu HEMEN
            // yansıtmalı. Aşağıdaki döngü satır satır ExecutablePath atayıp AvailableChoices
            // yeniliyordu; bu satır döngüden SONRA olduğu için döngüde beklenmedik bir istisna
            // toolbar'ı yanlışlıkla "kurulu değil" göstermeye devam ettiriyordu.
            state.IsInstalled = true;

            var updatedCount = 0;
            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.StandaloneEXE
                && EmulatorNameResolver.TryGetStandaloneId(e.EffectiveCoreOrEmulatorName) == state.EmulatorId))
            {
                emulator.ExecutablePath = result.ExecutablePath;
                emulator.RefreshAvailableChoices();
                updatedCount++;
            }

            ConfigService.SaveDefault(ToAppSettings());
            RequestShowMessage?.Invoke(updatedCount > 0
                ? $"{state.DisplayName} kuruldu ve {updatedCount} platforma otomatik atandı."
                : $"{state.DisplayName} kuruldu.");
        }
        catch (StandaloneEmulatorInstallerService.ManualDownloadUrlRequiredException)
        {
            // Görev talimatı: "Manual URL yoksa kullanıcıya 'Manual URL required' / 'Manuel indirme
            // linki gerekli' benzeri status göster" — genel hata mesajından AYRI, Ayarlar > Emülatörler
            // sekmesindeki "İndirme Kaynağı" alanına yönlendiren net bir durum metni.
            state.Status = "Manuel indirme linki gerekli";
            RequestShowMessage?.Invoke($"{state.DisplayName}: Manuel indirme linki gerekli. Ayarlar'daki \"İndirme Kaynağı\" bölümünden bu emülatör için bir URL tanımlayın.");
        }
        catch (Exception ex)
        {
            state.Status = "Hata";
            RequestShowMessage?.Invoke($"{state.DisplayName} kurulamadı: {ex.Message}");
        }
        finally
        {
            state.IsInstalling = false;
        }
    }

    // "kaldırma butonu ekle" (kullanıcı isteği) — InstallStandaloneEmulator'ın tersi: ThirdParty\
    // {EmulatorId}\ klasörünü siler, sadece BİZİM bu klasöre işaret eden ExecutablePath'leri temizler.
    [RelayCommand]
    private void UninstallStandaloneEmulator(StandaloneEmulatorInstallState state)
    {
        try
        {
            StandaloneEmulatorInstallerService.Uninstall(state.EmulatorId);
            state.IsInstalled = false;

            var installRoot = StandaloneEmulatorInstallerService.InstallRootFor(state.EmulatorId);
            foreach (var emulator in Emulators.Where(e => e.LauncherType == LauncherType.StandaloneEXE
                && EmulatorNameResolver.TryGetStandaloneId(e.EffectiveCoreOrEmulatorName) == state.EmulatorId))
            {
                if (emulator.ExecutablePath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                    emulator.ExecutablePath = string.Empty;
                emulator.RefreshAvailableChoices();
            }

            ConfigService.SaveDefault(ToAppSettings());
            RequestShowMessage?.Invoke($"{state.DisplayName} kaldırıldı.");
        }
        catch (Exception ex)
        {
            RequestShowMessage?.Invoke($"{state.DisplayName} kaldırılamadı: {ex.Message}");
        }
    }

    // Emülatörler tablosundaki "Durum" sütunundaki tek "İndir" butonu (kullanıcı mockup'ı: Ready /
    // Download / Missing Core) — satırın Mod'una göre YUKARIDAKİ paylaşılan komutlardan doğru olanı
    // tetikler: RetroArchCore ise InstallRetroArch (tüm RetroArchCore satırlarını günceller),
    // StandaloneEXE ise PreferredCore'u StandaloneEmulatorInstalls'taki bir id ile eşleşiyorsa
    // InstallStandaloneEmulator. Eşleşme yoksa (ör. Dolphin/Xenia gibi henüz otomatik kurulumu
    // yazılmamış emülatörler — "daha sonra hepsinin tek tek bağlarız" kullanıcı isteği) şimdilik
    // Gözat diyaloğuna düşer; StandaloneEmulatorInstallerService.Sources'a o emülatör eklendiğinde
    // bu satır BAŞKA BİR DEĞİŞİKLİK GEREKMEDEN otomatik çalışmaya başlar.
    [RelayCommand]
    private async Task InstallForEmulatorRow(EmulatorConfig emulator)
    {
        if (emulator.LauncherType == LauncherType.RetroArchCore)
        {
            await InstallRetroArch();
            return;
        }

        var standaloneId = EmulatorNameResolver.TryGetStandaloneId(emulator.EffectiveCoreOrEmulatorName);
        var state = standaloneId is not null ? StandaloneEmulatorInstalls.FirstOrDefault(s => s.EmulatorId == standaloneId) : null;
        if (state is not null)
        {
            await InstallStandaloneEmulator(state);
            return;
        }

        BrowseEmulatorPath(emulator);
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
        settings.MasterMetadataDbPath = MasterMetadataDbPath;
        settings.RowHeight = RowHeight;
        settings.ShowVersionsAsSingleCard = ShowVersionsAsSingleCard;
        settings.GroupPlatformsByCategory = GroupPlatformsByCategory;
        settings.PlatformListDisplayMode = PlatformListDisplayMode;
        settings.RegionColumnDisplayMode = RegionColumnDisplayMode;
        settings.ProviderDesignMode = ProviderDesignMode;
        settings.ArtworkMaxDimension = ArtworkMaxDimension;
        settings.CoreChoiceDisplayMode = CoreChoiceDisplayMode;
        settings.CategoryVisibility = CategoryOptions.ToDictionary(o => o.Key, o => o.IsVisible);
        settings.Emulators = Emulators.ToList();
        settings.RegionPriority = RegionPriority.ToList();
        settings.Commands = Commands.ToList();
        settings.EmulatorDownloadSources = EmulatorDownloadSettings
            .ToDictionary(e => e.EmulatorId, e => new EmulatorDownloadSourceSetting { SourceType = e.SourceType, Source = e.Source.Trim() });
        return settings;
    }

    // İçe aktarılan AppSettings verisini ekrandaki alanlara/ObservableCollection'lara uygular.
    private void LoadFromAppSettings(AppSettings settings)
    {
        ContextMenuDisplayMode = settings.ContextMenuDisplayMode;
        MasterMetadataDbPath = settings.MasterMetadataDbPath;
        RowHeight = settings.RowHeight;
        ShowVersionsAsSingleCard = settings.ShowVersionsAsSingleCard;
        GroupPlatformsByCategory = settings.GroupPlatformsByCategory;
        PlatformListDisplayMode = settings.PlatformListDisplayMode;
        RegionColumnDisplayMode = settings.RegionColumnDisplayMode;
        ProviderDesignMode = settings.ProviderDesignMode;
        ArtworkMaxDimension = settings.ArtworkMaxDimension;
        CoreChoiceDisplayMode = settings.CoreChoiceDisplayMode;
        BuildCategoryOptions(settings);

        Emulators.Clear();
        foreach (var emulator in settings.Emulators)
        {
            if (string.IsNullOrWhiteSpace(emulator.SelectedCoreOrEmulatorName))
                emulator.SelectedCoreOrEmulatorName = emulator.PreferredCore;
            Emulators.Add(emulator);
            emulator.RefreshAvailableChoices();
        }

        RegionPriority.Clear();
        foreach (var region in settings.RegionPriority)
            RegionPriority.Add(region);

        foreach (var entry in EmulatorDownloadSettings)
        {
            if (settings.EmulatorDownloadSources.TryGetValue(entry.EmulatorId, out var saved))
            {
                entry.SourceType = saved.SourceType;
                entry.Source = saved.Source;
            }
        }

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
