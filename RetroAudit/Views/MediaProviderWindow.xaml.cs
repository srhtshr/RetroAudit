using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

// Kullanıcı isteği: "media provider tool'u mevcut yapıya entegre et, eksik olanları görsün,
// ordan da indirme yapabilelim" — sürükle-bırak/sahte arama sonucu kartları kaldırıldı (bkz.
// MediaProviderViewModel), pencere artık MainViewModel'in gerçek oyun listesi üzerinden çalışıyor
// (RomImportWindow(mainVm) ile AYNI desen).
public partial class MediaProviderWindow : Window
{
    public MediaProviderWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new MediaProviderViewModel(mainVm.AllGames, game =>
        {
            mainVm.NotifyArtworkDownloaded(game);
            mainVm.ApplyFilter();
        });

        vm.RequestShowMessage += message =>
            MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

        // Embedded arama penceresi — MainViewModel.RequestSearchArtwork İLE AYNI handler deseni
        // (bkz. MainWindow.xaml.cs), burada ayrıca Media Provider'ın kendi MissingItems listesini
        // de güncelliyor (completedCallback zaten bunu ViewModel içinde yapıyor).
        vm.RequestSearchArtwork += request =>
        {
            var (url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback) = request;
            new MediaSearchWindow(url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback)
            {
                Owner = this,
            }.Show();
        };

        DataContext = vm;
    }
}
