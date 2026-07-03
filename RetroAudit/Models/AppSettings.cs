namespace RetroAudit.Models;

// Tek bir platform için emülatör başlatma ayarları (exe yolu + komut satırı parametreleri).
// "%ROM%" gibi bir yer tutucu, gerçek başlatma mantığı yazıldığında rom dosya yolu ile değiştirilecek.
public class EmulatorConfig
{
    public string PlatformName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Parameters { get; set; } = "%ROM%";
}

// Uygulamanın tüm kalıcı ayarlarını tutan kök nesne.
// Export/Import Config butonları bu sınıfı doğrudan JSON'a serileştirip geri okur.
public class AppSettings
{
    // LaunchBox kurulumunun kök dizini; ileride ROM/medya taraması bu yoldan başlayacak.
    public string LaunchBoxRootPath { get; set; } = string.Empty;

    // Platform başına bir emülatör kaydı.
    public List<EmulatorConfig> Emulators { get; set; } = new();

    // Aynı oyunun birden fazla bölge sürümü bulunduğunda hangisinin tercih edileceğini belirleyen sıra.
    // Liste başındaki bölge en yüksek önceliğe sahiptir (varsayılan: USA > EU > JP).
    public List<string> RegionPriority { get; set; } = new() { "USA", "EU", "JP" };
}
