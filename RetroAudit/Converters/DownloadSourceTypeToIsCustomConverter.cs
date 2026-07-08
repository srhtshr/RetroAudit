using System.Globalization;
using System.Windows.Data;
using RetroAudit.Models;

namespace RetroAudit.Converters;

// Ayarlar > Komutlar sekmesindeki "Emülatör İndirme Kaynakları" listesinde Kaynak metin kutusunun
// IsEnabled'ı — BuiltInApi (ör. RPCS3'ün kendi özel JSON güncelleme API'si) seçiliyken Kaynak alanı
// SADECE bilgilendirme amaçlı gösterilir (gerçek istek koddan kuruluyor, sabit bir metin olamaz),
// bu yüzden salt-okunur/gri görünmesi gerekiyor — bkz. StandaloneEmulatorInstallerService.ResolveSourceAsync.
public class DownloadSourceTypeToIsCustomConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not DownloadSourceType.BuiltInApi;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
