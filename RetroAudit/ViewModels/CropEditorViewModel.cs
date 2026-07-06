using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Converters;

namespace RetroAudit.ViewModels;

// Görsel kırpma/yeniden boyutlandırma diyaloğunun ViewModel'i (kullanıcı isteği: "kırpma kesme
// pixel küçültme işleri yapılır ordan"). Kırpma dikdörtgeni View'daki Canvas üzerinde EKRAN
// (display) koordinatlarında tutuluyor (bkz. CropLeft/Top/Width/Height) — CropEditorDialog.xaml.cs
// Thumb sürükleme olaylarıyla bunları günceller; Save sırasında SourcePixelWidth/Height ve
// DisplayWidth/Height oranı kullanılarak gerçek görsel piksel koordinatlarına çevrilir.
public partial class CropEditorViewModel : ObservableObject
{
    // Üstteki oran seçim şeridinde gösterilen seçenekler.
    public ObservableCollection<string> AspectRatios { get; } = new()
    {
        "Serbest", "Orijinal", "Kare", "4:3", "16:9", "2:3", "3:4",
    };

    [ObservableProperty]
    private string selectedAspectRatio = "Serbest";

    partial void OnSelectedAspectRatioChanged(string value) => ApplyAspectRatioToCrop();

    // Kırpılacak/kaydedilecek görselin dosya yolu.
    [ObservableProperty]
    private string imagePath = string.Empty;

    // Kaynak görselin GERÇEK piksel boyutu (Initialize'da dosyadan okunur).
    [ObservableProperty]
    private int sourcePixelWidth;

    [ObservableProperty]
    private int sourcePixelHeight;

    // Görselin Canvas üzerinde Stretch=Uniform ile çizildiği gerçek alan (letterbox sonrası) —
    // CropEditorDialog.xaml.cs, Image kontrolünün gerçek render boyutunu ölçüp SetDisplayBounds
    // ile buraya yazıyor.
    [ObservableProperty] private double displayLeft;
    [ObservableProperty] private double displayTop;
    [ObservableProperty] private double displayWidth;
    [ObservableProperty] private double displayHeight;

    // Kırpma dikdörtgeni — EKRAN uzayında (Canvas.Left/Top/Width/Height'e doğrudan bağlanıyor).
    // Köşe tutamaçlarının (14px, yarısı 7px) TAM köşe noktasına ortalanması için 4 hesaplanmış
    // Left/Top çifti aşağıda — her Crop* değişince NotifyPropertyChangedFor ile tazeleniyor.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopLeftHandleLeft))]
    [NotifyPropertyChangedFor(nameof(BottomLeftHandleLeft))]
    private double cropLeft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopLeftHandleTop))]
    [NotifyPropertyChangedFor(nameof(TopRightHandleTop))]
    private double cropTop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopRightHandleLeft))]
    [NotifyPropertyChangedFor(nameof(BottomRightHandleLeft))]
    private double cropWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BottomLeftHandleTop))]
    [NotifyPropertyChangedFor(nameof(BottomRightHandleTop))]
    private double cropHeight;

    private const double HandleOffset = 7;
    public double TopLeftHandleLeft => CropLeft - HandleOffset;
    public double TopLeftHandleTop => CropTop - HandleOffset;
    public double TopRightHandleLeft => CropLeft + CropWidth - HandleOffset;
    public double TopRightHandleTop => CropTop - HandleOffset;
    public double BottomLeftHandleLeft => CropLeft - HandleOffset;
    public double BottomLeftHandleTop => CropTop + CropHeight - HandleOffset;
    public double BottomRightHandleLeft => CropLeft + CropWidth - HandleOffset;
    public double BottomRightHandleTop => CropTop + CropHeight - HandleOffset;

    // "Pixel küçültme": kaydedilecek maksimum genişlik — "Orijinal" seçiliyse küçültme yapılmaz.
    public ObservableCollection<string> MaxWidthOptions { get; } = new() { "Orijinal", "1024", "512", "256", "128" };

    [ObservableProperty]
    private string selectedMaxWidth = "Orijinal";

    // Save veya Cancel'a basıldığında pencereyi kapatmak için View'a bırakılan olay.
    public event Action? RequestClose;

    // Kaydetme başarıyla bittiğinde (dosya diskte değişti) — çağıran taraf (MainWindow.xaml.cs)
    // bunu dinleyip Game modelindeki path'i yeniden atayarak (aynı değer olsa bile) WPF binding'inin
    // ThumbnailImageConverter.Invalidate ile temizlenmiş önbellekten YENİDEN decode etmesini sağlar.
    public event Action? Saved;

    // Dosyadan gerçek piksel boyutlarını okur — View, pencere açılırken bunu çağırır.
    public void Initialize(string imagePath)
    {
        ImagePath = imagePath;

        var probe = new BitmapImage();
        probe.BeginInit();
        probe.CacheOption = BitmapCacheOption.OnLoad;
        probe.UriSource = new Uri(imagePath, UriKind.Absolute);
        probe.EndInit();

        SourcePixelWidth = probe.PixelWidth;
        SourcePixelHeight = probe.PixelHeight;
    }

    // View, Image kontrolünün Stretch=Uniform sonrası gerçek çizim alanını ölçüp bunu çağırır —
    // kırpma dikdörtgeni başlangıçta görselin TAMAMINI kapsayacak şekilde kurulur.
    public void SetDisplayBounds(double left, double top, double width, double height)
    {
        DisplayLeft = left;
        DisplayTop = top;
        DisplayWidth = width;
        DisplayHeight = height;

        CropLeft = left;
        CropTop = top;
        CropWidth = width;
        CropHeight = height;
    }

    // Bir oran seçildiğinde mevcut kırpma dikdörtgenini merkezini koruyarak o orana uydurur;
    // "Serbest" seçiliyse hiçbir zorlama yapılmaz (kullanıcı Thumb'larla istediği gibi ayarlar).
    private void ApplyAspectRatioToCrop()
    {
        if (DisplayWidth <= 0 || DisplayHeight <= 0)
            return;

        var ratio = SelectedAspectRatio switch
        {
            "Kare" => 1.0,
            "4:3" => 4.0 / 3.0,
            "16:9" => 16.0 / 9.0,
            "2:3" => 2.0 / 3.0,
            "3:4" => 3.0 / 4.0,
            "Orijinal" => SourcePixelHeight > 0 ? (double)SourcePixelWidth / SourcePixelHeight : 1.0,
            _ => 0.0, // Serbest
        };
        if (ratio <= 0)
            return;

        var centerX = CropLeft + CropWidth / 2;
        var centerY = CropTop + CropHeight / 2;

        var newWidth = CropWidth;
        var newHeight = newWidth / ratio;
        if (newHeight > DisplayHeight)
        {
            newHeight = DisplayHeight;
            newWidth = newHeight * ratio;
        }
        if (newWidth > DisplayWidth)
        {
            newWidth = DisplayWidth;
            newHeight = newWidth / ratio;
        }

        CropWidth = newWidth;
        CropHeight = newHeight;
        CropLeft = Math.Clamp(centerX - newWidth / 2, DisplayLeft, DisplayLeft + DisplayWidth - newWidth);
        CropTop = Math.Clamp(centerY - newHeight / 2, DisplayTop, DisplayTop + DisplayHeight - newHeight);
    }

    // Sürükleme sırasında (bkz. CropEditorDialog.xaml.cs Thumb.DragDelta) "Serbest" DIŞINDA bir
    // oran seçiliyken genişlik/yükseklik birbirinden bağımsız değişmesin diye kullanılıyor.
    public double? CurrentLockedAspectRatio => SelectedAspectRatio switch
    {
        "Serbest" => null,
        "Kare" => 1.0,
        "4:3" => 4.0 / 3.0,
        "16:9" => 16.0 / 9.0,
        "2:3" => 2.0 / 3.0,
        "3:4" => 3.0 / 4.0,
        "Orijinal" => SourcePixelHeight > 0 ? (double)SourcePixelWidth / SourcePixelHeight : (double?)null,
        _ => null,
    };

    [RelayCommand]
    private void Save()
    {
        try
        {
            SaveCropped();
            Saved?.Invoke();
        }
        finally
        {
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();

    // Ekran (display) koordinatlarındaki kırpma dikdörtgenini gerçek görsel piksel koordinatlarına
    // çevirip CroppedBitmap ile keser, "Pixel küçültme" seçiliyse TransformedBitmap ile küçültür,
    // orijinal dosya UZANTISINA göre (PNG/JPEG) yeniden kodlayıp AYNI yolun üzerine yazar. Geçici
    // dosyaya yazıp sonra kopyalamak, yazma sırasında bir hata olursa orijinal dosyanın bozulmadan
    // kalmasını sağlıyor.
    private void SaveCropped()
    {
        if (DisplayWidth <= 0 || DisplayHeight <= 0 || SourcePixelWidth <= 0 || SourcePixelHeight <= 0)
            return;

        var scaleX = SourcePixelWidth / DisplayWidth;
        var scaleY = SourcePixelHeight / DisplayHeight;

        var pixelX = (int)Math.Round((CropLeft - DisplayLeft) * scaleX);
        var pixelY = (int)Math.Round((CropTop - DisplayTop) * scaleY);
        var pixelW = (int)Math.Round(CropWidth * scaleX);
        var pixelH = (int)Math.Round(CropHeight * scaleY);

        pixelX = Math.Clamp(pixelX, 0, SourcePixelWidth - 1);
        pixelY = Math.Clamp(pixelY, 0, SourcePixelHeight - 1);
        pixelW = Math.Clamp(pixelW, 1, SourcePixelWidth - pixelX);
        pixelH = Math.Clamp(pixelH, 1, SourcePixelHeight - pixelY);

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(ImagePath, UriKind.Absolute);
        source.EndInit();

        BitmapSource result = new CroppedBitmap(source, new Int32Rect(pixelX, pixelY, pixelW, pixelH));

        if (SelectedMaxWidth != "Orijinal" && int.TryParse(SelectedMaxWidth, out var maxWidth) && result.PixelWidth > maxWidth)
        {
            var scale = (double)maxWidth / result.PixelWidth;
            result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
        }

        var extension = Path.GetExtension(ImagePath).ToLowerInvariant();
        BitmapEncoder encoder = extension is ".jpg" or ".jpeg" ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));

        var tempPath = ImagePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create))
            encoder.Save(stream);
        File.Copy(tempPath, ImagePath, overwrite: true);
        File.Delete(tempPath);

        ThumbnailImageConverter.Invalidate(ImagePath);
    }
}
