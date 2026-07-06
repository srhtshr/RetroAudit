using System.Globalization;
using System.Windows.Data;
using RetroAudit.Services;

namespace RetroAudit.Converters;

// Bir Region string'ini (ör. "USA", "Japan") Images/Flags altındaki bayrak dosyasının TAM yoluna
// çevirir (bkz. FlagResolver) — Sürümler kartı, Alternate Names ve toolbar'daki USA/EU/Japan
// düğmelerinde ortak kullanılıyor. Eşleşen bir bayrak yoksa null döner, bağlı Image görünmez
// olur (bkz. XAML'deki Visibility="{Binding ..., Converter={StaticResource NotNullToVisibility}}"
// benzeri kullanım YERİNE burada doğrudan Source null olunca WPF Image zaten boş kalır).
public class RegionToFlagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        FlagResolver.Resolve(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
