using System.IO;
using System.Text.Json;
using RetroAudit.Models;

namespace RetroAudit.Services;

// AppSettings <-> JSON dönüşümünü yapan basit dosya tabanlı servis.
// Export/Import Config butonlarının arkasındaki okuma/yazma işlevine ek olarak, uygulama
// açılışında/Ayarlar kapanışında otomatik yükleme-kaydetme için sabit bir varsayılan dosya yolu
// da sağlıyor (bkz. LoadDefault/SaveDefault) — Ayarlar > Arayüz sekmesindeki
// ContextMenuDisplayMode gibi tercihlerin kalıcı olması için gerekliydi.
public static class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true, // dışa aktarılan JSON'un elle düzenlenebilir/okunabilir olması için
    };

    public static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroAudit", "settings.json");

    // RetroAuditDataPath hiç ayarlanmamışsa (ilk çalıştırma) kullanıcıyı Ayarlar'a gitmeye
    // zorlamak yerine otomatik oluşturulan varsayılan konum — RetroAuditUserData.db ile aynı
    // %LocalAppData%\RetroAudit kökü altında. Kullanıcı büyük arşivler için (PS2/PS3/Wii vb.)
    // dilerse Ayarlar > Genel'den başka bir sürücü/klasör seçebilir; bu sadece "hiç seçilmemiş"
    // durumundaki başlangıç değeri.
    private static readonly string DefaultDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroAudit", "Data");

    // Verilen ayarları belirtilen dosya yoluna biçimlendirilmiş JSON olarak yazar.
    public static void Export(AppSettings settings, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(filePath, json);
    }

    // Belirtilen JSON dosyasını okuyup AppSettings nesnesine çevirir.
    // Dosya bozuksa veya boşsa, varsayılan değerlerle boş bir AppSettings döner (uygulamayı çökertmez).
    public static AppSettings Import(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    // Uygulama açılışında çağrılır (bkz. MainViewModel). Dosya yoksa (ilk çalıştırma) ya da
    // bozuksa varsayılan AppSettings döner — hiçbir zaman istisna fırlatmaz.
    public static AppSettings LoadDefault()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(DefaultSettingsPath) ? Import(DefaultSettingsPath) : new AppSettings();
        }
        catch
        {
            settings = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(settings.RetroAuditDataPath))
        {
            Directory.CreateDirectory(DefaultDataPath);
            settings.RetroAuditDataPath = DefaultDataPath;
            SaveDefault(settings);
        }

        return settings;
    }

    public static void SaveDefault(AppSettings settings) => Export(settings, DefaultSettingsPath);
}
