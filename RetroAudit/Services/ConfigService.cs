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

    // Kullanıcı isteği: "onu atalım program içinde bi yere default olarakta ayarla yollarını
    // ordan görsün ... daha düşük boyutlu 2. bi db yapamazmıyız direk launchbox ın o dosyası
    // olmasın diye" — MetadataProviderViewModel.ReMatchSelected artık LaunchBox'ın kendi ham
    // (400MB, 10 tablo, çoğu kullanılmayan DOS/emulator alanı) dosyasına değil, ondan bir kez
    // damıtılmış (MasterMetadataReader'ın FİİLEN okuduğu 4 tablo/sütun + GameImages sadece
    // Box-Front/Screenshot-Gameplay/Clear Logo satırları) küçük bir kopyaya bakıyor — AppPaths.
    // Metadata altında, RetroAudit.db/RetroAuditUserData.db ile AYNI taşınabilir klasörde.
    private static readonly string DefaultMasterMetadataDbPath = Path.Combine(AppPaths.Metadata, "MasterMetadata.db");

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

        if (string.IsNullOrWhiteSpace(settings.MasterMetadataDbPath) && File.Exists(DefaultMasterMetadataDbPath))
            settings.MasterMetadataDbPath = DefaultMasterMetadataDbPath;

        return settings;
    }

    public static void SaveDefault(AppSettings settings) => Export(settings, DefaultSettingsPath);
}
