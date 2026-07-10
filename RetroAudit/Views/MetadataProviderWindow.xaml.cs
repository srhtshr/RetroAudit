using System.Windows;
using System.Windows.Input;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class MetadataProviderWindow : Window
{
    private readonly MainViewModel _mainVm;

    public MetadataProviderWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _mainVm = mainVm;

        var vm = new MetadataProviderViewModel(
            mainVm.AllGames,
            game => mainVm.NotifyMetadataEdited(game),
            () => mainVm.GetVisiblePlatformOrder(),
            mainVm.ProviderDesignMode == ProviderDesignMode.Modern);
        Action<Game> metadataChangedHandler = _ => vm.RefreshAll();

        vm.RequestShowMessage += message =>
            MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

        mainVm.PlatformListOrderChanged += vm.RefreshPlatformOrder;
        Closed += (_, _) => mainVm.PlatformListOrderChanged -= vm.RefreshPlatformOrder;
        mainVm.GameMetadataChanged += metadataChangedHandler;
        Closed += (_, _) => mainVm.GameMetadataChanged -= metadataChangedHandler;

        // Pencere kapatıldığında Owner (MainWindow) odağının kaybolmasını (minimize gibi görünmesini) engelle
        Closed += (s, e) =>
        {
            if (Owner != null)
            {
                if (Owner.WindowState == WindowState.Minimized)
                    Owner.WindowState = WindowState.Normal;
                Owner.Activate();
            }
        };

        vm.RequestEditMetadata += request =>
        {
            var (game, completedCallback) = request;
            var dialog = new EditMetadataWindow(game) { Owner = this };
            if (dialog.ShowDialog() == true)
                completedCallback();
        };

        vm.RequestSearchArtwork += request =>
        {
            var (url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game) = request;
            new MediaSearchWindow(url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game)
                { Owner = Owner ?? this }.Show();
        };

        DataContext = vm;
    }

    private void MissingItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MetadataProviderViewModel vm && vm.EditSelectedCommand.CanExecute(null))
            vm.EditSelectedCommand.Execute(null);
    }

    private void FilterBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = sender as DependencyObject;
        while (element != null && !(element is System.Windows.Controls.ListBoxItem))
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        if (element is System.Windows.Controls.ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private void ListBoxItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void OpenMediaProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var nextWin = new MediaProviderWindow(_mainVm)
            {
                Owner = Owner,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
                WindowState = WindowState
            };
            
            // Yeni pencere tamamen ekrana çizildikten ve Windows animasyonu bittikten sonra eskiyi kapat ( %100 seamless geçiş )
            nextWin.ContentRendered += async (s, ev) =>
            {
                await System.Threading.Tasks.Task.Delay(120);
                Close();
            };
            
            nextWin.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Media Provider acilamadi:\n{ex.Message}", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MissingMetadataItem item)
        {
            if (DataContext is MetadataProviderViewModel vm)
            {
                vm.SelectedMissingItem = item;
                if (vm.SearchSelectedCommand.CanExecute(null))
                    vm.SearchSelectedCommand.Execute(null);
            }
        }
    }
}
