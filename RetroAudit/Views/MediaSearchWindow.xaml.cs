using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using RetroAudit.Services;
using RetroAudit.Models;

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
    private Action? _completedCallback;
    private Game? _game; // VideoUrl kaydetme için, opsiyonel

    // completedCallback: indirme Completed durumuna geçtiğinde çağrılır — MainWindow bunu
    // MainViewModel.NotifyArtworkSearched'a bağlıyor (o TEK oyunun görsel yollarını tazeler).
    public MediaSearchWindow(string searchUrl, string targetFolder, string targetFileNameWithoutExtension,
        string gameTitle, string mediaTypeLabel, Action? completedCallback = null, Game? game = null)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        Title = $"RetroAudit - Görsel Ara: {gameTitle} ({mediaTypeLabel})";
        _searchUrl = searchUrl;
        _targetFolder = targetFolder;
        _targetFileNameWithoutExtension = targetFileNameWithoutExtension;
        _completedCallback = completedCallback;
        _game = game;

        Loaded += async (_, _) => await InitializeBrowserAsync(completedCallback);
    }

    private string? _lastSelectedImageUrl;

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

        Browser.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        Browser.CoreWebView2.DownloadStarting += (_, e) => CoreWebView2_DownloadStarting(e, completedCallback);

        // Sayfada tıklanan veya sağ tıklanan her görselin adresini yakalayıp C# tarafına gönderen JS
        const string script = @"
            window.addEventListener('DOMContentLoaded', () => {
                const handleImg = (el) => {
                    if (el && el.tagName === 'IMG' && el.src) {
                        try {
                            window.chrome.webview.postMessage({ type: 'img_selected', url: el.src });
                        } catch(e) {}
                    }
                };
                document.addEventListener('click', (e) => handleImg(e.target));
                document.addEventListener('contextmenu', (e) => handleImg(e.target));
            });
        ";
        await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        Browser.WebMessageReceived += Browser_WebMessageReceived;

        // Yeni pencere açmak yerine aynı pencerede gezin
        Browser.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Browser.CoreWebView2.Navigate(e.Uri);
        };

        // Sayfa içi dinamik yönlendirmelerde (örn. YouTube) URL değişimini anında yakalamak için SourceChanged kullanılır
        Browser.CoreWebView2.SourceChanged += (_, _) =>
        {
            _lastSelectedImageUrl = null; // Sayfa değişince görsel seçimi sıfırlanır
            UpdateNavButtons();
            UpdateBannerButtons(Browser.CoreWebView2?.Source ?? string.Empty);
        };

        Browser.CoreWebView2.Navigate(_searchUrl);
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            var message = System.Text.Json.JsonSerializer.Deserialize<WebMessagePayload>(json);
            if (message?.Type == "img_selected" && !string.IsNullOrWhiteSpace(message.Url))
            {
                _lastSelectedImageUrl = message.Url;
                Dispatcher.Invoke(() => SetBannerButton(SaveImageButton, true));
            }
        }
        catch { }
    }

    private class WebMessagePayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string? Type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? Url { get; set; }
    }


    // URL'e göre banner butonlarını aktif/pasif yap + pulse animasyonu başlat/durdur.
    // Resim URL'si: yaygın resim uzantısıyla biten (jpg/jpeg/png/webp/gif/bmp/avif).
    // Video URL'si: youtube.com/watch?v= içeren.
    private static readonly string[] _imageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif" };

    private void UpdateBannerButtons(string url)
    {
        Dispatcher.Invoke(() =>
        {
            var lower = url.ToLowerInvariant();

            // Resim butonu
            var isImage = _imageExts.Any(ext => lower.Split('?')[0].EndsWith(ext));
            SetBannerButton(SaveImageButton, isImage);

            // Video butonu
            var isYouTube = lower.Contains("youtube.com/watch") || lower.Contains("youtu.be/");
            SetBannerButton(SaveVideoButton, isYouTube);
        });
    }

    private void SetBannerButton(Button button, bool active)
    {
        var anim = (Storyboard)FindResource("PulseAnim");
        if (active)
        {
            button.IsEnabled = true;
            // Animasyonu butona bağlı olarak başlat
            Storyboard.SetTarget(anim, button);
            anim.Begin();
        }
        else
        {
            anim.Stop();
            button.BeginAnimation(OpacityProperty, null); // animasyonu temizle
            button.IsEnabled = false;
            button.Opacity = 0.3;
        }
    }


    private void UpdateNavButtons()
    {
        Dispatcher.Invoke(() =>
        {
            BackButton.IsEnabled = Browser.CoreWebView2?.CanGoBack ?? false;
            ForwardButton.IsEnabled = Browser.CoreWebView2?.CanGoForward ?? false;
        });
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true)
            Browser.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoForward == true)
            Browser.CoreWebView2.GoForward();
    }

    private async void SaveImageButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _lastSelectedImageUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = Browser.CoreWebView2?.Source ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(url)) return;
        await HandleCustomImageDownloadAsync(url);
    }

    private void SaveVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var url = Browser.CoreWebView2?.Source ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return;
        HandleVideoUrlSave(url);
    }

    private void CopyUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = Browser.CoreWebView2?.Source ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Clipboard.SetText(url);

            // Kısa süreli "Kopyalandı!" geri bildirimi
            if (CopyUrlButton.Content is TextBlock tb)
            {
                var original = tb.Text;
                tb.Text = "✔  Kopyalandı!";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (_, _) =>
                {
                    tb.Text = original;
                    timer.Stop();
                };
                timer.Start();
            }
        }
        catch { /* pano erişim hatası — sessizce geç */ }
    }

    private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // Tüm varsayılan tarayıcı menü öğelerini sil — bu tarayıcı programa özel
        e.MenuItems.Clear();

        var kind = e.ContextMenuTarget.Kind;
        var currentPageUrl = Browser.CoreWebView2.Source ?? "";

        if (kind == CoreWebView2ContextMenuTargetKind.Image)
        {
            var imageUrl = e.ContextMenuTarget.SourceUri;
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                var saveItem = Browser.CoreWebView2.Environment.CreateContextMenuItem(
                    "🎮  Görseli RetroAudit'e Kaydet",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);

                saveItem.CustomItemSelected += async (s, args) =>
                {
                    await HandleCustomImageDownloadAsync(imageUrl);
                };

                e.MenuItems.Add(saveItem);
            }
        }

        // YouTube video sayfasında sağ tıklanırsa video URL kaydet butonu göster
        if (_game != null && IsYouTubeVideoUrl(currentPageUrl))
        {
            var normalizedUrl = NormalizeYouTubeUrl(currentPageUrl);
            var saveVideoItem = Browser.CoreWebView2.Environment.CreateContextMenuItem(
                "🎬  Bu YouTube Videosunu RetroAudit'e Kaydet",
                null,
                CoreWebView2ContextMenuItemKind.Command);

            saveVideoItem.CustomItemSelected += (s, args) =>
            {
                HandleVideoUrlSave(normalizedUrl);
            };

            e.MenuItems.Add(saveVideoItem);
        }

        // Menüde hiç öğe yoksa menüyü tamamen gizle
        if (e.MenuItems.Count == 0)
        {
            e.Handled = true;
        }
    }

    private static bool IsYouTubeVideoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return (url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase));
    }

    // youtu.be / embed gibi varyantları standart watch?v= formatına dönüştür
    private static string NormalizeYouTubeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);

            // youtu.be/VIDEOID
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                var videoId = uri.AbsolutePath.TrimStart('/');
                return $"https://www.youtube.com/watch?v={videoId}";
            }

            // youtube.com/embed/VIDEOID
            if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
            {
                var videoId = uri.AbsolutePath.Substring("/embed/".Length).Split('?')[0];
                return $"https://www.youtube.com/watch?v={videoId}";
            }
        }
        catch { }

        // Zaten watch?v= formatındaysa olduğu gibi döndür
        return url;
    }

    private void HandleVideoUrlSave(string videoUrl)
    {
        if (_game is null)
        {
            MessageBox.Show(this,
                "Bu arama penceresinde video kaydetme desteklenmiyor.",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            UserDataService.SaveVideoUrlOverride(_game.GameKey, videoUrl);
            _game.VideoUrl = videoUrl;
            _completedCallback?.Invoke();

            MessageBox.Show(this,
                $"YouTube video bağlantısı başarıyla kaydedildi!\n\n{videoUrl}",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
            Owner?.Activate();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Video URL kaydedilirken hata oluştu:\n{ex.Message}",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private async Task HandleCustomImageDownloadAsync(string imageUrl)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressStatusText.Text = "⏳  İndiriliyor...";
        try
        {
            var isLogo = _targetFolder.Contains("Logo", StringComparison.OrdinalIgnoreCase);


            // Uzantıyı URL'den tahmin et (Logo ise mutlaka .png, değilse tahmin et veya varsayılan .jpg)
            var extension = isLogo ? ".png" : ".jpg";
            if (!isLogo)
            {
                try
                {
                    var uri = new Uri(imageUrl);
                    var ext = Path.GetExtension(uri.AbsolutePath).Split('?')[0];
                    if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5)
                        extension = ext;
                }
                catch { }
            }

            var destination = Path.Combine(_targetFolder, _targetFileNameWithoutExtension + extension);

            var settings = ConfigService.LoadDefault();
            var maxDimension = settings.ArtworkMaxDimension switch
            {
                Models.ArtworkMaxDimension.Px800    => 800,
                Models.ArtworkMaxDimension.Original => int.MaxValue,
                _                                   => 600,
            };

            // Strateji 1: C# HttpClient ile direkt indir.
            // Browser fetch'in aksine CORS kısıtlaması yoktur. Wikimedia, Wikipedia,
            // imgur gibi sitelerde browser'ın Same-Origin Policy'si engeller ama
            // HttpClient sunucu tarafında çalıştığı için geçer.
            byte[]? imageBytes = null;
            string? httpError = null;

            try
            {
                var referer = Browser.CoreWebView2?.Source ?? "https://www.google.com/";
                imageBytes = await DownloadViaCSharpHttpAsync(imageUrl, referer);
            }
            catch (Exception ex)
            {
                httpError = ex.Message;
            }

            // Strateji 2: C# başarısız olduysa (ör. oturum gerektiren site) tarayıcı fetch'e düş.
            // Tarayıcı çerezleri/oturumu taşıdığı için oturum açık sitelerde işe yarar.
            if (imageBytes is null)
            {
                var setUrlScript = $"window.__retroUrl = {System.Text.Json.JsonSerializer.Serialize(imageUrl)};";
                await Browser.CoreWebView2!.ExecuteScriptAsync(setUrlScript);

                const string fetchScript =
                    "(async () => {" +
                    "  try {" +
                    "    const r = await fetch(window.__retroUrl, { credentials: 'include' });" +
                    "    if (!r.ok) return 'ERR:HTTP' + r.status;" +
                    "    const buf = await r.arrayBuffer();" +
                    "    const bytes = new Uint8Array(buf);" +
                    "    let b64 = '';" +
                    "    const chunkSize = 8190;" +
                    "    for (let i = 0; i < bytes.length; i += chunkSize) {" +
                    "      b64 += btoa(String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize)));" +
                    "    }" +
                    "    return b64;" +
                    "  } catch(e) { return 'ERR:' + e.message; }" +
                    "})()";

                var result = await Browser.CoreWebView2.ExecuteScriptAsync(fetchScript);

                if (result.StartsWith("\"") && result.EndsWith("\""))
                    result = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? result;

                if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("ERR:") && result != "null")
                {
                    try { imageBytes = Convert.FromBase64String(result); }
                    catch { /* base64 bozuksa null kalır */ }
                }

                if (imageBytes is null)
                {
                    var reason = result?.StartsWith("ERR:") == true ? result : (httpError ?? "Bilinmeyen hata");
                    MessageBox.Show(this,
                        $"Görsel indirilemedi ({reason}).\nLütfen farklı bir görsel deneyin.",
                        "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string? saveError = null;
            bool success;
            try
            {
                success = await ArtworkService.ProcessAndSaveAsync(imageBytes, destination, isLogo, maxDimension);
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
                success = false;
            }

            if (success)
            {
                _completedCallback?.Invoke();
                MessageBox.Show(this,
                    "Görsel başarıyla indirildi ve RetroAudit kütüphanenize kaydedildi!",
                    "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
                Owner?.Activate();
                Close();
            }
            else
            {
                var detail = saveError is not null ? $"\n\nDetay: {saveError}" : string.Empty;
                MessageBox.Show(this,
                    $"Görsel kaydedilemedi. Dosya bozuk veya desteklenmeyen format olabilir.{detail}",
                    "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Görsel kaydedilirken hata oluştu:\n{ex.Message}",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    // CORS kısıtlaması olmayan C# HttpClient ile indir.
    // Referer: tarayıcının mevcut sayfası — bazı CDN'ler kontrol eder.
    private static readonly System.Net.Http.HttpClient _httpClient = new(new System.Net.Http.HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    });

    private static async Task<byte[]> DownloadViaCSharpHttpAsync(string url, string referer)
    {
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
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
