using System.Globalization;
using System.Windows.Data;
using RetroAudit.Services;

namespace RetroAudit.Converters;

// Bir Region string'ini (ör. "USA", "Japan") Images/Flags altındaki bayrak dosyasının TAM yoluna
// çevirir (bkz. FlagResolver) — Sürümler kartı, Alternate Names ve toolbar'daki USA/EU/Japan
// düğmelerinde ortak kullanılıyor. Eşleşen bir bayrak yoksa null döner, bağlı Image görünmez
// olur (bkz. XAML'deki Visibility="{Binding ..., Converter={StaticResource NotNullToVisibility}}"
// benzeri kullanım YERİNE burada doğrudan Source null olunca WPF Image zaten boş kalır).
//
// Kullanıcı bulgusu: "tam ekrandayken daha çok yapıyor bayraklardan dolayı olabilir mi" — DOĞRUYDU.
// Eskiden burada sadece yol STRING'i döndürülüyordu, WPF'in düz string->ImageSource dönüştürücüsü
// (DecodePixelWidth/önbellek YOK) her satır geri dönüşümünde tam çözünürlükte sıfırdan decode
// ediyordu — Logo sütununda daha önce düzeltilen AYNI sorun. Artık ThumbnailImageConverter.Resolve
// (AYNI küçük-decode + önbellek) kullanılıyor; bayrak dosyası sayısı (bir avuç ülke) çok az olduğu
// için ilk birkaç satırdan sonra pratikte her satır cache hit oluyor.
public class RegionToFlagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThumbnailImageConverter.Resolve(FlagResolver.Resolve(value as string), 28);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
