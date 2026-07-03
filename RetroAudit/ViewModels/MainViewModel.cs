using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Ana pencerenin ViewModel'i: toolbar, platform listesi, oyun DataGrid'i ve detay panelinin
// tüm durumu burada tutulur. Bu aşamada gerçek tarama/import mantığı yok — toolbar
// komutlarının çoğu (Import, Rescan, Refresh Media, ...) bilinçli olarak boş bırakıldı,
// ileride buraya gerçek işlevleri eklenecek.
public partial class MainViewModel : ObservableObject
{
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

    // Toolbar'daki "Tools" açılır menüsünün öğeleri; seçim, gerçek bir seçili durum değil
    // bir "eylem tetikleyici" gibi davranır (bkz. OnSelectedToolActionChanged).
    public ObservableCollection<string> ToolMenuItems { get; } = new() { "Tools", "Media Provider...", "Crop Editor...", "Ayarlar..." };

    // İkincil pencereleri açma isteğini View katmanına (MainWindow.xaml.cs) ileten olaylar.
    // ViewModel, Window tiplerine doğrudan bağımlı olmasın diye pencere açma işi View'da yapılır.
    public event Action? RequestOpenMediaProvider;
    public event Action? RequestOpenCropEditor;
    public event Action? RequestOpenSettings;

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

    // Stats bar'daki "Görünen / Toplam" metnini besleyen salt-okunur sayaçlar.
    public int TotalCount => _allGames.Count;
    public int VisibleCount => Games.Count;

    public MainViewModel()
    {
        _allGames = MockDataService.GetGames();
        Platforms = new ObservableCollection<Platform>(MockDataService.GetPlatforms());

        // Rozetlerdeki sayı mock/sabit bir değer değil, gerçek oyun listesinden hesaplanıyor —
        // böylece "0 oyun var ama rozet 1406 yazıyor" gibi senkronsuzluk hiç oluşmaz; RetroAudit.db
        // bağlandığında (Stage B) da bu hesap otomatik doğru sonuç verecek.
        SyncPlatformGameCounts();
        RebuildPlatformListItems();

        // Backing field'a doğrudan atama yapılıyor ki henüz Games/ApplyFilter hazır değilken
        // OnSelectedPlatformChanged tetiklenip erken/eksik bir filtreleme yapılmasın.
        selectedPlatform = Platforms.FirstOrDefault(p => p.Name == "Nintendo Entertainment System") ?? Platforms.First();

        ApplyFilter();

        // Referans tasarımdaki gibi açılışta "A Week of Garfield" seçili gelsin.
        selectedGame = Games.FirstOrDefault(g => g.Title == "A Week of Garfield") ?? Games.FirstOrDefault();
    }

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

    // Platform, arama metni ve Released/Junk anahtarlarına göre _allGames üzerinden
    // Games koleksiyonunu yeniden oluşturur. Basitlik için tüm listeyi temizleyip
    // yeniden dolduruyoruz; oyun sayısı mock veri boyutunda olduğu için performans sorun değil.
    private void ApplyFilter()
    {
        IEnumerable<Game> query = _allGames;

        if (SelectedPlatform is { IsAllPlatforms: false })
            query = query.Where(g => g.Platform == SelectedPlatform.Name);

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        query = query.Where(g => (ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk"));

        Games.Clear();
        foreach (var game in query)
            Games.Add(game);

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
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
    // Yeni bir platform eklemek ya da bir platformu "OTHERS"tan ana bir kategoriye taşımak,
    // sadece MockDataService'teki Platform.Category alanını değiştirmekle olur — bu metod
    // otomatik olarak doğru başlığın altına yerleştirir.
    private static readonly string[] CategoryOrder =
    {
        MockDataService.CategoryConsoles,
        MockDataService.CategoryHandhelds,
        MockDataService.CategoryArcade,
        MockDataService.CategoryComputers,
        MockDataService.CategoryClassic,
        MockDataService.CategoryOthers,
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

            var isOthers = category == MockDataService.CategoryOthers;
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
