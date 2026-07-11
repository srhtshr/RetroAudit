using System.IO;
using System.Windows;
using System.Windows.Data;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class RomImportWindow : Window
{
    public RomImportWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new RomImportViewModel(mainVm.AllGames, mainVm.GamesRootPath, () =>
        {
            mainVm.ReloadAppSettings();
            mainVm.RefreshLibrary();
        }, mainVm.RegisterNewCustomGame);
        vm.RequestShowMessage += message =>
            MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

        // Kullanıcı isteği: "seçili olanları sadece göstermek için toggle olsun" — Eşleşmeyenler
        // DataGrid'i XAML'de "UnmatchedView" CollectionViewSource'una bağlı; Filter burada, canlı
        // olarak vm.ShowOnlySelectedUnmatched'e bakıyor. Toggle değişince veya filtre açıkken tek
        // bir satırın işareti değişince (bkz. RomImportViewModel.OnUnmatchedItemPropertyChanged)
        // RequestRefreshUnmatchedView tetiklenip view.Refresh() çağrılıyor.
        var unmatchedView = (CollectionViewSource)FindResource("UnmatchedView");
        unmatchedView.Filter += (_, e) =>
            e.Accepted = !vm.ShowOnlySelectedUnmatched || (e.Item is UnmatchedRomFile { IsSelected: true });
        vm.RequestRefreshUnmatchedView += () => unmatchedView.View.Refresh();

        // Kullanıcı isteği: "tıklamalı seçenekli yap onları seçili olanları silsin" — MainWindow'daki
        // "Kalıcı Sil" onayıyla AYNI desen: silme gerçekleşmeden önce kaç dosyanın (Çöp Kutusu'na)
        // taşınacağı gösterilip onay isteniyor.
        vm.RequestDeleteSelectedUnmatchedConfirmation += count =>
        {
            var result = MessageBox.Show(
                $"{count} dosya Windows Çöp Kutusu'na taşınacak. Devam edilsin mi?",
                "Seçilenleri Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                vm.DeleteSelectedUnmatchedConfirmedCommand.Execute(null);
        };

        // Kullanıcı isteği: "eşleşmese bile açabilmeli bi yerden ... manuel bağlama" — dosyanın
        // BULUNDUĞU klasörün adı (zip ise zip'in klasörü), RomImportService'in kendi platform-klasör
        // eşleştirmesiyle (bkz. RomImportService.PlatformNameMatchesFolder) AYNI mantıkla, pencerenin
        // varsayılan olarak sadece o platformu göstermesi için kullanılıyor.
        vm.RequestManualLink += async file =>
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(file.SourcePath)) ?? string.Empty;
            var dialog = new ManualLinkWindow(mainVm.AllGames, folderName, file.FileName) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedGame is not { } selectedGame)
                return;

            // Kullanıcı isteği: "unknown değilde manuel bağlamaya yönlendirebilirsin ... 2 ayrı
            // eşleştirme şekli olmasın" — "+ Yeni Oyun" seçiliyse (bkz.
            // ManualLinkViewModel.NewGameSentinelKey) önce gerçek (kalıcı) bir Game kaydı oluşturulur,
            // sonrasında AYNI CompleteManualLinkAsync çağrısı (mevcut bir katalog oyununa bağlamakla
            // BİREBİR aynı yol) kullanılır.
            var targetGame = selectedGame.GameKey == ManualLinkViewModel.NewGameSentinelKey
                ? mainVm.RegisterNewCustomGame(selectedGame.Title, folderName)
                : selectedGame;
            await vm.CompleteManualLinkAsync(file, targetGame, dialog.SelectedVersion);
        };

        DataContext = vm;
    }
}
