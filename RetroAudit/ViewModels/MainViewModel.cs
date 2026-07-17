using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Catalog.Grouping;
using RetroAudit.Catalog.Metadata;
using RetroAudit.Catalog.Naming;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Ana pencerenin ViewModel'i: toolbar, platform listesi, oyun DataGrid'i ve detay panelinin
// tüm durumu burada tutulur. Import (bkz. RomImportWindow) dışındaki toolbar komutlarının
// çoğu (Rescan, Refresh Media, ...) bu aşamada bilinçli olarak boş bırakıldı, ileride buraya
// gerçek işlevleri eklenecek.
public partial class MainViewModel : ObservableObject
{
    // Sol paneldeki kategori başlıkları. RetroAudit.Catalog/Dat/PlatformCategoryMap.cs'teki
    // sabitlerle birebir aynı metinler — Builder bu string'leri Platforms.Category sütununa yazıyor.
    public const string CategoryConsoles = "CONSOLES";
    public const string CategoryHandhelds = "HANDHELDS";
    public const string CategoryArcade = "ARCADE";
    public const string CategoryComputers = "COMPUTERS";
    public const string CategoryClassic = "CLASSIC";
    public const string CategoryOthers = "OTHERS";

    // Filtrelenmemiş tam oyun listesi; Games koleksiyonu bunun bir alt kümesidir (bkz. ApplyFilter).
    private readonly List<Game> _allGames;

    // FilterByGenreToken'ın biriktirdiği, kullanıcının rozetlere tıklayarak seçtiği tür token'ları
    // (bkz. o metodun yorumu) — kullanıcı isteği: "shooter ve horror'u tıkladım ikisi de ayrı ayrı
    // gözükmeli", yani birden fazla tür rozetine art arda tıklamak ÖNCEKİni SİLMEK yerine BİRİKTİRİR
    // (OR: Shooter VEYA Horror).
    private readonly HashSet<string> _activeGenreTokens = new(StringComparer.OrdinalIgnoreCase);

    // Tam platform listesi (mock veri, kategorilere ayrılmış). Sol panelde doğrudan bu değil,
    // kategori başlıklarıyla iç içe geçmiş PlatformListItems gösterilir.
    public ObservableCollection<Platform> Platforms { get; }

    // Sol paneldeki ListBox'ın gerçek ItemsSource'u: kategori başlığı ve platform satırlarının
    // sırayla dizilmiş hali (bkz. RebuildPlatformListItems). "OTHERS" kategorisi varsayılan kapalı
    // geldiğinden, IsOthersExpanded=false iken o kategorinin platform satırları listede hiç yer almaz.
    public ObservableCollection<PlatformListItem> PlatformListItems { get; } = new();

    // DataGrid'e bağlanan, filtreleme sonrası görünen oyun listesi. [ObservableProperty] —
    // ApplyFilter platform değişince/vb. BÜTÜN koleksiyonu tek seferde değiştiriyor (bkz. orada),
    // eskiden Clear()+binlerce tekil Add() yapıyordu; her Add WPF DataGrid'e ayrı bir
    // CollectionChanged bildirimi olarak gidip büyük bir platforma (veya "All Platforms"a)
    // geçerken gözle görülür bir donmaya yol açıyordu (kullanıcı geri bildirimi: "platform
    // geçişleri akıcı olsun"). Tek bir PropertyChanged, DataGrid'in ItemsSource'u toptan
    // yenilemesini (Reset) sağlıyor — çok daha ucuz.
    [ObservableProperty]
    private ObservableCollection<Game> games = new();

    // Sağ paneldeki Versions listesi: SelectedGame değiştiğinde CatalogDatabaseService.GetVersions
    // ile talep üzerine doldurulur (67 bin oyunun tamamının sürüm verisini baştan yüklemek yerine).
    public ObservableCollection<GameVersion> SelectedGameVersions { get; } = new();

    // Detay panelinde başlığın altında her zaman açık gösterilen "ALTERNATE NAMES" listesi —
    // Versions listesiyle aynı "talep üzerine" prensibi: SelectedGame değiştiğinde
    // LoadSelectedGameVersions ile birlikte doldurulur (bkz. OnSelectedGameChanged). Kullanıcı
    // isteği: LaunchBox'ın kendi sitesindeki gösterim (isim + bölge), tıklanınca açılan bir menü
    // DEĞİL, doğrudan görünür olsun.
    public ObservableCollection<GameAlternateName> SelectedGameAlternateNames { get; } = new();

    // Toolbar'daki "Tools" açılır menüsünün öğeleri; seçim, gerçek bir seçili durum değil
    // bir "eylem tetikleyici" gibi davranır (bkz. OnSelectedToolActionChanged).
    public ObservableCollection<string> ToolMenuItems { get; } = new() { "Tools", "Media Provider...", "Crop Editor..." };

    // İkincil pencereleri açma isteğini View katmanına (MainWindow.xaml.cs) ileten olaylar.
    // ViewModel, Window tiplerine doğrudan bağımlı olmasın diye pencere açma işi View'da yapılır.
    public event Action? RequestOpenMediaProvider;
    public event Action? RequestOpenMetadataProvider;
    public event Action? RequestOpenCropEditor;
    public event Action? RequestOpenSettings;
    public event Action? RequestOpenRomImport;
    public event Action<string>? RequestShowMessage;
    public event Action? PlatformListOrderChanged;
    public event Action<Game>? GameMetadataChanged;

    // Tools ComboBox'ının o anki seçimi; bir eylem tetiklendikten sonra otomatik olarak
    // "Tools" değerine geri döner (bkz. OnSelectedToolActionChanged), böylece kalıcı bir
    // seçim gibi görünmez.
    [ObservableProperty]
    private string selectedToolAction = "Tools";

    // Sol panelde seçili platform; değiştiğinde oyun listesi yeniden filtrelenir.
    [ObservableProperty]
    private Platform? selectedPlatform;

    // ListBox.SelectedItem burayla bağlıdır (PlatformListItems, hem başlık hem platform satırları
    // içerir). Başlık satırları seçilemez (bkz. MainWindow.xaml ItemContainerStyle), bu yüzden bu
    // alan pratikte hep bir platform satırına işaret eder; değiştiğinde SelectedPlatform güncellenir.
    [ObservableProperty]
    private PlatformListItem? selectedListItem;

    // DataGrid'de seçili oyun; sağ detay panelinin tüm alanları buna bağlıdır.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayOverlay))]
    private Game? selectedGame;

    // Gameplay screenshot alanının embedded YouTube player'a mı yoksa normal screenshot'a mı
    // döneceğini belirler (bkz. MainWindow.xaml Grid.Row="4", MainWindow.xaml.cs
    // PlayYouTubeEmbedAsync/StopYouTubeEmbed). Kullanıcı isteği: "dış tarayıcı açılmasın, aynı
    // alan WebView2 ile gömülü player'a dönüşsün".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayOverlay))]
    private bool isPlayingVideo;

    // WebView2 navigasyonu başarısız olursa (bkz. MainWindow.xaml.cs YouTubePlayer_NavigationCompleted)
    // SADECE o durumda dış tarayıcıya düşen bir fallback buton gösterilir (bkz. OpenVideoUrlCommand).
    [ObservableProperty]
    private bool videoEmbedFailed;

    // Play overlay'i (gameplay alanının ortasındaki büyük ikon) sadece bir YouTube linki VE
    // henüz oynatılmıyorken görünür — video oynarken kapanır, kapat butonuna basılınca geri döner.
    public bool ShowPlayOverlay => SelectedGame?.HasYouTubeEmbed == true && !IsPlayingVideo;

    // Arama kutusu metni; her değişiklikte oyun listesi yeniden filtrelenir.
    [ObservableProperty]
    private string searchText = string.Empty;

    // "Released" / "Junk" filtre butonları — ikisi de kapalıysa liste boş görünür (bilinçli tercih,
    // kullanıcının filtre durumunu açıkça görmesi için).
    [ObservableProperty]
    private bool showReleased = true;

    [ObservableProperty]
    private bool showJunk;

    // Toolbar'daki USA/EU/Japan bayrak filtreleri (kullanıcı isteği, çoklu seçim) — sol paneldeki
    // platform ve üstteki playlist/chip'ten SONRA gelen 3. bir süzgeç. Bir oyunun HİÇ USA/EU/Japan
    // etiketli sürümü yoksa (sadece World/Unknown/diğer bölgelerdeyse) bu filtreden etkilenmez,
    // her zaman görünür kalır — sadece gerçekten USA/EU/Japan'da çıkmış ama işaretli region'ların
    // hiçbirinde bulunmayan oyunlar gizlenir (bkz. GetFilterScopePopulation, RecomputeRegionDisplay).
    [ObservableProperty]
    private bool showUsaRegion = true;

    [ObservableProperty]
    private bool showEuRegion = true;

    [ObservableProperty]
    private bool showJapanRegion = true;

    // Toolbar'daki USA/EU/Japan düğmelerinin bayrak ikonları (bkz. FlagResolver) — sabit/statik
    // olduğu için (hangi oyun seçili olursa olsun hep aynı) bir kere hesaplanıp property olarak
    // sunuluyor, XAML'de doğrudan bağlanabilsin diye.
    public string? UsaFlagPath { get; } = FlagResolver.Resolve("USA");
    public string? EuFlagPath { get; } = FlagResolver.Resolve("Europe");
    public string? JapanFlagPath { get; } = FlagResolver.Resolve("Japan");

    // DataGrid.RowHeight ile iki yönlü (TwoWay) bağlıdır. Değeri artık Ayarlar > Arayüz'de
    // ayarlanıyor (bkz. _appSettings.RowHeight); başlangıç değeri constructor'da,
    // güncellemesi ReloadAppSettings'te okunuyor.
    [ObservableProperty]
    private double rowHeight = 30;

    // Ayarlar > Arayüz'deki "Sürümler tek kart / tam liste" tercihi (bkz. AppSettings.
    // ShowVersionsAsSingleCard) — başlangıç değeri constructor'da, güncellemesi
    // ReloadAppSettings'te okunuyor.
    [ObservableProperty]
    private bool showVersionsAsSingleCard = true;

    // Sağ detay panelinin sabit genişliği — kullanıcı kararıyla artık elle (GridSplitter ile)
    // ayarlanamıyor, sadece IsDetailPanelExpanded ile 0/bu değer arasında açılıp kapanıyor (bkz.
    // MainWindow.xaml.cs ApplyDetailPanelWidth). NOT: MainWindow.xaml'de bu bir {Binding} ile
    // ColumnDefinition.Width'e bağlanmıyor — MainWindow.xaml.cs bu iki property'nin
    // PropertyChanged'ını dinleyip Width'i KENDİSİ, elle set ediyor.
    [ObservableProperty]
    private double detailPanelWidth = 340;

    // "OTHERS" kategorisinin açık/kapalı durumu. Varsayılan kapalı: kullanıcı ilk açılışta sadece
    // popüler kategorileri (CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC) görür.
    [ObservableProperty]
    private bool isOthersExpanded;

    // Sol paneldeki platform listesinin görünüm tercihleri (bkz. Ayarlar > Arayüz > Platform
    // Listesi, SettingsViewModel). Burada sadece OKUNUR/uygulanır — düzenlendiği yer Ayarlar
    // penceresi; başlangıç değeri constructor'da, güncellemesi ReloadAppSettings'te okunuyor.
    [ObservableProperty]
    private bool groupPlatformsByCategory = true;

    partial void OnGroupPlatformsByCategoryChanged(bool value) => RebuildPlatformListItems();

    [ObservableProperty]
    private PlatformListDisplayMode platformListDisplayMode = PlatformListDisplayMode.Text;

    // Tablodaki "Bölge" sütununun gösterim biçimi (bkz. Ayarlar > Arayüz, AppSettings.
    // RegionColumnDisplayMode) — başlangıç değeri constructor'da, güncellemesi
    // ReloadAppSettings'te okunuyor.
    [ObservableProperty]
    private RegionColumnDisplayMode regionColumnDisplayMode = RegionColumnDisplayMode.FlagAndText;

    [ObservableProperty]
    private ProviderDesignMode providerDesignMode = ProviderDesignMode.Classic;

    // Sağ detay panelinin açık/kapalı (sürgülü) durumu — DataGrid'e daha fazla yatay yer
    // bırakmak için kapatılabilir (bkz. MainWindow.xaml: ColumnDefinition Width, BoolToGridLength).
    [ObservableProperty]
    private bool isDetailPanelExpanded = true;

    [RelayCommand]
    private void ToggleDetailPanel() => IsDetailPanelExpanded = !IsDetailPanelExpanded;

    // Ana tablonun üstündeki hashtag/chip şeridi (Favorites + kullanıcı playlist'leri + Hidden +
    // Recycle Bin). Bir chip seçiliyken ApplyFilter normal Platform/Arama/Released-Junk/sütun
    // filtre hattını devre dışı bırakıp SADECE o chip'in üyelerini gösterir (bkz. plan).
    public ObservableCollection<PlaylistChip> PlaylistChips { get; } = new();

    [ObservableProperty]
    private PlaylistChip? selectedChip;

    // Seçili chip bir kullanıcı playlist'iyse üyelik anlık hesaplanmak yerine burada önbelleğe
    // alınır (chip değişince/bir oyun playlist'ten çıkarılınca yenilenir).
    private HashSet<string> _selectedChipMembership = new();

    [ObservableProperty]
    private bool isCreatingPlaylist;

    [ObservableProperty]
    private string newPlaylistName = string.Empty;

    // Stats bar'daki "Görünen / Toplam" metnini besleyen salt-okunur sayaçlar.
    public int TotalCount => _allGames.Count;
    public int VisibleCount => Games.Count;
    public int CurrentPlatformHealthPercent => ComputeHealthPercent(GetCurrentPlatformPopulation().ToList());

    // Kullanıcı isteği: "başlık kısmında seçili olan platformun logosu gözüksün başlık text i
    // yazmasın all platformdada retroauditin clearlogosu" — TitleColumnHeaderWithHealth artık
    // "Başlık" metni yerine bunu gösteriyor (bkz. MainWindow.xaml). "All Platforms" seçiliyken (ya
    // da hiç seçim yokken) AppPaths.NoImageLogo kullanılıyor — bu dosya aslında RetroAudit'in kendi
    // marka logosu (bkz. Images/NoImage/Logo.png), PlatformAuditSummary.PlatformLogoPath'teki
    // IsAllPlatforms dalıyla AYNI kasıtlı yeniden kullanım.
    public string CurrentPlatformLogoPath =>
        SelectedPlatform is { IsAllPlatforms: false } platform
            ? CatalogDatabaseService.GetPlatformLogoPath(platform.DisplayName)
            : AppPaths.NoImageLogo;

    // Kullanıcı isteği: "sen onu eski haline al başka logo kullancam onda" — NES için denenen
    // özel margin/width ayarları (Nintendo.png ile ilgiliydi) geri alındı; kullanıcı kendi logo
    // dosyasını değiştirecek, tüm platformlar tekrar aynı standart 120x22/Fill kutusunu kullanıyor.

    // Kullanıcı isteği: "platform sütununu all platform harici kaldıralım all platformda gözüksün
    // sadece zaten seçilende yukarda eşşek kadar yazıyor" — belirli bir platform seçiliyken adı
    // zaten "Başlık" sütun başlığında büyük logo olarak duruyor (bkz. CurrentPlatformLogoPath),
    // tablodaki "Platform" sütunu bu durumda tekrar (redundant) oluyor. Sadece "All Platforms"
    // seçiliyken (ya da hiç seçim yokken) gösteriliyor.
    public Visibility PlatformColumnVisibility =>
        SelectedPlatform is null || SelectedPlatform.IsAllPlatforms ? Visibility.Visible : Visibility.Collapsed;

    // RomImportWindow gibi ViewModel'e ayrı bağımlı pencerelerin ihtiyaç duyduğu salt-okunur
    // erişim — _allGames doğrudan dışarı sızdırılmıyor, sadece IReadOnlyList olarak.
    public IReadOnlyList<Game> AllGames => _allGames;
    public string GamesRootPath => AppPaths.Games;

    // DataGrid sütun başlıklarındaki joystick ikonuyla açılan filtre dropdown'ları. Her biri
    // _allGames üzerinden (tüm veri setinden, aktif filtrelerden bağımsız) bir kere hesaplanan
    // sabit bir değer+sayı listesi taşır — Excel'deki gibi filtre değiştikçe diğer sütunların
    // sayılarını yeniden hesaplamıyoruz (kapsamı sade tutmak için bilinçli bir basitleştirme).
    public ColumnFilterViewModel PlatformFilter { get; }
    public ColumnFilterViewModel GenresFilter { get; }
    public ColumnFilterViewModel PublisherFilter { get; }
    public ColumnFilterViewModel CommunityRatingFilter { get; }
    public ColumnFilterViewModel RegionFilter { get; }
    public ColumnFilterViewModel SourceFilter { get; }
    public ColumnFilterViewModel MatchMethodFilter { get; }
    public ColumnFilterViewModel MaxPlayersFilter { get; }
    public ColumnFilterViewModel TitleFilter { get; }
    public ColumnFilterViewModel FileFilter { get; }
    public ColumnFilterViewModel MatchedFilter { get; }
    public ColumnFilterViewModel FavoriteFilter { get; }
    public ColumnFilterViewModel HasLocalFileFilter { get; }
    public ActionsColumnFilterViewModel ActionsFilter { get; }
    public ColumnFilterViewModel BoxFilter { get; }
    public ColumnFilterViewModel ScreenshotFilter { get; }

    // Kullanıcı isteği: "filtrelenenler ... o alanda gözüksün tıklanılabilir filtre kaldırılabilir
    // şekilde" — RefreshActiveFilterChips'in dolaştığı tam filtre listesi.
    public ColumnFilterViewModel[] AllColumnFilters { get; private set; } = Array.Empty<ColumnFilterViewModel>();

    // Kullanıcı isteği: "ayrı ayrı badge olacak ben horror'u kaldırmak istediğimde tıklayıp
    // kaldırabilecem" — stats bar'daki (bkz. MainWindow.xaml) rozetlerin GERÇEK ItemsSource'u:
    // AllColumnFilters'ın aksine burada bir filtre değil, TEK bir DEĞER = bir rozet. Genres için
    // her _activeGenreTokens girdisi ayrı bir rozet (kaldırma = FilterByGenreToken'ı AYNI token'la
    // tekrar çağırmak, zaten toggle-off yapıyor); diğer filtreler için her işaretli Option ayrı bir
    // rozet (kaldırma = ColumnFilterViewModel.RemoveValueCommand).
    public ObservableCollection<ActiveFilterChip> ActiveFilterChips { get; } = new();

    private void RefreshActiveFilterChips()
    {
        ActiveFilterChips.Clear();

        // Kullanıcı isteği: "o arama alanında yapılan aramalar içinde badgeler oluşsun türlerdeki
        // gibi aynı yerde" — üstteki genel arama kutusu (SearchText) AllColumnFilters'ın parçası
        // DEĞİL (Başlık sütun filtresinden ayrı, bkz. GetFilterScopePopulation), bu yüzden ayrıca
        // ekleniyor.
        if (!string.IsNullOrWhiteSpace(SearchText))
            ActiveFilterChips.Add(new ActiveFilterChip(SearchText, new RelayCommand(() => SearchText = string.Empty)));

        foreach (var token in _activeGenreTokens)
        {
            var capturedToken = token;
            ActiveFilterChips.Add(new ActiveFilterChip(capturedToken, new RelayCommand(() => FilterByGenreToken(capturedToken))));
        }

        foreach (var filter in AllColumnFilters)
        {
            if (filter == GenresFilter || !filter.IsActive)
                continue;

            if (filter.IsSearchOnly)
            {
                ActiveFilterChips.Add(new ActiveFilterChip(filter.SearchText, filter.ClearFilterCommand));
                continue;
            }

            foreach (var option in filter.Options.Where(o => o.IsChecked))
            {
                var capturedFilter = filter;
                var capturedValue = option.Value;
                ActiveFilterChips.Add(new ActiveFilterChip(capturedValue, new RelayCommand(() => capturedFilter.RemoveValueCommand.Execute(capturedValue))));
            }
        }
    }

    // "Sütunlar" düğmesiyle açılan seçici — hangi DataGrid sütununun görünür olacağını belirler.
    // MainWindow.xaml.cs, IsVisible değiştiğinde ilgili DataGridColumn'ı Key'e göre bulup
    // Visibility'sini günceller (DataGridColumn görsel ağacın parçası olmadığı için doğrudan
    // XAML binding ile gizlenemiyor). Tüm 19 sütun da burada — artık hiçbiri "hep görünür/asla
    // kapatılamaz" değil. Varsayılan IsVisible değerleri BuildColumnOptions'da kuruluyor
    // (constructor'da, _appSettings.ColumnVisibility'deki kayıtlı tercihler bunun üzerine
    // uygulanıyor ki daha önce kapatılmış bir sütun açılışta tekrar açık gelmesin).
    public ObservableCollection<ColumnVisibilityOption> ColumnOptions { get; } = new();

    // Ayarlar > Arayüz sekmesinde değiştirilen ContextMenuDisplayMode/MasterMetadataDbPath gibi
    // tercihlerin kalıcı olması için (bkz. ConfigService.LoadDefault/SaveDefault). Bu alanlar
    // RetroAudit.db'ye değil, kullanıcının makinesindeki ayrı bir JSON dosyasına gider.
    private AppSettings _appSettings;

    public MainViewModel()
    {
        _appSettings = ConfigService.LoadDefault();
        BuildColumnOptions();
        rowHeight = _appSettings.RowHeight;
        showVersionsAsSingleCard = _appSettings.ShowVersionsAsSingleCard;
        detailPanelWidth = _appSettings.DetailPanelWidth;
        groupPlatformsByCategory = _appSettings.GroupPlatformsByCategory;
        platformListDisplayMode = _appSettings.PlatformListDisplayMode;
        regionColumnDisplayMode = _appSettings.RegionColumnDisplayMode;
        providerDesignMode = _appSettings.ProviderDesignMode;

        _allGames = CatalogDatabaseService.GetGames();
        // Kullanıcı isteği: "unknown değilde manuel bağlamaya yönlendirebilirsin ... aynı yani" —
        // katalogda hiç karşılığı olmayan, ROM İçe Aktar'ın Eşleşmeyenler sekmesinden "+ Yeni Oyun"
        // ile önceden oluşturulmuş bağımsız oyunlar (bkz. RegisterNewCustomGame) — Builder her
        // koşuda RetroAudit.db'yi baştan yazdığı için bunlar AYRI (RetroAuditUserData.db) saklanır.
        // MergeDuplicateCustomGames: kullanıcı geri bildirimi (aynı başlık iki ayrı satırda) —
        // RegisterNewCustomGame'deki tekilleştirme kontrolünden ÖNCE yanlışlıkla oluşmuş kayıtları
        // bir kez birleştirir (bkz. UserDataService.MergeDuplicateCustomGames yorumu).
        UserDataService.MergeDuplicateCustomGames();
        var customGamesLoaded = UserDataService.GetAllCustomGames().Select(BuildCustomGame).ToList();
        // Kullanıcı bulgusu: "geri dönüşüm kutusuna atmıştım programı kapatıp açınca geri geldiler"
        // — CatalogDatabaseService.GetGames() (bkz. ApplyUserData) gizli/silindi/favori durumunu
        // SADECE katalog oyunlarına bindiriyordu; custom oyunlar (BuildCustomGame) bu bindirmeden
        // hiç geçmediği için IsHidden/IsDeleted her zaman varsayılan false'ta kalıyordu — çöp
        // kutusuna atılan bir custom oyun veritabanında (GameState.IsDeleted=1) doğru işaretli
        // kalsa bile, bir sonraki açılışta normal kütüphanede "geri geliyordu". ApplyUserData ile
        // AYNI üç adım (overlay, favori, kalıcı silinenleri listeden çıkar) burada da uygulanıyor.
        var customGameStates = UserDataService.GetAllGameStates();
        var favoriteKeys = UserDataService.GetFavoriteGameKeys();
        customGamesLoaded.RemoveAll(g => customGameStates.TryGetValue(g.GameKey, out var state) && state.IsPermanentlyDeleted);
        foreach (var custom in customGamesLoaded)
        {
            if (customGameStates.TryGetValue(custom.GameKey, out var state))
            {
                custom.IsHidden = state.IsHidden;
                custom.IsDeleted = state.IsDeleted;
            }
            custom.IsFavorite = favoriteKeys.Contains(custom.GameKey);
        }
        _allGames.AddRange(customGamesLoaded);
        // Kullanıcı isteği: "sen bağlasana doğru oyuna eksikleri" — bölgesel/romanize isim farkı
        // yüzünden otomatik eşleştirmenin (Builder'ın kendi MasterMetadataReader.FindMatch'i) hiç
        // bulamadığı ama doğru LaunchBox DatabaseID'sinin elle bilindiği oyunlar için (bkz.
        // GameStateInfo.MetadataSourceIdOverride) — customGameStates zaten TÜM oyunların (custom +
        // katalog) durumunu taşıyor, ikinci bir DB sorgusuna gerek yok.
        ApplyManualMetadataSourceOverrides(customGameStates);
        // Ekran görüntüsü: gerçek katalog "Pinball" ile manuel "Pinball" ayrı satırlarda — "bu 2
        // pinball'ı birleştirirsem kartlar tek bir yerde toplanacak dimi". Yukarıdaki birleştirme
        // sadece custom-custom arasındaydı; bu, custom bir oyunu aynı başlık+platforma sahip
        // GERÇEK bir katalog oyunu VARSA ona taşır (bkz. FoldCustomGamesIntoMatchingCatalogGames).
        FoldCustomGamesIntoMatchingCatalogGames();
        BuildLocalFileIndex();
        foreach (var game in _allGames)
        {
            game.HasLocalFile = HasLocalFile(game);
            ApplyManualLinkInfo(game);
            NotifyArtworkDownloaded(game);
        }
        ComputeTopRankBadges();

        Platforms = new ObservableCollection<Platform>(CatalogDatabaseService.GetPlatforms());

        // Rozetlerdeki sayı sabit bir değer değil, gerçek oyun listesinden hesaplanıyor —
        // böylece "0 oyun var ama rozet 1406 yazıyor" gibi senkronsuzluk hiç oluşmaz.
        SyncPlatformGameCounts();
        RebuildPlatformListItems();

        PlatformFilter = BuildPlatformColumnFilter();
        GenresFilter = BuildColumnFilter("Türler", _allGames.Select(g => g.Genres));
        PublisherFilter = BuildColumnFilter("Yayıncı", _allGames.Select(g => g.Publisher));
        CommunityRatingFilter = BuildColumnFilter("Topluluk Puanı", _allGames.Select(g => g.CommunityRating.HasValue ? g.CommunityRating.Value.ToString("0.0") : string.Empty));
        RegionFilter = BuildColumnFilter("Bölge", _allGames.Select(g => g.Region));
        SourceFilter = BuildColumnFilter("Kaynak", _allGames.Select(g => g.SourceDat));
        MatchMethodFilter = BuildColumnFilter("Eşleşme Yöntemi", _allGames.Select(g => g.MatchMethod));
        MaxPlayersFilter = BuildColumnFilter("Oyuncu", _allGames.Select(g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString()));
        // Title/File: ~67 bin neredeyse hiç tekrarlamayan değer var — bunlar için tam checkbox
        // listesi kurmak (BuildColumnFilter) hem gereksiz (kimse 67 bin satırlık bir onay
        // listesinde gezinmez) hem de popup'ı açarken donmaya yol açıyordu. Sadece arama kutusu.
        TitleFilter = BuildSearchOnlyColumnFilter("Başlık");
        FileFilter = BuildSearchOnlyColumnFilter("File");
        MatchedFilter = BuildColumnFilter("Durum", _allGames.Select(GetMatchedStatusLabel));
        FavoriteFilter = BuildColumnFilter("Favori", _allGames.Select(g => g.IsFavorite ? "Evet" : "Hayır"));
        HasLocalFileFilter = BuildColumnFilter("Durum", _allGames.Select(g => g.HasLocalFile ? "Oynanabilir" : "Eksik"));
        ActionsFilter = new ActionsColumnFilterViewModel(FavoriteFilter, HasLocalFileFilter);
        BoxFilter = BuildColumnFilter("Box", _allGames.Select(g => g.HasBox ? "Evet" : "Hayır"));
        ScreenshotFilter = BuildColumnFilter("SS", _allGames.Select(g => g.HasScreenshot ? "Evet" : "Hayır"));

        AllColumnFilters = new[]
        {
            PlatformFilter, GenresFilter, PublisherFilter, CommunityRatingFilter,
            RegionFilter, SourceFilter, MatchMethodFilter, MaxPlayersFilter,
            TitleFilter, FileFilter, MatchedFilter, FavoriteFilter, HasLocalFileFilter,
            BoxFilter, ScreenshotFilter,
        };
        foreach (var filter in AllColumnFilters)
            filter.FilterChanged += ApplyFilter;

        // Kullanıcı isteği: "shooter ve horror'u tıkladım ... ikisi de ayrı ayrı gözükmeli" —
        // FilterByGenreToken'ın biriktirdiği token kümesi (bkz. _activeGenreTokens) filtre
        // kapsül menüden/rozetten TAMAMEN temizlenince (IsActive false'a düşünce) de senkron
        // kalsın diye.
        GenresFilter.FilterChanged += () =>
        {
            if (!GenresFilter.IsActive)
                _activeGenreTokens.Clear();
        };

        // AYRI bir döngüde (yukarıdaki _activeGenreTokens temizleme handler'ından SONRA) — multicast
        // delegate'ler kayıt sırasıyla çalışır, RefreshActiveFilterChips her zaman GÜNCEL
        // _activeGenreTokens'ı okumalı (bkz. RefreshActiveFilterChips yorumu).
        foreach (var filter in AllColumnFilters)
            filter.FilterChanged += RefreshActiveFilterChips;

        // Popup açılmadan hemen önce Options'ı güncel kapsama göre tazeler (bkz.
        // RefreshColumnFilterOptions) — Title/File IsSearchOnly olduğu için Options hiç kurmuyor,
        // bu yüzden burada yok (ColumnFilterViewModel.Open zaten onlar için tetiklemiyor).
        PlatformFilter.RequestRefreshOptions += RefreshPlatformFilterOptions;
        GenresFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(GenresFilter, g => g.Genres);
        PublisherFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(PublisherFilter, g => g.Publisher);
        CommunityRatingFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(CommunityRatingFilter, g => g.CommunityRating.HasValue ? g.CommunityRating.Value.ToString("0.0") : string.Empty);
        RegionFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(RegionFilter, g => g.Region);
        SourceFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(SourceFilter, g => g.SourceDat);
        MatchMethodFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MatchMethodFilter, g => g.MatchMethod);
        MaxPlayersFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MaxPlayersFilter, g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString());
        MatchedFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MatchedFilter, GetMatchedStatusLabel);
        FavoriteFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(FavoriteFilter, g => g.IsFavorite ? "Evet" : "Hayır");
        HasLocalFileFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(HasLocalFileFilter, g => g.HasLocalFile ? "Oynanabilir" : "Eksik");
        BoxFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(BoxFilter, g => g.HasBox ? "Evet" : "Hayır");
        ScreenshotFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(ScreenshotFilter, g => g.HasScreenshot ? "Evet" : "Hayır");

        RebuildPlaylistChips();

        // Backing field'a doğrudan atama yapılıyor ki henüz Games/ApplyFilter hazır değilken
        // OnSelectedPlatformChanged/OnSelectedChipChanged tetiklenip erken/eksik bir filtreleme
        // yapılmasın. Kullanıcı isteği: "program açılışında son açık bırakılan playlist ve
        // platformu açsın" — kayıtlı bir eşleşme yoksa (ör. platform silinmiş/kategori
        // kapatılmış) sessizce "All Platforms"/hiç chip'e düşer.
        selectedPlatform = Platforms.FirstOrDefault(p => p.Name == _appSettings.LastSelectedPlatform)
            ?? Platforms.FirstOrDefault(p => p.IsAllPlatforms)
            ?? Platforms.First();
        if (_appSettings.LastSelectedChipKind is { } lastChipKind)
            selectedChip = PlaylistChips.FirstOrDefault(c => c.Kind == lastChipKind && c.PlaylistId == _appSettings.LastSelectedPlaylistId);
        if (selectedChip is not null)
        {
            selectedChip.IsSelected = true;
            _selectedChipMembership = selectedChip is { Kind: PlaylistChipKind.Playlist, PlaylistId: int id }
                ? UserDataService.GetPlaylistGameKeys(id)
                : new HashSet<string>();
        }
        contextMenuDisplayMode = _appSettings.ContextMenuDisplayMode;

        ApplyFilter();

        selectedGame = Games.FirstOrDefault();
        LoadSelectedGameVersions();

        PrewarmThumbnailCache();
    }

    // Kullanıcı isteği: "cache sistemi mi yapsak" — hızlı kaydırırken ilk kez görünen bir satırın
    // logo görselini SENKRON decode etmek (kaçınılmaz bir tek seferlik maliyet) takılmaya yol
    // açıyordu; IsAsync=True (Binding) denendi ama görsel "pop" ederek beliriyordu, geri alındı.
    // Bunun yerine kütüphane yüklendikten HEMEN sonra, arka planda (UI thread'i hiç bloklamadan)
    // TÜM gerçek logo dosyaları önceden decode edilip ThumbnailImageConverter'ın önbelleğine
    // yazılıyor — kullanıcı bir satıra kaydırdığında görsel artık neredeyse her zaman önbellekte,
    // senkron/anlık (hiç "pop" olmadan) gösteriliyor.
    //
    // Kullanıcı isteği: "sen onu yükselt çünkü diğer platformlarda varya ona göre kapasite oluştur"
    // — sabit bir varsayım yerine kapasite HER açılışta o an diskte gerçekten kaç logo varsa ona
    // göre (üzerine %25 pay bırakarak) EnsureCapacity ile yükseltiliyor; kullanıcı zamanla başka
    // platformlar için görsel indirdikçe otomatik büyümeye devam eder, asla küçülmez.
    private void PrewarmThumbnailCache()
    {
        var logoPaths = _allGames
            .Where(g => g.HasClearLogo)
            .Select(g => (g.ClearLogoThumbnailPath, DecodePixelWidth: 128))
            .ToList();

        RetroAudit.Converters.ThumbnailImageConverter.EnsureCapacity((int)(logoPaths.Count * 1.25) + 500);
        Task.Run(() => RetroAudit.Converters.ThumbnailImageConverter.PrewarmAsync(logoPaths));
    }

    // Ayarlar penceresi kapanınca MainWindow.xaml.cs bunu çağırır — diskteki (en son "Kaydet" ile
    // yazılmış) hali okuyup uygular. Ayarlar penceresi AÇIKKEN yapılan canlı önizleme (bkz.
    // ApplyLiveSettings) burada YOK SAYILIR: kullanıcı "Kaydet"e basmadan kapatırsa arayüz
    // sessizce en son kaydedilmiş ayarlara geri döner — standart bir ayarlar penceresi davranışı.
    // NOT: eskiden burada BuildLocalFileIndex + tüm oyunlar için artwork/HasLocalFile yeniden
    // hesabı da yapılıyordu; Games/Images kökü artık Ayarlar'dan hiç değiştirilemediği
    // (bkz. AppPaths, "portable data layout") için bu tarama gereksizdi ve ~45 bin oyun üzerinde
    // gözle görülür bir donmaya/"kapanıp açılıyor" hissine yol açıyordu — kaldırıldı.
    public void ReloadAppSettings()
    {
        _appSettings = ConfigService.LoadDefault();
        ContextMenuDisplayMode = _appSettings.ContextMenuDisplayMode;
        RowHeight = _appSettings.RowHeight;
        ShowVersionsAsSingleCard = _appSettings.ShowVersionsAsSingleCard;
        DetailPanelWidth = _appSettings.DetailPanelWidth;
        GroupPlatformsByCategory = _appSettings.GroupPlatformsByCategory;
        PlatformListDisplayMode = _appSettings.PlatformListDisplayMode;
        RegionColumnDisplayMode = _appSettings.RegionColumnDisplayMode;
        ProviderDesignMode = _appSettings.ProviderDesignMode;
        RebuildPlatformListItems();
    }

    public void RefreshLibrary()
    {
        BuildLocalFileIndex();
        foreach (var game in _allGames)
        {
            game.HasLocalFile = HasLocalFile(game);
            ApplyManualLinkInfo(game);
        }
        ApplyFilter();
        if (SelectedGame is not null)
            LoadSelectedGameVersions();
        SyncPlatformGameCounts();
    }

    // Ayarlar penceresi AÇIKKEN her değişiklikte (Kaydet'e basılmadan) çağrılır — kullanıcının
    // "değişiklik kaydetmeden görünmüyor" isteği üzerine eklendi. SettingsViewModel'in o anki
    // (henüz diske yazılmamış) değerlerini doğrudan okuyup MainWindow'a yansıtır; kalıcı hale
    // gelmesi hâlâ "Kaydet"e bağlı (bkz. ReloadAppSettings, SettingsViewModel.SaveSettings).
    public void ApplyLiveSettings(SettingsViewModel s)
    {
        ContextMenuDisplayMode = s.ContextMenuDisplayMode;
        RowHeight = s.RowHeight;
        ShowVersionsAsSingleCard = s.ShowVersionsAsSingleCard;
        PlatformListDisplayMode = s.PlatformListDisplayMode;
        RegionColumnDisplayMode = s.RegionColumnDisplayMode;
        ProviderDesignMode = s.ProviderDesignMode;

        GroupPlatformsByCategory = s.GroupPlatformsByCategory;
        _appSettings.CategoryVisibility = s.CategoryOptions.ToDictionary(o => o.Key, o => o.IsVisible);
        RebuildPlatformListItems();
    }

    // Sütun anahtarı -> varsayılan (kayıtlı tercih yoksa kullanılacak) görünürlük ve başlık.
    // Sıra, DataGrid'deki sütun sırasıyla aynı — sadece okunabilirlik için, işlevsel bir önemi yok.
    private static readonly (string Key, string Header, bool DefaultVisible)[] ColumnDefinitions =
    {
        ("Hide", "Gizle", true),
        ("Matched", "Durum", true),
        ("Logo", "Logo", true),
        ("Actions", "Actions", true),
        ("Title", "Başlık", true),
        ("Box", "Box", true),
        ("Screenshot", "SS", true),
        ("File", "File", true),
        ("Platform", "Platform", true),
        ("Genres", "Türler", true),
        ("Publisher", "Yayıncı", false),
        ("CommunityRating", "Topluluk Puanı", false),
        ("MaxPlayers", "Oyuncu", false),
        ("Region", "Bölge", false),
        ("Source", "Kaynak", false),
        ("MatchMethod", "Eşleşme Yöntemi", false),
    };

    // ColumnOptions'ı sabit varsayılanlarla kurar, ardından _appSettings.ColumnVisibility'de
    // kayıtlı bir tercih varsa (kullanıcı daha önce bu sütunu açıp/kapattıysa) onu üzerine uygular.
    private void BuildColumnOptions()
    {
        ColumnOptions.Clear();
        foreach (var (key, header, defaultVisible) in ColumnDefinitions)
        {
            var isVisible = _appSettings.ColumnVisibility.GetValueOrDefault(key, defaultVisible);
            ColumnOptions.Add(new ColumnVisibilityOption { Key = key, Header = header, IsVisible = isVisible });
        }
    }

    // MainWindow.xaml.cs, bir sütunun IsVisible'ı değiştiğinde (kullanıcı "Sütunlar" listesinden
    // tikini açıp/kapattığında) bunu çağırır — o anki tüm sütun durumlarını tek seferde diske yazar.
    public void SaveColumnVisibility()
    {
        _appSettings.ColumnVisibility = ColumnOptions.ToDictionary(o => o.Key, o => o.IsVisible);
        ConfigService.SaveDefault(_appSettings);
    }

    // MainWindow.xaml.cs'in başlangıçta kayıtlı genişlikleri uygulaması için — anahtarlar
    // ColumnDefinitions'daki aynı Key'ler (ör. "Title", "Platform").
    public IReadOnlyDictionary<string, double> ColumnWidths => _appSettings.ColumnWidths;

    // MainWindow.xaml.cs, bir sütun başlığının kenarı sürüklenip bırakıldığında (debounce'lu)
    // çağırır — o anki tüm sütun genişliklerini tek seferde diske yazar.
    public void SaveColumnWidths(Dictionary<string, double> widths)
    {
        _appSettings.ColumnWidths = widths;
        ConfigService.SaveDefault(_appSettings);
    }

    // MainWindow.xaml.cs'in başlangıçta uygulayacağı sütun sabitleme (pin) durumu — bkz.
    // AppSettings.PinnedLeftColumns/PinnedRightColumns yorumu.
    public IReadOnlyList<string> PinnedLeftColumns => _appSettings.PinnedLeftColumns;
    public IReadOnlyList<string> PinnedRightColumns => _appSettings.PinnedRightColumns;

    public void SavePinnedColumns(List<string> pinnedLeft, List<string> pinnedRight)
    {
        _appSettings.PinnedLeftColumns = pinnedLeft;
        _appSettings.PinnedRightColumns = pinnedRight;
        ConfigService.SaveDefault(_appSettings);
    }

    // MainWindow.xaml.cs'in başlangıçta uygulayacağı, kullanıcının sürükleyerek belirlediği tam
    // sütun sırası — bkz. AppSettings.ColumnOrder yorumu.
    public IReadOnlyList<string> ColumnOrder => _appSettings.ColumnOrder;

    public void SaveColumnOrder(List<string> order)
    {
        _appSettings.ColumnOrder = order;
        ConfigService.SaveDefault(_appSettings);
    }

    // Boş değerleri "(Boş)" olarak grupluyor ki filtre listesinde görünmez bir satır olmasın;
    // ApplyFilter'daki karşılaştırma da aynı normalizasyonu kullanıyor (bkz. NormalizeForFilter).
    private static string NormalizeForFilter(string value) => string.IsNullOrWhiteSpace(value) ? "(Boş)" : value;

    private static ColumnFilterViewModel BuildColumnFilter(string headerText, IEnumerable<string> rawValues)
    {
        var options = rawValues
            .Select(NormalizeForFilter)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterOption { Value = g.Key, Count = g.Count() });

        return new ColumnFilterViewModel(options) { HeaderText = headerText };
    }

    private ColumnFilterViewModel BuildPlatformColumnFilter() =>
        new(CreatePlatformFilterOptions(_allGames)) { HeaderText = "Platform" };

    // Title/File gibi neredeyse hiç tekrarlamayan sütunlar için: Options listesi hiç kurulmuyor
    // (GroupBy'ı bile çalıştırmıyor) — bkz. ColumnFilterViewModel.IsSearchOnly.
    private static ColumnFilterViewModel BuildSearchOnlyColumnFilter(string headerText) =>
        new() { HeaderText = headerText, IsSearchOnly = true };

    // Tools menüsünden bir eylem seçildiğinde ilgili pencereyi açar, ardından seçimi
    // "Tools" placeholder'ına geri döndürür (aksi halde ComboBox hep son seçili eylemi gösterirdi).
    partial void OnSelectedToolActionChanged(string value)
    {
        if (value == "Media Provider...")
            OpenMediaProvider();
        else if (value == "Crop Editor...")
            OpenCropEditor();

        if (value != "Tools")
            SelectedToolAction = "Tools";
    }

    partial void OnSelectedPlatformChanged(Platform? value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(CurrentPlatformHealthPercent));
        OnPropertyChanged(nameof(CurrentPlatformLogoPath));
        OnPropertyChanged(nameof(PlatformColumnVisibility));
        SaveLastSelection();
    }

    partial void OnSelectedChipChanged(PlaylistChip? value)
    {
        foreach (var chip in PlaylistChips)
            chip.IsSelected = chip == value;

        _selectedChipMembership = value is { Kind: PlaylistChipKind.Playlist, PlaylistId: int id }
            ? UserDataService.GetPlaylistGameKeys(id)
            : new HashSet<string>();
        ApplyFilter();
        SaveLastSelection();
    }

    // Kullanıcı isteği: "program açılışında son açık bırakılan playlist ve platformu açsın" —
    // platform veya chip her değiştiğinde diske yazılır (bkz. constructor'daki geri okuma).
    // "All Platforms" null olarak kaydediliyor (silinmiş bir platform ismini saklamak yerine).
    private void SaveLastSelection()
    {
        _appSettings.LastSelectedPlatform = SelectedPlatform is { IsAllPlatforms: false } ? SelectedPlatform.Name : null;
        _appSettings.LastSelectedChipKind = SelectedChip?.Kind;
        _appSettings.LastSelectedPlaylistId = SelectedChip?.PlaylistId;
        ConfigService.SaveDefault(_appSettings);
    }

    // UserDataService.Playlists (Favorites dahil) + sentetik Hidden/Recycle Bin chip'lerini
    // sırayla kurar. Mevcut seçim korunur (aynı chip yeniden bulunup atanır) ki bir playlist
    // yeniden adlandırıldığında/rengi değiştiğinde görünüm sıfırlanmasın.
    private void RebuildPlaylistChips()
    {
        var previousSelection = SelectedChip;
        PlaylistChips.Clear();

        foreach (var playlist in UserDataService.GetPlaylists())
        {
            PlaylistChips.Add(new PlaylistChip
            {
                PlaylistId = playlist.PlaylistId,
                Name = playlist.Name,
                Color = playlist.Color,
                IsBuiltIn = playlist.IsBuiltIn,
                Kind = PlaylistChipKind.Playlist,
            });
        }

        PlaylistChips.Add(new PlaylistChip { Name = "Hidden", Color = "#8A8D93", IsBuiltIn = true, Kind = PlaylistChipKind.Hidden });
        PlaylistChips.Add(new PlaylistChip { Name = "Recycle Bin", Color = "#8A8D93", IsBuiltIn = true, Kind = PlaylistChipKind.RecycleBin });
        PlaylistChips.Add(new PlaylistChip { Name = "Ready to Play", Color = "#3FB950", IsBuiltIn = true, Kind = PlaylistChipKind.ReadyToPlay });
        // Kullanıcı isteği: "onun adını Missing Game mi yapsak" — "Needs Search"ten değiştirildi,
        // PlaylistChipKind.NeedsSearch (persistence/switch-case'lerde kullanılan asıl kimlik) AYNI
        // kaldı, sadece görünen isim.
        PlaylistChips.Add(new PlaylistChip { Name = "Missing Game", Color = "#E5484D", IsBuiltIn = true, Kind = PlaylistChipKind.NeedsSearch });
        PlaylistChips.Add(new PlaylistChip { Name = "Top 250", Color = "#D9932B", IsBuiltIn = true, Kind = PlaylistChipKind.Top250 });
        PlaylistChips.Add(new PlaylistChip { Name = "Top 100", Color = "#D9932B", IsBuiltIn = true, Kind = PlaylistChipKind.Top100 });
        PlaylistChips.Add(new PlaylistChip { Name = "Top 25", Color = "#D9932B", IsBuiltIn = true, Kind = PlaylistChipKind.Top25 });

        if (previousSelection is not null)
            SelectedChip = PlaylistChips.FirstOrDefault(c => c.Kind == previousSelection.Kind && c.PlaylistId == previousSelection.PlaylistId);
    }

    // Chip'e tıklama: zaten seçiliyse tekrar tıklamak seçimi kaldırır (normal görünüme döner).
    [RelayCommand]
    private void SelectChip(PlaylistChip chip) => SelectedChip = SelectedChip == chip ? null : chip;

    [RelayCommand]
    private void BeginCreatePlaylist() => IsCreatingPlaylist = true;

    [RelayCommand]
    private void CommitCreatePlaylist()
    {
        if (!string.IsNullOrWhiteSpace(NewPlaylistName))
        {
            UserDataService.CreatePlaylist(NewPlaylistName.Trim());
            RebuildPlaylistChips();
        }

        NewPlaylistName = string.Empty;
        IsCreatingPlaylist = false;
    }

    [RelayCommand]
    private void RenamePlaylistChip((PlaylistChip Chip, string NewName) args)
    {
        if (args.Chip.PlaylistId is int id && !string.IsNullOrWhiteSpace(args.NewName))
        {
            UserDataService.RenamePlaylist(id, args.NewName.Trim());
            args.Chip.Name = args.NewName.Trim();
        }
    }

    [RelayCommand]
    private void SetPlaylistChipColor((PlaylistChip Chip, string Color) args)
    {
        if (args.Chip.PlaylistId is int id)
        {
            UserDataService.SetPlaylistColor(id, args.Color);
            args.Chip.Color = args.Color;
        }
    }

    [RelayCommand]
    private void DeletePlaylistChip(PlaylistChip chip)
    {
        if (chip.PlaylistId is not int id || chip.IsBuiltIn)
            return;

        UserDataService.DeletePlaylist(id);
        if (SelectedChip == chip)
            SelectedChip = null;
        RebuildPlaylistChips();
    }

    // Sağ tık menüsünün hedefi: toplu moddaysa seçili tüm oyunlar, değilse sadece ContextMenuGame.
    private IEnumerable<Game> GetContextMenuTargets() =>
        IsBulkContextMenu ? ContextMenuSelection : (ContextMenuGame is null ? Enumerable.Empty<Game>() : new[] { ContextMenuGame });

    // Oyun DataGrid'inde sağ tıklama menüsündeki "Playlist'e Ekle" popup'ı bu komutu kullanır —
    // hem tekil hem toplu modda (bkz. GetContextMenuTargets).
    [RelayCommand]
    private void AddGameToPlaylist(PlaylistChip chip)
    {
        if (chip.PlaylistId is not int id)
            return;

        foreach (var game in GetContextMenuTargets())
            UserDataService.AddToPlaylist(id, game.GameKey);

        if (SelectedChip == chip)
            RefreshSelectedChipMembership();

        IsAddToPlaylistPopupOpen = false;
        IsVersionsPopupOpen = false;
        IsContextMenuOpen = false;
    }

    // "Playlist'e Ekle" popup'ının içindeki "+ Yeni Playlist" satırı — yeni playlist'i oluşturup
    // aynı anda o anki seçimi (tekil/toplu) içine ekler, ayrı bir adım gerektirmez.
    [RelayCommand]
    private void CreatePlaylistAndAddSelection()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName))
            return;

        var id = UserDataService.CreatePlaylist(NewPlaylistName.Trim());
        foreach (var game in GetContextMenuTargets())
            UserDataService.AddToPlaylist(id, game.GameKey);

        NewPlaylistName = string.Empty;
        IsCreatingPlaylist = false;
        IsAddToPlaylistPopupOpen = false;
        IsVersionsPopupOpen = false;
        IsContextMenuOpen = false;
        RebuildPlaylistChips();
    }

    private void RefreshSelectedChipMembership()
    {
        _selectedChipMembership = SelectedChip is { Kind: PlaylistChipKind.Playlist, PlaylistId: int id }
            ? UserDataService.GetPlaylistGameKeys(id)
            : new HashSet<string>();
        ApplyFilter();
    }

    // DataGrid'deki yıldız sütunu — Favorites da sıradan bir playlist olduğu için (bkz.
    // UserDataService.ToggleFavorite) tek satırlık bir toggle yeterli.
    [RelayCommand]
    private void ToggleFavorite(Game game)
    {
        game.IsFavorite = UserDataService.ToggleFavorite(game.GameKey);
        if (SelectedChip is { Kind: PlaylistChipKind.Playlist, IsBuiltIn: true, Name: "Favorites" })
            RefreshSelectedChipMembership();
    }

    // Platform başına bir kez Directory.EnumerateFiles yapıp dosya adlarını HashSet'e alır.
    // Önceden her oyun için ayrı ayrı File.Exists çağrılıyordu (67 bin oyun = 67 bin disk I/O),
    // bu da açılışı gözle görülür şekilde yavaşlatıyordu ("program geç açılıyor"); platform
    // klasörünü tek seferde listeleyip bellekte arama yapmak aynı sonucu tek bir dizin taraması
    // (platform sayısı kadar, ~40) ile veriyor.
    private Dictionary<string, HashSet<string>>? _filesByPlatform;

    // Kullanıcının "Şu anki yoldan kullan" ile içe aktardığı VEYA ROM İçe Aktar'ın Eşleşmeyenler
    // sekmesinden ELLE bir oyuna (isterse belirli bir sürümüne) bağladığı (bkz. ManualLinkViewModel)
    // ROM'lar — RetroAudit'in kendi {Platform}\{File} kuralına taşınmadan, kullanıcının kendi
    // arşivindeki orijinal konumundan kullanılır. GameKey -> o oyuna bağlı TÜM override'lar (kullanıcı
    // geri bildirimi: "sormasın ayrı bir sürüm gibi sürümlerine eklesin" — bir oyunun BİRDEN FAZLA
    // bağlı dosyası olabilir, biri "genel" (TargetVersionRawName null) diğerleri sürüme özel).
    private Dictionary<string, List<FilePathOverrideInfo>> _filePathOverrides = new();

    // BAŞLAT butonu/"eksik ROM'u ara" ikonu gibi TEK bir dosya gerektiren yerler için: Game
    // seviyesindeki GENEL bağlantı (TargetVersionRawName null) varsa o, yoksa listede bulunan
    // İLK override (hangisi olursa) — hiçbiri yoksa null (standart disk taramasına düşülür).
    private FilePathOverrideInfo? GetPrimaryOverride(Game game) =>
        _filePathOverrides.TryGetValue(game.GameKey, out var overrides) && overrides.Count > 0
            ? overrides.FirstOrDefault(o => string.IsNullOrEmpty(o.TargetVersionRawName)) ?? overrides[0]
            : null;

    // Box/BG/Logo/SS için ayrı ayrı (görünen platform adı -> uzantısız dosya adı -> tam yol)
    // dizinleri — "Görsel Getir" ile indirilen dosyaların koyduğu yer (bkz. ArtworkService.
    // BuildLocalPath: AppPaths.Images\{PlatformDisplayName}\{Type}\). Uzantısız anahtar kullanılıyor
    // çünkü indirilen dosyanın uzantısı (jpg/png) kaynağa göre değişir, ROM dosya adıyla (uzantısız)
    // birebir eşleşmesi gereken tek şey isim gövdesi. Logo için TAM YOL gerekiyor (Logo sütununda
    // gerçek bir küçük resim gösteriliyor); diğer üçü sadece var/yok kontrolü için kullanılıyor
    // ama aynı yapıyı paylaşmak kodu tekilleştiriyor.
    private Dictionary<string, Dictionary<string, string>> _boxByPlatform = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Dictionary<string, string>> _screenshotByPlatform = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Dictionary<string, string>> _clearLogoByPlatform = new(StringComparer.OrdinalIgnoreCase);

    // Üstteki arama kutusunun alternatif isimlerde de eşleşmesi için (kullanıcı isteği: "üstteki
    // ara alanı varya alternative namelerdede arama yapsın") — GameId -> alternatif isimler.
    // SelectedGameAlternateNames (detay paneli) tek oyun için anlık DB sorgusu yapıyor, ama arama
    // her tuş vuruşunda TÜM listeyi süzdüğü için ~67 bin oyun için sorgu tekrarı yerine tek seferlik
    // bir toplu yükleme (bkz. CatalogDatabaseService.GetAllAlternateNames, RomImportService'te
    // AYNI desenle zaten kullanılıyor).
    private readonly Dictionary<int, List<string>> _alternateNamesByGameId = CatalogDatabaseService.GetAllAlternateNames();

    private void BuildLocalFileIndex()
    {
        _filePathOverrides = UserDataService.GetAllFilePathOverrides();

        _filesByPlatform = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _boxByPlatform = BuildMediaTypeIndex("Box");
        _screenshotByPlatform = BuildMediaTypeIndex("SS");
        _clearLogoByPlatform = BuildMediaTypeIndex("Logo");

        foreach (var platformDir in Directory.EnumerateDirectories(AppPaths.Games))
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(platformDir))
                files.Add(Path.GetFileName(file));
            _filesByPlatform[Path.GetFileName(platformDir)] = files;
        }
    }

    private Dictionary<string, Dictionary<string, string>> BuildMediaTypeIndex(string typeFolder)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var platformDir in Directory.EnumerateDirectories(AppPaths.Images))
        {
            var mediaTypeDir = Path.Combine(platformDir, typeFolder);
            if (!Directory.Exists(mediaTypeDir))
                continue;

            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(mediaTypeDir))
                files[Path.GetFileNameWithoutExtension(file)] = file;
            result[Path.GetFileName(platformDir)] = files;
        }
        return result;
    }

    // Kullanıcı geri bildirimi: "manuel resim indirmede sorun var ... boş gösterdi ... programı
    // kapatıp açınca gözüktü" — kayıt tarafı (bkz. GetMediaBaseFileName/SearchArtwork) game.File
    // boşsa (custom/standalone oyunlar — bkz. RegisterNewCustomGame, BuildCustomGame hiç File set
    // etmiyor) başlıktan türetilmiş bir dosya adıyla KAYDEDİYORDU, ama bu okuma tarafı SADECE
    // game.File'a bakıyordu — boşsa direkt null dönüp dosya diskte gerçekten var olsa bile hiç
    // bulunamıyordu (restart'ta BuildMediaTypeIndex ile sözlük yeniden kurulsa da GetBoxPath vb.
    // yine buradan geçtiği için AYNI şekilde bulamıyordu — kullanıcının "restart'ta düzeldi"
    // izlenimi başka bir oyun/senaryo olmalı, ama bu boş game.File durumu HER ZAMAN başarısız
    // oluyordu). Artık ikisi de AYNI GetMediaBaseFileName mantığını kullanıyor.
    // Kullanıcı isteği: "toplu indirmelerde biraz yavaşlık oluyor" — BulkFetchArtworkForGamesAsync
    // artık birden fazla oyunu EŞ ZAMANLI indiriyor (bkz. o metot), bu yüzden RegisterDownloadedMedia
    // artık BİRDEN FAZLA arka plan thread'inden aynı anda çağrılabiliyor; bu sözlükleri (_boxByPlatform
    // vb., sıradan Dictionary — thread-safe DEĞİL) okuyan TryGetMediaPath de (grid binding'leri
    // üzerinden UI thread'inden) AYNI ANDA çalışabildiği için ikisi de AYNI kilitle korunuyor.
    private static readonly object MediaIndexLock = new();

    private static string? TryGetMediaPath(Dictionary<string, Dictionary<string, string>> byPlatform, Game game)
    {
        var baseName = GetMediaBaseFileName(game);
        lock (MediaIndexLock)
        {
            return byPlatform.TryGetValue(game.PlatformDisplayName, out var files) && files.TryGetValue(baseName, out var path)
                ? path
                : null;
        }
    }

    private string GetBoxPath(Game game) => TryGetMediaPath(_boxByPlatform, game) ?? string.Empty;
    private string GetScreenshotPath(Game game) => TryGetMediaPath(_screenshotByPlatform, game) ?? string.Empty;
    private string GetClearLogoPath(Game game) => TryGetMediaPath(_clearLogoByPlatform, game) ?? string.Empty;

    // Medya sözlükleri (_boxByPlatform vb.) uygulama açılışında diskten BİR KEZ kuruluyor —
    // "Görsel Getir" ile yeni indirilen bir dosya restart olmadan bu sözlüklerde yoktu, bu yüzden
    // NotifyArtworkDownloaded hemen ardından çağrılsa bile GetXPath boş dönüyordu (kullanıcı geri
    // bildirimi: "resim gelmiyor, kapatıp açınca geliyor"). DownloadArtworkAsync artık her başarılı
    // indirmeden sonra ilgili sözlüğü bu metotla güncelliyor, restart beklemeden anında görünüyor.
    // public: MediaProviderViewModel kendi indirmesini (ArtworkService.DownloadAsync'i doğrudan
    // çağırıyor, DownloadArtworkAsync'i KULLANMIYOR) MainViewModel'e bildirebilsin diye (bkz.
    // MediaProviderWindow.xaml.cs) — kullanıcı geri bildirimi: "media provider da otomatik indirme
    // çalışmıyor heralde" — dosya gerçekten iniyordu ama bu sözlükler hiç güncellenmediği için
    // NotifyArtworkDownloaded onu bulamıyordu (restart'a kadar boş görünüyordu, AYNI kök neden).
    public void RegisterDownloadedMedia(string type, string platformDisplayName, string baseFileName, string destination)
    {
        // Yeni indirilen görselin arayüzde (tabloda ve detay panelinde) güncellenmesi için cache'i geçersiz kıl
        RetroAudit.Converters.ThumbnailImageConverter.Invalidate(destination);

        var byPlatform = type switch
        {
            "Box" => _boxByPlatform,
            "SS" => _screenshotByPlatform,
            "Logo" => _clearLogoByPlatform,
            _ => null,
        };
        if (byPlatform is null)
            return;

        lock (MediaIndexLock)
        {
            if (!byPlatform.TryGetValue(platformDisplayName, out var files))
            {
                files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                byPlatform[platformDisplayName] = files;
            }
            files[baseFileName] = destination;
        }
    }

    // NotifyRomDownloaded ile aynı desen (bkz. aşağıda) — tek bir oyun için "Görsel Getir"
    // tamamlandığında uygulama yeniden başlatılmadan grid/Logo sütunu VE sağ detay panelindeki
    // Box/Background/Screenshot önizlemeleri güncellensin diye (bkz. Game.HasBox vb. — artık path'in
    // doluluğundan türetiliyor, ayrıca set edilmiyor).
    public void NotifyArtworkDownloaded(Game game)
    {
        game.BoxPath = GetBoxPath(game);
        game.ScreenshotPath = GetScreenshotPath(game);
        game.ClearLogoPath = GetClearLogoPath(game);
        // Kullanıcı geri bildirimi: "resim indirdim anında render olmadı ... kapatıp açınca render
        // oldu" — bu oyunun bu türü (Box/SS/Logo) AYNI deterministik yola DAHA ÖNCE de yazılmışsa
        // (ör. tekrar indirme/arama), yukarıdaki *Path set edilirken YENİ değer ESKİSİYLE AYNI
        // string oluyor — [ObservableProperty]'nin ürettiği setter eşit değerde PropertyChanged hiç
        // tetiklemiyor, görsel restart'a kadar güncellenmeden kalıyordu (bkz.
        // Game.RefreshImageDisplayPaths yorumu). NotifyArtworkDownloaded'ın TEK ve HER çağrıldığı
        // yerde (17 farklı çağrı noktası) merkezi olarak burada çözülüyor, her yere ayrı ayrı
        // eklemek yerine.
        game.RefreshImageDisplayPaths();
    }

    // Grid'deki "eksik ROM'u ara" sütunu ve context menüsündeki "Open File Location"ın paylaştığı
    // tek dosya-var-mı kontrolü. Önce kullanıcının "Şu anki yoldan kullan" ile kaydettiği bir
    // override var mı bakılır (bkz. FilePathOverrides); yoksa standart kural devreye girer:
    // AppPaths.Games\{PlatformDisplayName}\{File} var mı?
    public bool HasLocalFile(Game game)
    {
        if (GetPrimaryOverride(game) is { } primary)
            return File.Exists(primary.FilePath);

        if (string.IsNullOrWhiteSpace(game.File))
            return false;

        if (_filesByPlatform is not null)
            return _filesByPlatform.TryGetValue(game.PlatformDisplayName, out var files) && files.Contains(game.File);

        return File.Exists(GetLocalFilePath(game));
    }

    private string GetLocalFilePath(Game game) =>
        GetPrimaryOverride(game) is { FilePath: { } primaryPath }
            ? primaryPath
            : Path.Combine(AppPaths.Games, game.PlatformDisplayName, game.File);

    // Kullanıcı isteği: "manuel bağlanan kayıtlar detay panelinde ve ana tabloda açıkça 'Manual
    // Link' rozetiyle gösterilsin ve hangi Game/GameVersion'a bağlandığı görülebilsin" — bkz.
    // Game.IsManuallyLinked/ManualLinkTargetVersionLabel, HasLocalFile ile AYNI yükleme noktalarında
    // çağrılır (bkz. bu metodun tüm çağıranları). SADECE BAŞLAT'ın kullanacağı (GetPrimaryOverride)
    // dosya manuel bağlıysa rozet gösterilir — oyunun BAŞKA bir sürümü ayrıca manuel bağlıysa o,
    // sadece kendi sürüm kartında işaretlenir (bkz. LoadSelectedGameVersions), Game rozetini etkilemez.
    private void ApplyManualLinkInfo(Game game)
    {
        var primary = GetPrimaryOverride(game);
        if (primary is not null && string.Equals(primary.MatchMethod, MatchMethods.ManualLink, StringComparison.Ordinal))
        {
            game.IsManuallyLinked = true;
            game.ManualLinkTargetVersionLabel = primary.TargetVersionRawName ?? string.Empty;

            // Kullanıcı isteği: "tabloda manuel eklenenlerin eşleşme yöntemi manuel gözükmeli
            // şuan boş gözüküyor" — Game.MatchMethod normalde LaunchBox METADATA eşleşme türünü
            // taşır (custom oyunlarda hiç dolmaz, gerçek bir katalog oyununda ROM manuel
            // bağlandığında ise artık alakasız kalır) — Eşleşme Yöntemi sütununda/filtresinde
            // kullanıcıya asıl anlamlı olan "bu satırın ROM'u nasıl bağlandığı" bilgisi.
            game.MatchMethod = "Manuel";
        }
        else
        {
            game.IsManuallyLinked = false;
            game.ManualLinkTargetVersionLabel = string.Empty;
        }
    }

    // UserDataService.GetAllCustomGames'ten gelen bir kaydı, katalogdan gelen bir Game ile AYNI
    // şekilde tabloda/detay panelinde görüntülenebilecek bir Game'e çevirir (bkz. CustomGameInfo
    // yorumu) — Title/Platform dışındaki tüm alanlar (Yayıncı, Tür, Yıl, kapak...) bilinçli olarak
    // boş bırakılıyor: Game'in kendi varsayılanları zaten "Unknown"/"-" gösteriyor (bkz.
    // Game.PublisherDisplay/ReleaseYearDisplay), burada AYRICA doldurmaya gerek yok.
    private static Game BuildCustomGame(CustomGameInfo info) => new()
    {
        GameId = 0,
        GameKey = info.GameKey,
        Title = info.Title,
        Platform = info.Platform,
        PlatformDisplayName = info.PlatformDisplayName,
        PlatformLogoPath = CatalogDatabaseService.GetPlatformLogoPath(info.PlatformDisplayName),
        Version = "Released",
        // Kullanıcı isteği: "oyuncu sayısını otomatik 1 yazıyor ... boş olmalı manueller için" —
        // Game.MaxPlayers'ın sınıf varsayılanı 1 (bilinen tek oyunculu oyunlar için), custom
        // oyunlarda bu YANLIŞ bir bilgi iddiası olurdu; 0 = bilinmiyor (bkz. MaxPlayersDisplay).
        MaxPlayers = 0,
    };

    // ManualLinkViewModel'in "+ Yeni Oyun" seçeneği (bkz. ManualLinkViewModel.NewGameSentinelKey)
    // seçildiğinde RomImportWindow.xaml.cs tarafından çağrılır — kataloktaki HİÇBİR Game'e karşılık
    // gelmeyen, dosyanın kendi adından türetilmiş bağımsız bir oyun kaydı oluşturur. Bundan sonraki
    // adım (dosyanın KENDİSİNİ bu yeni oyuna bağlamak) BİLEREK burada YAPILMIYOR — çağıran, döndürülen
    // Game'i normal RomImportViewModel.CompleteManualLink'e (AYNI SaveFilePathOverride/
    // MatchMethods.ManualLink çağrısı) geçirir, böylece "yeni oyun oluştur" ile "mevcut oyuna
    // bağla" TEK bir mekanizmayı paylaşır (kullanıcı isteği: "2 ayrı eşleştirme şekli olmasın").
    // scannedFolderName ile eşleşen bilinen bir katalog platformu varsa (bkz.
    // RomImportService.PlatformNameMatchesFolder) onun Platform/PlatformDisplayName'i kullanılır —
    // böylece yeni oyun de o platformun logosu/gruplamasıyla tutarlı görünür; yoksa klasör adının
    // kendisi ham platform adı olarak kullanılır.
    public Game RegisterNewCustomGame(string title, string scannedFolderName)
    {
        var matchingPlatformGame = _allGames.FirstOrDefault(g => RomImportService.PlatformNameMatchesFolder(g, scannedFolderName));
        var platform = matchingPlatformGame?.Platform ?? scannedFolderName;
        var platformDisplayName = matchingPlatformGame?.PlatformDisplayName ?? scannedFolderName;

        // Kullanıcı isteği: "manuellerdede aynı isimdekileri tek bi yerde toplasın ayrı ayrı
        // açmasın ... tabloda tek [satır], sürüm kartları ayrı yani" — aynı başlık+platform için
        // önceden oluşturulmuş bir oyun varsa (custom VEYA GERÇEK katalog oyunu) YENİ bir satır
        // AÇILMAZ, o oyun geri döndürülür; çağıran (RomImportViewModel.CompleteManualLinkAsync/
        // ImportUnmatchedFileAsNewGameAsync) yeni dosyayı bu oyunun override'larına EK bir sürüm
        // kartı olarak ekler (bkz. LoadSelectedGameVersions'daki sentetik kart üretimi, bir GameKey
        // birden fazla override taşıyabilir). GERÇEK katalog eşleşmesi TERCİH EDİLİR — kapak/tür/
        // yayıncı gibi tam metadata taşıdığı için custom'un "Unknown" görünümünden daha iyi.
        var existing = _allGames.FirstOrDefault(g =>
            string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(g.PlatformDisplayName, platformDisplayName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var info = new CustomGameInfo($"custom:{Guid.NewGuid():N}", title, platform, platformDisplayName);
        UserDataService.AddCustomGame(info);

        var game = BuildCustomGame(info);
        _allGames.Add(game);
        SyncPlatformGameCounts();
        RebuildPlatformListItems();
        return game;
    }

    // Ekran görüntüsü: gerçek katalog "Pinball" (tam metadata) ile daha önce manuel bağlanmış
    // custom "Pinball" (kapak/tür/yayıncı yok, kırmızı çarpı) ayrı satırlarda — "bu 2 pinball'ı
    // birleştirirsem kartlar tek bir yerde toplanacak dimi". RegisterNewCustomGame'deki kontrol
    // BUNDAN SONRAKİ bağlamalar için bu duruma hiç düşülmesini engelliyor; bu metot SADECE o
    // düzeltmeden ÖNCE (ya da katalog Builder ile yeniden üretilip "Pinball" artık gerçekten
    // eklendiğinde) oluşmuş custom satırları, aynı başlık+platforma sahip GERÇEK bir katalog
    // oyunu varsa ona bir kez taşır — custom satır tamamen kaldırılır, dosyası gerçek oyunun
    // Sürümler listesine (bkz. LoadSelectedGameVersions'daki sentetik kart üretimi) eklenmiş olur.
    private void FoldCustomGamesIntoMatchingCatalogGames()
    {
        var customGames = _allGames.Where(g => g.GameKey.StartsWith("custom:", StringComparison.Ordinal)).ToList();
        if (customGames.Count == 0)
            return;

        var catalogGames = _allGames.Where(g => !g.GameKey.StartsWith("custom:", StringComparison.Ordinal)).ToList();

        // Kullanıcı bulgusu: "Karaoke Studio Senyou Cassette Vol. 1" (custom) ile katalogdaki
        // "Karaoke Studio Senyou Cassette - Top Hit 20 Vol. 1" AYNI dosya (CRC32: 466061D7) ama
        // başlıklar hem birebir hem alternatif isim listesinde de örtüşmüyor — bu yüzden CRC32
        // (dosya İÇERİĞİ, başlıktan bağımsız en güvenilir sinyal) burada AYRICA bir katman: custom
        // oyunun kendi override'larındaki CRC32, katalogdaki GERÇEK bir sürümün CRC32'siyle TEK bir
        // oyunda kesişiyorsa (RomImportService Tier 2 ile AYNI mantık) o oyuna katlanır.
        var catalogGamesById = catalogGames
            .Where(g => g.GameId != 0)
            .GroupBy(g => g.GameId)
            .ToDictionary(gr => gr.Key, gr => gr.First());
        var allOverrides = UserDataService.GetAllFilePathOverrides();
        var catalogGamesByKey = catalogGames.ToDictionary(g => g.GameKey);

        // Kullanıcı bulgusu: "program geç açılıyor" — burada eskiden kataloğun TÜMÜNÜN
        // (GameVersions+GameHashes, ~116 bin satır) CRC32 indeksi kuruluyordu ama sadece
        // custom oyunların override'larında geçen bir avuç CRC32 değeri için kullanılıyordu.
        // Artık SADECE o değerler için hedefli bir sorgu (WHERE Crc32 IN (...)) atılıyor.
        var customCrc32Values = customGames
            .SelectMany(c => allOverrides.TryGetValue(c.GameKey, out var overrides) ? overrides : Enumerable.Empty<FilePathOverrideInfo>())
            .Select(o => o.Crc32)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var catalogCrc32Index = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        if (customCrc32Values.Count > 0)
        {
            foreach (var (crc32, gameIds) in CatalogDatabaseService.GetGameIdsByCrc32(customCrc32Values))
            {
                var games = gameIds
                    .Select(id => catalogGamesById.TryGetValue(id, out var g) ? g : null)
                    .Where(g => g is not null)
                    .Select(g => g!)
                    .Distinct()
                    .ToList();
                if (games.Count > 0)
                    catalogCrc32Index[crc32] = games;
            }
        }

        // Kullanıcı bulgusu: "Karaoke Studio Senyou Cassette Vol. 1" custom'unun override'ı ile
        // GERÇEK katalog oyunu "Top Hit 20 Vol. 1"in KENDİ override'ı AYNI dosya yoluna işaret
        // ediyor (kullanıcı iki kez, biri katalog oyununa biri de farkında olmadan yeni bir custom
        // oyuna, elle bağlamış) — DAT'taki resmi CRC32 farklı olduğu için yukarıdaki CRC32 katmanı
        // bunu YAKALAMAZ. En kesin sinyal: aynı FilePath'e sahip başka bir (custom OLMAYAN)
        // override var mı — varsa dosya zaten gerçek oyuna bağlı demektir, custom satır fazlalık.
        Game? FindDuplicateFilePathTarget(Game custom)
        {
            if (!allOverrides.TryGetValue(custom.GameKey, out var customOverrides))
                return null;
            var customFilePaths = customOverrides
                .Select(o => o.FilePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (customFilePaths.Count == 0)
                return null;

            var duplicateTargetKeys = allOverrides
                .Where(kv => !kv.Key.StartsWith("custom:", StringComparison.Ordinal)
                    && kv.Value.Any(o => o.FilePath is not null && customFilePaths.Contains(o.FilePath)))
                .Select(kv => kv.Key)
                .Distinct()
                .ToList();

            return duplicateTargetKeys.Count == 1 && catalogGamesByKey.TryGetValue(duplicateTargetKeys[0], out var target)
                ? target
                : null;
        }

        // Kullanıcı bulgusu: "Phantom Air Mission" (custom, elle bağlanmış) "Flight of the
        // Intruder"ın LaunchBox alternatif adı — başlıklar FARKLI olduğu için yukarıdaki tam
        // başlık eşleşmesi bunu hiç yakalamıyor, satır Manuel'de tek başına kalıyor. RomImportService
        // Tier 6 (ResolveCandidate) ile AYNI iki aşamalı (tam, sonra normalize) alternatif isim
        // eşleşmesi burada da uygulanıyor — TEK aday varsa (birden fazla katalog oyunu aynı
        // alternatif adı paylaşıyorsa katlanmıyor, RomImportService ile AYNI güvenlik kuralı).
        // "geç açılıyor" düzeltmesinin devamı: bu tam olarak _alternateNamesByGameId field
        // initializer'ının (yukarıda, satır ~859) yüklediği AYNI veri — 30 bin satırlık ikinci bir
        // sorgu atmak yerine o zaten hazır sözlük burada da kullanılıyor.
        var alternateNamesByGameId = _alternateNamesByGameId;
        var anyFolded = false;
        foreach (var custom in customGames)
        {
            var match = FindDuplicateFilePathTarget(custom);

            if (match is null && allOverrides.TryGetValue(custom.GameKey, out var customOverrides))
            {
                var crc32Candidates = customOverrides
                    .Select(o => o.Crc32)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .SelectMany(c => catalogCrc32Index.TryGetValue(c!, out var list) ? list : Enumerable.Empty<Game>())
                    .Distinct()
                    .ToList();
                if (crc32Candidates.Count == 1)
                    match = crc32Candidates[0];
            }

            match ??= catalogGames.FirstOrDefault(g =>
                string.Equals(g.Title, custom.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.PlatformDisplayName, custom.PlatformDisplayName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var platformScoped = catalogGames.Where(g =>
                    string.Equals(g.PlatformDisplayName, custom.PlatformDisplayName, StringComparison.OrdinalIgnoreCase)
                    && alternateNamesByGameId.TryGetValue(g.GameId, out var names)
                    && names.Any(n => string.Equals(n, custom.Title, StringComparison.OrdinalIgnoreCase))).ToList();

                if (platformScoped.Count == 0)
                {
                    var normalizedCustomTitle = RomImportService.NormalizeTitleAggressive(custom.Title);
                    platformScoped = catalogGames.Where(g =>
                        string.Equals(g.PlatformDisplayName, custom.PlatformDisplayName, StringComparison.OrdinalIgnoreCase)
                        && alternateNamesByGameId.TryGetValue(g.GameId, out var names)
                        && names.Any(n => RomImportService.NormalizeTitleAggressive(n) == normalizedCustomTitle)).ToList();
                }

                if (platformScoped.Count == 1)
                    match = platformScoped[0];
            }

            if (match is null)
                continue;

            UserDataService.ReassignFilePathOverrides(custom.GameKey, match.GameKey);
            UserDataService.DeleteCustomGame(custom.GameKey);
            _allGames.Remove(custom);
            anyFolded = true;
        }

        if (anyFolded)
            UserDataService.DeduplicateFilePathOverrides();
    }

    // Ana tablodaki kapsül (sağ tık) menüsündeki "Bağla" — kullanıcı isteği: "tablodan bağlama
    // yapmak istediğimde klasörden değil tablodaki oyunların listesinden seçmeli". Bilgisayardan
    // dosya seçmek YERİNE (eski davranış), bu satırın dosyasını tablodaki BAŞKA bir oyuna taşımak
    // için ManualLinkWindow'daki AYNI arama kutulu oyun listesi açılır (bkz. MainWindow.xaml.cs) —
    // ör. yanlışlıkla ayrı kalmış bir custom oyunu gerçek katalog karşılığına elle birleştirmek gibi.
    public event Action<Game, string>? RequestLinkToGameSelection;

    [RelayCommand]
    private void RequestLinkToGame(Game game)
    {
        // Kullanıcı isteği: "rom unun olup olmaması fark etmemeli" — dosyası olmayan bir oyun da
        // artık Bağla ile başka bir oyuna sahipsiz bir sürüm kartı olarak eklenebilir (bkz.
        // LinkGameFileToGameAsync). İkinci parametre sadece ManualLinkWindow'un başlığında
        // gösterilen bir etiket (bkz. MainWindow.xaml.cs) — dosya yoksa oyunun kendi adı kullanılır.
        var displayLabel = HasLocalFile(game) ? GetLocalFilePath(game) : game.Title;
        RequestLinkToGameSelection?.Invoke(game, displayLabel);
    }

    // MainWindow.xaml.cs, ManualLinkWindow'dan "Bağla" ile dönünce çağırır. sourceGame'in dosyası
    // targetGame'e taşınır: targetGame'e yeni bir override eklenir; sourceGame'in kendi override'ı
    // (varsa) silinir. sourceGame custom bir oyunsa VE başka hiç dosyası kalmadıysa (bkz.
    // FoldCustomGamesIntoMatchingCatalogGames ile AYNI mantık) satırın kendisi de kaldırılır —
    // "birleştirme" gerçekten TEK satırda toplanmış olsun diye. Standart (override'sız, kataloğun
    // Games\{Platform}\{File} kuralıyla diskte bulunan) bir dosyaysa sourceGame'den DETACH
    // edilemiyor (fiziksel dosyayı taşımak/silmek kapsam dışı) — bu durumda dosya HER İKİ oyunda da
    // "sahiplenilmiş" görünür, sourceGame'e dokunulmaz.
    public async Task LinkGameFileToGameAsync(Game sourceGame, Game targetGame, GameVersion? targetVersion)
    {
        // Kullanıcı isteği: "rom unun olup olmaması fark etmemeli" — sourceGame'in hiç dosyası
        // yoksa (ör. kataloğun DAT'ında ayrı bir Game olarak duran ama hiç ROM'u bulunmayan bir
        // varyant) yine de targetGame'in Sürümler listesine sahipsiz (kırmızı çarpı) bir kart
        // olarak eklenir; sourceGame'in KENDİSİ tablodan gizlenir (satır kaybolur, "Ayır" ile
        // (bkz. RemoveVersionLink) veya Gizlenenler'den geri alınabilir).
        if (!HasLocalFile(sourceGame))
        {
            var rawName = targetVersion?.RawDatName;
            if (string.IsNullOrEmpty(rawName))
                rawName = GetSourceVersionRawName(sourceGame);

            UserDataService.SaveFilePathOverride(targetGame.GameKey, null, MatchMethods.ManualLink, rawName, sourceGameKey: sourceGame.GameKey);
            UserDataService.SetHidden(sourceGame.GameKey, true);
            sourceGame.IsHidden = true;

            RefreshLibrary();
            RequestShowMessage?.Invoke($"\"{sourceGame.Title}\" -> \"{targetGame.Title}\" oyununa (ROM'suz) bağlandı.");
            return;
        }

        var filePath = GetLocalFilePath(sourceGame);

        var sourceOverride = _filePathOverrides.TryGetValue(sourceGame.GameKey, out var sourceOverrides)
            ? sourceOverrides.FirstOrDefault(o => string.Equals(o.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            : null;

        var zipEntryName = sourceOverride?.ZipEntryName ?? RomImportService.GetSoleZipEntryName(filePath);
        string? crc32;
        try
        {
            crc32 = sourceOverride?.Crc32 ?? await Task.Run(() => RomImportService.ComputeCrc32(filePath, zipEntryName));
        }
        catch (Exception)
        {
            crc32 = null;
        }

        UserDataService.SaveFilePathOverride(targetGame.GameKey, filePath, MatchMethods.ManualLink, targetVersion?.RawDatName, zipEntryName, crc32, sourceGameKey: sourceGame.GameKey);

        var sourceRemoved = false;
        if (sourceOverride is not null)
        {
            UserDataService.RemoveFilePathOverride(sourceGame.GameKey, filePath);

            if (sourceGame.GameKey.StartsWith("custom:", StringComparison.Ordinal) && UserDataService.CountFilePathOverrides(sourceGame.GameKey) == 0)
            {
                UserDataService.DeleteCustomGame(sourceGame.GameKey);
                _allGames.Remove(sourceGame);
                sourceRemoved = true;
            }
        }

        // Kullanıcı geri bildirimi: "ilk bağlamada oyuna bağlıyor sonra bidaha bağlaya basınca
        // listeden gidiyor" — sourceOverride YOKSA (standart, katalog kuralıyla diskte bulunan bir
        // dosya) satır BİLEREK dokunulmadan bırakılıyordu (fiziksel dosya "koparılamıyor" diye),
        // bu yüzden ancak İKİNCİ bir Bağla (başka bir mekanizmayla) satırı gizliyordu — kullanıcı
        // her Bağla'nın TEK seferde satırı kaldırmasını bekliyor. Dosyanın kendisi taşınmasa/
        // kopyalanmasa bile satırı TABLODAN gizlemek (silmek değil) tamamen güvenli — "Ayır" ile
        // (bkz. RemoveVersionLink) her zaman geri getirilebiliyor.
        if (!sourceRemoved)
        {
            UserDataService.SetHidden(sourceGame.GameKey, true);
            sourceGame.IsHidden = true;
        }

        RefreshLibrary();
        RequestShowMessage?.Invoke($"\"{Path.GetFileName(filePath)}\" -> \"{targetGame.Title}\" oyununa bağlandı.");
    }

    // Kullanıcı geri bildirimi: "kartlardayken crc32 kodları falan gözükmüyor" — dosyasız bir
    // birleştirmede gerçek bir crc32 zaten olamaz (dosya yok), ama kaynağın KENDİ kataloğundaki
    // (SourceGameKey, bkz. LinkGameFileToGameAsync) o sürüme ait BEKLENEN dosya adlarını en azından
    // bilgi amaçlı gösterebiliriz — tıpkı gerçek ama sahipsiz bir DAT sürümünün (kırmızı çarpı)
    // kendi Hashes'ini göstermesi gibi.
    private List<GameHash> GetExpectedHashesForFilelessLink(FilePathOverrideInfo custom)
    {
        if (custom.SourceGameKey is null)
            return new List<GameHash>();

        var source = _allGames.FirstOrDefault(g => g.GameKey == custom.SourceGameKey);
        if (source is null || source.GameId == 0)
            return new List<GameHash>();

        var match = CatalogDatabaseService.GetVersions(source.GameId, source.GameKey)
            .FirstOrDefault(v => string.Equals(v.RawDatName, custom.TargetVersionRawName, StringComparison.OrdinalIgnoreCase));
        return match?.Hashes ?? new List<GameHash>();
    }

    // LinkGameFileToGameAsync'in dosyasız dalı için — targetVersion verilmemişse (ör. BulkLinkAsync,
    // dosya olmadığı için kendi sentetik ismini üretemez) kaynağın KENDİ kataloğundaki Preferred
    // (yoksa ilk) sürümünün ham DAT adını kullanır, custom oyunlarda (GameId=0, hiç sürümü yok)
    // başlığa düşülür.
    private static string GetSourceVersionRawName(Game source)
    {
        if (source.GameId != 0)
        {
            var versions = CatalogDatabaseService.GetVersions(source.GameId, source.GameKey);
            var preferred = versions.FirstOrDefault(v => v.IsPreferred) ?? versions.FirstOrDefault();
            if (preferred is not null)
                return preferred.RawDatName;
        }
        return source.Title;
    }

    // Kullanıcı isteği: "kart içine ayırma butonu ekle ... kendi kartının üstünde olsun bağlı
    // olanın" — Sürümler panelindeki manuel bağlı (IsManuallyLinked) bir kartın üstündeki "Ayır"
    // butonu. TargetVersionRawName'e göre siliniyor (bkz. UserDataService.
    // RemoveFilePathOverrideByTargetVersion yorumu) — hem gerçek bir DAT sürümüne sonradan manuel
    // bağlanmış kartlar HEM DE tamamen sentetik (custom/dosyasız birleştirme) kartlar için AYNI
    // şekilde çalışır. Kullanıcı geri bildirimi: "merge'i kaldırınca tabloya geri gelmiyor" —
    // dosyasız birleştirmede kaynak oyun gizlenmişti (bkz. LinkGameFileToGameAsync); SourceGameKey
    // hâlâ _allGames'te VE gizliyse burada tekrar görünür yapılır (custom bir oyun MERGE sırasında
    // TAMAMEN SİLİNMİŞSE, bkz. aynı metodun dosyalı dalı, geri getirilecek bir şey kalmaz — bu
    // durumda FirstOrDefault null döner, sessizce atlanır).
    [RelayCommand]
    private void RemoveVersionLink(GameVersion version)
    {
        if (SelectedGame is not { } game || string.IsNullOrEmpty(version.RawDatName))
            return;

        UserDataService.RemoveFilePathOverrideByTargetVersion(game.GameKey, version.RawDatName);

        if (version.SourceGameKey is not null
            && _allGames.FirstOrDefault(g => g.GameKey == version.SourceGameKey) is { IsHidden: true } source)
        {
            UserDataService.SetHidden(source.GameKey, false);
            source.IsHidden = false;
        }

        RefreshLibrary();
    }

    // View katmanı (MainWindow.xaml.cs) bunu dinleyip embedded WebView2 penceresini açıyor —
    // ViewModel doğrudan bir Window tipine bağımlı olmasın diye (bkz. RequestOpenMediaProvider
    // ile aynı desen).
    // ForcedFileName: null ise (grid'deki genel "Web'de Ara") tarayıcının önerdiği isim aynen
    // korunur (eski davranış). Dolu ise (bkz. SearchRomForVersion) indirilen dosya, tarayıcının
    // önerdiği isim NE OLURSA OLSUN bu isme zorlanır — kullanıcı isteği: "indirirken otomatik o
    // isimle kaydetsin, isim yazmakla uğraşmayım", tıpkı görsel aramasındaki (bkz. MediaSearchWindow)
    // otomatik isimlendirme gibi.
    public event Action<(string Url, string TargetFolder, string? ForcedFileName, Game Game)>? RequestSearchRom;

    [RelayCommand]
    private void SearchWeb(Game game)
    {
        var region = string.IsNullOrWhiteSpace(game.Region) || game.Region == "Unknown" ? "USA" : game.Region;
        var tag = RomSearchTagMap.Resolve(game.Platform);
        var query = string.IsNullOrEmpty(tag)
            ? $"{game.Title} ({region}) {game.PlatformDisplayName} rom"
            : $"{game.Title} ({region}) {game.PlatformDisplayName} rom + {tag}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);

        var targetFolder = Path.Combine(AppPaths.Games, game.PlatformDisplayName);

        RequestSearchRom?.Invoke((url, targetFolder, null, game));
    }

    // Sürüm kartındaki "Ara" (ROM) düğmesi. Sorgu BİLİNÇLİ OLARAK hiçbir SİTEYE kısıtlı değil
    // ("site:archive.org", "reddit" gibi önceden tahmin edilen kaynaklara sınırlamıyoruz, dosya
    // internetin herhangi bir köşesinde barındırılıyor olabilir) — ama tam dosya adı tek başına
    // sık sık salt hash/bilgi listeleyen detay sayfalarını öne çıkarıyordu, indirme linkinin
    // kendisini bulmak zorlaşıyordu. Bu yüzden SAYFA TÜRÜNE (site'a değil) göre bir OR grubu
    // eklendi: download/"download page"/"direct link"/roms — bunlardan biri sayfada geçmeli.
    // Hangi sonuca tıklanacağına HER ZAMAN kullanıcı karar verir (uygulama otomatik bir
    // tarama/parse yapmıyor, insan kontrolünde kalıyor — bkz. RomSearchWindow'un genel tasarım
    // felsefesi). İndirilen dosya, katalogdaki bu sürümün TAM beklenen dosya adına (Hashes[0].
    // FileName, ör. "Air Raid (USA).a26") zorlanıyor, böylece HasLocalFile/IsVersionOwned
    // eşleşmesi başka bir işlem gerekmeden hemen tutar.
    [RelayCommand]
    private void SearchRomForVersion(GameVersion version)
    {
        if (SelectedGame is not { } game)
            return;

        var forcedFileName = version.Hashes.FirstOrDefault()?.FileName;

        // Beklenmedik durum: bu sürümün hiç hash/dosya adı kaydı yoksa (katalogda eksik veri),
        // tam dosya adına göre arama yapılamaz — eski başlık-tabanlı sorguya düşülüyor.
        var query = !string.IsNullOrWhiteSpace(forcedFileName)
            ? $"\"{forcedFileName}\" (download | \"download page\" | \"direct link\" | roms)"
            : $"{game.Title} {game.PlatformDisplayName} rom";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);

        var targetFolder = Path.Combine(AppPaths.Games, game.PlatformDisplayName);

        RequestSearchRom?.Invoke((url, targetFolder, forcedFileName, game));
    }

    // RomSearchWindow, WebView2'nin DownloadStarting/StateChanged olaylarını izleyip bir indirme
    // Completed durumuna geçtiğinde bunu çağırır (bkz. MainWindow.xaml.cs). Kapsamlı bir CRC32/
    // MD5 doğrulaması ve katalog güncellemesi henüz yok (bkz. README "Yol haritası" — gerçek ROM
    // tarama/hash kontrolü ayrı, daha büyük bir iş); burada sadece o TEK oyun için "dosya var mı"
    // durumu anında tazeleniyor ki grid'deki arama ikonu (ve aktifse Dosya filtresi) kullanıcı
    // hiçbir şey yapmadan (yeniden başlatmadan) güncellensin. Filtre seçeneklerindeki "Var (N)"
    // sayacı bir sonraki açılışa kadar bir eksik kalabilir — kozmetik, ciddiye alınacak bir
    // tutarsızlık değil.
    public void NotifyRomDownloaded(Game game)
    {
        game.HasLocalFile = HasLocalFile(game);
        ApplyManualLinkInfo(game);
        ApplyFilter();

        // Sürüm kartından (SearchRomForVersion) indirilmiş olabilir — Sürümler listesindeki
        // Play/çarpı ikonlarının (IsVersionOwned) anında güncellenmesi için yeniden yükleniyor.
        if (game == SelectedGame)
            LoadSelectedGameVersions();
    }

    [RelayCommand]
    private void OpenFileLocation(Game game)
    {
        if (!HasLocalFile(game))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{GetLocalFilePath(game)}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }

    // Detay panelindeki platform logosu rozetine tıklayınca (bkz. MainWindow.xaml) o platformun
    // Games\{PlatformDisplayName} klasörünü açar — henüz hiç ROM içe aktarılmadıysa klasör
    // olmayabilir, bu durumda önce oluşturuluyor (Explorer boş bir klasörü de açabilir).
    [RelayCommand]
    private void OpenPlatformFolder(Game game)
    {
        var folder = Path.Combine(AppPaths.Games, game.PlatformDisplayName);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }

    // Detay panelindeki Box art'a/Clear Logo'ya sağ tıklayınca çıkan küçük kapsül menüsündeki
    // "Klasöre Git" düğmeleri — gerçek görsel yoksa (yer tutucu gösteriliyorsa) hiçbir şey yapmaz.
    [RelayCommand]
    private void OpenBoxArtFolder(Game game)
    {
        if (!game.HasBox)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.BoxPath}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }

    [RelayCommand]
    private void OpenClearLogoFolder(Game game)
    {
        var folder = Path.Combine(AppPaths.Images, game.PlatformDisplayName, "Logo");
        Directory.CreateDirectory(folder);
        // Gerçek logo varsa dosyayı seç, yoksa sadece klasörü aç
        if (game.HasClearLogo)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.ClearLogoPath}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
    }

    [RelayCommand]
    private void OpenScreenshotFolder(Game game)
    {
        var folder = Path.Combine(AppPaths.Images, game.PlatformDisplayName, "SS");
        Directory.CreateDirectory(folder);
        if (game.HasScreenshot)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.ScreenshotPath}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
    }

    [RelayCommand]
    private void DeleteBoxArt(Game game)
    {
        var path = game.BoxPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            // 1. Dosya kilidini kırmak için WPF Binding'i boşalt
            game.BoxPath = string.Empty;
            NotifyArtworkDownloaded(game);

            // 2. Thumbnail Cache'ini temizle (Dosya kilitleri serbest kalsın)
            RetroAudit.Converters.ThumbnailImageConverter.Invalidate(path);

            // 3. Dosyayı sil
            File.Delete(path);

            if (_boxByPlatform.TryGetValue(game.PlatformDisplayName, out var files))
                files.Remove(GetMediaBaseFileName(game));

            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            // Hata durumunda eski path'i geri yaz ki arayüzde kalsın
            game.BoxPath = path;
            NotifyArtworkDownloaded(game);
            RequestShowMessage?.Invoke($"Kapak silinirken hata oluştu: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteClearLogo(Game game)
    {
        var path = game.ClearLogoPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            // 1. Dosya kilidini kırmak için WPF Binding'i boşalt
            game.ClearLogoPath = string.Empty;
            NotifyArtworkDownloaded(game);

            // 2. Thumbnail Cache'ini temizle
            RetroAudit.Converters.ThumbnailImageConverter.Invalidate(path);

            // 3. Dosyayı sil
            File.Delete(path);

            if (_clearLogoByPlatform.TryGetValue(game.PlatformDisplayName, out var files))
                files.Remove(GetMediaBaseFileName(game));

            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            game.ClearLogoPath = path;
            NotifyArtworkDownloaded(game);
            RequestShowMessage?.Invoke($"Logo silinirken hata oluştu: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteScreenshot(Game game)
    {
        var path = game.ScreenshotPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            // 1. Dosya kilidini kırmak için WPF Binding'i boşalt
            game.ScreenshotPath = string.Empty;
            NotifyArtworkDownloaded(game);

            // 2. Thumbnail Cache'ini temizle
            RetroAudit.Converters.ThumbnailImageConverter.Invalidate(path);

            // 3. Dosyayı sil
            File.Delete(path);

            if (_screenshotByPlatform.TryGetValue(game.PlatformDisplayName, out var files))
                files.Remove(GetMediaBaseFileName(game));

            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            game.ScreenshotPath = path;
            NotifyArtworkDownloaded(game);
            RequestShowMessage?.Invoke($"Ekran görüntüsü silinirken hata oluştu: {ex.Message}");
        }
    }


    // --- Hide / Recycle Bin ---
    // Bu dört komut da doğrudan _allGames üzerindeki Game nesnesinin IsHidden/IsDeleted alanını
    // günceller (ObservableProperty değil ama ApplyFilter zaten satırı listeden çıkarıp/görünür
    // yapıp tazeliyor, ayrıca bir bildirime gerek yok).

    [RelayCommand]
    private void HideGame(Game game)
    {
        UserDataService.SetHidden(game.GameKey, true);
        game.IsHidden = true;
        ApplyFilter();
    }

    [RelayCommand]
    private void UnhideGame(Game game)
    {
        UserDataService.SetHidden(game.GameKey, false);
        game.IsHidden = false;
        ApplyFilter();
    }

    [RelayCommand]
    private void DeleteGame(Game game)
    {
        UserDataService.SoftDelete(game.GameKey);
        game.IsDeleted = true;
        ApplyFilter();
    }

    // Kullanıcı bulgusu: "Wanpaku Duck Yume Bouken" (gerçekte resmi DuckTales) LaunchBox'ın kendi
    // verisindeki belirsiz bir alternatif isim çakışması yüzünden yanlışlıkla Junk'a düşmüştü —
    // kök neden Builder'ın eşleştirmesinde (kataloğu yeniden inşa etmeden düzeltilemez). Kullanıcı
    // kararı: "istenilen oyunu junk'a atabilelim veya junktaki bi oyunu release'e çekebilelim ...
    // tek tuş toggle gibi kapsülde sabit koy" — kapsülde HER ZAMAN görünen tek bir düğme, mevcut
    // Version'a göre karşıt değere kalıcı olarak zorluyor (bkz. GameStateInfo.VersionOverride).
    [RelayCommand]
    private void ToggleJunkStatus(Game game)
    {
        var newVersion = game.Version == "Junk" ? "Released" : "Junk";
        UserDataService.SetVersionOverride(game.GameKey, newVersion);
        game.Version = newVersion;
        ApplyFilter();
        SyncPlatformGameCounts();
    }

    [RelayCommand]
    private void RestoreGame(Game game)
    {
        UserDataService.RestoreFromRecycleBin(game.GameKey);
        game.IsDeleted = false;
        ApplyFilter();
    }

    // Çöp kutusundan kalıcı silme — RetroAudit.db'nin kendisinden bir satır silmez (Builder zaten
    // bir sonraki koşuda o oyunu yeniden üretir), bunun yerine kalıcı bir dışlama listesine
    // eklenir (bkz. UserDataService/CatalogDatabaseService.ApplyUserData). Onay MainWindow
    // code-behind'ında PermanentDeleteConfirmationDialog ile alınır (bkz. context menü kablolaması,
    // RequestPermanentDeleteConfirmation) — [RelayCommand] YOK, çünkü kalıcı silme her zaman bu
    // onay penceresinden geçmeli.
    //
    // Kullanıcı isteği: "yani rom u resmi vs herşeyiyle silmeli çöp kutusundan temizlediğimde ...
    // rom box ss vs silinecekleri tikle işaretleyebilmeli kullanıcı" — oyunun kendisi (RetroAudit
    // kütüphanesinden kaldırma) HER ZAMAN gerçekleşir; deleteRom/deleteBox/deleteScreenshot/
    // deleteLogo SADECE hangi GERÇEK dosyaların da (Windows Çöp Kutusu'na taşınarak, kalıcı
    // File.Delete DEĞİL — bkz. RecycleBinService, RomImportService.DeleteSelectedFiles ile AYNI
    // güvenli yöntem) silineceğini belirler.
    public void PermanentlyDeleteGame(Game game, bool deleteRom, bool deleteBox, bool deleteScreenshot, bool deleteLogo)
    {
        foreach (var path in CollectGameFilePaths(game, deleteRom, deleteBox, deleteScreenshot, deleteLogo))
        {
            if (File.Exists(path))
                RecycleBinService.MoveToRecycleBin(path);
        }

        UserDataService.PermanentlyDelete(game.GameKey);
        _allGames.Remove(game);
        ApplyFilter();
    }

    // PermanentlyDeleteGame'in sildiği dosyaların tam listesi — ROM'lar (kataloktaki her sürüm,
    // override varsa onun yolu, standart Games\{Platform}\{FileName} kuralı yoksa) + manuel bağlı/
    // custom oyunların (kataloğun hiç bilmediği) dosyaları + indirilmiş görseller (Box/SS/Logo),
    // her biri kendi bool parametresiyle devre dışı bırakılabilir (bkz. PermanentDeleteConfirmationDialog).
    private IEnumerable<string> CollectGameFilePaths(Game game, bool includeRom, bool includeBox, bool includeScreenshot, bool includeLogo)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeRom)
        {
            foreach (var version in CatalogDatabaseService.GetVersions(game.GameId, game.GameKey))
            {
                var path = ResolveVersionFilePath(game, version);
                if (path is not null)
                    paths.Add(path);
            }

            if (_filePathOverrides.TryGetValue(game.GameKey, out var overrides))
            {
                foreach (var o in overrides)
                {
                    if (o.FilePath is not null)
                        paths.Add(o.FilePath);
                }
            }
        }

        if (includeBox && game.HasBox)
            paths.Add(game.BoxPath);
        if (includeScreenshot && game.HasScreenshot)
            paths.Add(game.ScreenshotPath);
        if (includeLogo && game.HasClearLogo)
            paths.Add(game.ClearLogoPath);

        return paths;
    }

    // --- Kapsül sağ tık menüsü ---
    // DataGrid satırına sağ tıklandığında MainWindow.xaml.cs bu metodu çağırıp Popup'ı açar
    // (bkz. Views/MainWindow.xaml.cs OnRowPreviewMouseRightButtonDown). Menü, hangi satıra
    // tıklandığına göre Delete/Restore, Hide/Unhide gibi eylemleri değiştirir — bkz.
    // MainWindow.xaml'deki DataTrigger'lar (ContextMenuGame.IsHidden/IsDeleted).
    [ObservableProperty]
    private Game? contextMenuGame;

    [ObservableProperty]
    private bool isContextMenuOpen;

    [ObservableProperty]
    private bool isAddToPlaylistPopupOpen;

    // Kapsüldeki "Versions" düğmesiyle açılan/kapanan alt popup (bkz. FocusVersions).
    [ObservableProperty]
    private bool isVersionsPopupOpen;

    // Sütun görünürlüğü seçici popup'ı — artık ayrı bir "Sütunlar" düğmesi yok, herhangi bir
    // sütun başlığına sağ tıklandığında açılır (bkz. MainWindow.xaml.cs, GamesGrid_PreviewMouseRightButtonUp).
    [ObservableProperty]
    private bool isColumnPickerOpen;

    // Ayarlar > Arayüz sekmesinden değiştirilir (bkz. SettingsViewModel, AppSettings persistence).
    [ObservableProperty]
    private ContextMenuDisplayMode contextMenuDisplayMode = ContextMenuDisplayMode.IconAndText;

    // Birden fazla satır seçiliyken sağ tıklanırsa menü "toplu" moda geçer: Edit/Versions/Folder/
    // tekil Delete-Restore-Kalıcı Sil/Re-match/Search Web gibi TEK oyuna özel eylemler çakışacağı
    // (ya da anlamsız olacağı) için gizlenir — sadece Favori/Playlist'e Ekle/Gizle/Sil kalır
    // (bkz. MainWindow.xaml.cs GamesGrid_PreviewMouseRightButtonUp, OpenBulkContextMenuFor).
    [ObservableProperty]
    private bool isBulkContextMenu;

    public ObservableCollection<Game> ContextMenuSelection { get; } = new();

    // Kullanıcı geri bildirimi: "çöp kutusunda toplu silmede delete butonu çıkıyor delete perma
    // butonu çıkmıyor ... delete butonu geri dönüşüm kutusuna gönderiyordu, çöp kutusu içinde
    // tekrar çıkmasının anlamı yok" — tekil moddaki IsDeleted DataTrigger'ıyla AYNI mantık, toplu
    // seçim için: seçili TÜM satırlar zaten silinmişse (Recycle Bin görünümündeyken pratikte hep
    // öyle olur) Sil yerine Geri Yükle + Kalıcı Sil gösterilir (bkz. MainWindow.xaml bulk panel).
    public bool IsContextMenuSelectionAllDeleted => ContextMenuSelection.Count > 0 && ContextMenuSelection.All(g => g.IsDeleted);

    public void OpenContextMenuFor(Game game)
    {
        IsBulkContextMenu = false;
        ContextMenuGame = game;
        SelectedGame = game;
        IsAddToPlaylistPopupOpen = false;
        IsVersionsPopupOpen = false;
        IsContextMenuOpen = true;
    }

    public void OpenBulkContextMenuFor(IReadOnlyList<Game> games)
    {
        IsBulkContextMenu = true;
        ContextMenuGame = null;
        ContextMenuSelection.Clear();
        foreach (var game in games)
            ContextMenuSelection.Add(game);
        OnPropertyChanged(nameof(IsContextMenuSelectionAllDeleted));
        IsAddToPlaylistPopupOpen = false;
        IsVersionsPopupOpen = false;
        IsContextMenuOpen = true;
    }

    [RelayCommand]
    private void CloseContextMenu() => IsContextMenuOpen = false;

    // --- Toplu eylemler (bkz. IsBulkContextMenu) ---

    // Kullanıcı isteği: "toplu seçimde sağ tıktan bağlaya bastığımda otomatik bağlamalı datası
    // olan asıl kayıt diğeri sürümlerin içine, ikisinde de ayrı kayıt varsa bitanesini tutup
    // diğerini sürümlere atmalı" — tekli "Bağla"nın (bkz. RequestLinkToGame/ManualLinkWindow)
    // aksine burada seçim zaten "hangi oyunlar" sorusunu cevaplıyor, bir pencere açmaya gerek
    // yok: hangisinin "asıl" (gerçek katalog verisi taşıyan) olduğu otomatik seçilir — custom
    // OLMAYAN (gerçek katalog eşleşmesi) > metadata kaynağı olan > en düşük GameId — geri
    // kalanların dosyaları LinkGameFileToGameAsync ile AYNI mekanizmayla ona taşınır.
    [RelayCommand]
    private async Task BulkLinkAsync()
    {
        var selected = ContextMenuSelection.ToList();
        IsContextMenuOpen = false;

        if (selected.Count < 2)
        {
            RequestShowMessage?.Invoke("Bağlamak için en az 2 oyun seçin.");
            return;
        }

        var primary = selected
            .OrderBy(g => g.GameKey.StartsWith("custom:", StringComparison.Ordinal))
            .ThenByDescending(g => g.HasArtworkSource)
            .ThenBy(g => g.GameId == 0 ? int.MaxValue : g.GameId)
            .First();

        // Kullanıcı isteği: "rom unun olup olmaması fark etmemeli" — ROM'u olmayan bir oyun da
        // artık atlanmıyor, LinkGameFileToGameAsync'in kendi dosyasız dalı devreye giriyor (satırı
        // gizleyip hedefe sahipsiz bir sürüm kartı ekliyor, bkz. o metodun yorumu).
        var others = selected.Where(g => g != primary).ToList();
        foreach (var other in others)
        {
            var syntheticVersion = HasLocalFile(other)
                ? new GameVersion { RawDatName = Path.GetFileNameWithoutExtension(GetLocalFilePath(other)), IsCustomEntry = true }
                : null;
            await LinkGameFileToGameAsync(other, primary, syntheticVersion);
        }

        RequestShowMessage?.Invoke($"{others.Count} oyun \"{primary.Title}\" oyununa bağlandı.");
    }

    [RelayCommand]
    private void BulkHide()
    {
        foreach (var game in ContextMenuSelection)
        {
            UserDataService.SetHidden(game.GameKey, true);
            game.IsHidden = true;
        }

        IsContextMenuOpen = false;
        ApplyFilter();
    }

    [RelayCommand]
    private void BulkDelete()
    {
        foreach (var game in ContextMenuSelection)
        {
            UserDataService.SoftDelete(game.GameKey);
            game.IsDeleted = true;
        }

        IsContextMenuOpen = false;
        ApplyFilter();
    }

    // Tekil moddaki RestoreGame ile AYNI, sadece TÜM seçim için (bkz. IsContextMenuSelectionAllDeleted).
    [RelayCommand]
    private void BulkRestore()
    {
        foreach (var game in ContextMenuSelection)
        {
            UserDataService.RestoreFromRecycleBin(game.GameKey);
            game.IsDeleted = false;
        }

        IsContextMenuOpen = false;
        ApplyFilter();
    }

    // Tekil moddaki RequestPermanentDeleteConfirmation ile AYNI desen — kalıcı silme geri
    // alınamaz olduğu için doğrudan gitmiyor, View katmanına (PermanentDeleteConfirmationDialog)
    // bir istek gönderir. Onaylanırsa MainWindow.xaml.cs BulkPermanentlyDeleteGames'i çağırır.
    public event Action<IReadOnlyList<Game>>? RequestBulkPermanentDeleteConfirmation;

    [RelayCommand]
    private void RequestBulkPermanentDelete()
    {
        IsContextMenuOpen = false;
        RequestBulkPermanentDeleteConfirmation?.Invoke(ContextMenuSelection.ToList());
    }

    // PermanentlyDeleteGame'in toplu hali — ApplyFilter'ı N kez değil bir kez çağırması dışında
    // aynı mantık (bkz. o metodun yorumu: ROM/görsel silme Windows Çöp Kutusu'na taşıyarak).
    public void BulkPermanentlyDeleteGames(IReadOnlyList<Game> games, bool deleteRom, bool deleteBox, bool deleteScreenshot, bool deleteLogo)
    {
        foreach (var game in games)
        {
            foreach (var path in CollectGameFilePaths(game, deleteRom, deleteBox, deleteScreenshot, deleteLogo))
            {
                if (File.Exists(path))
                    RecycleBinService.MoveToRecycleBin(path);
            }

            UserDataService.PermanentlyDelete(game.GameKey);
            _allGames.Remove(game);
        }

        ApplyFilter();
    }

    // Toplu favorileme kasıtlı olarak "toggle" değil "hepsini ekle" — karışık durumdaki (bazısı
    // favori bazısı değil) bir seçimde toggle mantığı tutarsız/şaşırtıcı sonuç verirdi.
    [RelayCommand]
    private void BulkAddToFavorites()
    {
        foreach (var game in ContextMenuSelection)
            game.IsFavorite = UserDataService.AddToFavorites(game.GameKey);

        IsContextMenuOpen = false;
        if (SelectedChip is { Kind: PlaylistChipKind.Playlist, IsBuiltIn: true, Name: "Favorites" })
            RefreshSelectedChipMembership();
    }

    [RelayCommand]
    private void ToggleAddToPlaylistPopup() => IsAddToPlaylistPopupOpen = !IsAddToPlaylistPopupOpen;

    // "Versions" menü öğesi ayrı bir pencere açmıyor — kapsülün kendi altında bir alt popup açıp
    // kapatıyor (bkz. MainWindow.xaml "Versions" Popup, aynı Playlist'e Ekle popup deseni). Sağ
    // paneldeki "Sürümler (Region)" listesi (bkz. Stage B) hâlâ ayrıca duruyor — bu sadece kapsülden
    // erişimi hızlandırıyor, sağ panele gitmeye gerek bırakmıyor (kullanıcı isteği).
    [RelayCommand]
    private void FocusVersions() => IsVersionsPopupOpen = !IsVersionsPopupOpen;

    // View katmanına (MainWindow.xaml.cs) EditMetadataWindow'u açma isteği — ViewModel doğrudan
    // Window tiplerine bağımlı olmasın diye (bkz. RequestOpenMediaProvider ile aynı desen).
    public event Action<Game>? RequestEditMetadata;

    [RelayCommand]
    private void EditMetadata(Game game)
    {
        IsContextMenuOpen = false;
        RequestEditMetadata?.Invoke(game);
    }

    // Kalıcı silme geri alınamaz olduğu için doğrudan UserDataService'e gitmiyor — View katmanına
    // (PermanentDeleteConfirmationDialog) bir istek gönderir. Onaylanırsa MainWindow.xaml.cs
    // PermanentlyDeleteGame'i seçilen dosya türleriyle kendisi çağırır.
    public event Action<Game>? RequestPermanentDeleteConfirmation;

    [RelayCommand]
    private void RequestPermanentDelete(Game game)
    {
        IsContextMenuOpen = false;
        RequestPermanentDeleteConfirmation?.Invoke(game);
    }

    // Kullanıcı isteği: "sen bağlasana doğru oyuna eksikleri" — GameKey'i MetadataSourceIdOverride
    // taşıyan her oyunu (bkz. GameStateInfo yorumu) MasterMetadataReader.GetByDatabaseId ile
    // doğrudan ID üzerinden çekip ReMatchMetadata/BulkReMatchMetadata ile AYNI ApplyMetadataMatch
    // fonksiyonunu kullanarak uygular — normal bir otomatik eşleşme gibi davranır (metadata +
    // görsel indirme akışları hiç fark etmeden çalışır). Reader SADECE override varsa açılır.
    private void ApplyManualMetadataSourceOverrides(Dictionary<string, GameStateInfo> states)
    {
        var overrides = states.Where(kv => kv.Value.MetadataSourceIdOverride.HasValue).ToList();
        if (overrides.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_appSettings.MasterMetadataDbPath) || !File.Exists(_appSettings.MasterMetadataDbPath))
            return;

        var gamesByKey = _allGames.ToDictionary(g => g.GameKey);
        using var reader = new MasterMetadataReader(_appSettings.MasterMetadataDbPath);
        foreach (var (gameKey, state) in overrides)
        {
            if (!gamesByKey.TryGetValue(gameKey, out var game))
                continue;

            var match = reader.GetByDatabaseId(state.MetadataSourceIdOverride!.Value);
            if (match is null)
                continue;

            ApplyMetadataMatch(game, match);
            game.StatusOk = true;
        }
    }

    // Builder'ın CatalogBuilder.Run içinde yaptığı eşleştirmenin aynısını tek bir oyun için
    // yeniden çalıştırır (RetroAudit.Catalog'a proje referansı, bkz. RetroAudit.csproj). Sonuç
    // RetroAudit.db'ye değil doğrudan canlı Game nesnesine yazılır — bu bir kullanıcı override'ı
    // değil, katalog eşleştirmesinin kendisinin yeniden çalıştırılması, bu yüzden
    // MetadataOverrides'a gitmiyor (bir sonraki Builder koşusu zaten aynı sonucu üretecektir).
    [RelayCommand]
    private void ReMatchMetadata(Game game)
    {
        IsContextMenuOpen = false;

        if (string.IsNullOrWhiteSpace(_appSettings.MasterMetadataDbPath) || !File.Exists(_appSettings.MasterMetadataDbPath))
        {
            RequestShowMessage?.Invoke("MasterMetadata.db yolu Ayarlar > Genel'de tanımlı değil ya da bulunamadı.");
            return;
        }

        using var reader = new MasterMetadataReader(_appSettings.MasterMetadataDbPath);
        if (!reader.IsPlatformKnown(game.Platform))
            return;

        var compareTitle = VersionResolver.NormalizeForCompare(game.Title);
        var match = reader.FindMatch(game.Platform, compareTitle, game.Title);
        if (match is null)
            return;

        ApplyMetadataMatch(game, match);
        ApplyFilter();
    }

    // ReMatchMetadata (tekil) ve BulkReMatchMetadata'nın paylaştığı asıl alan atama mantığı — ikisi
    // de aynı eşleştirme sonucunu aynı şekilde canlı Game nesnesine yazıyor, sadece reader'ı açma/
    // döngü kurma tarzları farklı (bulk, tek bir reader'ı tüm seçim için yeniden kullanıyor).
    private static void ApplyMetadataMatch(Game game, MetadataMatch match)
    {
        game.Developer = match.Developer ?? game.Developer;
        game.Publisher = match.Publisher ?? game.Publisher;
        // ReleaseYear ve ReleaseDate LaunchBox'ta birbirinden bağımsız nullable alanlar (bkz.
        // CatalogDatabaseService.GetGames yorumu) — ReleaseYear boşsa ReleaseDate'ten türetilir.
        game.ReleaseYear = match.ReleaseYear ?? match.ReleaseDate?.Year ?? game.ReleaseYear;
        game.Description = match.Overview ?? game.Description;
        game.MaxPlayers = match.MaxPlayers ?? game.MaxPlayers;
        if (match.Genres.Length > 0)
            game.Genres = string.Join(", ", match.Genres);
        game.MatchMethod = match.MatchMethod;
        game.NeedsReview = match.Confidence < MasterMetadataReader.FuzzyAcceptThreshold;
        game.ReleaseDate = match.ReleaseDate ?? game.ReleaseDate;
        game.CommunityRating = match.CommunityRating ?? game.CommunityRating;
        game.VideoUrl = match.VideoUrl ?? game.VideoUrl;
        game.WikipediaUrl = match.WikipediaUrl ?? game.WikipediaUrl;
        game.SteamAppId = match.SteamAppId ?? game.SteamAppId;
        game.Cooperative = match.Cooperative ?? game.Cooperative;
        game.GameMode = game.Cooperative switch
        {
            true => "Kooperatif",
            false => "Kooperatif Değil",
            null => game.GameMode,
        };
        game.MetadataSourceId = match.MetadataSourceId;
    }

    // Toplu seçimdeki her oyun için ReMatchMetadata'nın aynısını çalıştırır — TEK bir reader
    // açılıp tüm döngü boyunca yeniden kullanılıyor (MasterMetadataReader kendi içinde fuzzy
    // eşleştirme kovalarını önbelleğe alıyor, bkz. _platformGameNameBuckets — bulk'ta reader'ı N
    // kere açıp kapatmak yerine bu, aynı platformdaki oyunlar için gerçek bir performans kazancı).
    [RelayCommand]
    private void BulkReMatchMetadata()
    {
        IsContextMenuOpen = false;

        if (string.IsNullOrWhiteSpace(_appSettings.MasterMetadataDbPath) || !File.Exists(_appSettings.MasterMetadataDbPath))
        {
            RequestShowMessage?.Invoke("Metadata veritabanı yolu Ayarlar > Genel'de tanımlı değil ya da bulunamadı.");
            return;
        }

        using var reader = new MasterMetadataReader(_appSettings.MasterMetadataDbPath);
        foreach (var game in ContextMenuSelection)
        {
            if (!reader.IsPlatformKnown(game.Platform))
                continue;

            var compareTitle = VersionResolver.NormalizeForCompare(game.Title);
            var match = reader.FindMatch(game.Platform, compareTitle, game.Title);
            if (match is not null)
                ApplyMetadataMatch(game, match);
        }

        ApplyFilter();
    }

    // Toplu "Görsel Getir" sırasında yeniden giriş engeller (bkz. BulkFetchArtwork) — bulk
    // capsül düğmesi bunun üzerinden devre dışı bırakılıyor.
    [ObservableProperty]
    private bool isBulkArtworkFetching;

    public bool CanBulkFetchArtwork => !IsBulkArtworkFetching;

    partial void OnIsBulkArtworkFetchingChanged(bool value) => OnPropertyChanged(nameof(CanBulkFetchArtwork));

    // Tekli/toplu "Görsel Getir" sırasında pencerenin altındaki ilerleme çubuğunu besler (bkz.
    // MainWindow.xaml, kullanıcı isteği: "% gösteren ilerleyen bir gösterge bar"). Tekli indirmede
    // her bir görsel (en fazla 4), toplu indirmede her bir oyun tamamlandığında güncellenir.
    [ObservableProperty]
    private bool isArtworkDownloadInProgress;

    [ObservableProperty]
    private double artworkDownloadProgress;

    // Kullanıcı isteği: "provider açıkken ana UI'daki çubuk gizlensin, kapanınca geri gelsin" —
    // Media Provider penceresi kendi indirme ilerleme çubuğunu gösterdiği için (bkz.
    // MediaProviderWindow.xaml) MainWindow'daki asıl çubuk aynı anda ikinci kez görünmesin diye
    // bu pencere açıkken gizleniyor. MediaProviderWindow.xaml.cs Loaded/Closed'da set ediyor.
    [ObservableProperty]
    private bool isMediaProviderWindowOpen;

    public bool ShowMainArtworkProgressBar => IsArtworkDownloadInProgress && !IsMediaProviderWindowOpen;

    partial void OnIsArtworkDownloadInProgressChanged(bool value) => OnPropertyChanged(nameof(ShowMainArtworkProgressBar));

    partial void OnIsMediaProviderWindowOpenChanged(bool value) => OnPropertyChanged(nameof(ShowMainArtworkProgressBar));

    // "Görsel Getir" başlamadan önce hangi türlerin (Box/Clear Logo/Gameplay) indirileceğini
    // sormak için View'a bırakılan istek (bkz. ArtworkTypeSelectionDialog, MainWindow.xaml.cs).
    // Parametre: (HasBox, HasClearLogo, HasScreenshot) — zaten mevcut olan türler diyalogda
    // varsayılan olarak İŞARETSİZ gelsin diye (kullanıcı isteği: "kapak indirilmişse gene
    // sormasın, işaretsiz çıksın"). Kullanıcı iptal ederse veya hiç tür seçmezse null/boş küme
    // döner, indirme hiç başlamaz.
    public event Func<(bool HasBox, bool HasClearLogo, bool HasScreenshot), HashSet<string>?>? RequestArtworkTypeSelection;

    // MainWindow.xaml'deki "Durdur" düğmesi bunu Cancel() ile tetikler (bkz. CancelArtworkDownload).
    // Tekli/toplu indirme başlarken yeniden oluşturulur, bitince/iptal olunca Dispose edilir.
    private CancellationTokenSource? _artworkDownloadCts;

    [RelayCommand]
    private void CancelArtworkDownload() => _artworkDownloadCts?.Cancel();

    // Detay panelindeki TEK Download butonu (kullanıcı isteği: "ayrı ayrı bi sürü buton olmasın,
    // resmi olmayanları indirsin ayrı ayrı tanımlama") — hangi türlerin eksik olduğunu kendisi
    // hesaplayıp SADECE onları indirir, çoklu tür seçim diyaloğu (RequestArtworkTypeSelection)
    // hiç açılmaz. Buton zaten Game.HasMissingArtwork'e göre sadece eksik varsa görünür.
    [RelayCommand]
    private async Task FetchMissingArtwork(Game game)
    {
        // Kullanıcı geri bildirimi: "super star force için 'eşleşmiş bir metadata kaydı yok' diyor"
        // — bkz. BulkFetchArtworkForGamesAsync'teki AYNI gerekçe, HasArtworkSource artık zorunlu
        // değil (2. kaynak LaunchBox eşleşmesine ihtiyaç duymuyor).
        var missingTypes = new HashSet<string>();
        if (!game.HasBox) missingTypes.Add("Box");
        if (!game.HasClearLogo) missingTypes.Add("Logo");
        if (!game.HasScreenshot) missingTypes.Add("SS");
        if (missingTypes.Count == 0)
            return;

        IsArtworkDownloadInProgress = true;
        try
        {
            var result = await DownloadArtworkAsync(game, missingTypes, CancellationToken.None);
            NotifyArtworkDownloaded(game);
            ApplyFilter();

            if (result.Total == 0)
            {
                var url = "https://www.google.com/search?q=" + Uri.EscapeDataString($"{game.Title} {game.PlatformDisplayName} görsel") + "&tbm=isch";
                var fallbackBaseFileName = GetMediaBaseFileName(game);
                RequestSearchArtwork?.Invoke((url, Path.Combine(AppPaths.Images, game.PlatformDisplayName, "Box"), fallbackBaseFileName, game.Title, "görsel", actualPath =>
                {
                    RegisterDownloadedMedia("Box", game.PlatformDisplayName, fallbackBaseFileName, actualPath);
                    NotifyArtworkDownloaded(game);
                    ApplyFilter();
                }, game));
            }
            else if (result.Succeeded < result.Total)
                RequestShowMessage?.Invoke($"{result.Total - result.Succeeded} görsel indirilemedi — \"Ara\" ile deneyebilirsiniz.");
        }
        finally
        {
            IsArtworkDownloadInProgress = false;
        }
    }

    // Detay panelindeki tek-görsel Search butonları — otomatik indirme (LaunchBox kaynağı)
    // hiçbir şey bulamazsa/başarısız olursa kullanıcının kendi bulup indirebilmesi için (kullanıcı
    // isteği: "indiremezse bulunamazsa search ile bizim webview'den çekebilelim"). RomSearchWindow
    // ile aynı gömülü WebView2 deseni (bkz. MediaSearchWindow) — sadece dosya adı ROM'la eşleşecek
    // şekilde zorlanıyor (bkz. MediaSearchWindow.xaml.cs).
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action<string> CompletedCallback, Game Game)>? RequestSearchArtwork;

    [RelayCommand]
    private void SearchBoxArt(Game game) => SearchArtwork(game, "Box", "cover");

    [RelayCommand]
    private void SearchClearLogoArt(Game game) => SearchArtwork(game, "Logo", "clear logo");

    [RelayCommand]
    private void SearchScreenshotArt(Game game) => SearchArtwork(game, "SS", "gameplay");

    [RelayCommand]
    private void SearchVideo(Game game)
    {
        // Video arama: YouTube'da oyun adını ara — kullanıcı istediği videoyu sağ tıkla kaydeder.
        // Artwork aramadan farklı olarak hedef klasör/dosya yok — MediaSearchWindow video URL modunda
        // çalışır (completedCallback null, targetFolder/FileName boş).
        var query = $"{game.Title} {game.PlatformDisplayName} gameplay video";
        var url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query);
        RequestSearchArtwork?.Invoke((url, string.Empty, string.Empty, game.Title, "video", _ =>
        {
            // Video URL kaydedildi — herhangi bir görsel sözlüğü güncellemesi gerekmez
            OnPropertyChanged(nameof(SelectedGame));
        }, game));
    }

    private void SearchArtwork(Game game, string type, string mediaTypeLabel)
    {
        // Clear Logo genelde platformdan bağımsız tek bir marka/logosu (kullanıcı isteği:
        // "clearlogoda platformu yazmana gerek yok") — platform ekleyince arama sonuçları
        // gereksiz yere daralıyordu.
        var query = type == "Logo"
            ? $"{game.Title} {mediaTypeLabel}"
            : $"{game.Title} {game.PlatformDisplayName} {mediaTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&tbm=isch";
        var targetFolder = Path.Combine(AppPaths.Images, game.PlatformDisplayName, type);
        var baseFileName = GetMediaBaseFileName(game);

        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, game.Title, mediaTypeLabel, actualPath =>
        {
            // Kullanıcı bulgusu: "Could not find file '...(Not For Resale).jpg'" — burada eskiden
            // hedef dosya adı TAHMİN ediliyordu (Box/SS için hep ".jpg" varsayılıyordu), ama
            // MediaSearchWindow'un gerçekte kaydettiği dosya kaynak görselin/tarayıcının önerdiği
            // uzantıyı (ör. ".webp") kullanıyordu — ikisi uyuşmayınca BoxPath var olmayan bir
            // dosyaya işaret ediyor, Crop Editor'da tıklanınca çöküyordu. actualPath artık
            // MediaSearchWindow'dan GERÇEKTEN yazılan tam yol olarak geliyor, tahmin yok.
            RegisterDownloadedMedia(type, game.PlatformDisplayName, baseFileName, actualPath);
            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }, game));
    }

    [RelayCommand]
    private async Task FetchArtwork(Game game)
    {
        IsContextMenuOpen = false;

        // Kullanıcı geri bildirimi: "super star force için 'eşleşmiş bir metadata kaydı yok' diyor"
        // — bkz. BulkFetchArtworkForGamesAsync'teki AYNI gerekçe, HasArtworkSource artık zorunlu
        // değil (2. kaynak LaunchBox eşleşmesine ihtiyaç duymuyor).
        var selectedTypes = RequestArtworkTypeSelection?.Invoke((game.HasBox, game.HasClearLogo, game.HasScreenshot));
        if (selectedTypes is null || selectedTypes.Count == 0)
            return;

        _artworkDownloadCts = new CancellationTokenSource();
        IsArtworkDownloadInProgress = true;
        ArtworkDownloadProgress = 0;
        (int Succeeded, int Total) result = (0, 0);
        var cancelled = false;
        try
        {
            result = await DownloadArtworkAsync(game, selectedTypes, _artworkDownloadCts.Token, (completed, total) =>
                ArtworkDownloadProgress = total == 0 ? 100 : (double)completed / total * 100);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            IsArtworkDownloadInProgress = false;
            _artworkDownloadCts?.Dispose();
            _artworkDownloadCts = null;
        }

        NotifyArtworkDownloaded(game);
        ApplyFilter();

        // Başarılı indirmede bilgi mesajı gösterilmiyor (kullanıcı kararı: "gereksiz") — sadece
        // durdurulduğunda, hiç görsel bulunamadığında veya bir kısmı indirilemediğinde uyarılıyor.
        if (cancelled)
            RequestShowMessage?.Invoke("İndirme durduruldu.");
        else if (result.Total == 0)
            RequestShowMessage?.Invoke("Bu oyun için indirilebilecek görsel bulunamadı.");
        else if (result.Succeeded < result.Total)
            RequestShowMessage?.Invoke($"{result.Total - result.Succeeded} görsel indirilemedi.");
    }

    [RelayCommand]
    private async Task BulkFetchArtwork()
    {
        IsContextMenuOpen = false;
        await BulkFetchArtworkForGamesAsync(ContextMenuSelection.ToList());
    }

    // Kullanıcı isteği: "otomatik indir butonuna basıyorum indirmiyor ... normal otomatik indirme
    // komutuna bağlasana ... toplu seçimlerede uygun olacak" — Media Provider'ın kendi (ayrı,
    // hatalı) tek-satır indirme mantığı yerine ana tablonun kapsül menüsündeki "Görsel Getir"nin
    // KULLANDIĞI AYNI toplu indirme akışı (bkz. MediaProviderWindow.xaml.cs) — ContextMenuSelection'a
    // bağımlı olmayan genel hali, tek bir oyun için de (Count=1) sorunsuz çalışır.
    public async Task BulkFetchArtworkForGamesAsync(IReadOnlyList<Game> games)
    {
        // Kullanıcı geri bildirimi: "super star force için 'eşleşmiş bir metadata kaydı yok' diyor"
        // — HasArtworkSource (LaunchBox eşleşmesi) artık ZORUNLU değil; 2. kaynak (bkz.
        // ArtworkService.DownloadFromLibretroThumbnailsAsync) kataloğun kendi DAT adıyla çalışıyor,
        // LaunchBox eşleşmesine hiç ihtiyaç duymuyor. Bu yüzden LaunchBox'ta hiç kaydı olmayan
        // oyunlar artık baştan ELENMİYOR — DownloadArtworkAsync her tür için ikisini de dener,
        // hiçbiri bulamazsa aşağıdaki "indirilebilecek görsel bulunamadı" mesajı zaten devreye girer.
        var targets = games.ToList();
        if (targets.Count == 0)
            return;

        // Toplu indirimde "zaten var" SEÇİLİ oyunların HEPSİNDE zaten var olması demek — bir
        // tek oyunda bile eksikse, o tür yine varsayılan işaretli gelir (kullanıcı isteği:
        // "kapak indirilmişse gene sormasın" tekli senaryo için netti, toplu için en makul
        // genelleme bu: gereksiz yeniden indirmeyi sadece HERKESTE fazlalıksa engelle).
        var selectedTypes = RequestArtworkTypeSelection?.Invoke((
            targets.All(g => g.HasBox),
            targets.All(g => g.HasClearLogo),
            targets.All(g => g.HasScreenshot)));
        if (selectedTypes is null || selectedTypes.Count == 0)
            return;

        _artworkDownloadCts = new CancellationTokenSource();
        IsBulkArtworkFetching = true;
        IsArtworkDownloadInProgress = true;
        ArtworkDownloadProgress = 0;
        var totalDownloaded = 0;
        var totalAssets = 0;
        var completedGames = 0;
        var cancelled = false;

        // Kullanıcı bulgusu: "toplu indirmelerde biraz yavaşlık oluyor ... launchbox'ın kendi
        // programı daha hızlı indiriyordu" — sebep görsel dönüştürme (decode/resize/encode)
        // DEĞİL: her oyunun indirmesi bir öncekinin TAMAMEN bitmesini (ağ isteği + kayıt) bekleyerek
        // SIRAYLA yapılıyordu. Ağ gecikmesi (her istek onlarca-yüzlerce ms) yüzlerce oyun × 4 türde
        // katlanarak toplam süreyi domine ediyordu — LaunchBox'ın kendi indiricisi (çoğu toplu
        // indirici gibi) muhtemelen AYNI ANDA birden fazla bağlantı kullanıyor. Burada da aynı
        // yaklaşım: SemaphoreSlim ile sınırlı (aşırı sunucu/disk yükü olmasın diye) EŞ ZAMANLI
        // indirme. NOT: bir ara "oyun başına" bu semafor kaldırılıp kaynak bazlı (Source1/Source2)
        // iki ayrı semaforla değiştirilmişti — kullanıcı bulgusu: "%1'de takılı kalıyor, indiriyor
        // ama bar ilerlemiyor, önceki sistemde böyle olmuyordu" — o değişiklik (yüzlerce oyunun
        // TÜMÜNÜN aynı anda Task olarak başlaması, sadece ağ isteklerinin iki semaforla
        // sınırlanması) UI/ilerleme çubuğunda gözle görülür bir gerileme yarattı, bu yüzden geri
        // alındı — tek, kanıtlanmış "oyun başına" semafor modeline dönüldü. DownloadArtworkAsync'in
        // içindeki RegisterDownloadedMedia artık lock ile korunuyor (bkz. o metot) çünkü artık
        // BİRDEN FAZLA arka plan thread'inden aynı anda çağrılabiliyor; NotifyArtworkDownloaded/
        // ArtworkDownloadProgress ise Game'in bağlı (bound) özelliklerini değiştirdiği için
        // Dispatcher ile bilerek UI thread'ine geri taşınıyor.
        const int maxConcurrentDownloads = 18;
        using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
        try
        {
            var downloadTasks = targets.Select(async game =>
            {
                await semaphore.WaitAsync(_artworkDownloadCts.Token);
                try
                {
                    var result = await DownloadArtworkAsync(game, selectedTypes, _artworkDownloadCts.Token);
                    Interlocked.Add(ref totalDownloaded, result.Succeeded);
                    Interlocked.Add(ref totalAssets, result.Total);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotifyArtworkDownloaded(game);
                        var done = Interlocked.Increment(ref completedGames);
                        ArtworkDownloadProgress = (double)done / targets.Count * 100;
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(downloadTasks);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            IsBulkArtworkFetching = false;
            IsArtworkDownloadInProgress = false;
            _artworkDownloadCts?.Dispose();
            _artworkDownloadCts = null;
        }

        ApplyFilter();

        // Başarılı indirmede bilgi mesajı gösterilmiyor (kullanıcı kararı: "gereksiz") — sadece
        // durdurulduğunda, hiçbir görsel bulunamadığında veya bir kısmı indirilemediğinde uyarılıyor.
        if (cancelled)
            RequestShowMessage?.Invoke($"İndirme durduruldu ({totalDownloaded} görsel indirildi).");
        else if (totalAssets == 0)
            RequestShowMessage?.Invoke("Seçilen oyunlar için indirilebilecek görsel bulunamadı.");
        else if (totalDownloaded < totalAssets)
            RequestShowMessage?.Invoke($"{totalAssets - totalDownloaded} görsel indirilemedi.");
    }

    // FetchArtwork/BulkFetchArtwork'ün paylaştığı indirme mantığı — mevcut olan (en fazla 4)
    // görsel varlığı sıralı (paralel değil) indirir, (başarı sayısı, toplam) döner — çağıran taraf
    // buna bakarak sadece eksik/başarısız durumda uyarı gösteriyor (bkz. RequestShowMessage
    // çağrıları, kullanıcı kararı: başarılı indirmede mesaj gösterilmiyor). selectedTypes:
    // kullanıcının ArtworkTypeSelectionDialog'da işaretlediği türler (Box/BG/Logo/SS) — dışındakiler
    // hiç indirilmez. onProgress: her görsel denemesinden sonra (completed, total) ile çağrılır —
    // sadece tekli indirmede kullanılıyor (bkz. FetchArtwork), toplu indirme kendi ilerlemesini
    // oyun bazında ayrıca hesaplıyor. cancellationToken: "Durdur" düğmesi tetiklenirse
    // OperationCanceledException fırlatır, çağıran taraf bunu yakalayıp döngüyü temiz kapatır.
    private async Task<(int Succeeded, int Total)> DownloadArtworkAsync(Game game, HashSet<string> selectedTypes, CancellationToken cancellationToken, Action<int, int>? onProgress = null)
    {
        if (selectedTypes.Count == 0)
            return (0, 0);

        // Kullanıcı isteği: "ilk kaynakta yoksa 2. kaynaktan çekip aynı 1. kaynaktaki akıştaki
        // şeyleri yapacak" — 1. kaynak (LaunchBox, bkz. assetsByType) bu türde HİÇ kayıt taşımıyor
        // olabilir (ör. Commodore 64/Atari 2600'de sık — ArtworkAssets'te satırı bile yok, eskiden
        // bu durumda tür hiç denenmiyordu) YA DA kaydı var ama indirme başarısız olabilir (404 vb.)
        // — ikisinde de 2. kaynağa (libretro-thumbnails) düşülüyor.
        var assetsByType = CatalogDatabaseService.GetArtworkAssets(game.GameId);
        var preferredRawDatName = GetSourceVersionRawName(game);
        var baseFileName = GetMediaBaseFileName(game);
        var maxDimension = GetArtworkMaxDimensionPixels();
        var types = selectedTypes.ToList();
        var succeeded = 0;
        var completed = 0;
        foreach (var type in types)
        {
            // Sadece Logo şeffaflık gerektirir (PNG) — Box/BG/SS küçük/kayıplı JPEG'e çevriliyor
            // (bkz. ArtworkService, kullanıcı kararı: dosya boyutunu azalt).
            var preserveTransparency = type == "Logo";
            var destination = ArtworkService.BuildLocalPath(AppPaths.Images, game.PlatformDisplayName, type, baseFileName, preserveTransparency);

            var downloaded = assetsByType.TryGetValue(type, out var fileName)
                && await ArtworkService.DownloadAsync(fileName, destination, preserveTransparency, maxDimension, cancellationToken);

            if (!downloaded)
                downloaded = await ArtworkService.DownloadFromLibretroThumbnailsAsync(game.Platform, type, preferredRawDatName, destination, preserveTransparency, maxDimension, cancellationToken);

            // Kullanıcı kararı: "Box"ta 1. kaynaktan gelen kapak yatayken (ör. "3D Cart" render)
            // otomatik olarak 2. kaynağı da deneyen özellik (bkz. ArtworkService.IsLandscapeCover)
            // KAPATILDI — bulk indirmede yaşanan "%1'de takılı kalıyor" sorununun olası nedenlerinden
            // biri olabileceği düşünüldüğü için geçici olarak devre dışı bırakıldı. Ekranda hâlâ
            // UniformToFill kullanılıyor (bkz. MainWindow.xaml Box görseli) — yatay kapaklar
            // bozulmadan kutuyu doldurup kenarlardan kırpılıyor, sadece otomatik 2. kaynak
            // denemesi yok.

            if (downloaded)
            {
                RegisterDownloadedMedia(type, game.PlatformDisplayName, baseFileName, destination);
                succeeded++;
            }
            completed++;
            onProgress?.Invoke(completed, types.Count);
        }
        return (succeeded, types.Count);
    }

    // Ayarlar > Genel'de seçilen boyutu (bkz. AppSettings.ArtworkMaxDimension) ArtworkService'in
    // beklediği piksel değerine çevirir. Original: int.MaxValue — hiçbir gerçek görsel bu kadar
    // büyük olmadığı için resize hiç tetiklenmez.
    private int GetArtworkMaxDimensionPixels() => _appSettings.ArtworkMaxDimension switch
    {
        ArtworkMaxDimension.Px800 => 800,
        ArtworkMaxDimension.Original => int.MaxValue,
        _ => 600,
    };

    // Durum sütunu/filtresi için — kullanıcı geri bildirimi: "ünlemlerde eşleşmedide gözüküyor
    // onları manuel olarak değiştir" — manuel bağlanan oyunlar StatusOk=false olsa bile (satırda
    // turuncu ünlem gösteriliyor, kırmızı çarpı değil) "Eşleşmedi" filtresine düşüyordu; artık
    // kendi "Manuel" değeriyle ayrı bir filtre seçeneği oluyor. IsEffectivelyMatched (bkz. Game.cs)
    // — kullanıcı isteği: "2'sinden biriyle eşleşirse eşleşmedi çıkmasın" — StatusOk (LaunchBox
    // metadata) yoksa bile herhangi bir kaynaktan görsel bulunmuşsa artık "Eşleşti" sayılıyor.
    //
    // Kullanıcı bulgusu: "manuel i de düzelt 18 gözüküyor hala ne alakaysa" — birden fazla custom
    // oyunu gerçek katalog karşılığına birleştirdikten SONRA bile Manuel sayısı hiç düşmüyordu.
    // Sebep: Durum İKONU (bkz. MainWindow.xaml, IsManuallyLinked && !IsEffectivelyMatched → "!")
    // daha önce düzeltilmişti ama bu ETİKET/FİLTRE fonksiyonu unutulmuştu — bir oyun gerçek katalog
    // verisiyle (tür/yayıncı/görsel) eşleşmiş olsa bile, dosyası elle bağlandığı için hep "Manuel"
    // sayılmaya devam ediyordu, ikonla (✓) tutarsız. Artık İKİSİ AYNI kuralı kullanıyor: "Manuel"
    // sadece HİÇBİR kaynaktan (LaunchBox/libretro) görsel/metadata bulunamamış custom satırlar için.
    private static string GetMatchedStatusLabel(Game game) =>
        game.IsManuallyLinked && !game.IsEffectivelyMatched ? "Manuel" : (game.IsEffectivelyMatched ? "Eşleşti" : "Eşleşmedi");

    // Normalde ROM dosya adı (uzantısız) kullanılır; ROM henüz yoksa başlıktan, geçersiz
    // dosya adı karakterleri temizlenerek türetilir.
    private static string GetMediaBaseFileName(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.File))
            return Path.GetFileNameWithoutExtension(game.File);

        var invalid = Path.GetInvalidFileNameChars();
        return new string(game.Title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // Kullanıcı isteği: "detaylardaki videodan gameplay resmi alma yapabilirmiyiz ... o butonla
    // videodan gameplay resmi snapshot alıp kaydedebilirmiyiz" — WebView2'nin o anki render
    // edilmiş görüntüsünü YAKALAMAK (CoreWebView2.CapturePreviewAsync) View katmanının işi (bkz.
    // MainWindow.xaml.cs CaptureVideoSnapshot_Click), bu metot sadece SONUCU (ham JPEG byte'ları)
    // SS olarak kaydedip diğer TÜM görsel indirme yollarıyla (RegisterDownloadedMedia +
    // NotifyArtworkDownloaded, bkz. o metotların yorumları) AYNI şekilde kütüphaneyi canlı
    // günceller — restart beklemeden hemen görünür, mevcut SS varsa üzerine yazar.
    public async Task<bool> SaveVideoSnapshotAsync(Game game, byte[] imageBytes)
    {
        var baseFileName = GetMediaBaseFileName(game);
        var destination = ArtworkService.BuildLocalPath(AppPaths.Images, game.PlatformDisplayName, "SS", baseFileName, preserveTransparency: false);
        var success = await ArtworkService.ProcessAndSaveAsync(imageBytes, destination, preserveTransparency: false, GetArtworkMaxDimensionPixels());
        if (success)
        {
            RegisterDownloadedMedia("SS", game.PlatformDisplayName, baseFileName, destination);
            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }
        return success;
    }

    // Bir oyuna tıklanınca detay paneli otomatik açılır (kullanıcı isteği) — platform listesinde
    // gezinirken SelectedGame null'a düşer, panel MainWindow.xaml.cs (ApplyDetailPanelWidth) ve
    // XAML'deki Visibility tetikleyicisi sayesinde gizlenir (boş panel gösterme sorunu). Kullanıcı
    // IsDetailPanelExpanded'ı toolbar düğmesiyle manuel kapatırsa bu, sadece SONRAKİ oyun
    // seçiminde tekrar açılana kadar geçerli kalır.
    partial void OnSelectedGameChanged(Game? value)
    {
        LoadSelectedGameVersions();
        LoadSelectedGameAlternateNames();
        if (value is not null)
            IsDetailPanelExpanded = true;

        // Başka oyun seçilince embedded video otomatik dursun ve screenshot moduna dönsün
        // (kullanıcı isteği) — asıl durdurma (WebView2.Navigate("about:blank")) IsPlayingVideo
        // false'a düşünce MainWindow.xaml.cs'teki PropertyChanged aboneliğinde yapılıyor.
        IsPlayingVideo = false;
        VideoEmbedFailed = false;
    }

    // Sağ paneldeki Versions listesini seçili oyuna göre yeniden doldurur.
    private void LoadSelectedGameVersions()
    {
        SelectedGameVersions.Clear();
        if (SelectedGame is not null)
        {
            var versions = new List<GameVersion>();
            foreach (var version in CatalogDatabaseService.GetVersions(SelectedGame.GameId, SelectedGame.GameKey))
            {
                version.IsOwned = IsVersionOwned(SelectedGame, version);
                var matchingOverride = _filePathOverrides.TryGetValue(SelectedGame.GameKey, out var overrides)
                    ? overrides.FirstOrDefault(o => string.Equals(o.MatchMethod, MatchMethods.ManualLink, StringComparison.Ordinal)
                        && string.Equals(o.TargetVersionRawName, version.RawDatName, StringComparison.OrdinalIgnoreCase))
                    : null;
                version.IsManuallyLinked = matchingOverride is not null;
                version.SourceGameKey = matchingOverride?.SourceGameKey;
                versions.Add(version);
            }

            // Kullanıcı isteği: "ayrı ayrı gözükmesin, Bağla mantığında olacak, onlar tek kayıt
            // gibi düşün" — otomatik sürüm-gruplamayla (bkz. Game.MergedIntoGameId,
            // CatalogDatabaseService.ApplyAutoVersionGrouping) bu oyuna bağlanmış "kardeş" DAT
            // kayıtları ana tabloda ayrı satır olarak GÖRÜNMÜYOR; onların kendi GERÇEK
            // sürümleri/ROM dosyaları burada, bu oyunun Sürümler listesine ek kart olarak eklenir.
            foreach (var sibling in _allGames.Where(g => g.MergedIntoGameId == SelectedGame.GameId))
            {
                foreach (var siblingVersion in CatalogDatabaseService.GetVersions(sibling.GameId, sibling.GameKey))
                {
                    siblingVersion.IsOwned = IsVersionOwned(sibling, siblingVersion);
                    siblingVersion.SourceGameKey = sibling.GameKey;
                    siblingVersion.MergedFromTitle = sibling.Title;
                    versions.Add(siblingVersion);
                }
            }

            // Kullanıcı isteği: "yeni olsun bi tane ona tıklayınca dosya ismi neyse onunla açsın" —
            // ManualLinkWindow'da kataloktaki HİÇBİR sürüme zorlanmadan "dosyanın kendi adıyla"
            // bağlanan kayıtlar (bkz. GameVersion.IsCustomEntry) RetroAudit.db'de hiç yok, yukarıdaki
            // GetVersions döngüsünde hiç görünmezler — bu yüzden burada AYRICA, TargetVersionRawName'i
            // yukarıdaki GERÇEK sürümlerin HİÇBİRİYLE eşleşmeyen her override için sentetik bir kart
            // ekleniyor, aksi halde bu bağlantı hiçbir yerde görünmez kalırdı.
            if (_filePathOverrides.TryGetValue(SelectedGame.GameKey, out var gameOverrides))
            {
                var realRawNames = new HashSet<string>(versions.Select(v => v.RawDatName), StringComparer.OrdinalIgnoreCase);
                foreach (var custom in gameOverrides.Where(o => !string.IsNullOrEmpty(o.TargetVersionRawName) && !realRawNames.Contains(o.TargetVersionRawName!)))
                {
                    // Kullanıcı geri bildirimi: "böyle gözüküyor" (kart boş, sadece ikonlar) — Region/
                    // VersionLabel/Hashes hiç doldurulmuyordu, kart şablonu bunlara bağlı olduğu için
                    // boş görünüyordu. RawDatName zaten No-Intro kalıbında (ör. "... (USA) (Rev A)
                    // (GameCube Edition)") — DatNameParser.Parse ile AYNI mantıkla ayrıştırılıp
                    // gerçek kartlarla TUTARLI görünmesi sağlanıyor.
                    var parsed = DatNameParser.Parse(custom.TargetVersionRawName!);
                    versions.Add(new GameVersion
                    {
                        RawDatName = custom.TargetVersionRawName!,
                        Region = parsed.Regions.Length > 0 ? string.Join(", ", parsed.Regions) : "Manuel",
                        VersionLabel = parsed.VersionLabel ?? string.Empty,
                        SourceDat = "manuel",
                        IsOwned = File.Exists(custom.FilePath),
                        IsManuallyLinked = true,
                        IsCustomEntry = true,
                        SourceGameKey = custom.SourceGameKey,
                        // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan
                        // alıp yazamıyormu buraya" — bkz. FilePathOverrideInfo.Crc32 (MainViewModel.
                        // LinkFile / RomImportViewModel.CompleteManualLinkAsync/
                        // ImportUnmatchedFileAsNewGameAsync tarafından önceden hesaplanıp kaydedilir).
                        // FilePath null olabilir (kullanıcı isteği: "rom unun olup olmaması fark
                        // etmemeli" — dosyasız birleştirme, bkz. LinkGameFileToGameAsync) — bu
                        // durumda GERÇEK crc32 yok, ama kullanıcı geri bildirimi: "kartlardayken
                        // crc32 kodları falan gözükmüyor" — en azından kaynağın kendi katalog
                        // verisindeki BEKLENEN dosya adları (crc32'siz, bilgi amaçlı) gösterilir.
                        Hashes = custom.FilePath is not null
                            ? new List<GameHash> { new() { FileName = Path.GetFileName(custom.FilePath), Crc32 = custom.Crc32 ?? string.Empty } }
                            : GetExpectedHashesForFilelessLink(custom),
                    });

                    // Kullanıcı geri bildirimi: "gözükmüyor crc32 leri tekrar taradım dosyayı" — bu
                    // CRC32 desteğinden ÖNCE kaydedilmiş eski manuel bağlantılarda Crc32 hiç
                    // yazılmamıştı; klasörü yeniden taramak bunu geriye dönük düzeltmiyor (Eşleşmeyenler
                    // taraması FilePathOverrides'tan tamamen bağımsız). Kart ilk açıldığında (Crc32 boş
                    // + dosya hâlâ diskteyse) arka planda BİR KEZ hesaplanıp kalıcı olarak kaydedilir.
                    if (string.IsNullOrEmpty(custom.Crc32) && File.Exists(custom.FilePath))
                        _ = BackfillMissingCrc32Async(SelectedGame.GameKey, custom);
                }
            }

            // Kullanıcı isteği: "oynamaya hazır olan versiyonlar kart listesinde her zaman üstte
            // gözüksün" — OrderByDescending KARARLI (stable) bir sıralama, yani sahip olunan/olunmayan
            // gruplarının KENDİ İÇİNDEKİ sıra (katalogdan geldiği sıra, ya da manuel eklenenler en
            // sonda) korunur, sadece "sahip olunanlar" bloğu başa taşınır.
            foreach (var version in versions.OrderByDescending(v => v.IsOwned))
                SelectedGameVersions.Add(version);
        }

        // Sürümler (Region) artık tek kart gösteriyor (kullanıcı isteği: "sürümlerde tek kart
        // gözükecek şekilde yapalım") — varsayılan olarak Preferred işaretli sürüm GÖSTERİLİR AMA
        // SADECE ROM'u varsa (kullanıcı isteği: "eşleşenlerde rom varsa preferredde rom yoksa
        // mavi olan seçili gelsin sürüm olarak öncelik usa eu japan sonra manuel rom varsa ama") —
        // Preferred'ın ROM'u yoksa (kırmızı çarpı), ROM'u OLAN sürümlerden bölge önceliğine göre
        // (USA > Europe > Japan > Manuel) biri seçilir; hiçbirinde ROM yoksa eskisi gibi Preferred
        // (yoksa ilk kart) gösterilir.
        var preferredVersion = SelectedGameVersions.FirstOrDefault(v => v.IsPreferred);
        SelectedVersionCard = preferredVersion is { IsOwned: true }
            ? preferredVersion
            : SelectedGameVersions.Where(v => v.IsOwned).OrderBy(GetVersionRegionPriority).FirstOrDefault()
                ?? preferredVersion
                ?? SelectedGameVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleVersionCards));
        OnPropertyChanged(nameof(OtherVersionCards));
        OnPropertyChanged(nameof(SingleVersionCard));
        IsVersionCardMenuOpen = false;
    }

    // LoadSelectedGameVersions'daki ROM'u olan sürümler arasından seçim önceliği (kullanıcı
    // isteği: "öncelik usa eu japan sonra manuel rom varsa"). Region alanı bazen birden fazla
    // bölge içerebilir (ör. "USA, Europe"), bu yüzden Contains kullanılıyor.
    private static int GetVersionRegionPriority(GameVersion version)
    {
        if (version.Region.Contains("USA", StringComparison.OrdinalIgnoreCase)) return 0;
        if (version.Region.Contains("Europe", StringComparison.OrdinalIgnoreCase)) return 1;
        if (version.Region.Contains("Japan", StringComparison.OrdinalIgnoreCase)) return 2;
        if (version.IsManuallyLinked || string.Equals(version.Region, "Manuel", StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    // Bkz. LoadSelectedGameVersions'daki çağrı yorumu. Eski kayıtlarda ZipEntryName de hiç
    // kaydedilmemiş olabilir (bu alan Crc32 ile AYNI anda eklendi) — LinkFile ile AYNI mantıkla,
    // dosya TEK girdili bir zip'se o girdi otomatik bulunur. Task.Run: PS2/PS3 boyutundaki
    // dosyalarda UI donmasın diye.
    private async Task BackfillMissingCrc32Async(string gameKey, FilePathOverrideInfo overrideInfo)
    {
        // Çağıran zaten File.Exists(overrideInfo.FilePath) ile korunuyor (bkz. LoadSelectedGameVersions),
        // ama dosyasız (ROM'suz) bağlantılarda (bkz. LinkGameFileToGameAsync) FilePath null olabildiği
        // için burada da ayrıca güvenceye alınıyor.
        if (overrideInfo.FilePath is not { } filePath)
            return;

        var zipEntryName = overrideInfo.ZipEntryName ?? RomImportService.GetSoleZipEntryName(filePath);
        string crc32;
        try
        {
            crc32 = await Task.Run(() => RomImportService.ComputeCrc32(filePath, zipEntryName));
        }
        catch (Exception)
        {
            return;
        }

        UserDataService.SaveFilePathOverride(gameKey, overrideInfo.FilePath, overrideInfo.MatchMethod, overrideInfo.TargetVersionRawName, zipEntryName, crc32);

        if (_filePathOverrides.TryGetValue(gameKey, out var overrides))
        {
            var index = overrides.IndexOf(overrideInfo);
            if (index >= 0)
                overrides[index] = overrideInfo with { ZipEntryName = zipEntryName, Crc32 = crc32 };
        }

        if (SelectedGame?.GameKey == gameKey)
            LoadSelectedGameVersions();
    }

    // Sürümler (Region) tek-kart alanı: o an gösterilen tek sürüm + birden fazlaysa "▾" ile
    // sağ-tık kapsül menüsündekiyle AYNI popup'tan seçilebilen diğerleri.
    [ObservableProperty]
    private GameVersion? selectedVersionCard;

    partial void OnSelectedVersionCardChanged(GameVersion? value)
    {
        OnPropertyChanged(nameof(OtherVersionCards));
        OnPropertyChanged(nameof(SingleVersionCard));
    }

    // Tek-kart ListBox'ının ItemsSource'u — ListBox bir koleksiyon beklediği için tek nesne
    // doğrudan bağlanamıyor, 0 ya da 1 elemanlı bir dizi olarak sarmalanıyor.
    public IEnumerable<GameVersion> SingleVersionCard => SelectedVersionCard is null
        ? Array.Empty<GameVersion>()
        : new[] { SelectedVersionCard };

    [ObservableProperty]
    private bool isVersionCardMenuOpen;

    public bool HasMultipleVersionCards => SelectedGameVersions.Count > 1;

    public IEnumerable<GameVersion> OtherVersionCards => SelectedGameVersions.Where(v => v != SelectedVersionCard);

    // Sürümler (Region) tek-kart alanının açılır popup'ında bir karta tıklanınca çalışır — o
    // sürümü tek-kart alanına taşır ve popup'ı kapatır.
    [RelayCommand]
    private void SelectVersionCard(GameVersion version)
    {
        SelectedVersionCard = version;
        IsVersionCardMenuOpen = false;
    }

    // Detay panelindeki "ALTERNATE NAMES" listesini seçili oyuna göre yeniden doldurur.
    private void LoadSelectedGameAlternateNames()
    {
        SelectedGameAlternateNames.Clear();
        if (SelectedGame is not null)
        {
            foreach (var alt in CatalogDatabaseService.GetAlternateNames(SelectedGame.GameId))
                SelectedGameAlternateNames.Add(alt);
        }

        // İlk 2 alternatif isim her zaman doğrudan (statik satırlar olarak) gösterilir — kullanıcı
        // isteği: "alternative names minimum satır gösterme sınırını 2 satır yap". 2'den fazlaysa
        // geri kalanlar (3., 4., ...) "▾" ile açılan bir listede gösterilir.
        OnPropertyChanged(nameof(VisibleAlternateNames));
        OnPropertyChanged(nameof(OverflowAlternateNames));
        OnPropertyChanged(nameof(HasOverflowAlternateNames));
        OnPropertyChanged(nameof(HasNoAlternateNames));
    }

    // Box art'ın altındaki sabit alanda her zaman doğrudan gösterilen ilk 2 alternatif isim.
    public IEnumerable<GameAlternateName> VisibleAlternateNames => SelectedGameAlternateNames.Take(2);

    // Alternatif ismi olmayan oyunlarda "-" göstermek için (kullanıcı isteği: "Alternate name
    // yazsın gene - işareti olsun sadece") — blok artık HİÇ gizlenmiyor (bkz. XAML yorumu), bu
    // yüzden boşken de 2 satırlık alanın içinde bir şey (kısa çizgi) görünür.
    public bool HasNoAlternateNames => SelectedGameAlternateNames.Count == 0;

    // 2'den fazla alternatif isim varsa, ilk 2'nin ötesindeki geri kalanı — "▾" açılır listesinde
    // gösterilir (bkz. MainWindow.xaml AlternateNamesOverflowToggle).
    public IEnumerable<GameAlternateName> OverflowAlternateNames => SelectedGameAlternateNames.Skip(2);

    public bool HasOverflowAlternateNames => SelectedGameAlternateNames.Count > 2;

    // Başlık/alternatif isim menüsündeki "kopyala" tıklamaları — kullanıcı isteği: "isim
    // kopyalanabilir olsun". Windows panosu tek seferde tek işleme açık — başka bir uygulama
    // (pano geçmişi/antivirüs/vs.) o an panoyu kilitli tutuyorsa çakışma olabiliyor.
    // Clipboard.SetText, veriyi uygulama kapansa bile panoda kalıcı olsun diye ayrıca
    // OleFlushClipboard da çağırıyor (Clipboard.SetDataObject(data, copy:true)) — bu EK adım
    // panoyu ikinci kez açıp/kapatıyor, çakışma ihtimalini ikiye katlıyor VE her tekrar denemede
    // (bkz. aşağıdaki retry) yavaş/gözle görülür bir bekleme yaratıyordu (kullanıcı geri bildirimi:
    // "kopyalamaya basınca bi süre neden bekliyor"). copy:false ile Flush adımını atlıyoruz —
    // veri sadece uygulama açıkken panoda kalır (bir oyun ismini kopyalayıp yapıştırmak için
    // yeterli, kritik bir kayıp değil), ama işlem hem hızlanıyor hem çakışma yüzeyi küçülüyor.
    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: false);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    // Bir sürümün Hashes listesindeki dosya adlarından herhangi biri diskte (ya da "Şu anki yoldan
    // kullan" override'ında) bulunuyorsa o sürüm "sahip olunan" sayılır — HasLocalFile'ın aksine
    // bu, oyunun o an tercih edilen tek dosyasına değil, o SÜRÜME özel dosya adlarına bakar (aynı
    // oyunun farklı region/dump sürümleri farklı dosya adlarına sahip olabilir).
    private bool IsVersionOwned(Game game, GameVersion version) => ResolveVersionFilePath(game, version) is not null;

    // Bir sürümü gerçekten başlatmak/sahiplik göstermek için kullanılacak somut yolu bulur.
    // Override bir .zip'e işaret ediyorsa ZİP'İN YOLU döner (çoğu emülatör zip içinden doğrudan
    // ROM okuyabilir, bkz. RomImportViewModel yorumları) — arşiv sadece bu sürümün dosyalarından
    // birini içeriyorsa. Eşleşme yoksa null.
    private string? ResolveVersionFilePath(Game game, GameVersion version)
    {
        if (_filePathOverrides.TryGetValue(game.GameKey, out var overrides))
        {
            // Kullanıcı isteği: "istenirse GameVersion seviyesinde de" manuel bağlanabilsin, "sormasın
            // ayrı bir sürüm gibi sürümlerine eklesin" — bu sürüme ÖZEL bir override varsa (bkz.
            // ManualLinkViewModel, FilePathOverrideInfo.TargetVersionRawName), hash/dosya adı hiç
            // uyuşmasa bile (zaten bu yüzden otomatik eşleşmemişti) kullanıcının kendi elle onayı
            // SADECE o sürüm için "sahip olunan" sayılır. Diğer TÜM sürümler kendi override'ı yoksa
            // aşağıdaki GENEL override/disk taramasına düşer, birbirlerini ETKİLEMEZLER.
            var versionSpecific = overrides.FirstOrDefault(o => string.Equals(o.TargetVersionRawName, version.RawDatName, StringComparison.OrdinalIgnoreCase));
            if (versionSpecific is not null)
                return versionSpecific.FilePath;

            var general = overrides.FirstOrDefault(o => string.IsNullOrEmpty(o.TargetVersionRawName));
            // Genel (TargetVersionRawName boş) override'lar sadece dosya tabanlı akışlarca üretilir
            // (bkz. dosyasız birleştirme, LinkGameFileToGameAsync — o HER ZAMAN spesifik bir
            // TargetVersionRawName kullanır, hiç genel override üretmez) — FilePath burada teorik
            // olarak null olamaz, yine de derleyiciye ve gelecekteki değişikliklere karşı güvenceye alınıyor.
            if (general is { FilePath: { } generalFilePath })
            {
                if (string.Equals(Path.GetExtension(generalFilePath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (ZipContainsAnyHashFile(generalFilePath, version.Hashes))
                        return generalFilePath;
                }
                else if (version.Hashes.Any(h => string.Equals(h.FileName, Path.GetFileName(generalFilePath), StringComparison.OrdinalIgnoreCase)))
                {
                    return generalFilePath;
                }
            }
        }

        if (_filesByPlatform is not null && _filesByPlatform.TryGetValue(game.PlatformDisplayName, out var files))
        {
            var match = version.Hashes.FirstOrDefault(h => files.Contains(h.FileName));
            if (match is not null)
                return Path.Combine(AppPaths.Games, game.PlatformDisplayName, match.FileName);
        }

        return null;
    }

    private static bool ZipContainsAnyHashFile(string zipPath, List<GameHash> hashes)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.Any(entry => hashes.Any(h => string.Equals(h.FileName, entry.Name, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Sağ panelin Versions listesinde bir kartı çift tıklamak — o SÜRÜMÜN kendi dosyasını,
    // oyunun o an tercih edilen dosyasından bağımsız olarak tanımlı emülatörle başlatır (bkz.
    // MainWindow.xaml.cs VersionsList_MouseDoubleClick). Tek tık seçimi salt görsel (ListBox'ın
    // kendi IsSelected'ı, bkz. MainWindow.xaml) — kalıcı bir "preferred" değişikliği yapmaz.
    [RelayCommand]
    private void LaunchVersion(GameVersion version)
    {
        if (SelectedGame is null)
            return;

        var filePath = ResolveVersionFilePath(SelectedGame, version);
        if (filePath is null)
        {
            RequestShowMessage?.Invoke("Bu sürümün dosyası bulunamadı.");
            return;
        }

        LaunchWithEmulator(SelectedGame, filePath);
    }

    // ListBox seçimi (başlık + platform satırlarının karışık olduğu liste) değiştiğinde, gerçek
    // bir platform satırıysa SelectedPlatform'u günceller; başlık satırları zaten seçilemediği
    // için (bkz. MainWindow.xaml) value.Platform null olan bir durumla pratikte karşılaşılmaz.
    partial void OnSelectedListItemChanged(PlatformListItem? value)
    {
        if (value?.Platform is not null)
            SelectedPlatform = value.Platform;
    }

    // "OTHERS" başlığına tıklanınca açılıp kapanır; listeyi (platform satırları dahil/hariç)
    // yeniden kurmak gerekir.
    partial void OnIsOthersExpandedChanged(bool value) => RebuildPlatformListItems();

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        RefreshActiveFilterChips();
    }

    partial void OnShowReleasedChanged(bool value)
    {
        // Kullanıcı geri bildirimi: "sayılar değişmiyor" — Platform.GameCount observable bir
        // property DEĞİL (bkz. Models/Platform.cs), bu yüzden SyncPlatformGameCounts sadece
        // mutasyon yapmak yeterli değil; sol paneldeki satırlar RebuildPlatformListItems ile
        // (Clear+yeniden Add, bkz. constructor'daki AYNI eşleştirme) fiziksel olarak yeniden
        // kurulmadıkça WPF eski değeri göstermeye devam ediyordu.
        SyncPlatformGameCounts();
        RebuildPlatformListItems();
        ApplyFilter();
    }

    partial void OnShowJunkChanged(bool value)
    {
        SyncPlatformGameCounts();
        RebuildPlatformListItems();
        ApplyFilter();
    }

    // Bayrak filtreleri hem görünürlüğü (bkz. GetFilterScopePopulation) HEM DE tablodaki
    // Region/SourceDat/File bilgisinin hangi sürümden geleceğini etkiliyor — ikincisi ApplyFilter'ın
    // kendisinden ayrı, çünkü _allGames'in İÇERİĞİNİ (Game.Region vb.) değiştiriyor, sadece hangi
    // oyunların göründüğünü değil.
    partial void OnShowUsaRegionChanged(bool value) => OnRegionFlagsChanged();
    partial void OnShowEuRegionChanged(bool value) => OnRegionFlagsChanged();
    partial void OnShowJapanRegionChanged(bool value) => OnRegionFlagsChanged();

    private void OnRegionFlagsChanged()
    {
        RecomputeRegionDisplay();
        SyncPlatformGameCounts();
        ApplyFilter();
    }

    // "USA"/"Europe"/"World"/"Japan"/diğer ham region string'ini 3 bayrak kovasından birine
    // eşler — VersionResolver.RegionRank ile aynı sınıflandırma mantığı (World, USA/EU/Japan'ın
    // hiçbirine düşmüyor, kasıtlı olarak "diğer" sayılıyor: bir World sürümü zaten bölgelerin
    // hiçbirine özel değil, bayrak filtresi onu dışlamamalı).
    private static RegionFlag ClassifyRegion(string region) => region switch
    {
        "USA" => RegionFlag.Usa,
        "Europe" => RegionFlag.Eu,
        "Japan" => RegionFlag.Japan,
        _ => RegionFlag.Other,
    };

    private bool IsRegionFlagChecked(RegionFlag flag) => flag switch
    {
        RegionFlag.Usa => ShowUsaRegion,
        RegionFlag.Eu => ShowEuRegion,
        RegionFlag.Japan => ShowJapanRegion,
        _ => true,
    };

    // Bir oyunun HİÇ USA/EU/Japan etiketli sürümü yoksa (sadece World/Unknown/diğer bölgelerdeyse)
    // bayrak filtresinden hiç etkilenmemesi gerekiyor — aksi halde ör. sadece Almanya'da çıkmış bir
    // oyun, kullanıcı "sadece USA" seçtiğinde haksız yere kaybolurdu (bu, Region SÜTUN FİLTRESİNİN
    // işi, toolbar bayrakları sadece USA/EU/Japan'ı kapsıyor).
    private bool MatchesRegionFlags(Game game)
    {
        var buckets = game.AllVersions.Select(v => ClassifyRegion(v.Region)).Where(b => b != RegionFlag.Other).ToList();
        return buckets.Count == 0 || buckets.Any(IsRegionFlagChecked);
    }

    // Toolbar'daki USA/EU/Japan bayrakları değiştiğinde, birden fazla sürümü olan (bkz.
    // Game.AllVersions) her oyunun Region/SourceDat/File alanlarını İŞARETLİ region'lar arasından
    // yeniden seçer — kullanıcı isteği: "USA'yı seçtiğimde tablodaki bilgiler USA'nın bilgileri
    // olsun". Tek sürümlü oyunlara dokunmuyor (değişecek bir şey yok, gereksiz iş). Title bilinçli
    // olarak DEĞİŞTİRİLMİYOR — CatalogBuilder.MergeRegionVariants ile birleşen oyunlarda bile
    // (ör. Alpha 2/Zero 2) ana başlık hep RegionPriority'nin global tercihinde kalır, sadece
    // hangi FİZİKSEL dosyanın/region'ın gösterileceği değişir.
    private void RecomputeRegionDisplay()
    {
        foreach (var game in _allGames)
        {
            if (game.AllVersions.Count <= 1)
                continue;

            var eligible = game.AllVersions.Where(v => IsRegionFlagChecked(ClassifyRegion(v.Region))).ToList();
            if (eligible.Count == 0)
                continue; // hiçbiri işaretli değil -> zaten görünürlükten düşecek, mevcut değeri koru

            var best = eligible
                .OrderBy(v => ClassifyRegion(v.Region) switch
                {
                    RegionFlag.Usa => 0,
                    RegionFlag.Eu => 1,
                    RegionFlag.Japan => 3,
                    _ => 2, // World/Unknown/diğer: USA/EU'dan sonra ama Japan'dan önce (mevcut RegionRank sırasıyla tutarlı)
                })
                .First();

            game.Region = best.Region;
            game.SourceDat = best.SourceDat;
            game.File = best.FileName;

            // File değiştiği için ona bağlı her şey (ROM'un yerelde olup olmadığı, Box/BG/SS/Logo
            // önizlemeleri) da yeniden hesaplanmalı — aksi halde ör. USA'ya geçince Japan sürümünün
            // eski medya/ROM eşleşmesi bir süre daha ekranda kalırdı.
            game.HasLocalFile = HasLocalFile(game);
            ApplyManualLinkInfo(game);
            NotifyArtworkDownloaded(game);
        }
    }

    private enum RegionFlag
    {
        Usa,
        Eu,
        Japan,
        Other,
    }

    // Chip/platform/arama'ya göre TABAN popülasyon — Top 250/100/25 için henüz ağırlıklı sıralama/
    // kesme (Take N) UYGULANMAMIŞ hâlidir. Hem ApplyFilter'ın kendisi hem de filtre dropdown'larının
    // (bkz. RefreshColumnFilterOptions) sayıları AYNI bu popülasyondan hesaplanır — böylece "USA
    // (1400)" gibi bir sayı top-25'e kesilmeden ÖNCEKİ gerçek aday havuzunu yansıtır.
    //
    // Sol paneldeki platform seçimi HER ZAMAN 1. süzgeç, üstteki playlist/chip şeridi 2. süzgeç
    // (kullanıcı kararı) — bu yüzden SelectedPlatform daraltması chip seçili olsun olmasın aynı
    // şekilde uygulanıyor: "Ready to Play" gibi bir chip artık seçili platforma özel, önceden
    // platformdan bağımsız tüm kütüphaneyi gösteriyordu (kullanıcı geri bildirimi).
    private IEnumerable<Game> GetFilterScopePopulation()
    {
        IEnumerable<Game> query;

        if (SelectedChip is not null)
        {
            query = SelectedChip.Kind switch
            {
                PlaylistChipKind.Hidden => _allGames.Where(g => g.IsHidden),
                PlaylistChipKind.RecycleBin => _allGames.Where(g => g.IsDeleted),
                // Kullanıcı geri bildirimi: "geri dönüşüm kutusunda bekleyenlerde gözüküyor orda
                // gözükmemeli" — Hidden/RecycleBin'in AKSİNE bu ikisi "genel" görünümler, "hiç
                // chip seçili değilken" (aşağıdaki else dalı, "!g.IsHidden && !g.IsDeleted") ile
                // AYNI kuralı uygulamalı: çöp kutusundaki/gizli bir oyun ROM'u eksik diye "Needs
                // Search"te, ROM'u varken de "Ready to Play"de görünmemeli — o oyunlar zaten
                // kendi chip'lerinde (Hidden/Recycle Bin) görünüyor.
                PlaylistChipKind.ReadyToPlay => _allGames.Where(g => g.HasLocalFile && !g.IsEffectivelyHidden && !g.IsDeleted),
                PlaylistChipKind.NeedsSearch => _allGames.Where(g => !g.HasLocalFile && !g.IsEffectivelyHidden && !g.IsDeleted),
                // Sıralama/kesme burada DEĞİL, ApplyFilter'da sütun filtrelerinden SONRA
                // uygulanıyor (bkz. orada).
                PlaylistChipKind.Top250 or PlaylistChipKind.Top100 or PlaylistChipKind.Top25 => GetTopRatedPopulation(),
                _ => _allGames.Where(g => _selectedChipMembership.Contains(g.GameKey)),
            };

            if (SelectedPlatform is { IsAllPlatforms: false })
                query = query.Where(g => g.Platform == SelectedPlatform.Name);
        }
        else
        {
            query = _allGames.Where(g => !g.IsEffectivelyHidden && !g.IsDeleted);

            if (SelectedPlatform is { IsAllPlatforms: false })
                query = query.Where(g => g.Platform == SelectedPlatform.Name);

            query = query.Where(g => (ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk"));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (_alternateNamesByGameId.TryGetValue(g.GameId, out var altNames)
                    && altNames.Any(n => n.Contains(SearchText, StringComparison.OrdinalIgnoreCase))));

        query = query.Where(MatchesRegionFlags);

        return query;
    }

    // Kullanıcı isteği: "başlıktaki yüzdeyide ayarla aynı şekilde" — sol paneldeki platform
    // rozetleriyle (bkz. SyncPlatformGameCounts) AYNI kök nedendi: Released/Junk anahtarları
    // hiç dikkate alınmıyordu, bu yüzden Başlık sütunu başlığındaki % sağlık göstergesi Junk
    // açılıp kapatıldığında değişmiyordu.
    private IEnumerable<Game> GetCurrentPlatformPopulation()
    {
        var query = _allGames.Where(g => !g.IsEffectivelyHidden && !g.IsDeleted
            && ((ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk")));
        if (SelectedPlatform is { IsAllPlatforms: false })
            query = query.Where(g => g.Platform == SelectedPlatform.Name);

        return query;
    }

    // Platform, arama metni, Released/Junk anahtarları ve sütun başlıklarındaki joystick
    // filtrelerine göre _allGames üzerinden Games koleksiyonunu yeniden oluşturur. Basitlik için
    // tüm listeyi temizleyip yeniden dolduruyoruz; 67 bin oyun için bu hâlâ anlık.
    //
    // Bir chip seçiliyse (Favorites/kullanıcı playlist'i/Hidden/Recycle Bin/Ready to Play/Needs
    // Search/Top 250-100-25) o chip'in kendi popülasyonu TABAN olarak kullanılır (bkz.
    // GetFilterScopePopulation); arama metni ve sütun başlığı filtreleri (Platform/Genres/...
    // popup'ları) bunun ÜSTÜNE, HER ZAMAN uygulanır (kullanıcı geri bildirimi: "filtreler her
    // playlistte çalışsın" — önceden chip seçiliyken bu filtreler tamamen atlanıyordu).
    //
    // Top 250/100/25 için ağırlıklı sıralama VE kesme (Take N) sütun filtrelerinden SONRA
    // uygulanır — "Top 25" gerçekten "şu an filtrelenmiş kümenin ilk 25'i" anlamına gelsin diye
    // (kullanıcı isteği: "USA'nın top 25'ini görmek istiyorum, USA+EU seçiliyken ikisi arasından
    // top 25, hepsi seçiliyken hepsi arasından top 25 — seçilenlere göre göstermesi lazım"). Filtre
    // ÖNCE, sıralama/kesme SONRA uygulanmazsa, "Top 25" sabit bir 25'lik listeyi filtreleyip 25'ten
    // az sonuç verirdi; bu sırayla her zaman (yeterli aday varsa) tam 25 sonuç, filtrelenmiş
    // kümenin GERÇEK ilk 25'i olarak dönüyor.
    //
    // public: Edit Metadata penceresi kapandıktan sonra MainWindow.xaml.cs bunu çağırıp
    // DataGrid'in güncellenen değerleri (Title/Genre/... ObservableProperty olmadığı için)
    // göstermesini sağlıyor.
    // Sütun başlığındaki "Sırala A-Z/Z-A" düğmesiyle seçilen aktif sıralama (bkz.
    // MainWindow.xaml.cs ApplyHeaderSort). Kullanıcı bulgusu: "başlıklarda isimlerin a dan z'ye
    // sıralamasında bi sorun var" — ApplyFilter, Games koleksiyonunu HER seferinde (platform
    // değişimi, filtre, hatta arka planda görsel indirme sonrası) sıfırdan yeni bir
    // ObservableCollection olarak kuruyordu; DataGrid'in ICollectionView'ına doğrudan uygulanan
    // eski SortDescriptions yaklaşımı bu yeni koleksiyonla birlikte sessizce kayboluyordu. Artık
    // aktif sıralama burada (ViewModel'de) saklanıyor ve ApplyFilter'ın kendisi query'yi
    // sıralayarak kuruyor — hangi filtre/yenileme tetiklenirse tetiklensin sıralama kalıcı kalır.
    // Kullanıcı isteği: "varsayılan olarak da A-Z sıralı olmasını bekliyordum" — hiç sıralama
    // seçilmemişse liste DAT/katalog ekleme sırasıyla geliyordu (alfabetik değil, kafa karıştırıcı).
    // Başlangıç değeri artık "Title"/Ascending — MainWindow.xaml.cs de Başlık sütununun ok
    // ikonunu bu varsayılanla eşleşecek şekilde başlangıçta işaretliyor (bkz. MainWindow ctor).
    private string? _activeSortMemberPath = nameof(Game.Title);
    private ListSortDirection _activeSortDirection = ListSortDirection.Ascending;

    private static readonly IComparer<object?> SortValueComparer = Comparer<object?>.Create((a, b) =>
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        return Comparer<object>.Default.Compare(a, b);
    });

    public void SetActiveSort(string? memberPath, ListSortDirection direction)
    {
        _activeSortMemberPath = memberPath;
        _activeSortDirection = direction;
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var query = GetFilterScopePopulation();

        query = ApplyColumnFilter(query, PlatformFilter, g => g.PlatformDisplayName);
        query = ApplyColumnFilter(query, GenresFilter, g => g.Genres);
        query = ApplyColumnFilter(query, PublisherFilter, g => g.Publisher);
        query = ApplyColumnFilter(query, CommunityRatingFilter, g => g.CommunityRating.HasValue ? g.CommunityRating.Value.ToString("0.0") : string.Empty);
        query = ApplyColumnFilter(query, RegionFilter, g => g.Region);
        query = ApplyColumnFilter(query, SourceFilter, g => g.SourceDat);
        query = ApplyColumnFilter(query, MatchMethodFilter, g => g.MatchMethod);
        query = ApplyColumnFilter(query, MaxPlayersFilter, g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString());
        query = ApplyColumnFilter(query, TitleFilter, g => g.Title);
        query = ApplyColumnFilter(query, FileFilter, g => g.File);
        query = ApplyColumnFilter(query, MatchedFilter, GetMatchedStatusLabel);
        query = ApplyColumnFilter(query, FavoriteFilter, g => g.IsFavorite ? "Evet" : "Hayır");
        query = ApplyColumnFilter(query, HasLocalFileFilter, g => g.HasLocalFile ? "Oynanabilir" : "Eksik");
        query = ApplyColumnFilter(query, BoxFilter, g => g.HasBox ? "Evet" : "Hayır");
        query = ApplyColumnFilter(query, ScreenshotFilter, g => g.HasScreenshot ? "Evet" : "Hayır");

        query = SelectedChip?.Kind switch
        {
            PlaylistChipKind.Top250 => ComputeTopRatedForScope(query, 250),
            PlaylistChipKind.Top100 => ComputeTopRatedForScope(query, 100),
            PlaylistChipKind.Top25 => ComputeTopRatedForScope(query, 25),
            _ => query,
        };

        if (_activeSortMemberPath is { Length: > 0 } sortMemberPath
            && typeof(Game).GetProperty(sortMemberPath) is { } sortProperty)
        {
            query = _activeSortDirection == ListSortDirection.Ascending
                ? query.OrderBy(g => sortProperty.GetValue(g), SortValueComparer)
                : query.OrderByDescending(g => sortProperty.GetValue(g), SortValueComparer);
        }

        Games = new ObservableCollection<Game>(query);

        OnPropertyChanged(nameof(CurrentPlatformHealthPercent));
        OnPropertyChanged(nameof(CurrentPlatformLogoPath));
        OnPropertyChanged(nameof(PlatformColumnVisibility));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    // Bir sütun filtresi popup'ı açılmadan HEMEN önce (bkz. ColumnFilterViewModel.
    // RequestRefreshOptions) çağrılır — Options listesindeki değer/sayıları GÜNCEL kapsama (chip/
    // platform/arama, bkz. GetFilterScopePopulation) göre yeniden hesaplar. Kullanıcının o an
    // işaretli/işaretsiz bıraktığı değerler KORUNUR, sadece değer kümesi ve sayılar tazelenir.
    private void RefreshColumnFilterOptions(ColumnFilterViewModel filter, Func<Game, string> selector)
    {
        var previousChecked = filter.Options.ToDictionary(o => o.Value, o => o.IsChecked, StringComparer.OrdinalIgnoreCase);

        var freshOptions = GetFilterScopePopulation()
            .Select(selector)
            .Select(NormalizeForFilter)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterOption
            {
                Value = g.Key,
                Count = g.Count(),
                IsChecked = !previousChecked.TryGetValue(g.Key, out var wasChecked) || wasChecked,
            });

        filter.Options.Clear();
        foreach (var option in freshOptions)
            filter.Options.Add(option);
    }

    private void RefreshPlatformFilterOptions()
    {
        var previousChecked = PlatformFilter.Options.ToDictionary(o => o.Value, o => o.IsChecked, StringComparer.OrdinalIgnoreCase);

        var freshOptions = CreatePlatformFilterOptions(GetFilterScopePopulation())
            .Select(option => new FilterOption
            {
                Value = option.Value,
                Count = option.Count,
                HealthPercent = option.HealthPercent,
                IsChecked = !previousChecked.TryGetValue(option.Value, out var wasChecked) || wasChecked,
            });

        PlatformFilter.Options.Clear();
        foreach (var option in freshOptions)
            PlatformFilter.Options.Add(option);
    }

    // "Top 250/100/25" chip'leri için ağırlıklı ortalama (IMDb'nin kendi Top 250'sinde kullandığı
    // Bayesian tahminle aynı formül): WR = (v/(v+m))*R + (m/(v+m))*C. Ham CommunityRating'e göre
    // sıralamak yanıltıcı olurdu — az oyla mükemmel puan almış bir oyun, binlerce oyla biraz daha
    // düşük puan almış bir oyunun önüne geçerdi. m (bu popülasyondaki tipik oy sayısı, medyan) ve
    // C (bu popülasyonun ortalama puanı) HER ZAMAN verilen "population" kümesinden hesaplanır —
    // yani çağıran taraf hangi alt kümeyi verirse (tek platform ya da tüm kütüphane) ağırlıklandırma
    // ona göre normalize olur. Tam sıralı listeyi döndürür; kesme noktası (250/100/25) çağıran tarafta.
    private static List<Game> RankByWeightedRating(IEnumerable<Game> population)
    {
        var rated = population.Where(g => g.CommunityRating is > 0 && g.CommunityRatingCount is > 0).ToList();
        if (rated.Count == 0)
            return new List<Game>();

        var meanRating = rated.Average(g => g.CommunityRating!.Value);
        var voteCounts = rated.Select(g => g.CommunityRatingCount!.Value).OrderBy(v => v).ToList();
        var m = voteCounts[voteCounts.Count / 2];

        return rated
            .Select(g => new
            {
                Game = g,
                Weighted = (g.CommunityRatingCount!.Value / (double)(g.CommunityRatingCount.Value + m)) * g.CommunityRating!.Value
                           + (m / (double)(g.CommunityRatingCount.Value + m)) * meanRating,
            })
            .OrderByDescending(x => x.Weighted)
            .Select(x => x.Game)
            .ToList();
    }

    private static IEnumerable<Game> ComputeTopRated(IEnumerable<Game> population, int take) =>
        RankByWeightedRating(population).Take(take);

    // "Tüm Platformlar" seçiliyken düz ComputeTopRated (tüm platformları TEK bir havuzda
    // sıralayıp kesme) ile Game.TopRankBadge (bkz. ComputeTopRankBadges, HER ZAMAN platform
    // başına ayrı hesaplanır) birbiriyle ÇELİŞİYORDU: küçük bir platformda gerçekten "Top 25"
    // olan bir oyun, büyük platformların oylarıyla dolu global sıralamada 100'ün dışında kalıp
    // "Top 100" listesinde hiç görünmeyebiliyor, ya da tam tersi — kullanıcı geri bildirimi:
    // "top100'ü açınca gözükenlerin badge'inde top 25 yazıyor". Tek platform seçiliyken zaten
    // tek bir platform var, sorun yok; "Tüm Platformlar"da HER platformu KENDİ İÇİNDE ayrı ayrı
    // sıralayıp kesip birleştiriyoruz — böylece listede görünen her oyun, rozetiyle (o platform
    // içindeki sıralamayla) her zaman tutarlı.
    private IEnumerable<Game> ComputeTopRatedForScope(IEnumerable<Game> population, int take) =>
        SelectedPlatform is { IsAllPlatforms: false }
            ? ComputeTopRated(population, take)
            : population.GroupBy(g => g.Platform).SelectMany(platformGroup => RankByWeightedRating(platformGroup).Take(take));

    // Top 250/100/25 chip'lerinin filtre popülasyonu. Platform daraltması artık burada değil,
    // GetFilterScopePopulation'da TÜM chip'ler için tek/ortak bir yerde uygulanıyor (bkz. orada).
    private IEnumerable<Game> GetTopRatedPopulation() => _allGames.Where(g => !g.IsEffectivelyHidden && !g.IsDeleted);

    // Uygulama açılışında BİR KEZ hesaplanır (bkz. constructor) — her oyunun KENDİ platformu
    // içindeki ağırlıklı sıralamaya göre en yüksek rozeti (Top25 > Top100 > Top250) belirlenir.
    // Bilinçli olarak SelectedPlatform/chip filtresinden BAĞIMSIZ: detay panelindeki rozet, sol
    // panelde hangi platform seçili olursa olsun aynı kalmalı (bkz. kullanıcı isteği, Images/Badges).
    private void ComputeTopRankBadges()
    {
        foreach (var platformGroup in _allGames.GroupBy(g => g.Platform))
        {
            var ranked = RankByWeightedRating(platformGroup);
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].TopRankBadge = i switch
                {
                    < 25 => "Top 25",
                    < 100 => "Top 100",
                    < 250 => "Top 250",
                    _ => string.Empty,
                };
            }
        }
    }

    // Bir sütun filtresi "aktif" (en az bir değer işaretsiz) değilse hiçbir şey elemez; aktifse
    // sadece işaretli değerlerden birine sahip oyunları geçirir. IsSearchOnly sütunlarda (Title/
    // File) checkbox listesi hiç yok — SearchText içeriğe göre doğrudan alt-dize eşleşmesi yapar.
    private static IEnumerable<Game> ApplyColumnFilter(IEnumerable<Game> query, ColumnFilterViewModel filter, Func<Game, string> selector)
    {
        if (!filter.IsActive)
            return query;

        if (filter.IsSearchOnly)
            return query.Where(g => selector(g).Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase));

        var selected = filter.SelectedValues;
        return query.Where(g => selected.Contains(NormalizeForFilter(selector(g))));
    }

    private static bool HasMatchedMetadata(Game game) => game.MetadataSourceId.HasValue || game.StatusOk;

    private static int ComputeHealthPercent(IReadOnlyCollection<Game> games)
    {
        var totalGames = games.Count;
        if (totalGames == 0)
            return 0;

        var matchedCount = games.Count(HasMatchedMetadata);
        var missingLogoCount = games.Count(g => !g.HasClearLogo);
        var missingBoxCount = games.Count(g => !g.HasBox);
        var missingScreenshotCount = games.Count(g => !g.HasScreenshot);

        var healthPercent = (int)Math.Round(
            (matchedCount / (double)totalGames * 50d) +
            ((totalGames - missingLogoCount) / (double)totalGames * 15d) +
            ((totalGames - missingBoxCount) / (double)totalGames * 20d) +
            ((totalGames - missingScreenshotCount) / (double)totalGames * 15d),
            MidpointRounding.AwayFromZero);

        return Math.Clamp(healthPercent, 0, 100);
    }

    private static IEnumerable<FilterOption> CreatePlatformFilterOptions(IEnumerable<Game> games) =>
        games.GroupBy(g => NormalizeForFilter(g.PlatformDisplayName), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var groupedGames = g.ToList();
                return new FilterOption
                {
                    Value = g.Key,
                    Count = groupedGames.Count,
                    HealthPercent = ComputeHealthPercent(groupedGames),
                };
            });

    // Her platformun rozetindeki sayıyı _allGames içinde o platforma ait kaç kayıt olduğuna göre
    // hesaplar ("All Platforms" toplam sayıyı gösterir). GetPlatforms()'taki sabit GameCount
    // değerleri sadece ilk yapım aşamasının kalıntısıydı; artık tek doğru kaynak gerçek oyun listesi.
    // Kullanıcı geri bildirimi: "release ve junk'a bastığımda değişmiyor ... platform listesindeki
    // sayılar değişmiyor" — önceki tasarım bilerek Released/Junk'tan bağımsız tutuyordu, ama
    // kullanıcı artık bu ikisini YANSITMASINI istiyor (region bayrağı/arama/sütun filtreleri gibi
    // DAHA DAR kapsamlı filtrelerden hâlâ bağımsız — rozet "Görünen" sayacı değil, sadece Released/
    // Junk açık/kapalı durumuna göre "bu platformda toplam kaç oyun var" sorusuna cevap verir).
    // Kullanıcı bulgusu: "badge'de 1268 gözüküyor görünende 1260" — rozet IsHidden/IsDeleted'ı hiç
    // kontrol etmiyordu (NES'te tam 7 gizli + 1 çöp kutusunda oyun vardı, farkla birebir eşleşti) —
    // gizli/silinmiş bir oyunun "kaç oyun var" sayısına dahil olması hiçbir yorumda mantıklı değil,
    // bu yüzden (region/arama/sütun filtrelerinin aksine) bu ikisi rozette de uygulanıyor artık.
    private void SyncPlatformGameCounts()
    {
        foreach (var platform in Platforms)
        {
            var population = platform.IsAllPlatforms
                ? _allGames.AsEnumerable()
                : _allGames.Where(g => g.Platform == platform.Name);
            platform.GameCount = population.Count(g => !g.IsEffectivelyHidden && !g.IsDeleted
                && ((ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk")));
        }
    }

    // Kategori sırası sabit: popüler kategoriler önce, "OTHERS" en sonda ve varsayılan kapalı.
    // Bir platformu "OTHERS"tan ana bir kategoriye taşımak RetroAudit.Catalog/Dat/
    // PlatformCategoryMap.cs'i değiştirmekle olur (Builder tarafı) — bu metod Platforms.Category
    // sütununda ne yazıyorsa otomatik olarak doğru başlığın altına yerleştirir.
    private static readonly string[] CategoryOrder =
    {
        CategoryConsoles,
        CategoryHandhelds,
        CategoryArcade,
        CategoryComputers,
        CategoryClassic,
        CategoryOthers,
    };

    // Platforms (tam liste, kategorilere ayrılmış) içinden sol panelde gösterilecek satır dizisini
    // (başlık + platform satırları) kurar. "OTHERS" kategorisinin platform satırları sadece
    // IsOthersExpanded=true iken eklenir.
    private void RebuildPlatformListItems()
    {
        PlatformListItems.Clear();

        var allPlatformsEntry = Platforms.FirstOrDefault(p => p.IsAllPlatforms);
        if (allPlatformsEntry is not null)
            PlatformListItems.Add(new PlatformListItem { Platform = allPlatformsEntry });

        var realPlatforms = Platforms.Where(p => !p.IsAllPlatforms);

        // Gruplama kapalıyken (Ayarlar > Arayüz > Platform Listesi) kategori başlıkları hiç
        // gösterilmez — tek düz, kullanıcının sürükle-bırakla ayarladığı sırada bir liste.
        if (!GroupPlatformsByCategory)
        {
            foreach (var platform in OrderPlatforms(realPlatforms))
                PlatformListItems.Add(new PlatformListItem { Platform = platform });
            return;
        }

        foreach (var category in CategoryOrder)
        {
            // Ayarlar'da bu kategori gizlenmişse (bkz. SettingsViewModel.CategoryOptions) o
            // kategorinin başlığı da platformları da sol panelde hiç görünmez.
            if (!_appSettings.CategoryVisibility.GetValueOrDefault(category, true))
                continue;

            var platformsInCategory = OrderPlatforms(realPlatforms.Where(p => p.Category == category));
            if (platformsInCategory.Count == 0)
                continue;

            var isOthers = category == CategoryOthers;
            PlatformListItems.Add(new PlatformListItem
            {
                IsHeader = true,
                HeaderText = category,
                IsCollapsibleHeader = isOthers,
                ExpandGlyph = isOthers ? (IsOthersExpanded ? "▾" : "▸") : string.Empty,
            });

            if (isOthers && !IsOthersExpanded)
                continue;

            foreach (var platform in platformsInCategory)
                PlatformListItems.Add(new PlatformListItem { Platform = platform });
        }

        PlatformListOrderChanged?.Invoke();
    }

    // _appSettings.PlatformOrder'daki (kullanıcının sürükle-bırakla ayarladığı) sıraya göre
    // dizer; listede olmayan platformlar (yeni eklenmiş/hiç taşınmamış) doğal sıralarıyla sona
    // eklenir — hiçbir platform sessizce kaybolmaz.
    private List<Platform> OrderPlatforms(IEnumerable<Platform> platforms)
    {
        var order = _appSettings.PlatformOrder;
        return platforms
            .Select((p, i) => (Platform: p, OriginalIndex: i))
            .OrderBy(t => order.IndexOf(t.Platform.Name) is var idx && idx >= 0 ? idx : int.MaxValue)
            .ThenBy(t => t.OriginalIndex)
            .Select(t => t.Platform)
            .ToList();
    }

    // Sol paneldeki platform listesinde sürükleme SIRASINDA (her DragOver'da) çağrılır — sadece
    // o an EKRANDA görüneni (PlatformListItems) taşıyıp anlık görsel geri bildirim verir, DİSKE
    // YAZMAZ (bkz. MainWindow.xaml.cs PlatformRow_DragOver). Kalıcı kayıt, sürükleme bitince
    // CommitPlatformOrder ile tek seferde olur. Kategoriler açıkken (GroupPlatformsByCategory)
    // sadece AYNI kategori içinde taşımaya izin verilir (kategori ataması Builder tarafına ait,
    // bkz. Platform.Category yorumu, RetroAudit.Catalog/Dat/PlatformCategoryMap.cs).
    public void MoveInPlatformListItems(Platform dragged, Platform target)
    {
        if (dragged == target)
            return;

        if (GroupPlatformsByCategory && dragged.Category != target.Category)
            return;

        var draggedItem = PlatformListItems.FirstOrDefault(i => i.Platform == dragged);
        var targetItem = PlatformListItems.FirstOrDefault(i => i.Platform == target);
        if (draggedItem is null || targetItem is null)
            return;

        var oldIndex = PlatformListItems.IndexOf(draggedItem);
        var newIndex = PlatformListItems.IndexOf(targetItem);
        if (oldIndex != newIndex)
        {
            PlatformListItems.Move(oldIndex, newIndex);
            PlatformListOrderChanged?.Invoke();
        }
    }

    // Sürükleme bittiğinde (bkz. MainWindow.xaml.cs PlatformRow_MouseMove, DragDrop.DoDragDrop
    // dönünce çağrılıyor) o an ekranda görünen sırayı kalıcı hale getirir.
    public void CommitPlatformOrder()
    {
        var visibleOrder = PlatformListItems
            .Where(i => i.Platform is { IsAllPlatforms: false })
            .Select(i => i.Platform!.Name)
            .ToList();

        // Görünmeyen (gizli kategori / kapalı "OTHERS") platformlar PlatformListItems'ta hiç yok —
        // önceki PlatformOrder'da onlar için kayıtlı bir konum varsa kaybolmasın diye sona ekleniyor.
        var missing = _appSettings.PlatformOrder.Where(name => !visibleOrder.Contains(name));
        visibleOrder.AddRange(missing);

        _appSettings.PlatformOrder = visibleOrder;
        ConfigService.SaveDefault(_appSettings);
        PlatformListOrderChanged?.Invoke();
    }

    public IReadOnlyList<Platform> GetVisiblePlatformOrder() =>
        PlatformListItems
            .Where(i => i.Platform is not null)
            .Select(i => i.Platform!)
            .ToList();

    // Kullanıcı bulgusu: "resimlerdeki toplamı topladım RetroAudit toplamıyla tutmuyor" — kök neden
    // Media/Metadata Provider'ın platform kart listesinin GetVisiblePlatformOrder() (sol paneldeki
    // "OTHERS" kategorisi kapalıysa veya bir kategori Ayarlar'dan gizlenmişse o platformlar hiç
    // dönmüyor) kullanmasıydı — ama "All Platforms" satırı sidebar görünürlüğünden bağımsız TÜM
    // oyunları sayıyordu, bu yüzden kart toplamları asla aggregate'i tutturamıyordu. Provider
    // pencereleri artık bunun yerine bu metodu kullanıyor: sidebar'da neyin açık/kapalı/gizli
    // olduğuna bakmaksızın HER zaman tüm gerçek platformları (kullanıcının PlatformOrder'ıyla
    // sıralı) döner.
    public IReadOnlyList<Platform> GetAllPlatformsOrdered()
    {
        var result = new List<Platform>();
        var allPlatformsEntry = Platforms.FirstOrDefault(p => p.IsAllPlatforms);
        if (allPlatformsEntry is not null)
            result.Add(allPlatformsEntry);
        result.AddRange(OrderPlatforms(Platforms.Where(p => !p.IsAllPlatforms)));
        return result;
    }

    public void NotifyMetadataEdited(Game game)
    {
        ApplyFilter();
        GameMetadataChanged?.Invoke(game);
    }

    // "OTHERS" başlığına tıklanınca açar/kapatır.
    [RelayCommand]
    private void ToggleOthers() => IsOthersExpanded = !IsOthersExpanded;

    // --- Toolbar komutları ---

    [RelayCommand]
    private void Import() => RequestOpenRomImport?.Invoke();

    // "Temizle" butonu: arama metnini, platform seçimini ve filtre anahtarlarını varsayılana döndürür.
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);
        ShowReleased = true;
        ShowJunk = false;
    }

    // Detay panelindeki Tür rozetine (tek tür) veya açılır menüsündeki bir seçeneğe (birden fazla
    // tür) tıklanınca çalışır — YENİ bir filtre sistemi DEĞİL, sütun başlığındaki filtre popup'ıyla
    // BİREBİR AYNI GenresFilter'ı kullanır (bkz. Models/ColumnFilter.cs). GenresFilter.Options her
    // oyunun TAM (virgülle ayrılmış) Genres string'ini tek bir seçenek olarak tutuyor; bir option,
    // seçili token'lardan (bkz. _activeGenreTokens) HERHANGİ birini İÇERİYORSA işaretlenir (OR).
    // Kullanıcı geri bildirimi: "shooter ve horror'u tıkladım ikisi de ayrı ayrı gözükmeli" —
    // birden fazla rozete art arda tıklamak ÖNCEKİni SİLMEK yerine BİRİKTİRİR; AYNI rozete İKİNCİ
    // kez tıklamak sadece O token'ı kümeden çıkarır (toggle off). "ALL" tüm kümeyi temizler
    // (dropdown'daki "All Genres").
    [RelayCommand]
    private void FilterByGenreToken(string token)
    {
        if (token == "ALL")
            _activeGenreTokens.Clear();
        else if (!_activeGenreTokens.Add(token))
            _activeGenreTokens.Remove(token);

        foreach (var option in GenresFilter.Options)
            option.IsChecked = _activeGenreTokens.Count == 0
                || option.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(_activeGenreTokens.Contains);

        GenresFilter.ApplyFilterCommand.Execute(null);

        // Rozette (bkz. ColumnFilterViewModel.SetActiveSummaryOverride) Options'tan türetilen genel
        // özet YERİNE kullanıcının GERÇEKTEN tıkladığı token'ların kendisi gösterilir — ör.
        // "Shooter, Horror" (co-occur eden "Platform"/"Flight Simulator" gibi başka türler karışmaz).
        // ApplyFilterCommand.Execute HEMEN yukarıda override'ı temizlediği için burada, ondan SONRA
        // set ediliyor.
        GenresFilter.SetActiveSummaryOverride(_activeGenreTokens.Count == 0 ? null : string.Join(", ", _activeGenreTokens));
    }

    // Detay panelindeki "Released"/"Junk" rozetine tıklanınca çalışır — YENİ bir filtre değil,
    // toolbar'daki Released/Junk toggle'larıyla AYNI ShowReleased/ShowJunk alanlarını kullanır
    // (bkz. OnShowReleasedChanged/OnShowJunkChanged, ikisi de zaten ApplyFilter'ı tetikliyor).
    // Rozete tıklamak o sürüm türünü DIŞLAYICI olarak seçer (ör. Released'a tıklayınca Junk kapanır).
    [RelayCommand]
    private void FilterByVersion(string version)
    {
        ShowReleased = version == "Released";
        ShowJunk = version == "Junk";
    }

    // Sağ paneldeki BAŞLAT butonu — o an seçili oyunu başlatır (grid'deki Play butonuyla aynı
    // LaunchGame'i kullanır).
    [RelayCommand]
    private void Launch()
    {
        if (SelectedGame is not null)
            LaunchGame(SelectedGame);
    }

    // Grid'deki satır bazlı Play butonu (ROM mevcutsa Search'ün yerini alır, bkz. MainWindow.xaml
    // SearchColumn). Ayarlar > Emülatörler'de PlatformName'i bu oyunun platformuyla eşleşen bir
    // kayıt aranır — hem ham (Platform) hem görünen (PlatformDisplayName) ada karşı, çünkü o
    // DataGrid'deki Platform sütunu serbest metin ve kullanıcı hangisini yazdığını hatırlamayabilir.
    [RelayCommand]
    private void LaunchGame(Game game)
    {
        if (!HasLocalFile(game))
        {
            RequestShowMessage?.Invoke("Bu oyunun ROM dosyası bulunamadı.");
            return;
        }

        LaunchWithEmulator(game, GetLocalFilePath(game));
    }

    // LaunchGame (grid'deki Play butonu) ve LaunchVersion (Sürümler panelindeki çift tık) tarafından
    // paylaşılan asıl başlatma mantığı — ikisi de sadece hangi dosya yolunun kullanılacağını farklı
    // şekilde çözer, emülatör eşleştirme/başlatma her ikisinde de aynı.
    private void LaunchWithEmulator(Game game, string romPath)
    {
        var emulator = _appSettings.Emulators.FirstOrDefault(e =>
            string.Equals(e.PlatformName, game.Platform, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.PlatformName, game.PlatformDisplayName, StringComparison.OrdinalIgnoreCase));

        if (emulator is null)
        {
            RequestShowMessage?.Invoke($"\"{game.PlatformDisplayName}\" platformu için Ayarlar > Emülatörler'de bir emülatör tanımlanmamış.");
            return;
        }

        // İki modlu (RetroArchCore/StandaloneEXE) yer tutucu doldurma + Process.Start mantığı
        // artık tek bir yerde, LaunchEngine'de yaşıyor.
        var result = LaunchEngine.Launch(emulator, romPath);
        if (!result.Success)
            RequestShowMessage?.Invoke(result.ErrorMessage ?? "Emülatör başlatılamadı.");
    }

    // Gameplay screenshot alanındaki YouTube oynatma — artık dış tarayıcı AÇMIYOR (kullanıcı
    // isteği), aynı alanda embedded WebView2 player'a geçiyor (bkz. MainWindow.xaml Grid.Row="4",
    // MainWindow.xaml.cs PlayYouTubeEmbedAsync). Hem başlık satırındaki YouTube butonu hem de
    // gameplay alanının ortasındaki Play overlay'i bunu çağırıyor.
    [RelayCommand]
    private void PlayVideo(Game game)
    {
        if (!game.HasYouTubeEmbed)
            return;

        VideoEmbedFailed = false;
        IsPlayingVideo = true;
    }

    [RelayCommand]
    private void CloseVideo()
    {
        IsPlayingVideo = false;
        VideoEmbedFailed = false;
    }

    // SADECE embed navigasyonu başarısız olursa gösterilen fallback buton (bkz. VideoEmbedFailed) —
    // bu tek durumda dış tarayıcıda açmak makul, aksi halde kullanıcının izleyecek hiçbir yolu kalmaz.
    [RelayCommand]
    private void OpenVideoUrl(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.VideoUrl))
            Process.Start(new ProcessStartInfo(game.VideoUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenWikipediaUrl(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.WikipediaUrl))
            Process.Start(new ProcessStartInfo(game.WikipediaUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenMediaProvider() => RequestOpenMediaProvider?.Invoke();

    [RelayCommand]
    private void OpenMetadataProvider() => RequestOpenMetadataProvider?.Invoke();

    [RelayCommand]
    private void OpenCropEditor() => RequestOpenCropEditor?.Invoke();

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke();
}
