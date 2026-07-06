using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroAudit.Services;

// Katalogdaki görsel varlık dosya adlarını (bkz. CatalogDatabaseService.GetArtworkAssets) indirip
// diske yazar. Saf, UI'dan bağımsız statik servis — RomImportService.cs ile aynı tarzda.
public static class ArtworkService
{
    // Host, bu public depoda düz metin bir literal olarak görünmesin diye Base64 ile saklanıyor
    // (gerçek bir gizlilik sağlamaz — istek zaten düz metin gider, sadece kaynak kodda görünmesin diye).
    private const string EncodedHost = "aW1hZ2VzLmxhdW5jaGJveC1hcHAuY29t";
    private static readonly string Host = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedHost));

    private static readonly HttpClient Http = new();

    // Kaynak dosyalar (özellikle Screenshot) birkaç MB'a kadar çıkabiliyordu — uygulama
    // bunları hiçbir yerde bu boyuttan büyük göstermiyor (en büyük gösterim alanı ~300x240), bu
    // yüzden indirdikten sonra uzun kenarı Ayarlar > Genel'de seçilen boyuta küçültüp yeniden
    // kodluyoruz (bkz. AppSettings.ArtworkMaxDimension, MainViewModel çağrı yeri). "Original"
    // seçiliyse int.MaxValue verilir — pratikte hiçbir görsel bu kadar büyük olmadığı için resize
    // hiç tetiklenmez, sadece JPEG/asla-büyütme mantığı devrede kalır.
    private static string BuildUrl(string fileName) => $"https://{Host}/{fileName}";

    // baseFileName = ROM dosya adıyla aynı kimlik (uzantısız) — görsel/ROM arasında 1-e-1 karşılık
    // kurar. Uzantı artık kaynak dosyadan değil, yeniden kodlama formatından geliyor (bkz.
    // DownloadAsync/preserveTransparency) — Logo (şeffaflık gerekli) PNG, diğerleri JPEG.
    public static string BuildLocalPath(string imagesRoot, string platform, string typeFolder, string baseFileName, bool preserveTransparency) =>
        Path.Combine(imagesRoot, platform, typeFolder, baseFileName + (preserveTransparency ? ".png" : ".jpg"));

    // Başarısızlıkta (ağ hatası, 404, disk hatası) false döner — çağıran taraf bunu bir özet
    // sayaca topluyor (bkz. RomImportViewModel'in per-item try/catch deseni). preserveTransparency:
    // Logo için PNG (alfa kanalı korunur), diğer türler için küçük/kayıplı JPEG (%90 kalite —
    // opak fotoğraf içerikte gözle fark edilmeyen bir kalite kaybı, PNG'ye göre çok daha küçük).
    // cancellationToken: kullanıcı "Durdur"a basarsa OperationCanceledException fırlatılır —
    // bilinçli olarak alttaki genel catch'e YAKALANMADAN yeniden fırlatılıyor, aksi halde iptal
    // sessizce "bu görsel indirilemedi" sayılıp döngü durmadan devam ederdi.
    public static async Task<bool> DownloadAsync(string fileName, string destinationPath, bool preserveTransparency, int maxDimension, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bytes = await Http.GetByteArrayAsync(BuildUrl(fileName), cancellationToken);
            var encoded = ResizeAndEncode(bytes, preserveTransparency, maxDimension);
            await File.WriteAllBytesAsync(destinationPath, encoded, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ResizeAndEncode(byte[] sourceBytes, bool preserveTransparency, int maxDimension)
    {
        using var sourceStream = new MemoryStream(sourceBytes);
        var decoder = BitmapDecoder.Create(sourceStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource resized = scale < 1.0 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;

        BitmapEncoder encoder = preserveTransparency ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(resized));

        using var outputStream = new MemoryStream();
        encoder.Save(outputStream);
        var encoded = outputStream.ToArray();

        // Küçük/basit görsellerde (ör. az renkli Clear Logo, düşük çözünürlüklü screenshot) yeniden
        // kodlama orijinalden BÜYÜK çıkabiliyor (PNG bu tür içerikte JPEG'den daha iyi sıkıştırıyor,
        // ölçek zaten 1.0 kalıyorsa küçültmenin de bir kazancı olmuyor) — doğrulandı (bkz. gerçek
        // dosyalarla manuel test). Bu durumda orijinal baytlar korunuyor, asla büyütülmüyor.
        return encoded.Length < sourceBytes.Length ? encoded : sourceBytes;
    }
}
