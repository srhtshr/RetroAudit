namespace RetroAudit.Catalog.Dat;

// DAT klasör ağacını (ör. libretro-database'in metadat/ dizini altındaki no-intro, redump, tosec,
// mame, mame-split, mame-nonmerged, mame-member, fbneo-member, fbneo-split alt klasörleri) tarar.
// Her alt klasör bir "kaynak kategorisi" (SourceCategory), her .dat/.xml dosyası bir platformdur
// (dosya adından, uzantı hariç, türetilir — ör. "Nintendo - Nintendo Entertainment System").
public static class DatSourceScanner
{
    public class ScanResult
    {
        public List<DatGameEntry> Entries { get; } = new();

        // Format desteklenmediği (ör. Logiqx XML) ya da okunamadığı için atlanan dosyalar.
        public List<string> SkippedFiles { get; } = new();

        public List<string> ScannedFiles { get; } = new();
    }

    // sourceCategories: metadat/ altında taranacak alt klasör adları (ör. sadece {"no-intro"}).
    // platformFilter: verilirse, sadece dosya adı (uzantısız) bu değere tam eşit olan DAT işlenir
    // — CLI'daki --platform argümanı için (tek platformluk hızlı doğrulama koşusu).
    public static ScanResult Scan(string datRoot, IEnumerable<string> sourceCategories, string? platformFilter)
    {
        var result = new ScanResult();

        foreach (var category in sourceCategories)
        {
            var categoryDir = Path.Combine(datRoot, category);
            if (!Directory.Exists(categoryDir))
                continue;

            var files = Directory.EnumerateFiles(categoryDir, "*.dat", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(categoryDir, "*.xml", SearchOption.TopDirectoryOnly));

            foreach (var file in files)
            {
                var platformName = Path.GetFileNameWithoutExtension(file);

                if (platformFilter is not null && !string.Equals(platformName, platformFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parser = DatParserFactory.Resolve(file);
                if (parser is null)
                {
                    result.SkippedFiles.Add(file);
                    continue;
                }

                try
                {
                    var entries = parser.Parse(file).ToList();
                    foreach (var entry in entries)
                    {
                        entry.SourceCategory = category;
                        entry.PlatformName = platformName;
                    }

                    result.Entries.AddRange(entries);
                    result.ScannedFiles.Add(file);
                }
                catch (NotSupportedException)
                {
                    // Desteklenmeyen format (ör. Logiqx XML) — mimari hazır, ileride buraya
                    // gerçek bir parser eklenince otomatik olarak işlenmeye başlayacak.
                    result.SkippedFiles.Add(file);
                }
            }
        }

        return result;
    }
}
