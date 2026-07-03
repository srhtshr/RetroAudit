using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: Game game } &&
            DataContext is MainViewModel vm)
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

    // "Sütunlar" seçicisindeki her satır bir DataGridColumn'a karşılık gelir (Key ile eşleşir).
    // DataGridColumn görsel ağacın parçası olmadığı için Visibility'si XAML'de doğrudan
    // bağlanamıyor — bu yüzden ColumnVisibilityOption.IsVisible değiştiğinde ilgili sütunu burada,
    // kod-arkasında güncelliyoruz.
    private void WireColumnVisibility(MainViewModel vm)
    {
        var columnsByKey = new Dictionary<string, DataGridColumn>
        {
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
                if (e.PropertyName == nameof(option.IsVisible))
                    column.Visibility = option.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            };
        }
    }
}
