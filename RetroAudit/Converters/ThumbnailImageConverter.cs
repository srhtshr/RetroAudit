using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RetroAudit.Converters;

// DataGrid'de küçük bir hücrede gösterilen görseller (ör. Logo sütunu) için — düz string->
// ImageSource bağlaması BitmapImage'i kaynak dosyanın TAM çözünürlüğünde decode ediyordu (görsel
// getirdikten sonra 600px'e kadar çıkabiliyor), sonra ekranda ~28px'e küçültülüyordu. Bu, hızlı
// kaydırırken (VirtualizationMode="Recycling" — her yeni satır kendi decode'unu tetikliyor) gözle
// görülür bir performans sorununa yol açtı (kullanıcı geri bildirimi). DecodePixelWidth, decoder'a
// hedef boyutu baştan söyleyip gereksiz büyük bir bitmap'i belleğe hiç almadan küçük decode
// yapılmasını sağlıyor — hem CPU hem bellek kazancı. CacheOption.OnLoad: decode biter bitmez
// dosya tutamacı serbest bırakılır (aksi halde dosya "kaydet üstüne" senaryolarında kilitli kalabilirdi).
//
// LRU cache: Recycling virtualization aynı satıra geri dönüldüğünde (aşağı kaydırıp tekrar yukarı
// gelmek gibi) decode'u SIFIRDAN tekrarlıyordu. Kullanıcı bulgusu: "hızlı kaydırırken bazı
// noktalarda takılma" — IsAsync=True (Binding) denendi ama görsel bir an boş gelip SONRA "pop"
// ederek beliriyordu ("orjinalliği gidiyor") — geri alındı. Asıl çözüm: kütüphanedeki TÜM gerçek
// logo dosyalarını kapsayacak kadar büyük bir kapasite + kütüphane yüklendikten sonra bunları
// arka planda ÖNCEDEN decode edip önbelleğe yazan bir ön ısıtma (bkz. PrewarmAsync, MainViewModel.
// PrewarmThumbnailCache) — kullanıcı bir satıra kaydırdığında görsel ZATEN önbellekte, senkron/
// anlık gösteriliyor, hiç "pop" olmuyor. Artık HEM UI thread'inden (Convert) HEM arka plan
// thread'inden (PrewarmAsync) erişildiği için paylaşılan durum bir lock ile korunuyor.
//
// Kullanıcı isteği: "sen onu yükselt çünkü diğer platformlarda varya ona göre kapasite oluştur" —
// sabit bir sayı (ör. şu an diskte gerçekten var olan logo sayısı) YANLIŞ tercih: kullanıcı zamanla
// başka platformlar için de görsel indirdikçe bu sayı büyüyecek. Bu yüzden kapasite artık sabit
// DEĞİL — MainViewModel her açılışta o an diskte GERÇEKTEN kaç logo varsa (+ pay bırakarak)
// EnsureCapacity ile yükseltiyor; asla küçültülmüyor, otomatik olarak kütüphaneyle birlikte büyüyor.
public class ThumbnailImageConverter : IValueConverter
{
    private const int DefaultMaxCacheEntries = 3000;
    private static int maxCacheEntries = DefaultMaxCacheEntries;

    private static readonly object CacheLock = new();

    public static void EnsureCapacity(int minCapacity)
    {
        lock (CacheLock)
        {
            if (minCapacity > maxCacheEntries)
                maxCacheEntries = minCapacity;
        }
    }

    // Değer: (Bitmap, kendi LinkedListNode'u) — node, O(1) ile "en son kullanılan" konumuna
    // taşınabilsin diye burada tutuluyor (bkz. Convert, cache hit dalı).
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Bitmap)>> Cache = new();
    private static readonly LinkedList<(string Key, BitmapImage Bitmap)> RecencyOrder = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var decodePixelWidth = parameter is string s && int.TryParse(s, out var width) ? width : 128;
        return Resolve(value as string, decodePixelWidth);
    }

    // Kullanıcı bulgusu: "tam ekrandayken daha çok yapıyor bayraklardan dolayı olabilir mi" —
    // haklıydı: Bölge sütunundaki bayrak <Image>'ı bu converter'ı DEĞİL, WPF'in düz string->
    // ImageSource dönüştürücüsünü kullanıyordu (RegionToFlagConverter sadece bir yol string'i
    // döndürüyordu) — DecodePixelWidth YOK, önbellek YOK, her satır geri dönüşümünde tam
    // çözünürlükte, sıfırdan decode. Artık RegionToFlagConverter da bu AYNI önbellekli/küçük-decode
    // yolunu (Resolve) kullanıyor — bayrak dosyası sayısı zaten çok az (bir avuç ülke) olduğu için
    // ilk birkaç satırdan sonra pratikte HER satır cache hit oluyor.
    public static BitmapImage? Resolve(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var cacheKey = $"{path}|{decodePixelWidth}";

        // Kullanıcı bulgusu: "tabloda kaydırırken takılmalar oluyor" — File.Exists eskiden cache
        // kontrolünden ÖNCE, yani HER binding değerlendirmesinde (Recycling virtualization'da her
        // satır geri dönüşünde) çalışıyordu — cache HIT'lerde bile gereksiz bir disk stat çağrısı.
        // Artık önce cache'e bakılıyor, disk sadece gerçek bir cache miss'te kontrol ediliyor.
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var existingNode))
            {
                // Cache hit: "en son kullanılan" ucuna taşı, sonraki temizlikte en son silinecek olsun.
                RecencyOrder.Remove(existingNode);
                RecencyOrder.AddLast(existingNode);
                return existingNode.Value.Bitmap;
            }
        }

        if (!File.Exists(path))
            return null;

        var bitmap = DecodeAndFreeze(path, decodePixelWidth);
        StoreInCache(cacheKey, bitmap);
        return bitmap;
    }

    private static BitmapImage DecodeAndFreeze(string path, int decodePixelWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void StoreInCache(string cacheKey, BitmapImage bitmap)
    {
        lock (CacheLock)
        {
            if (Cache.ContainsKey(cacheKey))
                return;

            var node = RecencyOrder.AddLast((cacheKey, bitmap));
            Cache[cacheKey] = node;

            if (Cache.Count > maxCacheEntries)
            {
                var oldest = RecencyOrder.First!;
                RecencyOrder.RemoveFirst();
                Cache.Remove(oldest.Value.Key);
            }
        }
    }

    // Kullanıcı isteği: "cache sistemi mi yapsak" — kütüphane yüklendikten sonra ÇAĞRILIR (bkz.
    // MainViewModel), arka plan thread'inde (Task.Run içinden) çalışması BEKLENİR — UI thread'i
    // hiç bloklamaz. Zaten önbellekte olanlar atlanır (TryAdd benzeri kontrol), her dosya için
    // decode BAĞIMSIZ try/catch'le sarılıyor (bozuk/eksik bir dosya tüm ön ısıtmayı durdurmasın).
    public static void PrewarmAsync(IEnumerable<(string Path, int DecodePixelWidth)> items, CancellationToken cancellationToken = default)
    {
        foreach (var (path, decodePixelWidth) in items)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var cacheKey = $"{path}|{decodePixelWidth}";
            lock (CacheLock)
            {
                if (Cache.ContainsKey(cacheKey))
                    continue;
            }

            if (!File.Exists(path))
                continue;

            try
            {
                var bitmap = DecodeAndFreeze(path, decodePixelWidth);
                StoreInCache(cacheKey, bitmap);
            }
            catch
            {
                // Bozuk/kilitli bir görsel dosyası ön ısıtmanın geri kalanını durdurmasın —
                // Convert() zaten bu dosyaya normal (satır görünür olduğunda) tekrar deneyecek.
            }
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // Kırpma/kaydetme (bkz. CropEditorViewModel) bir görseli AYNI dosya yoluna geri yazdığında
    // (path değişmiyor, sadece içerik) bu cache olmadan WPF eski (kırpılmamış) bitmap'i göstermeye
    // devam ederdi — path zaten cache'te olduğu için Convert hiç yeniden decode etmezdi. Aynı path
    // farklı DecodePixelWidth'lerle (ör. grid 128, detay paneli farklı) birden fazla kez cache'e
    // girmiş olabileceğinden TÜM eşleşen anahtarlar (path'e bakılmaksızın "|" öncesi eşleşenler)
    // temizlenir.
    public static void Invalidate(string path)
    {
        lock (CacheLock)
        {
            var keysToRemove = Cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove)
            {
                if (Cache.TryGetValue(key, out var node))
                {
                    RecencyOrder.Remove(node);
                    Cache.Remove(key);
                }
            }
        }
    }
}
