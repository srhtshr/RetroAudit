namespace RetroAudit.Catalog.Dat;

// DAT klasör ağacını (ör. libretro-database'in metadat/ dizini altındaki no-intro, redump, tosec,
// mame, fbneo-split gibi alt klasörleri) tarar. Her platform (dosya adından, uzantı hariç türetilir
// — ör. "Nintendo - Nintendo Entertainment System") RetroAudit.db'de TEK bir kaynaktan gelir:
// önce tüm verilen kaynak kategorilerinde o platformun hangi kaynaklarda bulunduğu tespit edilir,
// sonra PlatformSourceMap'e göre (ya da çakışmasız tek seçenek varsa otomatik olarak) tam olarak
// bir tanesi seçilir. İki kaynak ASLA aynı platform için birleştirilmez.
public static class DatSourceScanner
{
    // Xbox 360'ın dijital/mikro-içerik varyantları varsayılan olarak katalog dışı tutulur — bunlar
    // gerçek oyun değil (World-region analizinde doğrulandı: Rock Band Network şarkıları, Lips DLC
    // paketleri, silah/kostüm paketleri vb. tekil indirilebilir içerikler). Bu liste bilinçli olarak
    // kolayca bulunup boşaltılabilecek/genişletilebilecek şekilde ayrı tutuldu — ileride bir
    // "--include-digital-variants" CLI bayrağıyla bu davranış esnetilebilir.
    public static readonly HashSet<string> DefaultExcludedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft - Xbox 360 (Digital)",
        "Microsoft - Xbox 360 (Games on Demand)",
        "Microsoft - XBOX 360 (Title Updates)",
    };

    public class ScanResult
    {
        public List<DatGameEntry> Entries { get; } = new();

        // Format desteklenmediği (ör. Logiqx XML) ya da okunamadığı için atlanan dosyalar.
        public List<string> SkippedFiles { get; } = new();

        public List<string> ScannedFiles { get; } = new();

        // Her platform için hangi kaynağın seçildiğine dair şeffaf kayıt — BuildReport bunu
        // "belirsiz, otomatik seçildi" durumlarını göstermek için kullanır.
        public List<PlatformResolution> PlatformResolutions { get; } = new();

        public List<string> ExcludedPlatforms { get; } = new();
    }

    public class PlatformResolution
    {
        public string PlatformName { get; init; } = string.Empty;
        public string ChosenSource { get; init; } = string.Empty;
        public List<string> AvailableSources { get; init; } = new();
        public bool WasAmbiguous => AvailableSources.Count > 1;
        public bool WasExplicitOverride { get; init; }
    }

    // sourceCategories: metadat/ altında dikkate alınacak alt klasör adları (ör. {"no-intro",
    // "redump", "tosec"}). platformFilter: verilirse, sadece bu isimdeki platform işlenir — CLI'daki
    // --platform argümanı için (tek platformluk hızlı doğrulama koşusu).
    public static ScanResult Scan(string datRoot, IEnumerable<string> sourceCategories, string? platformFilter)
    {
        var result = new ScanResult();
        var categories = sourceCategories.ToList();

        // 1) Envanter: her platformun hangi kaynak(lar)da, hangi dosya yolunda bulunduğunu çıkar.
        var inventory = new Dictionary<string, List<(string Source, string FilePath)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            var categoryDir = Path.Combine(datRoot, category);
            if (!Directory.Exists(categoryDir))
                continue;

            var files = Directory.EnumerateFiles(categoryDir, "*.dat", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(categoryDir, "*.xml", SearchOption.TopDirectoryOnly));

            foreach (var file in files)
            {
                var platformName = Path.GetFileNameWithoutExtension(file);
                if (!inventory.TryGetValue(platformName, out var list))
                {
                    list = new List<(string, string)>();
                    inventory[platformName] = list;
                }
                list.Add((category, file));
            }
        }

        // 2) Her platform için TEK kaynak seç ve sadece o dosyayı işle.
        foreach (var (platformName, sources) in inventory)
        {
            if (platformFilter is not null && !string.Equals(platformName, platformFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (DefaultExcludedPlatforms.Contains(platformName))
            {
                result.ExcludedPlatforms.Add(platformName);
                continue;
            }

            var availableSourceNames = sources.Select(s => s.Source).ToList();
            string chosenSource;
            var wasOverride = false;

            if (PlatformSourceMap.Overrides.TryGetValue(platformName, out var overridden) &&
                availableSourceNames.Contains(overridden, StringComparer.OrdinalIgnoreCase))
            {
                chosenSource = overridden;
                wasOverride = true;
            }
            else if (sources.Count == 1)
            {
                chosenSource = sources[0].Source;
            }
            else
            {
                // Çakışma var ama açık bir kural tanımlı değil: sabit öncelik sırasına düş, ama
                // bunu HER ZAMAN raporla — sessizce yanlış/rastgele bir kaynak seçilmiş olmasın.
                chosenSource = PlatformSourceMap.FallbackPriorityOrder
                    .FirstOrDefault(p => availableSourceNames.Contains(p, StringComparer.OrdinalIgnoreCase))
                    ?? sources[0].Source;
            }

            result.PlatformResolutions.Add(new PlatformResolution
            {
                PlatformName = platformName,
                ChosenSource = chosenSource,
                AvailableSources = availableSourceNames,
                WasExplicitOverride = wasOverride,
            });

            var (_, filePath) = sources.First(s => s.Source == chosenSource);

            var parser = DatParserFactory.Resolve(filePath);
            if (parser is null)
            {
                result.SkippedFiles.Add(filePath);
                continue;
            }

            try
            {
                var entries = parser.Parse(filePath).ToList();
                foreach (var entry in entries)
                {
                    entry.SourceCategory = chosenSource;
                    entry.PlatformName = platformName;
                }

                result.Entries.AddRange(entries);
                result.ScannedFiles.Add(filePath);
            }
            catch (NotSupportedException)
            {
                // Desteklenmeyen format (ör. Logiqx XML) — mimari hazır, ileride buraya
                // gerçek bir parser eklenince otomatik olarak işlenmeye başlayacak.
                result.SkippedFiles.Add(filePath);
            }
        }

        return result;
    }
}
