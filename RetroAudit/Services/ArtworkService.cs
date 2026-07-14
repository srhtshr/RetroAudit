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

    // İKİNCİ kaynak (kullanıcı isteği: "ilk kaynaktan indiremediğini diğer taraftan çekecek") —
    // libretro-thumbnails (GitHub, RetroArch'ın resmi thumbnail arşivi). LaunchBox'ın (1. kaynak)
    // ArtworkAssets'te hiç kaydı olmayan (ör. Commodore 64/Atari 2600'de sık görülen) ya da kaydı
    // olup da indirilemeyen (404 vb.) görseller için devreye giriyor. Platform başına AYRI bir repo
    // (ör. "Nintendo - Nintendo Entertainment System" -> Nintendo_-_Nintendo_Entertainment_System,
    // sadece boşluk->alt çizgi), dosya adları No-Intro/TOSEC DAT adlarıyla BİREBİR aynı (ör.
    // "10-Yard Fight (Japan) (Rev 1).png") — doğrulandı (bkz. gerçek repo içeriği). Bu yüzden
    // kataloğun kendi RawDatName'i (GameVersion.RawDatName) doğrudan dosya adı olarak kullanılabiliyor,
    // LaunchBox'takine benzer ayrı bir ID eşleştirme katmanına gerek yok.
    private const string LibretroThumbnailsOrg = "libretro-thumbnails";

    private static string? BuildLibretroThumbnailUrl(string platform, string type, string rawDatName)
    {
        var typeFolder = type switch
        {
            "Box" => "Named_Boxarts",
            "Logo" => "Named_Logos",
            "SS" => "Named_Snaps",
            _ => null,
        };
        if (typeFolder is null || string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(rawDatName))
            return null;

        var repo = platform.Replace(' ', '_');
        var fileName = Uri.EscapeDataString(rawDatName + ".png");
        return $"https://raw.githubusercontent.com/{LibretroThumbnailsOrg}/{repo}/master/{typeFolder}/{fileName}";
    }

    // İmzası DownloadAsync ile bilerek AYNI şekilde (fileName yerine platform+rawDatName) — çağıran
    // taraf (bkz. MainViewModel.DownloadArtworkAsync) ikisini de AYNI sonraki adımla (ResizeAndEncode
    // + kayıt) çağırıyor, kullanıcı isteği: "aynı formatta indirmesi lazım şuanki ilk kaynak gibi".
    public static async Task<bool> DownloadFromLibretroThumbnailsAsync(string platform, string type, string rawDatName, string destinationPath, bool preserveTransparency, int maxDimension, CancellationToken cancellationToken = default)
    {
        var url = BuildLibretroThumbnailUrl(platform, type, rawDatName);
        if (url is null)
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
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

    // Tarayıcının kendi fetch()'i ile alınan ham baytları boyutlandırıp diske yazar.
    // MediaSearchWindow, korumalı/çerez gerektiren sitelerde HttpClient yerine
    // bunu kullanır (bkz. HandleCustomImageDownloadAsync).
    public static async Task<bool> ProcessAndSaveAsync(byte[] sourceBytes, string destinationPath, bool preserveTransparency, int maxDimension)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var encoded = ResizeAndEncode(sourceBytes, preserveTransparency, maxDimension);
            await File.WriteAllBytesAsync(destinationPath, encoded);
            return true;
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
        if (preserveTransparency)
            return encoded; // Şeffaflık garantisi için Logo'larda her zaman kodlanmış PNG dönülmeli
        return encoded.Length < sourceBytes.Length ? encoded : sourceBytes;
    }
}
