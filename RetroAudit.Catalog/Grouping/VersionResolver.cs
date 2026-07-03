using RetroAudit.Catalog.Dat;
using RetroAudit.Catalog.Naming;

namespace RetroAudit.Catalog.Grouping;

// Ham DatGameEntry kayıtlarını (Platform, temiz başlık) çiftine göre CatalogGame'lere gruplar ve
// her oyun için "tercih edilen" (Games tablosunda gösterilecek) sürümü seçer. Ana liste bu sayede
// şişmez: Beta/Proto/Test/Rev/Europe/Japan gibi her şey aynı CatalogGame'in Versions listesinde kalır.
public static class VersionResolver
{
    public static List<CatalogGame> Group(IEnumerable<DatGameEntry> entries)
    {
        var games = new Dictionary<(string Platform, string Title), CatalogGame>();

        foreach (var entry in entries)
        {
            var parsed = DatNameParser.Parse(entry.Name);
            var key = (entry.PlatformName, parsed.CleanTitle);

            if (!games.TryGetValue(key, out var game))
            {
                game = new CatalogGame
                {
                    PlatformName = entry.PlatformName,
                    Title = parsed.CleanTitle,
                    CompareTitle = NormalizeForCompare(parsed.CleanTitle),
                };
                games[key] = game;
            }

            game.Versions.Add(new CatalogGameVersion
            {
                RawDatName = entry.Name,
                SourceDat = entry.SourceCategory,
                Regions = parsed.Regions,
                VersionLabel = parsed.VersionLabel,
                Flags = parsed.Flags,
                Roms = entry.Roms,
            });
        }

        foreach (var game in games.Values)
            ResolvePreferred(game);

        return games.Values.ToList();
    }

    // Tercih sırası: önce "normal" (alt-sürüm bayrağı olmayan) sürümler, aralarında
    // USA > World > Europe > Japan > diğer; hiç normal sürüm yoksa (ör. sadece proto çıkmış bir
    // oyun) aynı bölge sıralaması alt-sürümler arasında uygulanıp yine tek bir tercih seçilir —
    // böylece her CatalogGame'in mutlaka bir Preferred sürümü olur.
    private static void ResolvePreferred(CatalogGame game)
    {
        var best = game.Versions
            .OrderBy(v => v.IsAltVersion ? 1 : 0)
            .ThenBy(v => RegionRank(v.Regions))
            .ThenBy(v => v.RawDatName.Length)
            .ThenBy(v => v.RawDatName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (best is not null)
            best.IsPreferred = true;
    }

    private static int RegionRank(string[] regions)
    {
        if (regions.Contains("USA", StringComparer.OrdinalIgnoreCase)) return 0;
        if (regions.Contains("World", StringComparer.OrdinalIgnoreCase)) return 1;
        if (regions.Contains("Europe", StringComparer.OrdinalIgnoreCase)) return 2;
        if (regions.Contains("Japan", StringComparer.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    // LaunchBox.Metadata.db'nin kendi CompareName üretim kuralını taklit eder (örneklerle doğrulandı:
    // "Kirby's Adventure" -> "KIRBYS ADVENTURE" [kesme işareti SİLİNİR, boşluk eklenmez],
    // "Super Mario All-Stars NES" -> "SUPER MARIO ALL STARS NES" [tire BOŞLUĞA çevrilir]).
    // Bu yüzden kesme işareti tamamen atılırken diğer tüm noktalama işaretleri boşluğa çevrilir;
    // birebir aynı algoritma olmayabilir ama gözlemlenen örneklerle eşleşiyor.
    public static string NormalizeForCompare(string title)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in title)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is '\'' or '’')
                continue; // kesme işareti: sil, boşluk ekleme
            else
                sb.Append(' '); // diğer tüm noktalama: boşluğa çevir
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return collapsed.ToUpperInvariant();
    }
}
