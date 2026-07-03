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

    // Tam platform listesi (mock veri). Sol panelde doğrudan bu değil, IsVisible'a göre
    // süzülmüş VisiblePlatforms gösterilir.
    public ObservableCollection<Platform> Platforms { get; }

    // Sol paneldeki ListBox'a bağlanan, kullanıcının "+" ile açtığı çoklu seçim listesinden
    // seçtiği platformlarla sınırlı görünen liste (bkz. RefreshVisiblePlatforms).
    public ObservableCollection<Platform> VisiblePlatforms { get; } = new();

    // "+" popup'ında gösterilecek seçilebilir platformlar. "All Platforms" özel bir satır
    // olduğu için (her zaman görünür kalması gerektiğinden) bu listeye dahil edilmiyor.
    public IEnumerable<Platform> SelectablePlatforms => Platforms.Where(p => !p.IsAllPlatforms);

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

    // "Platforms" başlığının yanındaki "+" butonuyla açılıp kapanan çoklu seçim popup'ının durumu.
    [ObservableProperty]
    private bool isPlatformPickerOpen;

    // Stats bar'daki "Görünen / Toplam" metnini besleyen salt-okunur sayaçlar.
    public int TotalCount => _allGames.Count;
    public int VisibleCount => Games.Count;

    public MainViewModel()
    {
        _allGames = MockDataService.GetGames();
        Platforms = new ObservableCollection<Platform>(MockDataService.GetPlatforms());

        RefreshVisiblePlatforms();

        // Backing field'a doğrudan atama yapılıyor ki henüz Games/ApplyFilter hazır değilken
        // OnSelectedPlatformChanged tetiklenip erken/eksik bir filtreleme yapılmasın.
        selectedPlatform = Platforms.FirstOrDefault(p => p.Name == "Nintendo") ?? Platforms.First();

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

    // Platforms (tam liste) içinden IsVisible=true olanları (ve her zaman "All Platforms" satırını)
    // VisiblePlatforms'a kopyalar. Sol paneldeki ListBox bu koleksiyona bağlıdır.
    // Sıralama: All Platforms en üstte, ardından favoriler, en altta favori olmayan görünür platformlar
    // — kullanıcının en sık kullandığı platformlar listenin başında kalsın diye.
    private void RefreshVisiblePlatforms()
    {
        VisiblePlatforms.Clear();
        var ordered = Platforms
            .Where(p => p.IsAllPlatforms || p.IsVisible)
            .OrderByDescending(p => p.IsAllPlatforms)
            .ThenByDescending(p => p.IsFavorite);

        foreach (var platform in ordered)
            VisiblePlatforms.Add(platform);

        // Seçili platform popup'tan kapatılmışsa (artık VisiblePlatforms'ta yoksa), seçimi
        // "All Platforms"a düşürerek DataGrid'in boş bir seçime bağlı kalmasını önlüyoruz.
        if (SelectedPlatform is not null && !VisiblePlatforms.Contains(SelectedPlatform))
            SelectedPlatform = VisiblePlatforms.FirstOrDefault(p => p.IsAllPlatforms) ?? VisiblePlatforms.FirstOrDefault();
    }

    // "+" butonuna basılınca popup'ı açar/kapatır.
    [RelayCommand]
    private void TogglePlatformPicker() => IsPlatformPickerOpen = !IsPlatformPickerOpen;

    // Popup içindeki checkbox'larla değiştirilen IsVisible değerlerini sol panele uygular ve popup'ı kapatır.
    [RelayCommand]
    private void ApplyPlatformVisibility()
    {
        RefreshVisiblePlatforms();
        IsPlatformPickerOpen = false;
    }

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
