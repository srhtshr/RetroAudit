using System.IO;
using System.Text.Json;
using RetroAudit.Models;

namespace RetroAudit.Services;

// AppSettings <-> JSON dönüşümünü yapan basit dosya tabanlı servis.
// Şu an tek sorumluluğu Export/Import Config butonlarının arkasındaki okuma/yazma işlemi;
// ileride uygulama açılışında otomatik ayar yükleme de buraya eklenebilir.
public static class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true, // dışa aktarılan JSON'un elle düzenlenebilir/okunabilir olması için
    };

    // Verilen ayarları belirtilen dosya yoluna biçimlendirilmiş JSON olarak yazar.
    public static void Export(AppSettings settings, string filePath)
    {
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
}
