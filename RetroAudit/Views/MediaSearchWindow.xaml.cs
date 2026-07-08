using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace RetroAudit.Views;

// Oyun satırındaki/detay panelindeki "Ara" butonuyla açılan, uygulama içi (embedded) görsel
// arama penceresi — RomSearchWindow ile aynı desen (bkz. o dosyadaki yorumlar), tek fark:
// indirilen dosya sadece HEDEF KLASÖRE değil, ROM dosya adıyla eşleşen SABİT bir isme de
// (uzantı korunarak) yönlendiriliyor — Game.BoxDisplayPath/ClearLogoDisplayPath/
// ScreenshotDisplayPath, MainViewModel.BuildMediaTypeIndex'in indekslediği "dosya adı
// (uzantısız) = ROM dosya adı" eşleşmesine dayanıyor, ROM aramasının aksine burada isim
// serbest bırakılamaz.
public partial class MediaSearchWindow : Window
{
    private readonly string _searchUrl;
    private readonly string _targetFolder;
    private readonly string _targetFileNameWithoutExtension;

    // completedCallback: indirme Completed durumuna geçtiğinde çağrılır — MainWindow bunu
    // MainViewModel.NotifyArtworkSearched'a bağlıyor (o TEK oyunun görsel yollarını tazeler).
    public MediaSearchWindow(string searchUrl, string targetFolder, string targetFileNameWithoutExtension,
        string gameTitle, string mediaTypeLabel, Action? completedCallback = null)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        Title = $"RetroAudit - Görsel Ara: {gameTitle} ({mediaTypeLabel})";
        _searchUrl = searchUrl;
        _targetFolder = targetFolder;
        _targetFileNameWithoutExtension = targetFileNameWithoutExtension;

        Loaded += async (_, _) => await InitializeBrowserAsync(completedCallback);
    }

    private async Task InitializeBrowserAsync(Action? completedCallback)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"WebView2 Runtime başlatılamadı. Microsoft Edge WebView2 Runtime kurulu olmayabilir.\n\nDetay: {ex.Message}",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        Browser.CoreWebView2.DownloadStarting += (_, e) => CoreWebView2_DownloadStarting(e, completedCallback);
        Browser.CoreWebView2.Navigate(_searchUrl);
    }

    private void CoreWebView2_DownloadStarting(CoreWebView2DownloadStartingEventArgs e, Action? completedCallback)
    {
        var deferral = e.GetDeferral();
        try
        {
            Directory.CreateDirectory(_targetFolder);

            // ROM arama penceresinden farkı burası: dosya adı SERBEST bırakılmıyor, tarayıcının
            // önerdiği uzantı korunup ROM dosya adıyla eşleşen sabit bir isme zorlanıyor (bkz.
            // sınıf yorumu).
            var extension = Path.GetExtension(e.ResultFilePath);
            e.ResultFilePath = Path.Combine(_targetFolder, _targetFileNameWithoutExtension + extension);
            e.Handled = true;

            if (completedCallback is not null)
            {
                e.DownloadOperation.StateChanged += (sender, _) =>
                {
                    if (sender is not CoreWebView2DownloadOperation { State: CoreWebView2DownloadState.Completed })
                        return;

                    completedCallback();
                };
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
