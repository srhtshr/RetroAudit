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
    private bool isActive;

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

    [RelayCommand]
    private void ApplyFilter()
    {
        IsActive = Options.Any(o => !o.IsChecked);
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
}
