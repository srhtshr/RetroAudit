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
        try
        {
            return File.Exists(DefaultSettingsPath) ? Import(DefaultSettingsPath) : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveDefault(AppSettings settings) => Export(settings, DefaultSettingsPath);
}
