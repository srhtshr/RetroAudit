using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Catalog.Grouping;
using RetroAudit.Catalog.Metadata;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Ana pencerenin ViewModel'i: toolbar, platform listesi, oyun DataGrid'i ve detay panelinin
// tüm durumu burada tutulur. Bu aşamada gerçek tarama/import mantığı yok — toolbar
// komutlarının çoğu (Import, Rescan, Refresh Media, ...) bilinçli olarak boş bırakıldı,
// ileride buraya gerçek işlevleri eklenecek.
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

    // DataGrid'e bağlanan, filtreleme sonrası görünen oyun listesi.
    public ObservableCollection<Game> Games { get; } = new();

    // Sağ paneldeki Versions listesi: SelectedGame değiştiğinde CatalogDatabaseService.GetVersions
    // ile talep üzerine doldurulur (67 bin oyunun tamamının sürüm verisini baştan yüklemek yerine).
    public ObservableCollection<GameVersion> SelectedGameVersions { get; } = new();

    // Toolbar'daki "Tools" açılır menüsünün öğeleri; seçim, gerçek bir seçili durum değil
    // bir "eylem tetikleyici" gibi davranır (bkz. OnSelectedToolActionChanged).
    public ObservableCollection<string> ToolMenuItems { get; } = new() { "Tools", "Media Provider...", "Crop Editor...", "Ayarlar..." };

    // İkincil pencereleri açma isteğini View katmanına (MainWindow.xaml.cs) ileten olaylar.
    // ViewModel, Window tiplerine doğrudan bağımlı olmasın diye pencere açma işi View'da yapılır.
    public event Action? RequestOpenMediaProvider;
    public event Action? RequestOpenCropEditor;
    public event Action? RequestOpenSettings;
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
    private Game? selectedGame;

    // Arama kutusu metni; her değişiklikte oyun listesi yeniden filtrelenir.
    [ObservableProperty]
    private string searchText = string.Empty;

    // "Released" / "Junk" filtre butonları — ikisi de kapalıysa liste boş görünür (bilinçli tercih,
    // kullanıcının filtre durumunu açıkça görmesi için).
    [ObservableProperty]
    private bool showReleased = true;

    [ObservableProperty]
    private bool showJunk;

    // "Satır:" kaydırıcısına bağlı; DataGrid.RowHeight ile iki yönlü (TwoWay) bağlıdır.
    [ObservableProperty]
    private double rowHeight = 30;

    // "OTHERS" kategorisinin açık/kapalı durumu. Varsayılan kapalı: kullanıcı ilk açılışta sadece
    // popüler kategorileri (CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC) görür.
    [ObservableProperty]
    private bool isOthersExpanded;

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

    // DataGrid sütun başlıklarındaki joystick ikonuyla açılan filtre dropdown'ları. Her biri
    // _allGames üzerinden (tüm veri setinden, aktif filtrelerden bağımsız) bir kere hesaplanan
    // sabit bir değer+sayı listesi taşır — Excel'deki gibi filtre değiştikçe diğer sütunların
    // sayılarını yeniden hesaplamıyoruz (kapsamı sade tutmak için bilinçli bir basitleştirme).
    public ColumnFilterViewModel PlatformFilter { get; }
    public ColumnFilterViewModel StatusFilter { get; }
    public ColumnFilterViewModel GenresFilter { get; }
    public ColumnFilterViewModel DeveloperFilter { get; }
    public ColumnFilterViewModel PublisherFilter { get; }
    public ColumnFilterViewModel RegionFilter { get; }
    public ColumnFilterViewModel SourceFilter { get; }
    public ColumnFilterViewModel MatchMethodFilter { get; }
    public ColumnFilterViewModel ReleaseYearFilter { get; }
    public ColumnFilterViewModel MaxPlayersFilter { get; }
    public ColumnFilterViewModel TitleFilter { get; }
    public ColumnFilterViewModel FileFilter { get; }
    public ColumnFilterViewModel MatchedFilter { get; }
    public ColumnFilterViewModel FavoriteFilter { get; }
    public ColumnFilterViewModel HasLocalFileFilter { get; }
    public ColumnFilterViewModel BoxFilter { get; }
    public ColumnFilterViewModel BackgroundFilter { get; }
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

        _allGames = CatalogDatabaseService.GetGames();
        BuildLocalFileIndex();
        foreach (var game in _allGames)
            game.HasLocalFile = HasLocalFile(game);

        Platforms = new ObservableCollection<Platform>(CatalogDatabaseService.GetPlatforms());

        // Rozetlerdeki sayı sabit bir değer değil, gerçek oyun listesinden hesaplanıyor —
        // böylece "0 oyun var ama rozet 1406 yazıyor" gibi senkronsuzluk hiç oluşmaz.
        SyncPlatformGameCounts();
        RebuildPlatformListItems();

        PlatformFilter = BuildColumnFilter("Platform", _allGames.Select(g => g.PlatformDisplayName));
        StatusFilter = BuildColumnFilter("Sürüm", _allGames.Select(g => g.Version));
        GenresFilter = BuildColumnFilter("Türler", _allGames.Select(g => g.Genres));
        DeveloperFilter = BuildColumnFilter("Geliştirici", _allGames.Select(g => g.Developer));
        PublisherFilter = BuildColumnFilter("Yayıncı", _allGames.Select(g => g.Publisher));
        RegionFilter = BuildColumnFilter("Bölge", _allGames.Select(g => g.Region));
        SourceFilter = BuildColumnFilter("Kaynak", _allGames.Select(g => g.SourceDat));
        MatchMethodFilter = BuildColumnFilter("Eşleşme Yöntemi", _allGames.Select(g => g.MatchMethod));
        ReleaseYearFilter = BuildColumnFilter("Yıl", _allGames.Select(g => g.ReleaseYear == 0 ? string.Empty : g.ReleaseYear.ToString()));
        MaxPlayersFilter = BuildColumnFilter("Maks. Oyuncu", _allGames.Select(g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString()));
        // Title/File: ~67 bin neredeyse hiç tekrarlamayan değer var — bunlar için tam checkbox
        // listesi kurmak (BuildColumnFilter) hem gereksiz (kimse 67 bin satırlık bir onay
        // listesinde gezinmez) hem de popup'ı açarken donmaya yol açıyordu. Sadece arama kutusu.
        TitleFilter = BuildSearchOnlyColumnFilter("Başlık");
        FileFilter = BuildSearchOnlyColumnFilter("File");
        MatchedFilter = BuildColumnFilter("Durum", _allGames.Select(g => g.StatusOk ? "Eşleşti" : "Eşleşmedi"));
        FavoriteFilter = BuildColumnFilter("Favori", _allGames.Select(g => g.IsFavorite ? "Evet" : "Hayır"));
        HasLocalFileFilter = BuildColumnFilter("Dosya", _allGames.Select(g => g.HasLocalFile ? "Var" : "Yok"));
        BoxFilter = BuildColumnFilter("Box", _allGames.Select(g => g.HasBox ? "Evet" : "Hayır"));
        BackgroundFilter = BuildColumnFilter("BG", _allGames.Select(g => g.HasBackground ? "Evet" : "Hayır"));
        ScreenshotFilter = BuildColumnFilter("SS", _allGames.Select(g => g.HasScreenshot ? "Evet" : "Hayır"));

        var allColumnFilters = new[]
        {
            PlatformFilter, StatusFilter, GenresFilter, DeveloperFilter, PublisherFilter,
            RegionFilter, SourceFilter, MatchMethodFilter, ReleaseYearFilter, MaxPlayersFilter,
            TitleFilter, FileFilter, MatchedFilter, FavoriteFilter, HasLocalFileFilter,
            BoxFilter, BackgroundFilter, ScreenshotFilter,
        };
        foreach (var filter in allColumnFilters)
            filter.FilterChanged += ApplyFilter;

        RebuildPlaylistChips();

        // Backing field'a doğrudan atama yapılıyor ki henüz Games/ApplyFilter hazır değilken
        // OnSelectedPlatformChanged tetiklenip erken/eksik bir filtreleme yapılmasın.
        selectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms) ?? Platforms.First();
        contextMenuDisplayMode = _appSettings.ContextMenuDisplayMode;

        ApplyFilter();

        selectedGame = Games.FirstOrDefault();
        LoadSelectedGameVersions();
    }

    // Ayarlar penceresi kapanınca MainWindow.xaml.cs bunu çağırır — ContextMenuDisplayMode gibi
    // canlı-yansıması-gereken tercihler disk'ten yeniden okunup güncellenir (bkz. plan: Ayarlar
    // ayrı bir pencere/ViewModel olduğu için değişiklik doğrudan burayı etkilemiyor).
    public void ReloadAppSettings()
    {
        _appSettings = ConfigService.LoadDefault();
        ContextMenuDisplayMode = _appSettings.ContextMenuDisplayMode;

        BuildLocalFileIndex();
        foreach (var game in _allGames)
            game.HasLocalFile = HasLocalFile(game);
        ApplyFilter();
    }

    // Sütun anahtarı -> varsayılan (kayıtlı tercih yoksa kullanılacak) görünürlük ve başlık.
    // Sıra, DataGrid'deki sütun sırasıyla aynı — sadece okunabilirlik için, işlevsel bir önemi yok.
    private static readonly (string Key, string Header, bool DefaultVisible)[] ColumnDefinitions =
    {
        ("Matched", "Durum", true),
        ("Logo", "Logo", true),
        ("Favorite", "Favori", true),
        ("Search", "Ara (Eksik ROM)", true),
        ("Title", "Başlık", true),
        ("Box", "Box", true),
        ("Background", "BG", true),
        ("Screenshot", "SS", true),
        ("File", "File", true),
        ("Platform", "Platform", true),
        ("Status", "Sürüm", true),
        ("Genres", "Türler", true),
        ("Developer", "Geliştirici", false),
        ("Publisher", "Yayıncı", false),
        ("ReleaseYear", "Yıl", false),
        ("MaxPlayers", "Maks. Oyuncu", false),
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
        else if (value == "Ayarlar...")
            OpenSettings();

        if (value != "Tools")
            SelectedToolAction = "Tools";
    }

    partial void OnSelectedPlatformChanged(Platform? value) => ApplyFilter();

    partial void OnSelectedChipChanged(PlaylistChip? value)
    {
        foreach (var chip in PlaylistChips)
            chip.IsSelected = chip == value;

        _selectedChipMembership = value is { Kind: PlaylistChipKind.Playlist, PlaylistId: int id }
            ? UserDataService.GetPlaylistGameKeys(id)
            : new HashSet<string>();
        ApplyFilter();
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
        IsContextMenuOpen = false;
        IsContextMenuOverflowOpen = false;
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
        IsContextMenuOpen = false;
        IsContextMenuOverflowOpen = false;
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

    private void BuildLocalFileIndex()
    {
        _filesByPlatform = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_appSettings.RetroAuditDataPath) || !Directory.Exists(_appSettings.RetroAuditDataPath))
            return;

        foreach (var platformDir in Directory.EnumerateDirectories(_appSettings.RetroAuditDataPath))
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(platformDir))
                files.Add(Path.GetFileName(file));
            _filesByPlatform[Path.GetFileName(platformDir)] = files;
        }
    }

    // Grid'deki "eksik ROM'u ara" sütunu ve context menüsündeki "Open File Location"ın paylaştığı
    // tek dosya-var-mı kontrolü — henüz gerçek bir ROM tarama/kütüphane özelliği yok, bu yüzden
    // basit bir kural kullanılıyor: AppSettings.RetroAuditDataPath\{Platform}\{File} var mı?
    // Ayarlar > Genel'de veri kök dizini boşsa (ilk çalıştırma) her zaman false döner.
    public bool HasLocalFile(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.File))
            return false;

        if (_filesByPlatform is not null)
            return _filesByPlatform.TryGetValue(game.Platform, out var files) && files.Contains(game.File);

        return !string.IsNullOrWhiteSpace(_appSettings.RetroAuditDataPath) && File.Exists(GetLocalFilePath(game));
    }

    private string GetLocalFilePath(Game game) => Path.Combine(_appSettings.RetroAuditDataPath, game.Platform, game.File);

    [RelayCommand]
    private void SearchWeb(Game game)
    {
        var region = string.IsNullOrWhiteSpace(game.Region) || game.Region == "Unknown" ? "USA" : game.Region;
        var query = $"{game.Title} ({region}) {game.Platform} rom";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFileLocation(Game game)
    {
        if (!HasLocalFile(game))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{GetLocalFilePath(game)}\"") { UseShellExecute = true });
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
    private bool isContextMenuOverflowOpen;

    [ObservableProperty]
    private bool isAddToPlaylistPopupOpen;

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
        IsContextMenuOverflowOpen = false;
        IsAddToPlaylistPopupOpen = false;
        IsContextMenuOpen = true;
    }

    public void OpenBulkContextMenuFor(IReadOnlyList<Game> games)
    {
        IsBulkContextMenu = true;
        ContextMenuGame = null;
        ContextMenuSelection.Clear();
        foreach (var game in games)
            ContextMenuSelection.Add(game);
        IsContextMenuOverflowOpen = false;
        IsAddToPlaylistPopupOpen = false;
        IsContextMenuOpen = true;
    }

    [RelayCommand]
    private void CloseContextMenu() => IsContextMenuOpen = false;

    [RelayCommand]
    private void ToggleContextMenuOverflow() => IsContextMenuOverflowOpen = !IsContextMenuOverflowOpen;

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

    // "Versions" menü öğesi ayrı bir pencere açmıyor — sağ paneldeki mevcut "Sürümler (Region)"
    // listesi (bkz. Stage B) zaten bu veriyi gösteriyor; burada sadece panel kapalıysa açılıyor.
    [RelayCommand]
    private void FocusVersions()
    {
        IsDetailPanelExpanded = true;
        IsContextMenuOpen = false;
    }

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

        game.Developer = match.Developer ?? game.Developer;
        game.Publisher = match.Publisher ?? game.Publisher;
        game.ReleaseYear = match.ReleaseYear ?? game.ReleaseYear;
        game.Description = match.Overview ?? game.Description;
        game.MaxPlayers = match.MaxPlayers ?? game.MaxPlayers;
        if (match.Genres.Length > 0)
            game.Genres = string.Join(", ", match.Genres);
        game.MatchMethod = match.MatchMethod;
        game.NeedsReview = match.Confidence < LaunchBoxMetadataReader.FuzzyAcceptThreshold;

        ApplyFilter();
    }

    partial void OnSelectedGameChanged(Game? value) => LoadSelectedGameVersions();

    // Sağ paneldeki Versions listesini seçili oyuna göre yeniden doldurur.
    private void LoadSelectedGameVersions()
    {
        SelectedGameVersions.Clear();
        if (SelectedGame is null)
            return;

        foreach (var version in CatalogDatabaseService.GetVersions(SelectedGame.GameId, SelectedGame.GameKey))
            SelectedGameVersions.Add(version);
    }

    // Sağ panelin Versions listesindeki "Preferred yap" düğmesi — RetroAuditUserData.db'ye
    // yazılır (bkz. UserDataService.SavePreferredVersionOverride), Builder'ın varsayılan
    // USA>Europe>World>Japan seçiminin üzerine geçer ve rebuild'ler arasında kalıcıdır.
    [RelayCommand]
    private void SetPreferredVersion(GameVersion version)
    {
        if (SelectedGame is null)
            return;

        UserDataService.SavePreferredVersionOverride(SelectedGame.GameKey, version.RawDatName);
        LoadSelectedGameVersions();
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

    // Platform, arama metni, Released/Junk anahtarları ve sütun başlıklarındaki joystick
    // filtrelerine göre _allGames üzerinden Games koleksiyonunu yeniden oluşturur. Basitlik için
    // tüm listeyi temizleyip yeniden dolduruyoruz; 67 bin oyun için bu hâlâ anlık.
    //
    // Bir chip seçiliyse (Favorites/kullanıcı playlist'i/Hidden/Recycle Bin) bu, normal
    // Platform+Arama+Released-Junk+sütun-filtre hattının YERİNE geçer — chip'ler "ayrı bir görünüm"
    // (bkz. plan), üst üste bindirilmiyor. Normal görünümde ise gizli/çöp kutusundaki oyunlar
    // hiç gösterilmez (onları görmek için ilgili chip'e tıklanır).
    //
    // public: Edit Metadata penceresi kapandıktan sonra MainWindow.xaml.cs bunu çağırıp
    // DataGrid'in güncellenen değerleri (Title/Genre/... ObservableProperty olmadığı için)
    // göstermesini sağlıyor.
    public void ApplyFilter()
    {
        IEnumerable<Game> query;

        if (SelectedChip is not null)
        {
            query = SelectedChip.Kind switch
            {
                PlaylistChipKind.Hidden => _allGames.Where(g => g.IsHidden),
                PlaylistChipKind.RecycleBin => _allGames.Where(g => g.IsDeleted),
                _ => _allGames.Where(g => _selectedChipMembership.Contains(g.GameKey)),
            };
        }
        else
        {
            query = _allGames.Where(g => !g.IsHidden && !g.IsDeleted);

            if (SelectedPlatform is { IsAllPlatforms: false })
                query = query.Where(g => g.Platform == SelectedPlatform.Name);

            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(g => g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            query = query.Where(g => (ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk"));

            query = ApplyColumnFilter(query, PlatformFilter, g => g.PlatformDisplayName);
            query = ApplyColumnFilter(query, StatusFilter, g => g.Version);
            query = ApplyColumnFilter(query, GenresFilter, g => g.Genres);
            query = ApplyColumnFilter(query, DeveloperFilter, g => g.Developer);
            query = ApplyColumnFilter(query, PublisherFilter, g => g.Publisher);
            query = ApplyColumnFilter(query, RegionFilter, g => g.Region);
            query = ApplyColumnFilter(query, SourceFilter, g => g.SourceDat);
            query = ApplyColumnFilter(query, MatchMethodFilter, g => g.MatchMethod);
            query = ApplyColumnFilter(query, ReleaseYearFilter, g => g.ReleaseYear == 0 ? string.Empty : g.ReleaseYear.ToString());
            query = ApplyColumnFilter(query, MaxPlayersFilter, g => g.MaxPlayers == 0 ? string.Empty : g.MaxPlayers.ToString());
            query = ApplyColumnFilter(query, TitleFilter, g => g.Title);
            query = ApplyColumnFilter(query, FileFilter, g => g.File);
            query = ApplyColumnFilter(query, MatchedFilter, g => g.StatusOk ? "Eşleşti" : "Eşleşmedi");
            query = ApplyColumnFilter(query, FavoriteFilter, g => g.IsFavorite ? "Evet" : "Hayır");
            query = ApplyColumnFilter(query, HasLocalFileFilter, g => g.HasLocalFile ? "Var" : "Yok");
            query = ApplyColumnFilter(query, BoxFilter, g => g.HasBox ? "Evet" : "Hayır");
            query = ApplyColumnFilter(query, BackgroundFilter, g => g.HasBackground ? "Evet" : "Hayır");
            query = ApplyColumnFilter(query, ScreenshotFilter, g => g.HasScreenshot ? "Evet" : "Hayır");
        }

        Games.Clear();
        foreach (var game in query)
            Games.Add(game);

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
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

        foreach (var category in CategoryOrder)
        {
            var platformsInCategory = Platforms.Where(p => p.Category == category).ToList();
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

    // "OTHERS" başlığına tıklanınca açar/kapatır.
    [RelayCommand]
    private void ToggleOthers() => IsOthersExpanded = !IsOthersExpanded;

    // --- Toolbar komutları ---
    // Aşağıdaki komutların çoğu bu aşamada bilinçli olarak boş (no-op): gerçek dosya taraması,
    // metadata indirme vb. mantık henüz yazılmadı. Buton/binding altyapısı hazır olduğu için
    // ileride sadece metod gövdelerinin doldurulması yeterli olacak.

    [RelayCommand]
    private void Import() { }

    [RelayCommand]
    private void Rescan() { }

    // "Temizle" butonu: arama metnini, platform seçimini ve filtre anahtarlarını varsayılana döndürür.
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);
        ShowReleased = true;
        ShowJunk = false;
    }

    [RelayCommand]
    private void RefreshMedia() { }

    [RelayCommand]
    private void MetadataRefresh() { }

    [RelayCommand]
    private void MoveToLibrary() { }

    [RelayCommand]
    private void ApplyResolver() { }

    // Sağ paneldeki BAŞLAT butonu — gerçek emülatör başlatma mantığı Settings'teki
    // EmulatorConfig kayıtları kullanılarak ileride buraya eklenecek.
    [RelayCommand]
    private void Launch() { }

    [RelayCommand]
    private void OpenMediaProvider() => RequestOpenMediaProvider?.Invoke();

    [RelayCommand]
    private void OpenCropEditor() => RequestOpenCropEditor?.Invoke();

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke();
}
