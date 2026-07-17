using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

// Kullanıcı isteği: "media provider tool'u mevcut yapıya entegre et, eksik olanları görsün,
// ordan da indirme yapabilelim" — sürükle-bırak/sahte arama sonucu kartları kaldırıldı (bkz.
// MediaProviderViewModel), pencere artık MainViewModel'in gerçek oyun listesi üzerinden çalışıyor
// (RomImportWindow(mainVm) ile AYNI desen).
public partial class MediaProviderWindow : Window
{
    private readonly MainViewModel _mainVm;

    // Kullanıcı isteği: "indirme barı providerın içinde gözükse" — bu pencerenin kendi DataContext'i
    // MediaProviderViewModel, ama indirme ilerlemesi (ArtworkDownloadProgress vb.) MainViewModel'de
    // yaşıyor. XAML'de RelativeSource={RelativeSource AncestorType=Window} ile bu property'ye
    // ulaşabilmek için public olarak açılıyor.
    public MainViewModel MainVm => _mainVm;

    public MediaProviderWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);
        _mainVm = mainVm;

        var vm = new MediaProviderViewModel(mainVm.AllGames, (game, type, baseFileName, destination) =>
        {
            // Kullanıcı geri bildirimi: "media provider da otomatik indirme çalışmıyor heralde" —
            // MainViewModel'in kendi medya sözlüklerini (bkz. RegisterDownloadedMedia) ÖNCE
            // güncellemeden NotifyArtworkDownloaded çağırmak, dosya diske gerçekten inmiş olsa bile
            // onu bulamıyordu (restart'a kadar boş görünüyordu).
            mainVm.RegisterDownloadedMedia(type, game.PlatformDisplayName, baseFileName, destination);
            mainVm.NotifyArtworkDownloaded(game);
            mainVm.ApplyFilter();
        }, () => mainVm.GetAllPlatformsOrdered(), mainVm.ProviderDesignMode == ProviderDesignMode.Modern);
        Action<Game> metadataChangedHandler = _ => vm.RefreshAll();

        vm.RequestShowMessage += message =>
            MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

        vm.RequestEditMetadata += request =>
        {
            var (game, completedCallback) = request;
            var dialog = new EditMetadataWindow(game) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                mainVm.NotifyMetadataEdited(game);
                completedCallback();
            }
        };

        mainVm.PlatformListOrderChanged += vm.RefreshPlatformOrder;
        Closed += (_, _) => mainVm.PlatformListOrderChanged -= vm.RefreshPlatformOrder;
        mainVm.GameMetadataChanged += metadataChangedHandler;
        Closed += (_, _) => mainVm.GameMetadataChanged -= metadataChangedHandler;

        // Kullanıcı isteği: "provider açıkken ana UI'daki çubuk gizlensin, kapanınca geri gelsin"
        // — bkz. MainViewModel.ShowMainArtworkProgressBar.
        mainVm.IsMediaProviderWindowOpen = true;
        Closed += (_, _) => mainVm.IsMediaProviderWindowOpen = false;

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

        // Embedded arama penceresi — MainViewModel.RequestSearchArtwork İLE AYNI handler deseni
        // (bkz. MainWindow.xaml.cs), burada ayrıca Media Provider'ın kendi MissingItems listesini
        // de güncelliyor (completedCallback zaten bunu ViewModel içinde yapıyor).
        vm.RequestSearchArtwork += request =>
        {
            var (url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game) = request;
            new MediaSearchWindow(url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game)
            {
                Owner = this,
            }.Show();
        };

        DataContext = vm;
    }

    private void MissingItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MediaProviderViewModel vm && vm.EditSelectedCommand.CanExecute(null))
            vm.EditSelectedCommand.Execute(null);
    }

    // Kullanıcı isteği: "otomatik indir butonuna basıyorum indirmiyor ... görsel getir butonu
    // varya ona bağlıcan ... toplu seçimlerede uygun olacak" — MediaProviderViewModel'in kendi
    // (ayrı, hatalı) indirme mantığı kaldırıldı; burada seçili satır(lar) (Liste/Tablo görünümü,
    // hangisi görünürse) toplanıp GERÇEKTEN ÇALIŞAN mekanizmaya (MainViewModel.
    // BulkFetchArtworkForGamesAsync — ana tablonun kapsül menüsündeki "Görsel Getir"le AYNI)
    // veriliyor. Aynı oyunun birden fazla satırı (ör. hem Kapak hem Logo eksik) seçilse bile
    // Distinct ile oyun başına TEK indirme yapılıyor (o oyunun eksik TÜM türleri zaten tek seferde
    // sorulup indiriliyor).
    // Kullanıcı isteği: "otomatik indirin yanına hepsini seç butonu koy" — hangi görünüm (Liste/
    // Tablo) o an görünürse kullanıcı zaten onu kullanacak; ikisi de AYNI MissingItems'a bağlı
    // olduğu için görünmeyen kontrolde SelectAll çağırmak zararsız, sadece emin olmak için ikisi
    // de çağrılıyor.
    // GEÇİCİ: kullanıcı isteği "sayıları dışarı aktarmak için bişey ekle oraya geçici not
    // defterine atsın" — platform kartlarındaki Logo/Box/SS/Video/Wiki eksik sayılarını elle
    // toplayıp doğrulayabilmek için (bkz. MainWindow.xaml.cs ExportVisibleGamesButton_Click ile
    // AYNI desen). Kaldırılabilir.
    private void ExportPlatformSummariesButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaProviderViewModel vm)
            return;

        var lines = vm.PlatformAuditSummaries.Select(s =>
            $"{s.PlatformDisplayName} | Toplam {s.TotalGames} | Eşleşen {s.MatchedCount} | Eşleşmeyen {s.UnmatchedCount} | " +
            $"Logo {s.MissingLogoCount} | Box {s.MissingBoxCount} | SS {s.MissingScreenshotCount} | Video {s.MissingVideoCount} | Wiki {s.MissingWikipediaCount}");
        var tempPath = Path.Combine(Path.GetTempPath(), "RetroAudit_ProviderDisaAktar.txt");
        File.WriteAllLines(tempPath, lines);
        Process.Start(new ProcessStartInfo("notepad.exe", tempPath) { UseShellExecute = true });
    }

    // Metadata Provider'daki "Listeyi Dışa Aktar" ile AYNI mantık (bkz. MetadataProviderWindow.
    // xaml.cs ExportMissingItemsButton_Click) — kullanıcı isteği: "media providera da aynı
    // mantıkta dışa aktarma ekle, chatgpt'ye onlara baktırayım dışarı aktarıp". Eksik öğe
    // listesini (görsel türü + CRC32 dahil) düz metin olarak dışa aktarır.
    private void ExportMissingItemsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaProviderViewModel vm)
            return;

        var lines = vm.MissingItems.Select(i => $"{i.Title} | {i.Platform} | {i.MissingTypeLabel} | {i.Crc32}");
        var tempPath = Path.Combine(Path.GetTempPath(), "RetroAudit_EksikGorsellerDisaAktar.txt");
        File.WriteAllLines(tempPath, lines);
        Process.Start(new ProcessStartInfo("notepad.exe", tempPath) { UseShellExecute = true });
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        MissingItemsList.SelectAll();
        MissingItemsGrid.SelectAll();
    }

    private async void AutoDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaProviderViewModel vm)
            return;

        var selectedItems = MissingItemsList.SelectedItems.Cast<MissingMediaItem>()
            .Concat(MissingItemsGrid.SelectedItems.Cast<MissingMediaItem>())
            .Distinct()
            .ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show(this, "İndirmek için önce en az bir satır seçin.", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Kullanıcı isteği: "videolar ile ilgili filtre ... o komut pasif gözükmeli videoyla
        // alakası yok çünkü" — CanAutoDownload zaten butonu devre dışı bırakıyor, ama çoklu
        // seçimde (WPF'in SelectedItem'ı tek bir "çapa" öğeyi yansıttığı için CanAutoDownload'ın
        // göremediği) Video/Wiki satırları karışmışsa burada da GÜVENLİK olarak eleniyor.
        var games = selectedItems
            .Where(i => i.MissingType is not ("Video" or "Wiki"))
            .Select(i => i.Game)
            .Distinct()
            .ToList();

        if (games.Count == 0)
        {
            MessageBox.Show(this, "Seçili satırlar Video/Wiki eksiği — otomatik indirme sadece Box/Logo/SS için çalışır.", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        vm.IsBusy = true;
        try
        {
            await _mainVm.BulkFetchArtworkForGamesAsync(games);
        }
        finally
        {
            vm.IsBusy = false;
        }

        vm.RefreshAll();
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

    private void OpenMetadataProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var nextWin = new MetadataProviderWindow(_mainVm)
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
            MessageBox.Show(this, $"Metadata Provider acilamadi:\n{ex.Message}", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MissingMediaItem item)
        {
            if (DataContext is MediaProviderViewModel vm)
            {
                vm.SelectedMissingItem = item;
                if (vm.SearchSelectedCommand.CanExecute(null))
                    vm.SearchSelectedCommand.Execute(null);
            }
        }
    }
}
