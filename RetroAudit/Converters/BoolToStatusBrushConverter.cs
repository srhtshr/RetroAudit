using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RetroAudit.Converters;

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
