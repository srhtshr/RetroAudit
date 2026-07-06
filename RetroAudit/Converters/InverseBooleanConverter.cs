using System.Globalization;
using System.Windows.Data;

namespace RetroAudit.Converters;

// Aynı bool'a bağlı iki RadioButton'dan "false" tarafını temsil eder (ör. Ayarlar'daki
// Sürüm gösterimi seçimi) — EnumToBoolConverter'ın aksine ConvertBack de bool döner, enum
// bekleyip hata fırlatmaz.
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
