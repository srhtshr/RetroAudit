using System.Globalization;
using System.IO;
using System.Linq;
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
// gelmek gibi) decode'u SIFIRDAN tekrarlıyordu. En son kullanılan ~300 thumbnail'i (birkaç ekran
// dolusu satırı karşılayacak kadar) burada tutuyoruz — kütüphanede ~67 bin oyun olduğu için
// SINIRSIZ bir cache tüm listeyi kaydırınca gigabaytlarca belleğe çıkardı, bu yüzden kapasite
// dolunca en eski (en uzun süredir kullanılmayan) girdi atılıyor. Converter her zaman UI thread'inde
// (DataGrid'in kendi binding motoru) çağrıldığı için kilitleme (lock) gerekmiyor.
public class ThumbnailImageConverter : IValueConverter
{
    private const int MaxCacheEntries = 300;

    // Değer: (Bitmap, kendi LinkedListNode'u) — node, O(1) ile "en son kullanılan" konumuna
    // taşınabilsin diye burada tutuluyor (bkz. Convert, cache hit dalı).
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Bitmap)>> Cache = new();
    private static readonly LinkedList<(string Key, BitmapImage Bitmap)> RecencyOrder = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var decodePixelWidth = parameter is string s && int.TryParse(s, out var width) ? width : 128;
        var cacheKey = $"{path}|{decodePixelWidth}";

        if (Cache.TryGetValue(cacheKey, out var existingNode))
        {
            // Cache hit: "en son kullanılan" ucuna taşı, sonraki temizlikte en son silinecek olsun.
            RecencyOrder.Remove(existingNode);
            RecencyOrder.AddLast(existingNode);
            return existingNode.Value.Bitmap;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var node = RecencyOrder.AddLast((cacheKey, bitmap));
        Cache[cacheKey] = node;

        if (Cache.Count > MaxCacheEntries)
        {
            var oldest = RecencyOrder.First!;
            RecencyOrder.RemoveFirst();
            Cache.Remove(oldest.Value.Key);
        }

        return bitmap;
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
