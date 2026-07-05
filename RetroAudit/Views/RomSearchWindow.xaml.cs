using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace RetroAudit.Views;

// Oyun satırındaki "Ara" butonuyla açılan, uygulama içi (embedded) arama penceresi.
// Kullanıcı burada TAMAMEN kendi kontrolünde geziniyor (linklere kendi tıklıyor, "Download"
// butonuna kendi basıyor) — bu pencerenin tek otomasyonu, WebView2'nin resmi DownloadStarting
// olayını dinleyip indirmenin KAYDEDİLECEĞİ KLASÖRÜ (dosyayı değil, hedef yolu) oyunun platform
// klasörüne yönlendirmek, böylece kullanıcıyı "Farklı Kaydet" diyaloğuyla uğraştırmıyor.
// İndirmeyi başlatan/onaylayan her zaman kullanıcının kendisidir.
public partial class RomSearchWindow : Window
{
    private readonly string _searchUrl;
    private readonly string _targetFolder;

    // completedCallback: bir indirme Completed durumuna geçtiğinde (dosya adıyla) çağrılır —
    // MainWindow bunu MainViewModel.NotifyRomDownloaded'a bağlıyor (bkz. MainWindow.xaml.cs).
    public RomSearchWindow(string searchUrl, string targetFolder, string gameTitle, Action<string>? completedCallback = null)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        Title = $"RetroAudit - ROM Ara: {gameTitle}";
        _searchUrl = searchUrl;
        _targetFolder = targetFolder;

        Loaded += async (_, _) => await InitializeBrowserAsync(completedCallback);
    }

    private async Task InitializeBrowserAsync(Action<string>? completedCallback)
    {
        // EnsureCoreWebView2Async, WebView2 Runtime'ın kurulu olduğu her Windows 10/11
        // makinede sorunsuz çalışır (Windows 11'de ve güncel Edge'li Windows 10'da zaten
        // yerleşik gelir) — kurulu değilse burada bir istisna fırlatır, aşağıdaki catch bunu
        // kullanıcıya anlaşılır bir mesajla bildirir.
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

    // TargetFolder boşsa (RetroAuditDataPath ayarlanmamışsa) hiçbir şeye dokunulmuyor — WebView2
    // kendi varsayılan İndirilenler klasörünü ve normal "Farklı Kaydet" akışını kullanır.
    private void CoreWebView2_DownloadStarting(CoreWebView2DownloadStartingEventArgs e, Action<string>? completedCallback)
    {
        if (string.IsNullOrWhiteSpace(_targetFolder))
            return;

        // Deferral: ResultFilePath'i değiştirmeden/Handled'ı set etmeden önce (burada sadece
        // Directory.CreateDirectory gibi hızlı, senkron bir hazırlık var, ama WebView2'nin
        // resmi örnekleri her zaman deferral kullanmayı öneriyor — indirme, Complete()
        // çağrılana kadar askıda bekliyor, yarış durumu oluşmuyor).
        var deferral = e.GetDeferral();
        try
        {
            Directory.CreateDirectory(_targetFolder);

            var fileName = Path.GetFileName(e.ResultFilePath);
            e.ResultFilePath = Path.Combine(_targetFolder, fileName);

            // Handled=true: WebView2'nin kendi indirme çubuğu/"Farklı Kaydet" diyaloğu hiç
            // görünmüyor, indirme doğrudan yukarıdaki hedefe gidiyor. İndirmeyi TETİKLEYEN
            // (Download butonuna basan) hâlâ kullanıcının kendisi — burada sadece hedef klasör
            // değişiyor, indirme kararı değil.
            e.Handled = true;

            if (completedCallback is not null)
            {
                e.DownloadOperation.StateChanged += (sender, _) =>
                {
                    if (sender is not CoreWebView2DownloadOperation { State: CoreWebView2DownloadState.Completed } op)
                        return;

                    completedCallback(op.ResultFilePath);
                };
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
