using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using RetroAudit.Models;

namespace RetroAudit.Services;

// "Standalonelar içinde indirme koyacaz ayrıca" (kullanıcı isteği) — RetroArchInstallerService'in
// StandaloneEXE tarafına karşılığı.
//
// Görev talimatı: "download altyapısını tüm standalone emulatorlar için generic hale getir ...
// emulator bazlı kopya kod yazılmasın ... API kısmında API linki, API yoksa Direct URL — tek sütun
// yeterli" — ARTIK HER emülatörün "hangi URL'den indirileceği" TAMAMEN veri odaklı: TEK bir kaynak
// (AppSettings.EmulatorDownloadSources'teki bu emülatöre ait Tür+Kaynak — GitHubReleases: "owner/repo"
// → ResolveGitHubReleaseWindowsAssetAsync ile TEK, PAYLAŞILAN bir asset-seçim algoritması; DirectUrl:
// aynen kullanılır) kontrol edilir — bkz. ResolveSourceAsync. Bu ayar UI'dan (Ayarlar > Komutlar)
// DÜZENLENEBİLİR ve HER indirmede DİSKTEN TAZE okunur (bkz. SettingsViewModel) — "kaynak değiştirilirse
// program yeniden derlenmeden kullanılabilsin" talimatı gereği hiçbir kaynak artık koda GÖMÜLÜ değil.
//
// TEK istisna: RPCS3 — GitHub Releases kullanmıyor, kendi özel JSON güncelleme API'sini kullanıyor
// (bkz. ResolveRpcs3DownloadUrlAsync) — bu, gerçek dünyada var olan tek bir emülatöre özgü,
// kaçınılmaz bir farklılık (RPCS3 hiç GitHub Releases'e dağıtım yapmıyor). Kaynak alanı BOŞ bırakılırsa
// bu özel API kullanılır; admin doldurursa (GitHubReleases veya DirectUrl) o değer ONU geçersiz kılar.
public static class StandaloneEmulatorInstallerService
{
    public record InstallResult(string ExecutablePath);

    // Bu emülatör için Kaynak alanı boş/geçersiz olduğu VE (RPCS3 için) özel API'nin de bir sonuç
    // üretemediği durumda fırlatılır — SettingsViewModel bunu ayrıca yakalayıp "Kaynak tanımlı değil"
    // durumunu gösterir, genel hata mesajıyla KARIŞTIRMAZ.
    public sealed class ManualDownloadUrlRequiredException : Exception
    {
        public string EmulatorId { get; }

        public ManualDownloadUrlRequiredException(string emulatorId)
            : base("İndirme kaynağı tanımlı değil.")
        {
            EmulatorId = emulatorId;
        }
    }

    // Artık SADECE arşiv açıldıktan sonra hangi dosyanın gerçek çalıştırılabilir olduğunu bulmak için
    // kullanılıyor — indirme KAYNAĞI (bkz. dosya başı yorum) bu record'un bir parçası DEĞİL artık.
    private sealed record EmulatorSource(string[] ExeCandidates);

    private static readonly Dictionary<string, EmulatorSource> Sources = new()
    {
        ["PCSX2"] = new(new[] { "pcsx2-qt.exe", "pcsx2.exe" }),
        ["Xemu"] = new(new[] { "xemu.exe" }),
        ["RPCS3"] = new(new[] { "rpcs3.exe" }),
        ["Cemu"] = new(new[] { "Cemu.exe" }),
        ["melonDS"] = new(new[] { "melonDS.exe" }),
        ["Vita3K"] = new(new[] { "Vita3K.exe" }),
        ["Xenia"] = new(new[] { "xenia_canary.exe" }),
        ["Dolphin"] = new(new[] { "Dolphin.exe" }),
        ["Ryujinx"] = new(new[] { "Ryujinx.exe" }),
        ["Azahar"] = new(new[] { "azahar.exe" }),
    };

    public static IReadOnlyCollection<string> SupportedEmulatorIds => Sources.Keys;

    // Ayarlar penceresi ilk açıldığında (veya bu emülatör için henüz hiç kayıtlı ayar yokken)
    // "Download Source" alanını doldurmak için kullanılan varsayılanlar — gerçek /releases/latest
    // yanıtları çekilip doğrulanarak eklendi (bkz. görev geçmişi). Görev talimatı: "Download Source
    // alanı mevcut kaynakla otomatik doldurulsun" + "kaynakta tam adres yazsana" — GitHubReleases
    // türündekiler artık TAM API adresi (ör. "https://api.github.com/repos/PCSX2/pcsx2/releases/latest"),
    // kısa "owner/repo" DEĞİL. Dolphin/Ryujinx: resmi/otomatik bir GitHub Releases kaynağı hâlâ YOK
    // (Dolphin'in indirilebilir bir Windows arşivi yok, Ryujinx'in resmi kaynağı Mart 2024'te
    // kapatıldı) — kullanıcının VERDİĞİ gerçek Direct URL'ler kullanılıyor (curl ile doğrulandı: ikisi
    // de 200 dönüyor, Dolphin Content-Length ~18MB/.7z, Ryujinx Content-Length ~50MB/application/zip —
    // Ryujinx'in URL'sinde uzantı YOK, bkz. DetermineArchiveExtension'ın Content-Type/Content-Disposition
    // fallback'i). Bu linkler değişebilir/kırılabilir sabit sürüm/oturum bağlantıları — kullanıcı
    // kararı: "değişirse değiştiririz daha sonra zaten emulatorler programın içinde olacak paket olarak"
    // (yani bu geçici bir çözüm, ileride emülatörler uygulamayla birlikte paketlenecek). RPCS3
    // BuiltInApi türünde, gerçek istek URL'inin BİLGİLENDİRME amaçlı, salt-okunur bir görünümü (bkz.
    // DownloadSourceType.BuiltInApi, ResolveSourceAsync).
    public static readonly IReadOnlyDictionary<string, EmulatorDownloadSourceSetting> DefaultDownloadSources = new Dictionary<string, EmulatorDownloadSourceSetting>
    {
        ["PCSX2"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/PCSX2/pcsx2/releases/latest" },
        ["RPCS3"] = new() { SourceType = DownloadSourceType.BuiltInApi, Source = "https://update.rpcs3.net/?api=v3&os_type=windows&os_arch=x64" },
        ["Xemu"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/xemu-project/xemu/releases/latest" },
        ["Cemu"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/cemu-project/Cemu/releases/latest" },
        ["melonDS"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/melonDS-emu/melonDS/releases/latest" },
        ["Vita3K"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/Vita3K/Vita3K/releases/latest" },
        ["Xenia"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/xenia-canary/xenia-canary-releases/releases/latest" },
        ["Dolphin"] = new() { SourceType = DownloadSourceType.DirectUrl, Source = "https://dl.dolphin-emu.org/releases/2606/dolphin-2606-x64.7z" },
        ["Ryujinx"] = new() { SourceType = DownloadSourceType.DirectUrl, Source = "https://git.ryujinx.app/projects/Ryubing/releases/download/1.3.3/ryujinx-1.3.3-win_x64.zip" },
        ["Azahar"] = new() { SourceType = DownloadSourceType.GitHubReleases, Source = "https://api.github.com/repos/azahar-emu/azahar/releases/latest" },
    };

    // Kullanıcı isteği: "platform bazlı olmasın o zaman onlar sadece emulation klasörüne indirip
    // ordan algılasın" — ThirdParty\Emulation\{id}\, platform adına göre DEĞİL, emülatör kimliğine
    // göre (hangi platform satırından çağrılırsa çağrılsın aynı kurulum paylaşılır).
    public static string InstallRootFor(string emulatorId) => Path.Combine(AppPaths.Emulation, emulatorId);

    // source: AppSettings.EmulatorDownloadSources'ten bu indirme anında OKUNMUŞ, güncel değer —
    // "kaynak değiştirilirse program yeniden derlenmeden kullanılabilsin" (görev talimatı) gereği
    // ÇAĞIRAN TARAFTAN (SettingsViewModel) parametre olarak geliyor, bu sınıf hiçbir kaynağı kendi
    // başına diskten okumuyor/önbelleklemiyor.
    public static async Task<InstallResult> DownloadAndInstallAsync(string emulatorId, EmulatorDownloadSourceSetting? source, IProgress<(string Message, double Percent, bool IsIndeterminate)>? progress, CancellationToken cancellationToken)
    {
        if (!Sources.TryGetValue(emulatorId, out var emulatorSource))
            throw new InvalidOperationException($"Bilinmeyen emülatör: {emulatorId}");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RetroAudit");

        progress?.Report(("Sürüm bilgisi alınıyor...", 0, false));
        var downloadUrl = await ResolveSourceAsync(http, emulatorId, source, cancellationToken);

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ManualDownloadUrlRequiredException(emulatorId);

        // TEK bir istek: bazı manuel/direkt indirme servisleri (ör. tek kullanımlık oturum çerezli
        // "direct-download" hizmetleri) URL'de dosya uzantısı GÖSTERMEZ (ör. Ryujinx manuel linki:
        // ".../ryujinx/05rteyrhvf4uk518pidixf") — bu yüzden uzantı önce URL'den, bulunamazsa AYNI
        // yanıtın Content-Disposition dosya adından/Content-Type'ından belirlenir (bkz.
        // DetermineArchiveExtension) — ayrı bir "önce kontrol et sonra indir" isteği YAPILMAZ, aynı
        // bağlantı hem uzantıyı belirlemek hem gövdeyi indirmek için kullanılır.
        using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var extension = DetermineArchiveExtension(downloadUrl, response);
        if (extension is not (".7z" or ".zip" or ".exe"))
            throw new InvalidOperationException($"Desteklenmeyen indirme dosyası türü: \"{extension}\" (sadece .7z, .zip, .exe desteklenir).");
        if (extension == ".7z" && !File.Exists(AppPaths.SevenZipExe))
            throw new InvalidOperationException("7zr.exe bulunamadı (ThirdParty\\7-Zip\\7zr.exe) — kurulum bozuk olabilir.");

        var installRoot = InstallRootFor(emulatorId);
        Directory.CreateDirectory(installRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "RetroAudit_" + emulatorId + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var archivePath = Path.Combine(tempDir, "archive" + extension);
            progress?.Report(($"{emulatorId} indiriliyor...", 0, false));
            await DownloadResponseBodyAsync(response, archivePath, p => progress?.Report(($"{emulatorId} indiriliyor...", p * 0.85, false)), cancellationToken);

            string exePath;
            if (extension == ".exe")
            {
                // Arşiv değil, doğrudan taşınabilir (portable) tek bir .exe — bir GUI kurulum
                // sihirbazı DEĞİL varsayılıyor (admin manuel/direkt URL'i böyle bir dosyaya işaret
                // ederse sorumluluğu admin'e ait; burada msiexec/NSIS sessiz kurulum otomasyonu YOK,
                // "ilk faz" kapsamı dışında — bkz. görev talimatı).
                progress?.Report(($"{emulatorId} kuruluyor...", 90, false));
                var destExeName = emulatorSource.ExeCandidates.FirstOrDefault() ?? Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
                var destExePath = Path.Combine(installRoot, destExeName);
                File.Copy(archivePath, destExePath, overwrite: true);
                exePath = destExePath;
            }
            else
            {
                progress?.Report(($"{emulatorId} açılıyor...", 85, true));
                if (extension == ".zip")
                    ZipFile.ExtractToDirectory(archivePath, installRoot, overwriteFiles: true);
                else
                    await ExtractSevenZipAsync(archivePath, installRoot, cancellationToken);

                exePath = emulatorSource.ExeCandidates
                    .Select(name => FindFile(installRoot, name))
                    .FirstOrDefault(path => path != null)
                    ?? throw new InvalidOperationException($"Arşiv açıldı ama {emulatorId} çalıştırılabilir dosyası bulunamadı.");
            }

            progress?.Report(("Tamamlandı", 100, false));
            return new InstallResult(exePath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* geçici dosya, kritik değil */ }
        }
    }

    // RPCS3 (BuiltInApi) için Kaynak alanı SADECE bilgilendirme amaçlı — gerçek istek her seferinde
    // OS sürümü/rastgele hash içerdiğinden sabit bir metin OLAMAZ, bu yüzden Source İÇERİĞİ HER ZAMAN
    // yok sayılır, ResolveRpcs3DownloadUrlAsync doğrudan çağrılır (kullanıcı sorusu: "apiden çekmedik
    // mi onu" — evet, çekiyoruz, koddan). DİĞER HERKES için TEK, PAYLAŞILAN bir yol izlenir:
    // GitHubReleases veya DirectUrl (bkz. DownloadSourceType). Hiçbir dalda istisna FIRLATILMAZ —
    // bulunamayan/boş/hatalı her durum null döner, çağıran taraf bunu ManualDownloadUrlRequiredException
    // ile aynı şekilde ele alır (görev talimatı: "hata fırlatma; status olarak 'Manual URL required' göster").
    private static async Task<string?> ResolveSourceAsync(HttpClient http, string emulatorId, EmulatorDownloadSourceSetting? source, CancellationToken cancellationToken)
    {
        if (source?.SourceType == DownloadSourceType.BuiltInApi || (source is null && emulatorId == "RPCS3"))
            return await ResolveRpcs3DownloadUrlAsync(http, cancellationToken);

        if (source is null || string.IsNullOrWhiteSpace(source.Source))
            return null;

        return source.SourceType switch
        {
            DownloadSourceType.DirectUrl => source.Source.Trim(),
            DownloadSourceType.GitHubReleases => await ResolveGitHubReleaseWindowsAssetAsync(http, source.Source.Trim(), cancellationToken),
            _ => null,
        };
    }

    // TÜM GitHubReleases türündeki emülatörlerin (PCSX2/Xemu/Cemu/melonDS/Vita3K/Xenia/Azahar) TEK,
    // PAYLAŞILAN asset-seçim algoritması — gerçek /releases/latest yanıtlarına göre doğrulandı (bkz.
    // görev geçmişi): "windows" içeren, ARM/hata-ayıklama/libretro/diğer-platform asset'lerini hariç
    // tutan adaylar arasından uzantı önceliğine (.7z > .zip > .exe) göre seçim yapar. Uygun asset
    // bulunamazsa (ör. depo/sürüm yapısı değişmiş) İSTİSNA FIRLATMAZ, null döner. Kaynak hem TAM API
    // URL'i ("https://api.github.com/repos/owner/repo/releases/latest"), hem repo sayfası URL'i
    // ("https://github.com/owner/repo"), hem de kısa "owner/repo" biçimini kabul eder (bkz.
    // ParseOwnerRepo) — kullanıcı isteği: "kaynakta tam adres yazsana".
    private static async Task<string?> ResolveGitHubReleaseWindowsAssetAsync(HttpClient http, string sourceText, CancellationToken cancellationToken)
    {
        if (ParseOwnerRepo(sourceText) is not { } ownerRepo)
            return null; // "owner/repo" veya tanınan bir GitHub URL'i değil — admin yanlış yazmış olabilir.
        var (owner, repo) = ownerRepo;

        string releaseJson;
        try
        {
            releaseJson = await http.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null; // Depo yok / API'ye erişilemedi.
        }

        using var release = JsonDocument.Parse(releaseJson);
        if (!release.RootElement.TryGetProperty("assets", out var assetsElement))
            return null;

        var assets = assetsElement.EnumerateArray().ToList();

        static bool IsExcluded(string name) =>
            ContainsAny(name, "symbols", "pdb", "dbg", "debug", "arm64", "aarch64", "libretro", "android", "linux", "macos", "source");
        static bool ContainsAny(string name, params string[] terms) =>
            terms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

        var windowsCandidates = assets.Where(a =>
        {
            var name = a.GetProperty("name").GetString() ?? "";
            return name.Contains("windows", StringComparison.OrdinalIgnoreCase) && !IsExcluded(name);
        }).ToList();

        string[] extensionPriority = { ".7z", ".zip", ".exe" };
        foreach (var extension in extensionPriority)
        {
            var match = windowsCandidates.FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            if (match.ValueKind != JsonValueKind.Undefined)
                return match.GetProperty("browser_download_url").GetString();
        }

        return null;
    }

    // Kaynak metninden (owner, repo) çıkarır — üç biçimi de kabul eder: TAM API URL'i
    // ("https://api.github.com/repos/owner/repo/releases/latest"), repo sayfası URL'i
    // ("https://github.com/owner/repo[.git]") ve kısa "owner/repo". Tanınmayan bir biçimse null döner.
    private static (string Owner, string Repo)? ParseOwnerRepo(string sourceText)
    {
        var trimmed = sourceText.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var reposIndex = Array.IndexOf(segments, "repos");
            if (reposIndex >= 0 && segments.Length > reposIndex + 2)
                return (segments[reposIndex + 1], segments[reposIndex + 2]);
            if (segments.Length >= 2)
            {
                var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];
                return (segments[0], repo);
            }
            return null;
        }

        var parts = trimmed.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    // RPCS3, GitHub Releases yerine kendi uygulama-içi güncelleme kontrolünü kullanıyor (bkz.
    // RPCS3/rpcs3 deposu, rpcs3/rpcs3qt/update_manager.cpp) — biz de AYNI resmi, yapılandırılmış
    // JSON API'yi çağırıyoruz, bir HTML sayfası scrape etmiyoruz. Gerçek bir commit hash'imiz
    // olmadığı için (RetroAudit RPCS3'ü kendisi derlemiyor) API'ye rastgele/geçersiz bir hash
    // gönderiyoruz — sunucu bunu "özel/PR derlemesi" (return_code -1) sayıyor ama yine de en güncel
    // Windows derlemesinin indirme linkini/boyutunu/checksum'ını JSON'da döndürüyor (doğrulandı).
    // os_arch DEĞERİ ÖNEMLİ: kaynak kodunda "x64" (utils::get_OS_version, ARCH_X64 dalı) — "x86_64"
    // gönderilirse sunucu latest_build.windows alt-nesnesini hiç DÖNMÜYOR (denenip görüldü).
    private static async Task<string?> ResolveRpcs3DownloadUrlAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var osVersion = Environment.OSVersion.Version;
        var url = "https://update.rpcs3.net/?api=v3"
            + "&c=0000000000000000000000000000000000000000"
            + "&os_type=windows"
            + "&os_arch=x64"
            + $"&os_version={osVersion.Major}.{osVersion.Minor}.{osVersion.Build}";

        string json;
        try
        {
            json = await http.GetStringAsync(url, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("latest_build", out var latestBuild) ||
            !latestBuild.TryGetProperty("windows", out var windows) ||
            !windows.TryGetProperty("download", out var download))
            return null;

        return download.GetString();
    }

    // Bazı manuel/direkt indirme URL'lerinde (ör. tek kullanımlık oturum çerezli "direct-download"
    // hizmetleri) dosya uzantısı yok — önce URL yolundaki uzantı denenir, tanınmıyorsa AYNI yanıtın
    // Content-Disposition dosya adına, o da yoksa Content-Type'ına bakılır. Hiçbiri tanınan bir
    // arşiv/exe uzantısı vermezse URL'deki (boş olsa bile) uzantı olduğu gibi döner — çağıran taraf
    // zaten bunu ".7z"/".zip"/".exe" değilse reddediyor.
    private static string DetermineArchiveExtension(string url, HttpResponseMessage response)
    {
        var urlExtension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (urlExtension is ".7z" or ".zip" or ".exe")
            return urlExtension;

        var dispositionFileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(dispositionFileName))
        {
            var dispositionExtension = Path.GetExtension(dispositionFileName.Trim('"')).ToLowerInvariant();
            if (dispositionExtension is ".7z" or ".zip" or ".exe")
                return dispositionExtension;
        }

        return response.Content.Headers.ContentType?.MediaType switch
        {
            "application/zip" or "application/x-zip-compressed" => ".zip",
            "application/x-7z-compressed" => ".7z",
            _ => urlExtension,
        };
    }

    private static async Task DownloadResponseBodyAsync(HttpResponseMessage response, string destinationPath, Action<double>? onProgress, CancellationToken cancellationToken)
    {
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

    private static async Task ExtractSevenZipAsync(string archivePath, string destinationFolder, CancellationToken cancellationToken)
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

    // RetroArchInstallerService.IsInstalled'ın standalone karşılığı — bu emülatörün kendi
    // ThirdParty\{id}\ klasörümüze daha önce kurulup kurulmadığını (herhangi bir platform satırına
    // atanmış olsun ya da olmasın) kontrol eder.
    public static bool IsInstalled(string emulatorId) => GetInstalledExecutablePath(emulatorId) is not null;

    // SettingsViewModel'in başlangıçta "kurulu ama satırlara atanmamış" durumu (ör. kurulum sırasında
    // "Kaydet"e basılmadan pencere kapatılmışsa) fark edip otomatik düzeltebilmesi için — bkz.
    // SettingsViewModel.ReconcileStandalonePaths.
    public static string? GetInstalledExecutablePath(string emulatorId)
    {
        if (!Sources.TryGetValue(emulatorId, out var source))
            return null;

        var installRoot = InstallRootFor(emulatorId);
        if (!Directory.Exists(installRoot))
            return null;

        return source.ExeCandidates
            .Select(name => FindFile(installRoot, name))
            .FirstOrDefault(path => path is not null);
    }

    // "İndir & Kur"un tersi (kullanıcı isteği: "kaldırma butonu ekle") — ThirdParty\{id}\ klasörünü
    // tamamen siler.
    public static void Uninstall(string emulatorId)
    {
        var installRoot = InstallRootFor(emulatorId);
        if (Directory.Exists(installRoot))
            Directory.Delete(installRoot, recursive: true);
    }
}
