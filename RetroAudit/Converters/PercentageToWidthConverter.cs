using System.Globalization;
using System.Windows.Data;

namespace RetroAudit.Converters;

public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            int intValue => intValue,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            _ => 0d,
        };

        if (!double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxWidth))
            maxWidth = 64d;

        var normalized = Math.Clamp(percent, 0d, 100d) / 100d;
        return maxWidth * normalized;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
