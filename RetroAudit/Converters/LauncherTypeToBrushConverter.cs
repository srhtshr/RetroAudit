using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RetroAudit.Models;

namespace RetroAudit.Converters;

// "renkleri sütuna göre değil standalone ve retroarch a göre tanımla" (kullanıcı isteği) — Emülatörler
// tablosundaki Mod rozet-ComboBox'ının rengini LauncherType'a göre belirler. Önceki sürüm bir Style
// içindeki DataTrigger kullanıyordu; kullanıcı geri bildirimi "seçince renkler değişmiyor" — DOĞRUDAN
// bir Binding+Converter, Style/Trigger dolaylılığından kaçınıp her zaman güvenilir şekilde tepki verir.
public class LauncherTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isStandalone = value is LauncherType.StandaloneEXE;
        var key = isStandalone ? "Brush.Text.Secondary" : "Brush.Status.Ok";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
