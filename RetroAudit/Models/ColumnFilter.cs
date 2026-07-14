using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.Models;

// Filtre açılır listesindeki tek bir değer satırı (ör. "Atari - 2600 (540)").
public partial class FilterOption : ObservableObject
{
    public string Value { get; init; } = string.Empty;
    public int Count { get; init; }
    public int? HealthPercent { get; init; }
    public bool HasHealthPercent => HealthPercent.HasValue;

    [ObservableProperty]
    private bool isChecked = true;
}

// Bir DataGrid sütununun başlığındaki joystick ikonuna tıklanınca açılan filtre dropdown'ının
// durumu. DataGridColumn, görsel ağacın parçası olmadığı için normal ElementName/RelativeSource
// binding'leri çalışmaz; bunun yerine Column.Header'a doğrudan bu nesne atanır (bkz.
// MainWindow.xaml), HeaderTemplate'in DataContext'i otomatik olarak bu nesne olur.
public partial class ColumnFilterViewModel : ObservableObject
{
    public string HeaderText { get; init; } = string.Empty;
    public ObservableCollection<FilterOption> Options { get; }

    // Başlık/File gibi ~67 bin neredeyse hiç tekrarlamayan değere sahip sütunlarda checkbox
    // listesi kurmak (67 bin CheckBox+TextBlock oluşturup render etmek) popup'ı açarken UI'ı
    // donduruyordu. Bu sütunlarda Options hiç doldurulmuyor (bkz. MainViewModel.
    // BuildSearchOnlyColumnFilter); bunun yerine SearchText grid'i doğrudan (Contains) filtreliyor
    // — checkbox/Temizle/OK/Cancel hiç gösterilmiyor (bkz. MainWindow.xaml).
    public bool IsSearchOnly { get; init; }

    // Popup içindeki arama kutusuna bağlı — OptionsView'ı (aşağıda) anlık filtreler.
    [ObservableProperty]
    private string searchText = string.Empty;

    // ItemsControl bu view'a bağlanır (Options'a değil) ki arama kutusu listeyi daraltabilsin.
    public ICollectionView OptionsView { get; }

    [ObservableProperty]
    private bool isPopupOpen;

    // İkon rengini değiştirmek için: en az bir değer işaretsizse filtre "aktif" sayılır.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveValuesSummary))]
    private bool isActive;

    // Kullanıcı geri bildirimi: "shooter'a tıkladım sadece böyle yazdı [Shooter, Platform, Flight
    // Simulator +22] sadece shooter yazması lazım" — Options'tan (checked seçeneklerin İÇİNDEKİ
    // token'lardan, bkz. eski yorum) türetmek YANLIŞ: "Shooter" token'ına göre filtrelemek,
    // "Shooter, Platform" gibi Shooter'ı İÇEREN her kombinasyonu da işaretli hale getiriyor (bkz.
    // MainViewModel.FilterByGenreToken) — bu YANLIŞ değil (o oyunlar gerçekten "Shooter" içeriyor),
    // ama kullanıcının GERÇEKTEN TIKLADIĞI şey sadece "Shooter". Bu yüzden rozet artık Options'tan
    // türetilen bir tahmin DEĞİL, FilterByGenreToken'ın (ve benzerlerinin) SetActiveSummaryOverride
    // ile doğrudan yazdığı gerçek değeri gösteriyor; override yoksa (ör. sütun başlığındaki
    // checkbox popup'ından elle seçim) Options'tan türetilen genel özet devreye giriyor.
    private string? _activeSummaryOverride;

    public void SetActiveSummaryOverride(string? value)
    {
        _activeSummaryOverride = value;
        OnPropertyChanged(nameof(ActiveValuesSummary));
    }

    // Stats bar'daki aktif filtre rozeti (bkz. MainWindow.xaml, MainViewModel.AllColumnFilters) —
    // sadece sütun adını değil, GERÇEKTEN seçili olan değer(ler)i gösteriyor. IsSearchOnly (Title/
    // File) sütunlarda arama metninin kendisi; diğerlerinde (override yoksa) işaretli seçeneklerin
    // İÇİNDEKİ tekil token'ların (en fazla 3, fazlası "+N" ile) tekrarsız birleştirilmiş hali.
    public string ActiveValuesSummary
    {
        get
        {
            if (_activeSummaryOverride is not null)
                return _activeSummaryOverride;

            if (IsSearchOnly)
                return SearchText;

            var checkedOptions = Options.Where(o => o.IsChecked).ToList();
            if (checkedOptions.Count == 0 || checkedOptions.Count == Options.Count)
                return string.Empty;

            var distinctTokens = checkedOptions
                .SelectMany(o => o.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            const int maxShown = 3;
            var summary = string.Join(", ", distinctTokens.Take(maxShown));
            return distinctTokens.Count > maxShown ? $"{summary} +{distinctTokens.Count - maxShown}" : summary;
        }
    }

    // Popup her açıldığında, "Cancel" ile geri dönülebilsin diye o anki işaretli durum saklanır.
    private HashSet<string> _snapshotChecked = new();

    public event Action? FilterChanged;

    // Popup her açılmadan HEMEN önce (bkz. Open) tetiklenir — MainViewModel bunu dinleyip
    // Options'taki değer/sayıları GÜNCEL kapsama (seçili chip/platform/arama) göre yeniden
    // hesaplıyor. Önceden Options sadece uygulama açılışında, TÜM kütüphaneden bir kere
    // kuruluyordu; bir playlist/chip içindeyken (ör. "Top 25") filtre hâlâ tüm kütüphanenin
    // sayılarını gösteriyordu (kullanıcı geri bildirimi: "playliste göre vermiyor sayıları").
    public event Action? RequestRefreshOptions;

    public HashSet<string> SelectedValues => Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();

    // Alttaki düğmenin o anki etiketi/işlevi: tüm değerler işaretliyse "Temizle" (hepsini
    // kaldırır), değilse (hiçbiri ya da bir kısmı işaretliyse) "Hepsini Seç" (hepsini işaretler).
    // Kullanıcı kararıyla ayrı bir "hepsini seç" satırı yerine tek düğme iki işlevi de görüyor.
    public string ToggleAllLabel => Options.Count > 0 && Options.All(o => o.IsChecked) ? "Temizle" : "Hepsini Seç";

    // initialOptions toplu olarak (BuildColumnFilter'ın hazırladığı tam liste) Options'ın
    // ObservableCollection ctor'una veriliyor — tek tek Options.Add() ETMEK YERİNE. Sebep: Options
    // aşağıda OptionsView'a (bir Filter atanmış ICollectionView) bağlanıyor; Filter atanmış bir
    // CollectionView'a ObservableCollection.Add ile TEK TEK eklemek WPF'te her ekleme başına O(n)
    // maliyetli (view her Add'de mevcut öğeler arasında filtrelenmiş konumu yeniden hesaplıyor) —
    // Title/File gibi ~60 bin farklı değeri olan sütunlarda bu O(n²)'ye çıkıp açılışı ~15 saniye
    // yavaşlatıyordu (ölçüldü: Title addLoop=6.5s, File addLoop=8.8s, LINQ'ın kendisi <150ms).
    // ObservableCollection(IEnumerable) ctor'u ise CollectionChanged hiç tetiklemeden (henüz kimse
    // dinlemiyorken) tek seferde dolduruyor, bu yüzden Filter atanmadan önce bitmiş oluyor.
    public ColumnFilterViewModel(IEnumerable<FilterOption>? initialOptions = null)
    {
        Options = initialOptions is null ? new ObservableCollection<FilterOption>() : new ObservableCollection<FilterOption>(initialOptions);
        foreach (var option in Options)
            WireOption(option);

        OptionsView = CollectionViewSource.GetDefaultView(Options);
        OptionsView.Filter = FilterPredicate;
        Options.CollectionChanged += OnOptionsCollectionChanged;
    }

    // Options listesine (ilk toplu doldurmadan SONRA) yeni bir FilterOption eklenirse onun
    // IsChecked değişimini dinleyip ToggleAllLabel'ı güncel tutar.
    private void OnOptionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
            return;

        foreach (FilterOption option in e.NewItems)
            WireOption(option);
    }

    private void WireOption(FilterOption option) =>
        option.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FilterOption.IsChecked))
                OnPropertyChanged(nameof(ToggleAllLabel));
        };

    private bool FilterPredicate(object obj) =>
        string.IsNullOrWhiteSpace(SearchText) ||
        (obj is FilterOption option && option.Value.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    // Kullanıcı isteğiyle: arama artık her tuş vuruşunda DEĞİL, Enter'a basılınca uygulanıyor
    // (bkz. MainWindow.xaml TextBox.InputBindings). IsSearchOnly sütunlarda (Title/File) her
    // tuş vuruşunda tüm grid'i (66 bin oyun) yeniden filtreleyip Games koleksiyonunu baştan
    // doldurmak hızlı yazarken gözle görülür bir gecikme/donma hissi yaratıyordu; normal
    // sütunlarda da (checkbox listesini daraltan arama) aynı Enter-ile-ara davranışı için
    // tutarlılık adına uygulanıyor.
    [RelayCommand]
    private void Search()
    {
        if (IsSearchOnly)
        {
            IsActive = !string.IsNullOrWhiteSpace(SearchText);
            FilterChanged?.Invoke();
            return;
        }

        OptionsView.Refresh();
    }

    // Arama kutusunun içindeki "✕" düğmesi — Search'ün aksine Enter beklemeden hemen temizler
    // (kullanıcı isteği: önceki aramayı Backspace/Delete ile tek tek silmek yerine tek tıkla).
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        Search();
    }

    [RelayCommand]
    private void Open()
    {
        if (!IsSearchOnly)
            RequestRefreshOptions?.Invoke();
        _snapshotChecked = Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
        if (!IsSearchOnly)
            SearchText = string.Empty;
        IsPopupOpen = true;
    }

    [RelayCommand]
    private void ToggleAll()
    {
        var selectAll = ToggleAllLabel == "Hepsini Seç";
        foreach (var option in Options)
            option.IsChecked = selectAll;
    }

    // Kullanıcı bulgusu: "üst menüdeki badge'e tıklayıp kapattığımda hiçbişey göstermiyor" —
    // hem ApplyFilter hem RemoveValue eskiden IsActive'i "en az bir değer işaretsiz mi" diye
    // hesaplıyordu; bu, "hiçbir değer işaretli değil" durumunu da (ör. son kalan tek işaretli
    // değer RemoveValue ile kaldırıldığında) "aktif filtre" sayıp SelectedValues'u boş
    // HashSet'e çeviriyor, ApplyColumnFilter de bunu "hiçbir oyun eşleşmiyor" olarak
    // yorumluyordu (grid boşalıyordu). Genres rozetlerinin (bkz. MainViewModel.
    // FilterByGenreToken) ve ClearFilter'ın ZATEN yaptığı gibi: hiçbir değer işaretli
    // kalmadıysa bu "hiçbir şeyi gösterme" değil "filtre yok/hepsini göster" anlamına gelir —
    // hepsi otomatik yeniden işaretlenir.
    private void RecomputeIsActive()
    {
        if (!Options.Any(o => o.IsChecked))
        {
            foreach (var option in Options)
                option.IsChecked = true;
        }
        IsActive = Options.Any(o => !o.IsChecked);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        // FilterByGenreToken gibi bir çağıran, bu komutu çalıştırdıktan HEMEN sonra
        // SetActiveSummaryOverride ile kendi özetini yazabilir (bkz. o yorum) — burada override'ı
        // temizlemek, sütun başlığındaki checkbox popup'ından ELLE "Uygula"ya basıldığında eski/
        // yanlış bir override'ın (ör. önceki bir rozet tıklamasından kalma) rozette görünmeye
        // devam etmesini engelliyor.
        _activeSummaryOverride = null;
        RecomputeIsActive();
        IsPopupOpen = false;
        FilterChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelFilter()
    {
        foreach (var option in Options)
            option.IsChecked = _snapshotChecked.Contains(option.Value);
        IsPopupOpen = false;
    }

    // Kullanıcı isteği: "filtrelenenler ... o alanda gözüksün tıklanılabilir filtre kaldırılabilir
    // şekilde tıklandığında filtre kalkıcak" — stats bar'daki aktif filtre rozetinin (bkz.
    // MainViewModel.AllColumnFilters, MainWindow.xaml) tıklanınca çağırdığı komut: ToggleAll'daki
    // "Hepsini Seç" ile AYNI sonuç (tüm değerler tekrar işaretlenir) ama popup açmadan, ARama
    // kutulu sütunlarda (IsSearchOnly) SearchText'i temizler.
    [RelayCommand]
    private void ClearFilter()
    {
        _activeSummaryOverride = null;

        if (IsSearchOnly)
        {
            SearchText = string.Empty;
        }
        else
        {
            foreach (var option in Options)
                option.IsChecked = true;
        }

        IsActive = false;
        FilterChanged?.Invoke();
    }

    // Kullanıcı isteği: "ayrı ayrı badge olacak ... horror'u kaldırmak istediğimde tıklayıp
    // kaldırabilecem" — Genres DIŞINDAKİ filtreler için (bkz. MainViewModel.
    // RefreshActiveFilterChips, GenresFilter kendi token bazlı mantığını kullanıyor) TEK bir
    // seçili değeri işaretsiz bırakıp geri kalanına dokunmaz — ClearFilter'ın (hepsini sıfırlar)
    // aksine SADECE bu değeri kaldırır.
    [RelayCommand]
    private void RemoveValue(string value)
    {
        var option = Options.FirstOrDefault(o => o.Value == value);
        if (option is not null)
            option.IsChecked = false;

        RecomputeIsActive();
        FilterChanged?.Invoke();
    }

    // Kullanıcı isteği: "filtreleme menüsünüde badge gibi yap ... tıkladığım badge'i filtrelesin
    // ... normalde default olarak hepsini göstersin ben bi filtreye tıklarsam o tıkladığımı
    // gösterecek ... çoklu seçim gene yapılabilir aslında diğerleri silinmese" — checkbox'ların
    // yerini alan rozetlerin tıklama mantığı: BAŞLANGIÇTA (hiçbir şey elle daraltılmamışken) hepsi
    // işaretlidir ("filtre yok, hepsini göster"). Bu "el değmemiş" durumdan bir rozete tıklanırsa
    // (Türler rozetlerindeki İLK tıklama ile AYNI mantık) SADECE o değer işaretli kalır, geri
    // kalanı işaretsiz olur — "sadece tıkladığımı göster". Bu noktadan SONRAKİ tıklamalar (artık
    // "el değmemiş" olmadığı için) normal çoklu-seçim toggle'ı gibi davranır: o değeri diğerlerine
    // DOKUNMADAN işaretler/işaretsiz bırakır — kullanıcı isterse birden fazla değeri birlikte
    // seçebilir. Hiçbir değer işaretli kalmazsa (son işaretli de kaldırılırsa) "el değmemiş" hâle
    // (hepsi işaretli, filtre yok) otomatik geri dönülür.
    [RelayCommand]
    private void ToggleOption(FilterOption option)
    {
        var wasUntouched = Options.All(o => o.IsChecked);
        if (wasUntouched)
        {
            foreach (var o in Options)
                o.IsChecked = ReferenceEquals(o, option);
        }
        else
        {
            option.IsChecked = !option.IsChecked;
        }

        _activeSummaryOverride = null;
        RecomputeIsActive();
        // Kullanıcı isteği: "tıklayınca filtre yeri kapanmıyor onuda ayarlarmısın" — rozete
        // tıklamak artık OK'a basmış gibi hemen uygulayıp popup'ı da kapatıyor (Actions
        // popup'ındaki iki alt filtre için ActionsColumnFilterViewModel KENDİ IsPopupOpen'ını
        // bu FilterChanged sinyaline bakarak ayrıca kapatıyor, bkz. o yorum).
        IsPopupOpen = false;
        FilterChanged?.Invoke();
    }
}
