using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RetroAudit.Converters;

// Sağ detay panelinin sürgülü açılıp kapanmasını sağlar: true iken parametre (ör. "340")
// piksel genişliğine, false iken 0'a döner. ColumnDefinition (FrameworkContentElement
// olduğu için) normal {Binding} ile bağlanabiliyor, DataGridColumn'dan farklı olarak
// ekstra bir BindingProxy'ye gerek yok.
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var expanded = value is bool b && b;
        if (!expanded)
            return new GridLength(0);

        var width = parameter is string s && double.TryParse(s, out var parsed) ? parsed : 340;
        return new GridLength(width);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
