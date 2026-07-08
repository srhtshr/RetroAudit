using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

namespace RetroAudit.Services;

// "RetroArch İndir & Kur" (bkz. SettingsViewModel.InstallRetroArchCommand, kullanıcı isteği:
// "retroarch'ı indirip kurma da eklenebiliyor mu ilgili klasöre?"). RetroArch açık kaynak ve
// serbestçe dağıtılabilir olduğu için resmi buildbot'undan (buildbot.libretro.com) doğrudan
// indiriliyor — bizim koda bir bağımlılığı yok, sadece dışarıdan indirip AppPaths.ThirdParty
// altına açtığımız, sonra Process.Start ile çalıştırdığımız ayrı bir program (WebView2 gibi).
// "nightly" yolu kasıtlı: stable klasör yapısı versiyon numarasına göre değişirken (ör.
// /stable/1.22.2/...), nightly her zaman AYNI sabit URL'de en güncel derlemeye işaret ediyor —
// versiyon numarasını ayrıca çözmemize gerek kalmıyor.
public static class RetroArchInstallerService
{
    private const string RetroArchUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/RetroArch.7z";
    private const string CoresUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/RetroArch_cores.7z";

    // Tam cores.7z (258 MB, ~200 çekirdeğin TAMAMI) yerine TEK bir çekirdeği indirmek için — RetroArch'ın
    // resmi buildbot'u her çekirdeği ayrıca da, küçük bir .zip (7z değil) olarak yayınlıyor (bkz.
    // DownloadCoreAsync, kullanıcı isteği: "corelarda da tek tıkla veya manuel indirme koysana tek
    // tek uğraşmasın").
    private const string SingleCoreBaseUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/";

    public static string InstallRoot => Path.Combine(AppPaths.ThirdParty, "RetroArch");

    // Kullanıcı isteği: "cores in içinde her platform için ayrı klasör oluştur hepsi kendi
    // klasöründen okusun" — indirilen/eklenen HER çekirdek artık cores\RetroAudit\{PlatformName}\
    // altında yaşıyor, aynı çekirdeği (ör. Genesis Plus GX) paylaşan platformlar kendi ayrı
    // kopyalarını tutar. CoresFolder, RetroArch'ın KENDİ bulk kurulumunun (RetroArch_cores.7z) ham/
    // kategorize-edilmemiş çıktısını içeren kök (bkz. FindCoreFileAnywhere) — PlatformCoresRoot ayrı
    // bir "RetroAudit\" alt klasörü, RetroArch'ın kendi dosyalarıyla KARIŞMASIN diye (kullanıcı
    // isteği: "cores\RetroAudit\Nintendo mesela").
    public static string CoresFolder => Path.Combine(InstallRoot, "cores");

    public static string PlatformCoresRoot => Path.Combine(CoresFolder, "RetroAudit");

    public static string PlatformCoresFolder(string platformName) => Path.Combine(PlatformCoresRoot, AppPaths.SanitizeFolderName(platformName));

    public record InstallResult(string ExecutablePath, string CoresFolder);

    // Kullanıcı isteği: "ilerleme çubuğu oynamıyor yapay gecikme ekleme yüzde göstersin" — kök neden
    // BULUNDU: indirme sırasında gerçek byte-bazlı yüzde raporlanıyordu ama arşiv AÇMA (7zr.exe dış
    // process'i, ör. ~258 MB'lık RetroArch_cores.7z için) aşamasında HİÇ ilerleme bilgisi yoktu — bar
    // dakikalarca sabit bir sayıda (ör. %95) donmuş görünüyordu. Yapay bir sayaç UYDURMAK yerine
    // (yanıltıcı olurdu) IsIndeterminate=true raporlanıyor — bar bu aşamalarda "çalışıyor, süresi
    // belirsiz" modunda akıyor, sahte bir yüzde göstermiyor.
    public static async Task<InstallResult> DownloadAndInstallAsync(IProgress<(string Message, double Percent, bool IsIndeterminate)>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.SevenZipExe))
            throw new InvalidOperationException("7zr.exe bulunamadı (ThirdParty\\7-Zip\\7zr.exe) — kurulum bozuk olabilir.");

        Directory.CreateDirectory(InstallRoot);
        var tempDir = Path.Combine(Path.GetTempPath(), "RetroAudit_RetroArchInstall_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var archivePath = Path.Combine(tempDir, "RetroArch.7z");
            progress?.Report(("RetroArch indiriliyor...", 0, false));
            await DownloadFileAsync(RetroArchUrl, archivePath, p => progress?.Report(("RetroArch indiriliyor...", p * 0.35, false)), cancellationToken);

            progress?.Report(("RetroArch açılıyor...", 35, true));
            await ExtractArchiveAsync(archivePath, InstallRoot, cancellationToken);

            var coresArchivePath = Path.Combine(tempDir, "RetroArch_cores.7z");
            progress?.Report(("Çekirdekler indiriliyor...", 40, false));
            await DownloadFileAsync(CoresUrl, coresArchivePath, p => progress?.Report(("Çekirdekler indiriliyor...", 40 + p * 0.55, false)), cancellationToken);

            var coresFolder = Path.Combine(InstallRoot, "cores");
            Directory.CreateDirectory(coresFolder);
            progress?.Report(("Çekirdekler açılıyor...", 95, true));
            await ExtractArchiveAsync(coresArchivePath, coresFolder, cancellationToken);

            // İki arşiv de genelde köke düz açılıyor ama RetroArch bazı sürümlerde bir alt klasöre
            // (ör. "RetroArch-Win64\") açılabiliyor — konumdan bağımsız olmak için recursive arıyoruz.
            var exePath = FindFile(InstallRoot, "retroarch.exe")
                ?? throw new InvalidOperationException("Arşiv açıldı ama retroarch.exe bulunamadı.");

            progress?.Report(("Tamamlandı", 100, false));
            return new InstallResult(exePath, coresFolder);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* geçici dosya, kritik değil */ }
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, Action<double>? onProgress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        long readBytes = 0;
        int bytesRead;
        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            readBytes += bytesRead;
            if (totalBytes > 0)
                onProgress?.Invoke((double)readBytes / totalBytes * 100);
        }
    }

    // 7zr.exe: 7-Zip'in resmi, tek dosyalık ("reduced", .dll bağımlısı olmayan) komut satırı
    // aracı — sadece .7z formatını açar, bizim ihtiyacımız için yeterli (bkz. AppPaths.SevenZipExe
    // yorumu). "x" = tam yol koruyarak aç, "-y" = üzerine yazma sorusu sorma.
    private static async Task ExtractArchiveAsync(string archivePath, string destinationFolder, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(AppPaths.SevenZipExe)
        {
            ArgumentList = { "x", archivePath, $"-o{destinationFolder}", "-y" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("7zr.exe başlatılamadı.");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Arşiv açılamadı (7zr.exe çıkış kodu {process.ExitCode}): {error}");
        }
    }

    private static string? FindFile(string root, string fileName) =>
        Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();

    // Ayarlar penceresi açıldığında ("indirilenler/indirilmeyenler belli olmalı" — kullanıcı isteği)
    // ve kaldırma sonrası, RetroArch'ın bizim kendi ThirdParty\RetroArch\ klasörümüze daha önce
    // kurulup kurulmadığını kontrol eder — EmulatorConfig.IsReady'den FARKLI: bu, hiçbir platform
    // satırına atanmamış olsa bile "İndir & Kur" butonunun kendi durumunu bilmesi için.
    public static bool IsInstalled() => GetInstalledExecutablePath() is not null;

    // SettingsViewModel'in başlangıçta "kurulu ama satırlara atanmamış" durumu (ör. kurulum
    // sırasında "Kaydet"e basılmadan pencere kapatılmışsa) fark edip otomatik düzeltebilmesi için —
    // bkz. SettingsViewModel.ReconcileRetroArchPaths.
    public static string? GetInstalledExecutablePath() =>
        Directory.Exists(InstallRoot) ? FindFile(InstallRoot, "retroarch.exe") : null;

    // "hepsi kendi klasöründen okusun" (kullanıcı isteği) — artık SADECE bu platformun kendi
    // cores\RetroAudit\{Platform}\ klasörüne bakar, recursive global arama YOK. Bu klasörden ÖNCEKİ
    // (düz cores\ kökü veya cores\RetroArch-Win64\cores\ gibi) konumlardan göç, bir kerelik
    // SettingsViewModel.MigrateCoreFilesToPlatformFolders'ın işi — bir getter'ın disk yazması riskli
    // olduğu için burada değil.
    public static string? FindCoreFile(string platformName, string coreFileName)
    {
        var filePath = Path.Combine(PlatformCoresFolder(platformName), coreFileName);
        return File.Exists(filePath) ? filePath : null;
    }

    // SADECE MigrateCoreFilesToPlatformFolders ve InstallRetroArch'ın bulk-kurulum sonrası
    // dağıtımı için — cores\ kökü ALTINDA (platform klasörleri dahil) bu dosya adını RECURSIVE arar.
    public static string? FindCoreFileAnywhere(string coreFileName) =>
        Directory.Exists(CoresFolder) ? FindFile(CoresFolder, coreFileName) : null;

    // Tek bir çekirdeği (ör. "mame_libretro.dll") resmi buildbot'un tekil-core klasöründen indirir
    // (bkz. SingleCoreBaseUrl) — tüm cores.7z'yi yeniden indirmeden, sadece bir platformun eksik
    // çekirdeğini tamamlamak için. Arşiv düz bir .zip (RetroArch.7z/RetroArch_cores.7z'nin aksine
    // .7z DEĞİL) olduğu için 7zr.exe'ye ihtiyaç yok, System.IO.Compression yeterli. Doğrudan bu
    // platformun kendi klasörüne açılır.
    public static Task<string> DownloadCoreAsync(string platformName, string coreFileName, CancellationToken cancellationToken) =>
        DownloadCoreToFolderAsync(PlatformCoresFolder(platformName), coreFileName, cancellationToken);

    // Kullanıcı isteği: "önerilen ve alternatif olanları haricindekileri retroarch ın core klasöründen
    // okusun bizim klasörde karışıklık olmasın ... download edecez çünkü olmayanları" — katalogdaki
    // (Tercih Edilen/Alternatif DIŞINDaki) bir çekirdek indirilince de bizim TEMİZ cores\RetroAudit\
    // {Platform}\ klasörüne DEĞİL, RetroArch'ın kendi ham cores\ köküne iner — FindCoreFileAnywhere
    // onu orada zaten bulur.
    public static Task<string> DownloadCoreAsync(string coreFileName, CancellationToken cancellationToken) =>
        DownloadCoreToFolderAsync(CoresFolder, coreFileName, cancellationToken);

    private static async Task<string> DownloadCoreToFolderAsync(string destinationFolder, string coreFileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationFolder);

        var tempDir = Path.Combine(Path.GetTempPath(), "RetroAudit_Core_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var archivePath = Path.Combine(tempDir, "core.zip");
            await DownloadFileAsync($"{SingleCoreBaseUrl}{coreFileName}.zip", archivePath, null, cancellationToken);

            ZipFile.ExtractToDirectory(archivePath, destinationFolder, overwriteFiles: true);

            return FindFile(destinationFolder, coreFileName)
                ?? throw new InvalidOperationException($"{coreFileName} indirildi ama arşivden çıkarıldıktan sonra bulunamadı.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* geçici dosya, kritik değil */ }
        }
    }

    // "İndir & Kur"un tersi (kullanıcı isteği: "kaldırma butonu ekle") — ThirdParty\RetroArch\
    // klasörünü tamamen siler. Hangi EmulatorConfig satırlarının bu yola işaret ettiğini temizlemek
    // SettingsViewModel.UninstallRetroArch'ın işi (bu servis sadece dosya sistemiyle ilgilenir).
    public static void Uninstall()
    {
        if (Directory.Exists(InstallRoot))
            Directory.Delete(InstallRoot, recursive: true);
    }
}
