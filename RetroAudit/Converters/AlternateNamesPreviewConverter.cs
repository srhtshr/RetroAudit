using System.Globalization;
using System.Windows.Data;

namespace RetroAudit.Converters;

// ManualLinkWindow'daki arama sonuçları listesinde her oyunun altında (varsa) LaunchBox alternatif
// isimlerini küçük gri bir alt satır olarak göstermek için — values[0]: Game.GameId (int),
// values[1]: ManualLinkViewModel.AlternateNamesByGameId (IReadOnlyDictionary<int, List<string>>).
public sealed class AlternateNamesPreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [int gameId, IReadOnlyDictionary<int, List<string>> namesByGameId]
            && namesByGameId.TryGetValue(gameId, out var names) && names.Count > 0)
        {
            return string.Join(" · ", names);
        }
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
