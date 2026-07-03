using RetroAudit.Catalog;

// RetroAudit.db'yi (offline master katalog) üreten komut satırı aracı. Kullanım:
//   dotnet run --project RetroAudit.Builder -- [--dat-folder <yol>] [--sources no-intro,redump,...]
//                                              [--platform "Nintendo - Nintendo Entertainment System"]
//                                              [--launchbox-db <yol>] [--output <yol>]
// Varsayılanlar bu makinedeki gerçek konumlara göre ayarlandı; başka bir makinede argümanlarla geçilebilir.

var args2 = ParseArgs(args);

var datRoot = args2.GetValueOrDefault("dat-folder")
    ?? @"C:\Users\srhts\Desktop\libretro-database-master\metadat";

// Kullanıcı isteği: tüm kaynaklar (no-intro/redump/tosec/mame*/fbneo*) taranabilir durumda olsun,
// ama ilk aktif/doğrulanan çıktı sadece No-Intro üzerinden gelsin — bu yüzden varsayılan tek kaynak.
var sources = (args2.GetValueOrDefault("sources") ?? "no-intro")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

var platformFilter = args2.GetValueOrDefault("platform");

var launchBoxDb = args2.GetValueOrDefault("launchbox-db")
    ?? @"C:\Users\srhts\LaunchBox\Metadata\LaunchBox.Metadata.db";

// Repo'nun asla içine düşmeyecek bir varsayılan konum — RetroAudit.db LaunchBox'tan türetilmiş
// veri içerdiği için hiçbir zaman git'e girmemeli (bkz. .gitignore).
var outputDb = args2.GetValueOrDefault("output")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroAudit", "RetroAudit.db");

Console.WriteLine("RetroAudit DAT Builder");
Console.WriteLine($"  DAT root:       {datRoot}");
Console.WriteLine($"  Sources:        {string.Join(", ", sources)}");
Console.WriteLine($"  Platform filter:{(platformFilter is null ? " (none — all platforms in selected sources)" : " " + platformFilter)}");
Console.WriteLine($"  LaunchBox DB:   {launchBoxDb}");
Console.WriteLine($"  Output DB:      {outputDb}");
Console.WriteLine();

if (!Directory.Exists(datRoot))
{
    Console.Error.WriteLine($"DAT klasörü bulunamadı: {datRoot}");
    return 1;
}

if (!File.Exists(launchBoxDb))
{
    Console.Error.WriteLine($"LaunchBox metadata veritabanı bulunamadı: {launchBoxDb}");
    return 1;
}

var options = new BuildOptions
{
    DatRoot = datRoot,
    SourceCategories = sources,
    PlatformFilter = platformFilter,
    LaunchBoxDbPath = launchBoxDb,
    OutputDbPath = outputDb,
};

var report = CatalogBuilder.Run(options);

Console.WriteLine(report.ToString());
Console.WriteLine($"RetroAudit.db yazıldı: {outputDb}");

return 0;

static Dictionary<string, string> ParseArgs(string[] rawArgs)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < rawArgs.Length; i++)
    {
        if (!rawArgs[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        var key = rawArgs[i][2..];
        var value = i + 1 < rawArgs.Length ? rawArgs[i + 1] : string.Empty;
        result[key] = value;
        i++;
    }
    return result;
}
