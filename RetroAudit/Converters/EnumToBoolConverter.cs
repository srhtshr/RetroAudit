using System.Globalization;
using System.Windows.Data;

namespace RetroAudit.Converters;

// Bir enum değerini RadioButton grubuna bağlamanın standart WPF yolu: her RadioButton, kendi
// ConverterParameter'ı (enum üye adı, string) o anki değere eşitse IsChecked=true olur; işaretlenen
// RadioButton'a tıklanınca ConvertBack aynı ismi enum'a çevirip geri yazar.
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
