using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Catalog.Grouping;
using RetroAudit.Catalog.Metadata;
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
    public event Action? RequestOpenCropEditor;
    public event Action? RequestOpenSettings;
    public event Action? RequestOpenRomImport;
    public event Action<string>? RequestShowMessage;

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

    // "Sütunlar" düğmesiyle açılan seçici — hangi DataGrid sütununun görünür olacağını belirler.
    // MainWindow.xaml.cs, IsVisible değiştiğinde ilgili DataGridColumn'ı Key'e göre bulup
    // Visibility'sini günceller (DataGridColumn görsel ağacın parçası olmadığı için doğrudan
    // XAML binding ile gizlenemiyor). Tüm 19 sütun da burada — artık hiçbiri "hep görünür/asla
    // kapatılamaz" değil. Varsayılan IsVisible değerleri BuildColumnOptions'da kuruluyor
    // (constructor'da, _appSettings.ColumnVisibility'deki kayıtlı tercihler bunun üzerine
    // uygulanıyor ki daha önce kapatılmış bir sütun açılışta tekrar açık gelmesin).
    public ObservableCollection<ColumnVisibilityOption> ColumnOptions { get; } = new();

    // Ayarlar > Arayüz sekmesinde değiştirilen ContextMenuDisplayMode/LaunchBoxDbPath gibi
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

        _allGames = CatalogDatabaseService.GetGames();
        BuildLocalFileIndex();
        foreach (var game in _allGames)
        {
            game.HasLocalFile = HasLocalFile(game);
            NotifyArtworkDownloaded(game);
        }
        ComputeTopRankBadges();

        Platforms = new ObservableCollection<Platform>(CatalogDatabaseService.GetPlatforms());

        // Rozetlerdeki sayı sabit bir değer değil, gerçek oyun listesinden hesaplanıyor —
        // böylece "0 oyun var ama rozet 1406 yazıyor" gibi senkronsuzluk hiç oluşmaz.
        SyncPlatformGameCounts();
        RebuildPlatformListItems();

        PlatformFilter = BuildColumnFilter("Platform", _allGames.Select(g => g.PlatformDisplayName));
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
        MatchedFilter = BuildColumnFilter("Durum", _allGames.Select(g => g.StatusOk ? "Eşleşti" : "Eşleşmedi"));
        FavoriteFilter = BuildColumnFilter("Favori", _allGames.Select(g => g.IsFavorite ? "Evet" : "Hayır"));
        HasLocalFileFilter = BuildColumnFilter("Durum", _allGames.Select(g => g.HasLocalFile ? "Oynanabilir" : "Eksik"));
        ActionsFilter = new ActionsColumnFilterViewModel(FavoriteFilter, HasLocalFileFilter);
        BoxFilter = BuildColumnFilter("Box", _allGames.Select(g => g.HasBox ? "Evet" : "Hayır"));
        ScreenshotFilter = BuildColumnFilter("SS", _allGames.Select(g => g.HasScreenshot ? "Evet" : "Hayır"));

        var allColumnFilters = new[]
        {
            PlatformFilter, GenresFilter, PublisherFilter, CommunityRatingFilter,
            RegionFilter, SourceFilter, MatchMethodFilter, MaxPlayersFilter,
            TitleFilter, FileFilter, MatchedFilter, FavoriteFilter, HasLocalFileFilter,
            BoxFilter, ScreenshotFilter,
        };
        foreach (var filter in allColumnFilters)
            filter.FilterChanged += ApplyFilter;

        // Popup açılmadan hemen önce Options'ı güncel kapsama göre tazeler (bkz.
        // RefreshColumnFilterOptions) — Title/File IsSearchOnly olduğu için Options hiç kurmuyor,
        // bu yüzden burada yok (ColumnFilterViewModel.Open zaten onlar için tetiklemiyor).
        PlatformFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(PlatformFilter, g => g.PlatformDisplayName);
        GenresFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(GenresFilter, g => g.Genres);
        PublisherFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(PublisherFilter, g => g.Publisher);
        CommunityRatingFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(CommunityRatingFilter, g => g.CommunityRating.HasValue ? g.CommunityRating.Value.ToString("0.0") : string.Empty);
        RegionFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(RegionFilter, g => g.Region);
        SourceFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(SourceFilter, g => g.SourceDat);
        MatchMethodFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MatchMethodFilter, g => g.MatchMethod);
        MaxPlayersFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MaxPlayersFilter, g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString());
        MatchedFilter.RequestRefreshOptions += () => RefreshColumnFilterOptions(MatchedFilter, g => g.StatusOk ? "Eşleşti" : "Eşleşmedi");
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
        RebuildPlatformListItems();
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
        PlaylistChips.Add(new PlaylistChip { Name = "Needs Search", Color = "#E5484D", IsBuiltIn = true, Kind = PlaylistChipKind.NeedsSearch });
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

    // Kullanıcının "Şu anki yoldan kullan" ile içe aktardığı ROM'lar — RetroAudit'in kendi
    // {Platform}\{File} kuralına taşınmadan, kullanıcının kendi arşivindeki orijinal konumundan
    // kullanılır (bkz. RomImportService/RomImportViewModel). GameKey -> tam dosya yolu.
    private Dictionary<string, string> _filePathOverrides = new();

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

    private static string? TryGetMediaPath(Dictionary<string, Dictionary<string, string>> byPlatform, Game game)
    {
        if (string.IsNullOrWhiteSpace(game.File))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(game.File);
        return byPlatform.TryGetValue(game.PlatformDisplayName, out var files) && files.TryGetValue(baseName, out var path)
            ? path
            : null;
    }

    private string GetBoxPath(Game game) => TryGetMediaPath(_boxByPlatform, game) ?? string.Empty;
    private string GetScreenshotPath(Game game) => TryGetMediaPath(_screenshotByPlatform, game) ?? string.Empty;
    private string GetClearLogoPath(Game game) => TryGetMediaPath(_clearLogoByPlatform, game) ?? string.Empty;

    // Medya sözlükleri (_boxByPlatform vb.) uygulama açılışında diskten BİR KEZ kuruluyor —
    // "Görsel Getir" ile yeni indirilen bir dosya restart olmadan bu sözlüklerde yoktu, bu yüzden
    // NotifyArtworkDownloaded hemen ardından çağrılsa bile GetXPath boş dönüyordu (kullanıcı geri
    // bildirimi: "resim gelmiyor, kapatıp açınca geliyor"). DownloadArtworkAsync artık her başarılı
    // indirmeden sonra ilgili sözlüğü bu metotla güncelliyor, restart beklemeden anında görünüyor.
    private void RegisterDownloadedMedia(string type, string platformDisplayName, string baseFileName, string destination)
    {
        var byPlatform = type switch
        {
            "Box" => _boxByPlatform,
            "SS" => _screenshotByPlatform,
            "Logo" => _clearLogoByPlatform,
            _ => null,
        };
        if (byPlatform is null)
            return;

        if (!byPlatform.TryGetValue(platformDisplayName, out var files))
        {
            files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            byPlatform[platformDisplayName] = files;
        }
        files[baseFileName] = destination;
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
    }

    // Grid'deki "eksik ROM'u ara" sütunu ve context menüsündeki "Open File Location"ın paylaştığı
    // tek dosya-var-mı kontrolü. Önce kullanıcının "Şu anki yoldan kullan" ile kaydettiği bir
    // override var mı bakılır (bkz. FilePathOverrides); yoksa standart kural devreye girer:
    // AppPaths.Games\{PlatformDisplayName}\{File} var mı?
    public bool HasLocalFile(Game game)
    {
        if (_filePathOverrides.TryGetValue(game.GameKey, out var overridePath))
            return File.Exists(overridePath);

        if (string.IsNullOrWhiteSpace(game.File))
            return false;

        if (_filesByPlatform is not null)
            return _filesByPlatform.TryGetValue(game.PlatformDisplayName, out var files) && files.Contains(game.File);

        return File.Exists(GetLocalFilePath(game));
    }

    private string GetLocalFilePath(Game game) =>
        _filePathOverrides.TryGetValue(game.GameKey, out var overridePath)
            ? overridePath
            : Path.Combine(AppPaths.Games, game.PlatformDisplayName, game.File);

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

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{GetLocalFilePath(game)}\"") { UseShellExecute = true });
    }

    // Detay panelindeki platform logosu rozetine tıklayınca (bkz. MainWindow.xaml) o platformun
    // Games\{PlatformDisplayName} klasörünü açar — henüz hiç ROM içe aktarılmadıysa klasör
    // olmayabilir, bu durumda önce oluşturuluyor (Explorer boş bir klasörü de açabilir).
    [RelayCommand]
    private void OpenPlatformFolder(Game game)
    {
        var folder = Path.Combine(AppPaths.Games, game.PlatformDisplayName);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // Detay panelindeki Box art'a/Clear Logo'ya sağ tıklayınca çıkan küçük kapsül menüsündeki
    // "Klasöre Git" düğmeleri — gerçek görsel yoksa (yer tutucu gösteriliyorsa) hiçbir şey yapmaz.
    [RelayCommand]
    private void OpenBoxArtFolder(Game game)
    {
        if (!game.HasBox)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.BoxPath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenClearLogoFolder(Game game)
    {
        if (!game.HasClearLogo)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.ClearLogoPath}\"") { UseShellExecute = true });
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
    // code-behind'ında bir MessageBox ile alınır (bkz. context menü kablolaması).
    [RelayCommand]
    private void PermanentlyDeleteGame(Game game)
    {
        UserDataService.PermanentlyDelete(game.GameKey);
        _allGames.Remove(game);
        ApplyFilter();
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
        IsAddToPlaylistPopupOpen = false;
        IsVersionsPopupOpen = false;
        IsContextMenuOpen = true;
    }

    [RelayCommand]
    private void CloseContextMenu() => IsContextMenuOpen = false;

    // --- Toplu eylemler (bkz. IsBulkContextMenu) ---

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
    // (MessageBox onayı) bir istek gönderir. Onaylanırsa MainWindow.xaml.cs
    // PermanentlyDeleteGameCommand'ı kendisi çalıştırır.
    public event Action<Game>? RequestPermanentDeleteConfirmation;

    [RelayCommand]
    private void RequestPermanentDelete(Game game)
    {
        IsContextMenuOpen = false;
        RequestPermanentDeleteConfirmation?.Invoke(game);
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

        if (string.IsNullOrWhiteSpace(_appSettings.LaunchBoxDbPath) || !File.Exists(_appSettings.LaunchBoxDbPath))
        {
            RequestShowMessage?.Invoke("LaunchBox.Metadata.db yolu Ayarlar > Genel'de tanımlı değil ya da bulunamadı.");
            return;
        }

        using var reader = new LaunchBoxMetadataReader(_appSettings.LaunchBoxDbPath);
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
        game.NeedsReview = match.Confidence < LaunchBoxMetadataReader.FuzzyAcceptThreshold;
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
    // açılıp tüm döngü boyunca yeniden kullanılıyor (LaunchBoxMetadataReader kendi içinde fuzzy
    // eşleştirme kovalarını önbelleğe alıyor, bkz. _platformGameNameBuckets — bulk'ta reader'ı N
    // kere açıp kapatmak yerine bu, aynı platformdaki oyunlar için gerçek bir performans kazancı).
    [RelayCommand]
    private void BulkReMatchMetadata()
    {
        IsContextMenuOpen = false;

        if (string.IsNullOrWhiteSpace(_appSettings.LaunchBoxDbPath) || !File.Exists(_appSettings.LaunchBoxDbPath))
        {
            RequestShowMessage?.Invoke("Metadata veritabanı yolu Ayarlar > Genel'de tanımlı değil ya da bulunamadı.");
            return;
        }

        using var reader = new LaunchBoxMetadataReader(_appSettings.LaunchBoxDbPath);
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
        if (!game.HasArtworkSource)
        {
            RequestShowMessage?.Invoke("Bu oyun için eşleşmiş bir metadata kaydı yok, görsel aranamadı.");
            return;
        }

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
                RequestShowMessage?.Invoke("Eksik görseller için LaunchBox'ta kaynak bulunamadı — \"Ara\" ile deneyebilirsiniz.");
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
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action CompletedCallback)>? RequestSearchArtwork;

    [RelayCommand]
    private void SearchBoxArt(Game game) => SearchArtwork(game, "Box", "kapak resmi (box art)");

    [RelayCommand]
    private void SearchClearLogoArt(Game game) => SearchArtwork(game, "Logo", "clear logo (şeffaf png)");

    [RelayCommand]
    private void SearchScreenshotArt(Game game) => SearchArtwork(game, "SS", "oynanış ekran görüntüsü");

    private void SearchArtwork(Game game, string type, string mediaTypeLabel)
    {
        var query = $"{game.Title} {game.PlatformDisplayName} {mediaTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&tbm=isch";
        var targetFolder = Path.Combine(AppPaths.Images, game.PlatformDisplayName, type);
        var baseFileName = GetMediaBaseFileName(game);

        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, game.Title, mediaTypeLabel, () =>
        {
            NotifyArtworkDownloaded(game);
            ApplyFilter();
        }));
    }

    [RelayCommand]
    private async Task FetchArtwork(Game game)
    {
        IsContextMenuOpen = false;

        if (!game.HasArtworkSource)
        {
            RequestShowMessage?.Invoke("Bu oyun için eşleşmiş bir metadata kaydı yok, görsel aranamadı.");
            return;
        }

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

        var targets = ContextMenuSelection.Where(g => g.HasArtworkSource).ToList();
        if (targets.Count == 0)
        {
            RequestShowMessage?.Invoke("Seçilen oyunların hiçbirinde eşleşmiş bir metadata kaydı yok.");
            return;
        }

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
        var cancelled = false;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                _artworkDownloadCts.Token.ThrowIfCancellationRequested();
                var result = await DownloadArtworkAsync(targets[i], selectedTypes, _artworkDownloadCts.Token);
                totalDownloaded += result.Succeeded;
                totalAssets += result.Total;
                NotifyArtworkDownloaded(targets[i]);
                ArtworkDownloadProgress = (double)(i + 1) / targets.Count * 100;
            }
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
        var assets = CatalogDatabaseService.GetArtworkAssets(game.GameId)
            .Where(kv => selectedTypes.Contains(kv.Key))
            .ToList();
        if (assets.Count == 0)
            return (0, 0);

        var baseFileName = GetMediaBaseFileName(game);
        var succeeded = 0;
        var completed = 0;
        foreach (var (type, fileName) in assets)
        {
            // Sadece Logo şeffaflık gerektirir (PNG) — Box/BG/SS küçük/kayıplı JPEG'e çevriliyor
            // (bkz. ArtworkService, kullanıcı kararı: dosya boyutunu azalt).
            var preserveTransparency = type == "Logo";
            var destination = ArtworkService.BuildLocalPath(AppPaths.Images, game.PlatformDisplayName, type, baseFileName, preserveTransparency);
            if (await ArtworkService.DownloadAsync(fileName, destination, preserveTransparency, GetArtworkMaxDimensionPixels(), cancellationToken))
            {
                RegisterDownloadedMedia(type, game.PlatformDisplayName, baseFileName, destination);
                succeeded++;
            }
            completed++;
            onProgress?.Invoke(completed, assets.Count);
        }
        return (succeeded, assets.Count);
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

    // Normalde ROM dosya adı (uzantısız) kullanılır; ROM henüz yoksa başlıktan, geçersiz
    // dosya adı karakterleri temizlenerek türetilir.
    private static string GetMediaBaseFileName(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.File))
            return Path.GetFileNameWithoutExtension(game.File);

        var invalid = Path.GetInvalidFileNameChars();
        return new string(game.Title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
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
            foreach (var version in CatalogDatabaseService.GetVersions(SelectedGame.GameId, SelectedGame.GameKey))
            {
                version.IsOwned = IsVersionOwned(SelectedGame, version);
                SelectedGameVersions.Add(version);
            }
        }

        // Sürümler (Region) artık tek kart gösteriyor (kullanıcı isteği: "sürümlerde tek kart
        // gözükecek şekilde yapalım") — varsayılan olarak Preferred işaretli sürüm (yoksa ilki).
        SelectedVersionCard = SelectedGameVersions.FirstOrDefault(v => v.IsPreferred) ?? SelectedGameVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleVersionCards));
        OnPropertyChanged(nameof(OtherVersionCards));
        OnPropertyChanged(nameof(SingleVersionCard));
        IsVersionCardMenuOpen = false;
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
    // kopyalanabilir olsun".
    [RelayCommand]
    private void CopyText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
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
        if (_filePathOverrides.TryGetValue(game.GameKey, out var overridePath))
        {
            if (string.Equals(Path.GetExtension(overridePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (ZipContainsAnyHashFile(overridePath, version.Hashes))
                    return overridePath;
            }
            else if (version.Hashes.Any(h => string.Equals(h.FileName, Path.GetFileName(overridePath), StringComparison.OrdinalIgnoreCase)))
            {
                return overridePath;
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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowReleasedChanged(bool value) => ApplyFilter();

    partial void OnShowJunkChanged(bool value) => ApplyFilter();

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
                PlaylistChipKind.ReadyToPlay => _allGames.Where(g => g.HasLocalFile),
                PlaylistChipKind.NeedsSearch => _allGames.Where(g => !g.HasLocalFile),
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
            query = _allGames.Where(g => !g.IsHidden && !g.IsDeleted);

            if (SelectedPlatform is { IsAllPlatforms: false })
                query = query.Where(g => g.Platform == SelectedPlatform.Name);

            query = query.Where(g => (ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk"));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        query = query.Where(MatchesRegionFlags);

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
        query = ApplyColumnFilter(query, MatchedFilter, g => g.StatusOk ? "Eşleşti" : "Eşleşmedi");
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

        Games = new ObservableCollection<Game>(query);

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
    private IEnumerable<Game> GetTopRatedPopulation() => _allGames.Where(g => !g.IsHidden && !g.IsDeleted);

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

    // Her platformun rozetindeki sayıyı _allGames içinde o platforma ait kaç kayıt olduğuna göre
    // hesaplar ("All Platforms" toplam sayıyı gösterir). GetPlatforms()'taki sabit GameCount
    // değerleri sadece ilk yapım aşamasının kalıntısıydı; artık tek doğru kaynak gerçek oyun listesi.
    // BİLİNÇLİ TASARIM: bu sayı toolbar'daki USA/EU/Japan bayrak filtresinden VE Released/Junk
    // butonlarından etkilenmez — sol menü her zaman "bu platformda toplam kaç oyun var" sorusuna
    // cevap veren sabit bir referanstır. "Görünen" sayacı ise o an aktif olan TÜM filtrelerin
    // (region bayrağı + Released/Junk + arama + sütun filtreleri + playlist) sonucudur; ikisinin
    // bilerek ayrışması normaldir (rozet asla filtrelenmiş bir alt kümeyi göstermez).
    private void SyncPlatformGameCounts()
    {
        foreach (var platform in Platforms)
        {
            platform.GameCount = platform.IsAllPlatforms
                ? _allGames.Count
                : _allGames.Count(g => g.Platform == platform.Name);
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
            PlatformListItems.Move(oldIndex, newIndex);
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
    // oyunun TAM (virgülle ayrılmış) Genres string'ini tek bir seçenek olarak tutuyor; burada tek
    // bir token'a (ör. sadece "Shooter") göre birden fazla seçeneği aynı anda işaretleyebiliyoruz
    // çünkü o token'ı İÇEREN her kombinasyon aranıyor. "ALL" tüm seçenekleri işaretleyip filtreyi
    // sıfırlar (dropdown'daki "All Genres"). AÇMA/KAPAMA (toggle): aynı rozete İKİNCİ kez tıklanırsa
    // (filtre zaten TAM OLARAK bu token'a göre uygulanmışsa) filtre kaldırılır — kullanıcı geri
    // bildirimi: "tekrar tıklayınca gitmiyor" (önceki sürüm sadece uyguluyordu, hiç kaldırmıyordu).
    [RelayCommand]
    private void FilterByGenreToken(string token)
    {
        bool Matches(FilterOption option) =>
            token == "ALL" || option.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(token, StringComparer.OrdinalIgnoreCase);

        var alreadyApplied = token != "ALL" && GenresFilter.Options.All(o => o.IsChecked == Matches(o));

        foreach (var option in GenresFilter.Options)
            option.IsChecked = alreadyApplied || Matches(option);

        GenresFilter.ApplyFilterCommand.Execute(null);
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
    private void OpenCropEditor() => RequestOpenCropEditor?.Invoke();

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke();
}
