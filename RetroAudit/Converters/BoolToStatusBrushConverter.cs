using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RetroAudit.Converters;

// DataGrid'deki Box/BG/SS nokta göstergeleri ve durum ikonu için ortak renk mantığı:
// true -> mor (Brush.Status.Ok), false -> kırmızı (Brush.Status.Missing).
// Renkler ObsidianDark.xaml içinde tanımlı; burada sadece anahtar seçiliyor, böylece
// tema değişse bile bu converter'ın değişmesi gerekmez.
public class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ok = value is bool b && b;
        var key = ok ? "Brush.Status.Ok" : "Brush.Status.Missing";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Durum ikonu (mor tik / kırmızı çarpı) için Segoe MDL2 Assets glyph karakterini seçer.
// Karakterler \u kaçış dizisiyle yazıldı çünkü görünmez Unicode özel-kullanım-alanı
// karakterleri metin editörleri/araçlar arasında bozulmaya açık.
public class BoolToStatusGlyphConverter : IValueConverter
{
    // Segoe MDL2 Assets glyphs: E73E = CheckMark, E711 = Cancel (X)
    private const string CheckGlyph = "\uE73E";
    private const string CancelGlyph = "\uE711";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ok = value is bool b && b;
        return ok ? CheckGlyph : CancelGlyph;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
