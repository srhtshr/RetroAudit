using System.IO;

namespace RetroAudit.Services;

// Uygulamanın taşınabilir (portable) veri kökü — her zaman çalıştırılabilir dosyanın (.exe)
// bulunduğu klasörün yanında, kullanıcı tarafından değiştirilemez. Kullanıcı kararı: harici bir
// sürücü seçme esnekliği yerine tek-klasör taşınabilirlik tercih edildi (bkz. plan). Alt klasörler
// burada bir kere, eminlik için eagerly oluşturuluyor.
public static class AppPaths
{
#if DEBUG
    // Geliştirme sırasında derleme çıktısı bin\Debug\{tfm}\ içinde gömülü kalıyor — bulması zor
    // oluyordu (bkz. kullanıcı geri bildirimi). Üç seviye yukarı çıkıp proje klasörüne ulaşıyoruz;
    // gerçek bir yayınlanmış (RELEASE) kurulumda bu adım atlanır, kök doğrudan .exe'nin klasörüdür.
    // Path.Combine + GetFullPath kullanılıyor (Directory.GetParent değil) — BaseDirectory'nin
    // sonundaki "\" ile GetParent/.Parent zincirinde bir seviye "kayboluyordu" (normalize adımı
    // gerçek bir yukarı çıkış saymıyordu), ".." tabanlı çözümleme bu tuzağa düşmüyor.
    public static string Root { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
#else
    public static string Root { get; } = AppContext.BaseDirectory;
#endif
    public static string Games { get; } = Path.Combine(Root, "Games");
    public static string Images { get; } = Path.Combine(Root, "Images");
    public static string Metadata { get; } = Path.Combine(Root, "Metadata");
    public static string Emulation { get; } = Path.Combine(Root, "Emulation");

    // Görseli olmayan oyunlar için sabit yer tutucular (Images/NoImage altında elle eklenmiş
    // hazır dosyalar) — bkz. Game.BoxDisplayPath vb. Klasör repoyla birlikte geldiği için burada
    // eagerly oluşturulmuyor, Games/Images/Metadata/Emulation'ın aksine.
    public static string NoImageCover { get; } = Path.Combine(Images, "NoImage", "Cover.png");
    public static string NoImageBackground { get; } = Path.Combine(Images, "NoImage", "Background.png");
    public static string NoImageLogo { get; } = Path.Combine(Images, "NoImage", "Logo.png");

    // LaunchBox'ın gamesdb.launchbox-app.com/img/flags/ kaynağından indirilen region/dil bayrakları
    // (bkz. FlagResolver) — Images/Flags altında elle eklenmiş hazır dosyalar, diğer NoImage*
    // gibi burada eagerly oluşturulmuyor.
    public static string Flags { get; } = Path.Combine(Images, "Flags");

    // Gameplay ekranındaki embedded YouTube oynatıcısının statik dosyaları (player.html + Plyr
    // kütüphanesi, bkz. MainWindow.xaml.cs PlayYouTubeEmbedAsync) — repoyla birlikte gelen,
    // hiç değişmeyen dosyalar, NoImage*/Flags gibi burada eagerly oluşturulmuyor.
    public static string YouTubePlayerAssets { get; } = Path.Combine(Root, "Assets", "YouTubePlayer");

    static AppPaths()
    {
        Directory.CreateDirectory(Games);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(Metadata);
        Directory.CreateDirectory(Emulation);
    }
}
