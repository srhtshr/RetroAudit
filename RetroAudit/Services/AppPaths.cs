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

    // LaunchBox'ın kendi kurulumundaki AYNI isimlendirme (bkz. kullanıcı isteği: "ThirdParty
    // klasörüne portable atacak dimi") — RetroAudit'in KENDİ indirip yönettiği harici araçlar
    // burada yaşar: repoyla gelen sabit araçlar (ör. ThirdParty/7-Zip/7zr.exe, bkz. SevenZipExe)
    // VE kullanıcının "İndir & Kur" ile çalışma zamanında indirdiği şeyler (ör. ThirdParty/
    // RetroArch/, bkz. RetroArchInstallerService) aynı kökün altında.
    public static string ThirdParty { get; } = Path.Combine(Root, "ThirdParty");

    // Kullanıcı isteği: "emulation klasörünü de thirdparty içine al ... standalonelar sadece emulation
    // klasörüne indirip ordan algılasın" — Games/Images/Metadata gibi KÖK seviyede AYRI durmuyor
    // artık, ThirdParty'nin altında. TÜM standalone emülatörler burada yaşar: hem otomatik kurulumu
    // olanlar (PCSX2/RPCS3/Xemu, kendi emulatorId klasörü — bkz. StandaloneEmulatorInstallerService.
    // InstallRootFor) HEM DE Gözat-only olanlar (Dolphin, Cemu vb. — kullanıcının kendi kurduğu,
    // platform adına göre klasörlenmiş, bkz. BrowseEmulatorPath/AddCustomCore). RetroArch çekirdekleri
    // BURADA DEĞİL — onlar ThirdParty\RetroArch\cores\ altında ayrı yaşıyor (bkz. RetroArchInstallerService).
    public static string Emulation { get; } = Path.Combine(ThirdParty, "Emulation");

    // 7-Zip'in resmi "reduced" (sadece .7z formatını açan, tek dosyalık, kurulum gerektirmeyen)
    // komut satırı aracı — RetroArch.7z/RetroArch_cores.7z'yi açmak için. LaunchBox'ın kendi
    // ThirdParty\7-Zip\ klasörüne 7z.exe+7z.dll koyup aynı şeyi yaptığı görüldü (bkz. kullanıcı
    // isteği: "launchboxdaki sisteme baksana nasıl yapmış") — biz .dll'e bağımlı olmayan, tek
    // dosyalık 7zr.exe'yi tercih ettik (7-zip.org/GitHub'dan indirilip repoya eklendi).
    public static string SevenZipExe { get; } = Path.Combine(Root, "ThirdParty", "7-Zip", "7zr.exe");

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
        Directory.CreateDirectory(ThirdParty);
    }

    // Platform adlarından klasör adı üretirken kullanılan ortak yardımcı (bkz. RetroArchInstallerService.
    // PlatformCoresFolder, SettingsViewModel.EnsureEmulationPlatformFolders) — şu an hiçbir platform adı
    // geçersiz Windows dosya adı karakteri içermiyor (bkz. CanonicalEmulatorDefinitions), yine de güvenceye alınıyor.
    public static string SanitizeFolderName(string name)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidChar, '_');
        return name;
    }
}
