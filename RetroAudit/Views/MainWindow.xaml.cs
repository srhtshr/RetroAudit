using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Pencere açma, bilinçli olarak ViewModel'in RelayCommand'ları yerine burada, View
        // katmanında yapılıyor: MainViewModel doğrudan Window tiplerine bağımlı olmasın diye
        // ViewModel sadece bir olay (event) tetikliyor, gerçek "new Window().Show()" çağrısı burada.
        if (DataContext is MainViewModel vm)
        {
            vm.RequestOpenMediaProvider += () => new MediaProviderWindow { Owner = this }.Show();
            vm.RequestOpenCropEditor += () => new CropEditorDialog { Owner = this }.ShowDialog();
            vm.RequestOpenSettings += () =>
            {
                var settingsWindow = new SettingsWindow { Owner = this };
                settingsWindow.Closed += (_, _) => vm.ReloadAppSettings();
                settingsWindow.Show();
            };
            vm.RequestPermanentDeleteConfirmation += game =>
            {
                var result = MessageBox.Show(
                    $"\"{game.Title}\" çöp kutusundan kalıcı olarak silinsin mi? Bu işlem geri alınamaz.",
                    "Kalıcı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    vm.PermanentlyDeleteGameCommand.Execute(game);
            };
            vm.RequestEditMetadata += game =>
            {
                var dialog = new EditMetadataWindow(game) { Owner = this };
                if (dialog.ShowDialog() == true)
                    vm.ApplyFilter(); // Title/Genre/... ObservableProperty değil, satırı tazelemek gerekiyor
            };
            vm.RequestShowMessage += message => MessageBox.Show(message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

            WireColumnVisibility(vm);
        }

        // Kapsül menü her açıldığında (Popup.Opened her IsOpen=true geçişinde tetiklenir, ilk
        // oluşturmada değil) küçük bir ölçek+opaklık animasyonu oynatır — kullanıcı isteği
        // "Animasyonlu açılsın/kapansın", sade tutmak için sadece açılışta (~120ms).
        ContextMenuPopup.Opened += (_, _) =>
        {
            if (ContextMenuBorder.RenderTransform is not ScaleTransform scale)
                return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(120);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1.0, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1.0, duration) { EasingFunction = ease });
            ContextMenuBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration));
        };
    }

    // DataGrid'in kendi seçim/sağ tık davranışı yok (WPF DataGrid sağ tıkta satırı otomatik
    // seçmez) — bu yüzden tıklanan noktadaki DataGridRow'u görsel ağaçta yukarı doğru arayıp
    // bulunca ViewModel'e "bu oyun için menü aç" deniyor (bkz. MainViewModel.OpenContextMenuFor).
    // Down değil UP'ta açılıyor: StaysOpen="False" bir Popup'ı onu açan tıklamanın KENDİSİ
    // içinde (Down anında) senkron açmak WPF'te bilinen bir sorun — Popup'ın "dışarı tıklandı,
    // kapat" mekanizması aynı tıklamayı dışarıda sayıp anında kapatıyor. Up'ta açmak bu yarış
    // durumunu ortadan kaldırıyor çünkü o an itibariyle tıklama jesti zaten tamamlanmış oluyor.
    private void GamesGrid_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // Herhangi bir sütun başlığına sağ tıklamak, ayrı bir "Sütunlar" düğmesine gerek
        // kalmadan sütun görünürlüğü seçicisini açar (bkz. kullanıcı isteği: "fazladan gereksiz
        // buton olmaz"). Satır kontrolünden ÖNCE kontrol edilmeli — başlık, satırların üstünde
        // ayrı bir öğe.
        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            vm.IsColumnPickerOpen = true;
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: Game game })
        {
            e.Handled = true;

            // Sağ tıklanan satır, Ctrl/Shift ile yapılmış mevcut bir çoklu seçimin parçasıysa
            // (WPF DataGrid varsayılan SelectionMode="Extended" — sağ tık seçimi bozmaz) toplu
            // menü açılır; aksi halde her zamanki gibi sadece o satır hedeflenir.
            var selectedGames = GamesGrid.SelectedItems.Cast<Game>().ToList();
            if (selectedGames.Count > 1 && selectedGames.Contains(game))
                vm.OpenBulkContextMenuFor(selectedGames);
            else
                vm.OpenContextMenuFor(game);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null && element is not T)
            element = VisualTreeHelper.GetParent(element);
        return element as T;
    }

    private static T? FindVisualChild<T>(DependencyObject? element) where T : DependencyObject
    {
        if (element is null)
            return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match)
                return match;

            if (FindVisualChild<T>(child) is T found)
                return found;
        }

        return null;
    }

    // Sütun filtresi popup'ı açıldığında arama kutusuna otomatik odaklanır ve içindeki (varsa
    // önceki) metni seçili getirir — önceden kullanıcı popup'ı açtıktan sonra kutuya ayrıca
    // tıklaması gerekiyordu.
    private void FilterPopup_Opened(object sender, EventArgs e)
    {
        if (sender is not Popup { Child: DependencyObject child } || FindVisualChild<TextBox>(child) is not { } textBox)
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    // Sıralama artık DataGridColumnHeader'a tıklamakla değil (o artık filtre dropdown'ını açıyor —
    // bkz. FilterableColumnHeader, CanUserSortColumns="False"), bu dropdown'ın en üstündeki
    // "Sırala A-Z"/"Sırala Z-A" düğmeleriyle yapılıyor. Düğme, filtre şablonunun içinde olduğu için
    // görsel ağaçta yukarı çıkıp hangi DataGridColumnHeader'a (dolayısıyla hangi DataGridColumn'a)
    // ait olduğunu buluyoruz — WPF'in kendi otomatik sıralamasının kullandığı SortMemberPath +
    // ICollectionView.SortDescriptions mekanizmasının aynısı, sadece manuel tetikleniyor.
    private void SortAscending_Click(object sender, RoutedEventArgs e) => ApplyHeaderSort(sender, ListSortDirection.Ascending);

    private void SortDescending_Click(object sender, RoutedEventArgs e) => ApplyHeaderSort(sender, ListSortDirection.Descending);

    private void ApplyHeaderSort(object sender, ListSortDirection direction)
    {
        if (sender is not DependencyObject element)
            return;

        if (FindVisualParent<DataGridColumnHeader>(element)?.Column is not { } column ||
            string.IsNullOrEmpty(column.SortMemberPath))
            return;

        var view = CollectionViewSource.GetDefaultView(GamesGrid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));

        foreach (var col in GamesGrid.Columns)
            col.SortDirection = null;
        column.SortDirection = direction;

        if (element is FrameworkElement { DataContext: ColumnFilterViewModel filterVm })
            filterVm.IsPopupOpen = false;
    }

    // "Sütunlar" seçicisindeki her satır bir DataGridColumn'a karşılık gelir (Key ile eşleşir).
    // DataGridColumn görsel ağacın parçası olmadığı için Visibility'si XAML'de doğrudan
    // bağlanamıyor — bu yüzden ColumnVisibilityOption.IsVisible değiştiğinde ilgili sütunu burada,
    // kod-arkasında güncelliyoruz.
    private void WireColumnVisibility(MainViewModel vm)
    {
        var columnsByKey = new Dictionary<string, DataGridColumn>
        {
            ["Matched"] = MatchedColumn,
            ["Logo"] = LogoColumn,
            ["Favorite"] = FavoriteColumn,
            ["Search"] = SearchColumn,
            ["Title"] = TitleColumn,
            ["Box"] = BoxColumn,
            ["Background"] = BackgroundColumn,
            ["Screenshot"] = ScreenshotColumn,
            ["File"] = FileColumn,
            ["Platform"] = PlatformColumn,
            ["Status"] = StatusColumn,
            ["Genres"] = GenresColumn,
            ["Developer"] = DeveloperColumn,
            ["Publisher"] = PublisherColumn,
            ["ReleaseYear"] = ReleaseYearColumn,
            ["MaxPlayers"] = MaxPlayersColumn,
            ["Region"] = RegionColumn,
            ["Source"] = SourceColumn,
            ["MatchMethod"] = MatchMethodColumn,
        };

        foreach (var option in vm.ColumnOptions)
        {
            if (!columnsByKey.TryGetValue(option.Key, out var column))
                continue;

            column.Visibility = option.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(option.IsVisible))
                    return;

                column.Visibility = option.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                vm.SaveColumnVisibility();
            };
        }
    }
}
