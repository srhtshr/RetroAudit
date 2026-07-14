using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroAudit.Services;

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

// Tablodaki (DataGrid) "Bölge" sütununun gösterim biçimi — Ayarlar > Arayüz'den değiştirilir
// (kullanıcı isteği: "Bayrak Text / Text Bayrak / Bayrak / Sadece Text seçeneği koy").
public enum RegionColumnDisplayMode
{
    FlagAndText,
    TextAndFlag,
    FlagOnly,
    TextOnly,
}

public enum ProviderDesignMode
{
    Classic,
    Modern,
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

// "seçenek koy bide oraya önerilen ve alternatifleri göster veya hepsini göster diye seçilen ayara
// göre göstersin" (kullanıcı isteği) — Core Adı dropdown'ının açılır listesinde SADECE Tercih Edilen/
// Alternatif mi, yoksa "+"la eklenenler ve katalog çekirdekleri de (bkz. EmulatorConfig.AvailableChoices,
// EmulatorNameResolver.GetAdditionalCoreNames) dahil mi gösterilsin. Global bir ayar — tüm satırları
// aynı anda etkiler (bkz. EmulatorConfig.CoreChoiceDisplayMode static alanı).
public enum CoreChoiceDisplayMode
{
    PreferredAndAlternativeOnly,
    ShowAll,
}

// Bir platformun emülatörünün NASIL başlatılacağı — bkz. LaunchEngine, EmulatorConfig.LauncherType.
// RetroArchCore: ExecutablePath bir RetroArch.exe'ye işaret eder, CorePath o platformun libretro
// çekirdeğine (.dll); StandaloneEXE: ExecutablePath doğrudan bağımsız emülatörün kendi .exe'si,
// CorePath kullanılmaz.
public enum LauncherType
{
    RetroArchCore,
    StandaloneEXE,
}

// Bir standalone emülatörün indirme kaynağının TÜRÜ — görev talimatı: "API kısmında API linki, API
// yoksa Direct URL, tek sütun yeterli". GitHub Releases varsa GitHubReleases + TAM API adresi; yoksa
// (Dolphin/Ryujinx gibi) DirectUrl + admin'in bulduğu gerçek indirme linki (bkz.
// StandaloneEmulatorInstallerService.ResolveSourceAsync).
public enum DownloadSourceType
{
    // Source = GitHub Releases API adresi, TAM URL olarak (ör.
    // "https://api.github.com/repos/PCSX2/pcsx2/releases/latest") — kullanıcı isteği: "kaynakta tam
    // adres yazsana". Kısa "owner/repo" biçimi de kabul edilir (geriye dönük/kolaylık). En son
    // release'ten Windows'a uygun bir asset otomatik seçilir (bkz.
    // StandaloneEmulatorInstallerService.ResolveGitHubReleaseWindowsAssetAsync).
    GitHubReleases,
    // Source = doğrudan indirilebilir bir dosya URL'i — hiçbir API çağrısı/asset seçimi yapılmadan
    // AYNEN kullanılır.
    DirectUrl,
    // RPCS3'e özel: GitHub Releases kullanmıyor, kendi JSON güncelleme API'sini kullanıyor (bkz.
    // StandaloneEmulatorInstallerService.ResolveRpcs3DownloadUrlAsync) — kullanıcı sorusu "apiden
    // çekmedik mi onu" — evet, çekiyoruz, ama GERÇEK istek her seferinde OS sürümü/rastgele hash
    // içerdiğinden Kaynak'ta SABİT bir metin olarak tutulamaz; Kaynak alanı sadece BİLGİLENDİRME
    // amaçlı gösterilir (salt-okunur), gerçek istek kod tarafında kuruluyor.
    BuiltInApi,
}

// Ayarlar > Komutlar sekmesindeki "Emülatör İndirme Kaynakları" listesinin TEK bir satırı (bkz.
// SettingsViewModel.EmulatorDownloadSources) — kalıcı hâli. "Kaynak değiştirilirse program yeniden
// derlenmeden yeni kaynak kullanılabilsin" (görev talimatı) — bu yüzden hardcoded bir C# sözlüğü
// DEĞİL, AppSettings üzerinden diskten okunan/yazılan veri.
public sealed class EmulatorDownloadSourceSetting
{
    public DownloadSourceType SourceType { get; set; } = DownloadSourceType.GitHubReleases;
    public string Source { get; set; } = string.Empty;
}

// Tek bir platform için emülatör başlatma ayarları.
// PreferredCore/AlternativeCore, hangi emülatör(ler)in önerildiğini kaydeder (ör. "Mesen" / "Snes9x")
// — sadece bilgilendirme amaçlı bir ETİKET, LaunchEngine bunu OKUMAZ. ExecutablePath, LauncherType'a
// göre İKİ FARKLI ŞEYİ ifade eder: RetroArchCore modunda RetroArch.exe'nin kendi yolu, StandaloneEXE
// modunda emülatörün doğrudan kendi .exe'si — core adıyla karışmasın diye ayrı tutuldu. CorePath,
// SADECE RetroArchCore modunda kullanılan, o platformun libretro çekirdek dosyasının (.dll) yolu —
// Parameters şablonundaki "%CORE%" yer tutucusu bununla değiştirilir. "%ROM%" her iki modda da
// seçili oyunun dosya yoluyla değiştirilir (bkz. LaunchEngine.Launch).
// ObservableObject: "RetroArch İndir & Kur" gibi kod tarafından yapılan toplu güncellemelerin
// (bkz. RetroArchInstallerService, SettingsViewModel.InstallRetroArch) Emülatörler tablosunda
// ANINDA görünmesi için — düz bir POCO'ydu, WPF DataGrid INotifyPropertyChanged olmadan zaten
// EKRANDA olan satırların programatik değişikliklerini yansıtmıyordu. [ObservableProperty]
// alanları PascalCase property üretiyor, mevcut "new() { PlatformName = ... }" nesne
// başlatıcıları (bkz. SettingsViewModel.Emulators) DEĞİŞMEDEN çalışmaya devam ediyor.
// "Core Adı" ComboBox'ının tek bir öğesi (bkz. EmulatorConfig.AvailableChoices) — Name gerçek
// seçim değeri (SelectedCoreOrEmulatorName/EmulatorNameResolver'a giden), Label ise kullanıcıya
// gösterilen metin. ToString() da AYRICA Label döndürüyor — WPF ComboBox'ın kapalı kutu görünümü
// bazen DisplayMemberPath'i atlayıp ham ToString()'e düşebildiği için, hangi yolu seçerse seçsin
// sonuç HER ZAMAN doğru metin olsun diye.
public sealed class CoreChoiceOption
{
    public string Name { get; }
    public string Label { get; }

    // "açıklamalarıda mouse u üstüne getirince gözüksün" (kullanıcı isteği) — bkz.
    // EmulatorNameResolver.TryGetDescription, SettingsWindow.xaml CoreChoiceItemStyle ToolTip.
    public string? Description { get; }

    // "Önerilen ve Alternatiflerin rengi farklı olsun en üstte olsunlar" (kullanıcı isteği) — bkz.
    // EmulatorConfig.AvailableChoices (sıralama) ve SettingsWindow.xaml CoreChoiceItemStyle (renk).
    public bool IsPreferredOrAlternative { get; }

    // "olmayanları listeden silmede yanda yok olarak işaretle ... download edecez çünkü olmayanları"
    // (kullanıcı isteği) — diskte bulunamayan seçenekler ARTIK LİSTEDEN GİZLENMİYOR, diskte bulununcaya
    // kadar seçilebilir/indirilebilir kalıyor (bkz. SettingsWindow.xaml CoreChoiceItemStyle,
    // DownloadCoreForEmulatorCommand zaten SEÇİLİ olanı indiriyor). NOT: "yok yazma zaten yanında
    // download butonu çıkıyor ismini silik gri yazabilirsin" (kullanıcı isteği) — Label'a ARTIK
    // "(Yok)" EKLENMİYOR, sade isim + soluk renk (bkz. CoreChoiceItemStyle IsInstalled=False trigger)
    // yeterli, indirme ikonu zaten eksik olduğunu ayrıca gösteriyor.
    public bool IsInstalled { get; }

    public CoreChoiceOption(string name, string label, string? description = null, bool isPreferredOrAlternative = false, bool isInstalled = true)
    {
        Name = name;
        Label = label;
        Description = description;
        IsPreferredOrAlternative = isPreferredOrAlternative;
        IsInstalled = isInstalled;
    }

    public override string ToString() => Label;
}

// "core adındaki mame yazan açılır liste ... onun içine + add butonu ekle ordan tıkladıklarında
// seçip ekleyebilsinler ekledikleri de listeye eklensin" (kullanıcı isteği) — kullanıcının Core Adı
// dropdown'ına elle (dosya seçerek) eklediği, PreferredCore/AlternativeCore DIŞINDAKİ bir çekirdek/
// emülatör. FilePath kalıcı olarak saklanıyor çünkü EmulatorNameResolver bu ismi TANIMIYOR — normal
// akıştaki gibi isimden dosya adına çözmek yerine, doğrudan bu yola gidiliyor (bkz. EmulatorConfig.
// FindCustomChoice / RefreshResolvedPathForSelection).
public sealed class CustomCoreEntry
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public partial class EmulatorConfig : ObservableObject
{
    [ObservableProperty]
    private string platformName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsMissingCoreOnly))]
    [NotifyPropertyChangedFor(nameof(IsSelectedCoreOrEmulatorMissing))]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    private LauncherType launcherType = LauncherType.StandaloneEXE;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCoreOrEmulatorName))]
    [NotifyPropertyChangedFor(nameof(IsSelectedCoreOrEmulatorMissing))]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    private string preferredCore = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    private string alternativeCore = string.Empty;

    // NOT: AvailableChoices'i BURADA [NotifyPropertyChangedFor] ile bildirmiyoruz (bilinçli) —
    // RefreshResolvedPathForSelection (bkz. OnSelectedCoreOrEmulatorNameChanged) dropdown'da SADECE
    // seçim değiştirince de bu alanı günceller; o an AvailableChoices'i YENİDEN HESAPLAYIP ItemsSource'u
    // TAZE nesnelerle DEĞİŞTİRMEK, ComboBox kendi SelectedValue senkronizasyonunu HENÜZ TAMAMLAMAMIŞKEN
    // (aynı senkron çağrı zinciri içinde, reentrant) oluyordu — sonuç: ilk seçimde Core Adı dropdown'ı
    // BOŞ görünüyordu (Dosya Adı doğru güncelleniyordu, sadece ComboBox'ın kendi görsel seçimi
    // kayboluyordu), ikinci seçimde düzeliyordu (tanı: kullanıcı testiyle doğrulandı). Bunun yerine
    // İNDİRME/KURULUM/KALDIRMA gibi GERÇEKTEN yeni seçenek ekleyen/kaldıran dış eylemler (bkz.
    // SettingsViewModel.DownloadCoreForEmulator, InstallRetroArch, UninstallRetroArch,
    // InstallStandaloneEmulator, UninstallStandaloneEmulator, ReconcileInstalledEmulatorPaths,
    // BrowseEmulatorPath) RefreshAvailableChoices()'i AÇIKÇA, kendi senkron akışlarının SONUNDA çağırır.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsMissingCoreOnly))]
    [NotifyPropertyChangedFor(nameof(IsSelectedCoreOrEmulatorMissing))]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    private string executablePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsMissingCoreOnly))]
    [NotifyPropertyChangedFor(nameof(IsSelectedCoreOrEmulatorMissing))]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    private string corePath = string.Empty;

    [ObservableProperty]
    private string parameters = "%ROM%";

    // Kullanıcının "+" butonuyla elle eklediği, PreferredCore/AlternativeCore dışındaki çekirdek/
    // emülatörler (bkz. CustomCoreEntry, EmulatorConfig.AddCustomChoice) — JsonIgnore YOK, kalıcı.
    public ObservableCollection<CustomCoreEntry> CustomChoices { get; set; } = new();

    private CustomCoreEntry? FindCustomChoice(string name) =>
        CustomChoices.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    // Core Adı dropdown'ındaki "+" butonu (bkz. SettingsWindow.xaml, SettingsViewModel.AddCustomCore)
    // buraya düşer — seçilen dosyayı bu satırın listesine ekler ve hemen seçili yapar.
    public void AddCustomChoice(string name, string filePath)
    {
        if (FindCustomChoice(name) is null)
            CustomChoices.Add(new CustomCoreEntry { Name = name, FilePath = filePath });
        SelectedCoreOrEmulatorName = name;
        RefreshAvailableChoices();
    }

    // "hepsi için kendi emulatörleri gözüksün ... core adında seçili emulatörü indirecek" (kullanıcı
    // isteği) — "Core Adı" sütunundaki ComboBox artık TÜM diskteki dosyaları değil, SADECE bu satırın
    // kendi PreferredCore/AlternativeCore'unu (bkz. AvailableChoices) listeler; kullanıcının ikisi
    // arasından seçtiği AD burada tutulur (PreferredCore'dan farklı — kullanıcı Alternatif'i seçebilir).
    // Boşsa reconciliation PreferredCore'a düşer (bkz. SettingsViewModel constructor).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCoreOrEmulatorName))]
    [NotifyPropertyChangedFor(nameof(IsSelectedCoreOrEmulatorMissing))]
    [NotifyPropertyChangedFor(nameof(ResolvedFileName))]
    [NotifyPropertyChangedFor(nameof(SelectedCoreChoice))]
    private string selectedCoreOrEmulatorName = string.Empty;

    // Boşsa PreferredCore'a düşer — SelectedCoreOrEmulatorName hiç ayarlanmamış (ör. yeni eklenen bir
    // satır, ya da eski bir kayıttan içe/dışa aktarılmış) satırlarda dropdown'ın ve indirme butonunun
    // hâlâ makul bir varsayılanı olsun diye.
    [JsonIgnore]
    public string EffectiveCoreOrEmulatorName =>
        string.IsNullOrWhiteSpace(SelectedCoreOrEmulatorName) ? PreferredCore : SelectedCoreOrEmulatorName;

    // "parametrelerde seçili olan core ve standalone exe ye göre otomatik gelecek" (kullanıcı isteği)
    // — LaunchEngine.Launch GERÇEKTEN bu Parameters alanını okuyup çalıştırıyor (sadece bir görüntü
    // değeri değil), bu yüzden ResolvedFileName gibi ayrı bir "hesaplanan" property DEĞİL, doğrudan
    // BU alanı günceller — CommunityToolkit.Mvvm'in source generator'ının ürettiği setter'a otomatik
    // bağlanan partial hook'lar (On<Alan>Changed): kullanıcı Core Adı dropdown'ından farklı bir isim
    // seçtiğinde VEYA Mod'u değiştirdiğinde, o ismin/mod'un GERÇEK komut satırı kalıbına göre
    // Parameters'ı yeniden yazar. RetroArchCore'da TÜM çekirdekler AYNI evrensel kalıbı kullanır
    // (çekirdek DOSYASI zaten %CORE% üzerinden değişiyor); StandaloneEXE'de her isim FARKLI bir CLI
    // kuralına sahip olabilir (bkz. EmulatorNameResolver.TryGetStandaloneParameters).
    private const string RetroArchCoreParameters = "-L \"%CORE%\" \"%ROM%\"";

    partial void OnSelectedCoreOrEmulatorNameChanged(string value)
    {
        RefreshParametersForSelection();
        RefreshResolvedPathForSelection();
    }

    partial void OnLauncherTypeChanged(LauncherType value)
    {
        RefreshParametersForSelection();
        RefreshResolvedPathForSelection();
        RefreshAvailableChoices();
    }

    partial void OnPreferredCoreChanged(string value) => RefreshAvailableChoices();

    partial void OnAlternativeCoreChanged(string value) => RefreshAvailableChoices();

    private void RefreshParametersForSelection()
    {
        Parameters = LauncherType == LauncherType.RetroArchCore
            ? RetroArchCoreParameters
            : EmulatorNameResolver.TryGetStandaloneParameters(EffectiveCoreOrEmulatorName) ?? Parameters;
    }

    // Dropdown'da Tercih Edilen ↔ Alternatif (veya "+" ile eklenmiş özel bir çekirdek/emülatör)
    // arasında geçiş yapınca, LaunchEngine'in GERÇEKTEN okuduğu CorePath/ExecutablePath'i de o SEÇİLİ
    // isme göre günceller — ÖNCEDEN sadece Parameters/görüntü tazeleniyordu, CorePath son indirilenin
    // STALE kalıntısında kalıyordu (BAŞLAT hep İLK indirilen çekirdeği açardı, dropdown'daki seçime
    // bakmaksızın). Özel eklenen bir isim için EmulatorNameResolver'ın bilgisi yok, doğrudan
    // CustomCoreEntry.FilePath kullanılır.
    private void RefreshResolvedPathForSelection()
    {
        var selectedName = EffectiveCoreOrEmulatorName;
        if (string.IsNullOrWhiteSpace(selectedName))
            return;

        if (FindCustomChoice(selectedName) is { } custom)
        {
            if (LauncherType == LauncherType.RetroArchCore)
                CorePath = custom.FilePath;
            else
                ExecutablePath = custom.FilePath;
            return;
        }

        if (LauncherType == LauncherType.RetroArchCore)
        {
            if (EmulatorNameResolver.TryGetCoreFileName(selectedName) is { } coreFileName
                && FindRetroArchCoreFilePath(selectedName, coreFileName) is { } resolvedCorePath)
                CorePath = resolvedCorePath;
        }
        else if (EmulatorNameResolver.TryGetStandaloneId(selectedName) is { } standaloneId
            && StandaloneEmulatorInstallerService.GetInstalledExecutablePath(standaloneId) is { } resolvedExePath)
        {
            ExecutablePath = resolvedExePath;
        }
    }

    // Kullanıcı isteği: "önerilen ve alternatif olanları haricindekileri retroarch ın core
    // klasöründen okusun bizim klasörde karışıklık olmasın" — Tercih Edilen/Alternatif SADECE bu
    // platformun kendi (temiz, bizim yönettiğimiz) cores\RetroAudit\{Platform}\ klasöründe aranır;
    // ek katalog çekirdekleri (bkz. EmulatorNameResolver.GetAdditionalCoreNames) o klasöre HİÇ
    // KOPYALANMAZ — RetroArch'ın KENDİ ham cores\ ağacında (bulk kurulumun ham çıktısı dahil,
    // RECURSIVE) aranır.
    private string? FindRetroArchCoreFilePath(string selectedName, string coreFileName)
    {
        var isPreferredOrAlternative = string.Equals(selectedName, PreferredCore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(selectedName, AlternativeCore, StringComparison.OrdinalIgnoreCase);
        return isPreferredOrAlternative
            ? RetroArchInstallerService.FindCoreFile(PlatformName, coreFileName)
            : RetroArchInstallerService.FindCoreFileAnywhere(coreFileName);
    }

    // "core var ise işaret yoksa download butonu olsun" (kullanıcı isteği) — Core Adı'ndaki İndir
    // butonunun HER ZAMAN görünmesi yerine, sadece dropdown'da O AN SEÇİLİ olan çekirdek/emülatör
    // GERÇEKTEN eksikse görünür (bkz. SettingsWindow.xaml). CorePath/ExecutablePath'e değil, doğrudan
    // diske (RECURSIVE) bakar — kullanıcı dropdown'dan Alternatif'e geçip henüz indirmediyse, eski
    // (Tercih Edilen'e ait) CorePath'e bakıp yanlışlıkla "hazır" demesin diye.
    [JsonIgnore]
    public bool IsSelectedCoreOrEmulatorMissing
    {
        get
        {
            var selectedName = EffectiveCoreOrEmulatorName;
            if (string.IsNullOrWhiteSpace(selectedName))
                return true;

            if (FindCustomChoice(selectedName) is { } custom)
                return !File.Exists(custom.FilePath);

            if (LauncherType == LauncherType.RetroArchCore)
            {
                var coreFileName = EmulatorNameResolver.TryGetCoreFileName(selectedName);
                return coreFileName is null || FindRetroArchCoreFilePath(selectedName, coreFileName) is null;
            }

            var standaloneId = EmulatorNameResolver.TryGetStandaloneId(selectedName);
            return standaloneId is null || !StandaloneEmulatorInstallerService.IsInstalled(standaloneId);
        }
    }

    // "Core adının yanına bi sütun daha ekle seçili coreların ve exelerin tam adı yazsın ... dinamik
    // değil fbneo yu seçtim yandaki dll değişmedi" (kullanıcı isteği/geri bildirimi) — CorePath/
    // ExecutablePath'e DOĞRUDAN bakmak yerine (bunlar sadece EN SON indirilenin STALE kalıntısı),
    // O AN dropdown'da SEÇİLİ olan isme göre CANLI çözer — IsSelectedCoreOrEmulatorMissing ile AYNI
    // mantık, RetroArchCore'da bu platformun kendi cores\RetroAudit\{Platform}\ klasörüne bakar.
    // "Tercih edilen ve alternatif ... dosya adının sonunda yazsın (parantez içinde)" (kullanıcı
    // isteği) — bu etiket artık Core Adı dropdown'ının İÇİNDE değil, buradaki dosya adının SONUNA
    // ekleniyor (bkz. AvailableChoices).
    [JsonIgnore]
    public string ResolvedFileName
    {
        get
        {
            var selectedName = EffectiveCoreOrEmulatorName;
            if (string.IsNullOrWhiteSpace(selectedName))
                return string.Empty;

            if (FindCustomChoice(selectedName) is { } custom)
                return File.Exists(custom.FilePath) ? Path.GetFileName(custom.FilePath) : string.Empty;

            string? fileName;
            if (LauncherType == LauncherType.RetroArchCore)
            {
                var coreFileName = EmulatorNameResolver.TryGetCoreFileName(selectedName);
                fileName = coreFileName is null ? null : Path.GetFileName(FindRetroArchCoreFilePath(selectedName, coreFileName) ?? coreFileName);
            }
            else
            {
                var standaloneId = EmulatorNameResolver.TryGetStandaloneId(selectedName);
                var resolvedExePath = standaloneId is null ? null : StandaloneEmulatorInstallerService.GetInstalledExecutablePath(standaloneId);
                fileName = resolvedExePath is not null
                    ? Path.GetFileName(resolvedExePath)
                    : (string.IsNullOrWhiteSpace(ExecutablePath) ? null : Path.GetFileName(ExecutablePath));
            }

            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            if (string.Equals(selectedName, PreferredCore, StringComparison.OrdinalIgnoreCase))
                return $"{fileName} (Önerilen)";
            if (string.Equals(selectedName, AlternativeCore, StringComparison.OrdinalIgnoreCase))
                return $"{fileName} (Alternatif)";
            return fileName;
        }
    }

    // "seçenek koy bide oraya önerilen ve alternatifleri göster veya hepsini göster diye seçilen
    // ayara göre göstersin" (kullanıcı isteği) — TÜM satırlar için ORTAK, global bir görünüm anahtarı
    // (bkz. SettingsViewModel.CoreChoiceDisplayMode, Ayarlar > Emülatörler'deki seçici). Statik olması
    // bilinçli: ~43 EmulatorConfig örneğinin HER birine ayrı ayrı bir ayar taşımak yerine, ViewModel
    // değiştiğinde TÜM satırların RefreshAvailableChoices'ini çağırması yeterli.
    public static CoreChoiceDisplayMode DisplayMode { get; set; } = CoreChoiceDisplayMode.ShowAll;

    // Kullanıcı isteği: "olmayanları listeden silmede yanda yok olarak işaretle ... download edecez
    // çünkü olmayanları" — Tercih Edilen/Alternatif diskte yoklarsa listeden GİZLENMİYOR, Label'ına
    // "(Yok)" eklenir (bkz. CoreChoiceOption, IsInstalled) — kullanıcı onu seçip mevcut İndir
    // butonuyla indirebilsin diye. "+"la eklenenler/katalog çekirdekleri ise DisplayMode=ShowAll
    // DEĞİLSE hiç eklenmez (bkz. yukarısı).
    //
    // NOT (kök neden bulundu — kullanıcı testiyle doğrulandı): AvailableChoices ÖNCEDEN her okunuşta
    // TAZE bir liste döndüren düz bir computed property'ydi; ItemsSource'u komple değiştirmek kapalı
    // kutunun bozulmasına yol açıyordu. Sabit bir ObservableCollection'a geçildi (ItemsSource referansı
    // hiç değişmiyor, içerik yerinde güncelleniyor) — bu sorunu KISMEN çözdü ama TEK BAŞINA yeterli
    // değildi: bir isim listeye YENİ eklendiğinde (ör. bir standalone emülatör ilk kez indirildiğinde)
    // ComboBox'ın SelectedValue+SelectedValuePath (değer bazlı, string eşleştirmeli) mekanizması bunu
    // geriye dönük çözüp kapalı kutuda göstermeyi GÜVENİLİR şekilde yapmıyordu — dropdown açılınca
    // öğe listede duruyordu (seçim MODEL'de doğruydu) ama kapalı kutu boş kalıyordu. Çeşitli "zorla
    // yeniden bildir" numaraları (senkron, sonra Dispatcher'a ertelenmiş) DENENDİ, İKİSİ DE
    // GÜVENİLİR ÇALIŞMADI — SelectedValue'nun kendi iç çözümleme zamanlamasıyla uğraşmak yerine kökten
    // farklı bir yola gidildi: bkz. SelectedCoreChoice — ComboBox artık SelectedItem ile DOĞRUDAN bir
    // NESNE referansına bağlanıyor (SelectedValue/SelectedValuePath'in değer-bazlı yeniden çözümleme
    // adımı TAMAMEN devre dışı), bu sınıfın kendi bug geçmişinde SelectedValue zaten İKİ AYRI ciddi
    // bug'ın (bkz. Mode=TwoWay sorunu, bu dosyanın eski yorumları) kaynağıydı.
    private ObservableCollection<CoreChoiceOption>? availableChoices;

    [JsonIgnore]
    public ObservableCollection<CoreChoiceOption> AvailableChoices
    {
        get
        {
            if (availableChoices is null)
            {
                availableChoices = new ObservableCollection<CoreChoiceOption>();
                PopulateAvailableChoices(availableChoices);
            }
            return availableChoices;
        }
    }

    public void RefreshAvailableChoices()
    {
        if (availableChoices is null)
        {
            availableChoices = new ObservableCollection<CoreChoiceOption>();
        }

        // Eğer seçili olan emülatör/core kurulu değilse, seçimi boşalt
        if (!string.IsNullOrWhiteSpace(SelectedCoreOrEmulatorName))
        {
            if (!IsCandidateInstalled(SelectedCoreOrEmulatorName))
            {
                SelectedCoreOrEmulatorName = string.Empty;
            }
        }
        else
        {
            // Eğer seçim boşsa ama PreferredCore kuruluysa, otomatik geri seç
            if (!string.IsNullOrWhiteSpace(PreferredCore) && PreferredCore != "—" && IsCandidateInstalled(PreferredCore))
            {
                SelectedCoreOrEmulatorName = PreferredCore;
            }
        }

        // NOT (kullanıcı testiyle bulunan ÜÇÜNCÜ bir varyant — bu sefer TERS yönde: "sildiklerim
        // listede hâlâ gözüküyor, listeyi açınca gözükmüyor, açmadan gözükmeye devam ediyor"): bir
        // öğe listeden ÇIKARILDIĞINDA (ör. "+"la eklenmiş bir çekirdek diskten silinince) düz bir
        // OnPropertyChanged(SelectedCoreChoice) çağrısı — SelectedCoreOrEmulatorName DEĞİŞMEDİĞİ için
        // getter AYNI (artık geçersiz) sonucu üretebiliyor, WPF "değer aynı görünüyor" deyip kapalı
        // kutuyu tazelemeyebiliyor. Çözüm: SelectedCoreOrEmulatorName'i geçici olarak KESİNLİKLE
        // eşleşmeyecek bir sentinel'e çekip GERÇEK bir "eski ≠ null" geçişi yaşatıyoruz — bu, liste
        // güncellenmeden ÖNCE oluyor ki WPF eski nesne referansını tamamen bıraksın; liste
        // güncellendikten SONRA gerçek (yeni/kaldırılmış) durumu tekrar bildiriyoruz.
#pragma warning disable MVVMTK0034 // Bilinçli: OnSelectedCoreOrEmulatorNameChanged hook'unu tetiklemeden ham geçiş yapmak için.
        var currentSelection = selectedCoreOrEmulatorName;
        selectedCoreOrEmulatorName = " RefreshAvailableChoices ";
        OnPropertyChanged(nameof(SelectedCoreChoice));
        selectedCoreOrEmulatorName = currentSelection;
#pragma warning restore MVVMTK0034

        PopulateAvailableChoices(availableChoices);
        OnPropertyChanged(nameof(SelectedCoreChoice));
    }

    // "Core Adı" ComboBox'ının SelectedItem'ı (bkz. SettingsWindow.xaml — artık SelectedValue/
    // SelectedValuePath DEĞİL). SelectedCoreOrEmulatorName (string, kalıcı/JSON'a yazılan gerçek alan)
    // ile AvailableChoices'teki NESNE arasındaki köprü — SADECE görüntü katmanı, JsonIgnore.
    [JsonIgnore]
    public CoreChoiceOption? SelectedCoreChoice
    {
        get => AvailableChoices.FirstOrDefault(o => string.Equals(o.Name, SelectedCoreOrEmulatorName, StringComparison.OrdinalIgnoreCase));
        set => SelectedCoreOrEmulatorName = value?.Name ?? string.Empty;
    }

    private void PopulateAvailableChoices(ObservableCollection<CoreChoiceOption> target)
    {
        var options = new List<CoreChoiceOption>();
        TryAddChoice(options, PreferredCore, isPreferredOrAlternative: true);
        TryAddChoice(options, AlternativeCore, isPreferredOrAlternative: true);

        if (DisplayMode == CoreChoiceDisplayMode.ShowAll)
        {
            // "+" ile eklenmiş özel seçenekler. NOT: "hem silik gri dedik hem olmayanlar gözükmesin
            // dedik ... liste sapıtıyor olabilir" (kullanıcı geri bildirimi) — Custom'lar ÖNCEDEN
            // diskten silinince listeden tamamen ÇIKARILIYORDU (Preferred/Alternatif/katalogdan FARKLI
            // bir kural), bu TUTARSIZLIK gereksiz karmaşıklığa (ve WPF ComboBox'ın Remove/Replace
            // karışık senaryolarında kapalı kutuyu tazeleme sorunlarına) katkıda bulunuyordu. Şimdi
            // TÜM kategoriler AYNI kuralı izliyor: hiçbiri diskten kaybolunca listeden ÇIKARILMIYOR,
            // sadece soluk renkte kalıyor (bkz. IsInstalled, CoreChoiceItemTemplate).
            foreach (var custom in CustomChoices)
            {
                if (!options.Any(o => string.Equals(o.Name, custom.Name, StringComparison.OrdinalIgnoreCase)))
                    options.Add(new CoreChoiceOption(custom.Name, custom.Name, isInstalled: File.Exists(custom.FilePath)));
            }

            // Kullanıcı isteği: "bunları ilgili emulatörlere bağlayalım açılır listelerine ekleyelim"
            // — Tercih Edilen/Alternatif DIŞINDAKİ bilinen ek çekirdekler (bkz.
            // EmulatorNameResolver.GetAdditionalCoreNames), diskte olsun olmasın HER ZAMAN listelenir
            // — RetroArch'ın kendi ham cores\ ağacında (bulk kurulumun ham çıktısı dahil, bkz.
            // RetroArchInstallerService.FindCoreFileAnywhere) bulunamıyorsa "(Yok)" eklenir.
            if (LauncherType == LauncherType.RetroArchCore)
            {
                foreach (var name in EmulatorNameResolver.GetAdditionalCoreNames(PlatformName))
                {
                    if (options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var coreFileName = EmulatorNameResolver.TryGetCoreFileName(name);
                    var isInstalled = coreFileName is not null && RetroArchInstallerService.FindCoreFileAnywhere(coreFileName) is not null;
                    options.Add(new CoreChoiceOption(name, name, EmulatorNameResolver.TryGetDescription(name), isInstalled: isInstalled));
                }
            }
        }

        // NOT (kök neden bulundu — ikinci tur, kullanıcı testiyle doğrulandı): Clear()+Add() ile
        // güncellemek bile (ObservableCollection referansı sabit kalsa da) TEK bir "Reset" bildirimi
        // + N tane "Add" üretiyordu — Reset anında koleksiyon ANLIK olarak boşalıyor, WPF ComboBox'ın
        // kapalı kutusu bunu "artık hiçbir şey seçili değil" olarak yorumlayıp bir daha eski
        // SelectedValue'yu yeni (ama İÇERİK olarak AYNI) öğeyle eşleştirip göstermiyordu (özellikle bu
        // pencerede ~40 satır AYNI ANDA yenilenince, ör. "Core Adı listesi" görünüm seçicisinden).
        // Şimdi SADECE gerçekten eklenmesi/kaldırılması gereken öğeler için Insert/RemoveAt/Move
        // çağrılıyor — o an SEÇİLİ olan öğe (ör. Tercih Edilen, iki modda da hep listede) HİÇBİR
        // Remove/Add/Replace olayına maruz kalmadan AYNI nesne referansıyla yerinde kalıyor, ComboBox'ın
        // SelectedItem'ı bir an bile geçersiz hâle gelmiyor.
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!options.Any(o => string.Equals(o.Name, target[i].Name, StringComparison.OrdinalIgnoreCase)))
                target.RemoveAt(i);
        }

        for (var i = 0; i < options.Count; i++)
        {
            var existingIndex = -1;
            for (var j = 0; j < target.Count; j++)
            {
                if (string.Equals(target[j].Name, options[i].Name, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex == -1)
                target.Insert(Math.Min(i, target.Count), options[i]);
            else if (existingIndex != i)
                target.Move(existingIndex, i);
            else if (target[i].Label != options[i].Label || target[i].IsInstalled != options[i].IsInstalled)
                target[i] = options[i]; // içerik gerçekten değişti (ör. indirildi) — sadece o zaman değiştir
        }
    }

    private void TryAddChoice(List<CoreChoiceOption> options, string name, bool isPreferredOrAlternative = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "—")
            return;
        if (options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        options.Add(new CoreChoiceOption(name, name, EmulatorNameResolver.TryGetDescription(name), isPreferredOrAlternative, IsCandidateInstalled(name)));
    }

    // RetroArchCore: bu platformun kendi cores\RetroAudit\{Platform}\ klasöründe bu ismin çekirdek
    // dosyası (.dll) gerçekten var mı (bkz. RetroArchInstallerService.FindCoreFile). StandaloneEXE:
    // otomatik kurulumu olan isimler (PCSX2/RPCS3/Xemu, bkz. EmulatorNameResolver.TryGetStandaloneId)
    // için ThirdParty\{id}\ klasörü kontrol edilir. Otomatik kurulumu olmayan (Gözat-only, ör.
    // Dolphin/Cemu) isimlerde tek kanıt bu satırın kendi ExecutablePath'i — birden fazla Gözat-only
    // aday (ör. Nintendo Switch: Ryujinx/Yuzu) aynı ExecutablePath'i paylaştığı için bu durumda ikisi
    // de "kurulu" sayılabilir; tek bir yol alanına iki ayrı aday sığdırmanın bilinçli sınırlaması budur.
    private bool IsCandidateInstalled(string name)
    {
        if (FindCustomChoice(name) is { } custom)
            return File.Exists(custom.FilePath);

        if (LauncherType == LauncherType.RetroArchCore)
        {
            var coreFileName = EmulatorNameResolver.TryGetCoreFileName(name);
            return coreFileName is not null && RetroArchInstallerService.FindCoreFile(PlatformName, coreFileName) is not null;
        }

        var standaloneId = EmulatorNameResolver.TryGetStandaloneId(name);
        if (standaloneId is not null)
            return StandaloneEmulatorInstallerService.IsInstalled(standaloneId);

        return !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);
    }

    // Satırın "Durum" sütunu için: emülatör gerçekten çalıştırılabilir mi? RetroArchCore modunda
    // hem RetroArch.exe HEM de seçilen çekirdek (.dll) diskte olmalı; StandaloneEXE modunda
    // sadece kendi .exe'si yeterli (CorePath bu modda zaten kullanılmıyor).
    [JsonIgnore]
    public bool IsReady => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath)
        && (LauncherType != LauncherType.RetroArchCore || (!string.IsNullOrWhiteSpace(CorePath) && File.Exists(CorePath)));

    // Durum sütunundaki üçüncü, ayrı durum: RetroArchCore modunda RetroArch.exe zaten kurulu ama
    // BU platformun kendi çekirdek (.dll) dosyası henüz yok — kullanıcı mockup'ında "⚠ Missing Core"
    // olarak ayrı gösteriliyor (sıfırdan "İndir" ile karışmasın, zaten RetroArch kurulu, sadece
    // "Core Seç..." ile bu platforma özel çekirdek gösterilmeli).
    [JsonIgnore]
    public bool IsMissingCoreOnly => LauncherType == LauncherType.RetroArchCore
        && !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath)
        && !IsReady;
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

    // Re-match Metadata komutu için — Builder'ın kullandığı MasterMetadata.db yolu, WPF
    // tarafında da bilinmesi gerekiyor (bkz. plan: RetroAudit.Catalog referansı).
    public string MasterMetadataDbPath { get; set; } = string.Empty;

    // "Sütunlar" seçicisindeki her sütunun son görünürlük durumu (Key -> IsVisible). Kullanıcı
    // bir sütunu açıp/kapatınca MainViewModel.SaveColumnVisibility burayı güncelleyip diske yazar;
    // uygulama açılışında bu değerler ColumnOptions'ın sabit varsayılanlarının üzerine uygulanır.
    public Dictionary<string, bool> ColumnVisibility { get; set; } = new();

    // DataGrid satır yüksekliği (bkz. Ayarlar > Arayüz > Tablo Görünümü). Önceden ana penceredeki
    // araç çubuğunda bir kaydırıcıydı ve hiç kalıcı değildi; kullanıcı isteğiyle Ayarlar'a taşındı.
    public double RowHeight { get; set; } = 30;

    // Detay panelindeki "Sürümler (Region)" bölümü: true = tek kart + birden fazlaysa "▾" ile
    // sağ-tık kapsül menüsündekiyle aynı popup'tan seçim; false = eskisi gibi tüm sürümler alt
    // alta tam liste (bkz. Ayarlar > Arayüz, kullanıcı isteği: "ister bu şekilde açılır liste
    // isterse full açık olarak gösterebilme ayarı olsun").
    public bool ShowVersionsAsSingleCard { get; set; } = true;

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

    // Ayarlar > Emülatörler sekmesindeki DataGrid için ANA ızgaradaki ColumnWidths/ColumnOrder'ın
    // aynısı (bkz. SettingsWindow.xaml.cs) — kullanıcı geri bildirimi: "sütunları ayarlıyom kapatınca
    // ayar bozuluyor genişlik ve konum" — bu grid ayrı bir DataGrid olduğu için ana ızgaranınkinden
    // BAĞIMSIZ kendi anahtar kümesine ihtiyaç duyuyor (ör. "Platform", "Durum", "Mod" vb.).
    public Dictionary<string, double> EmulatorGridColumnWidths { get; set; } = new();
    public List<string> EmulatorGridColumnOrder { get; set; } = new();

    // Görev talimatı: "Her emulator için ... 'Download Source' ekle düzenlenebilir olsun ... API varsa
    // API linki, API yoksa Direct URL — tek sütun yeterli" — HER standalone emülatör için TEK bir
    // kaynak (EmulatorId -> tür + kaynak metni, bkz. DownloadSourceType,
    // StandaloneEmulatorInstallerService.DefaultDownloadSources bu sözlükte kayıt yoksa/ilk çalıştırmada
    // gösterilecek varsayılan değerleri sağlar). Kaynak Türü GitHubReleases ise Kaynak "owner/repo",
    // DirectUrl ise doğrudan indirilebilir bir URL (Dolphin/Ryujinx gibi otomatik kaynağı olmayanlar
    // için bu ZORUNLU). RPCS3 için Kaynak BOŞ bırakılırsa kendi özel JSON güncelleme API'si kullanılır
    // (bkz. StandaloneEmulatorInstallerService.ResolveSourceAsync) — doldurulursa bu değer ONU geçersiz kılar.
    public Dictionary<string, EmulatorDownloadSourceSetting> EmulatorDownloadSources { get; set; } = new();

    // Sağ detay panelinin GridSplitter ile ayarlanmış genişliği (bkz. MainWindow.xaml GridSplitter,
    // MainViewModel.DetailColumnWidth). Panel kapatılıp açıldığında bu genişliğe geri döner.
    public double DetailPanelWidth { get; set; } = 340;

    // Sol paneldeki platform listesi kategorilere (Konsollar, El Konsolları, ...) göre mi
    // gruplanacak, yoksa düz bir liste mi olacak (bkz. Ayarlar > Arayüz > Platform Listesi).
    public bool GroupPlatformsByCategory { get; set; } = true;

    // Platform satırlarının Yazı mı Logo mu gösterileceği (bkz. PlatformListDisplayMode).
    public PlatformListDisplayMode PlatformListDisplayMode { get; set; } = PlatformListDisplayMode.Text;

    // Tablodaki "Bölge" sütununun gösterim biçimi (bkz. RegionColumnDisplayMode).
    public RegionColumnDisplayMode RegionColumnDisplayMode { get; set; } = RegionColumnDisplayMode.FlagAndText;

    // Provider pencerelerinin görsel düzeni (Classic = mevcut tasarım, Modern = yeni dashboard düzeni).
    public ProviderDesignMode ProviderDesignMode { get; set; } = ProviderDesignMode.Classic;

    // "Görsel Getir" indirmelerinin küçültüleceği maksimum boyut (bkz. Ayarlar > Genel).
    public ArtworkMaxDimension ArtworkMaxDimension { get; set; } = ArtworkMaxDimension.Px600;

    // Core Adı dropdown'ında sadece Tercih Edilen/Alternatif mi, hepsi mi gösterilsin (bkz.
    // CoreChoiceDisplayMode, Ayarlar > Emülatörler).
    public CoreChoiceDisplayMode CoreChoiceDisplayMode { get; set; } = CoreChoiceDisplayMode.ShowAll;

    // Hangi kategorilerin sol panelde görünür olduğu (Key -> IsVisible). Kayıtlı değeri olmayan
    // bir kategori varsayılan olarak görünür sayılır.
    public Dictionary<string, bool> CategoryVisibility { get; set; } = new();

    // Kullanıcının platform listesinde sürükle-bırak ile ayarladığı özel sıra (Platform.Name,
    // ham DAT adı). Listede olmayan platformlar doğal sıralarıyla sona eklenir (bkz.
    // MainViewModel.OrderPlatforms). Kategoriler açıkken sıralama sadece aynı kategori içinde
    // yapılabilir (bkz. MainViewModel.ReorderPlatform).
    public List<string> PlatformOrder { get; set; } = new();

    // Uygulama açılışında son açık bırakılan platform/playlist'in otomatik seçilmesi için
    // (kullanıcı isteği) — bkz. MainViewModel constructor'daki geri okuma, SaveLastSelection.
    // LastSelectedPlatform null = "All Platforms" (silinmiş bir platform ismini saklı tutmak
    // yerine bilinçli olarak null).
    public string? LastSelectedPlatform { get; set; }
    public PlaylistChipKind? LastSelectedChipKind { get; set; }
    public int? LastSelectedPlaylistId { get; set; }

    // Provider pencerelerinde son seçilen görünüm modu (true = Tablo/DataGrid, false = Liste/ListBox).
    public bool ProviderShowAsTable { get; set; } = false;
}
