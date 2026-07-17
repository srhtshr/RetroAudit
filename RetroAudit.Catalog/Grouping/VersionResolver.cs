using RetroAudit.Catalog.Dat;
using RetroAudit.Catalog.Naming;

namespace RetroAudit.Catalog.Grouping;

// Ham DatGameEntry kayıtlarını (Platform, temiz başlık) çiftine göre CatalogGame'lere gruplar ve
// her oyun için "tercih edilen" (Games tablosunda gösterilecek) sürümü seçer. Ana liste bu sayede
// şişmez: Rev/USA/Europe/Japan/World gibi resmi sürümler aynı CatalogGame'in Versions listesinde
// kalır. Beta/Proto/Demo/Pirate/Cracked/BIOS/Utility/... gibi resmi olmayan kayıtlar (bkz.
// DatNameParser.ShouldExclude) burada tamamen elenir — GameVersions'a bile girmezler. Bir oyunun
// TÜM DAT kayıtları böyle elenirse (ör. sadece proto çıkmış bir başlık), o oyun hiç Games
// satırı oluşturmaz — RetroAudit sadece gerçekten resmi bir sürümü olan oyunları listeler.
public static class VersionResolver
{
    // Group() çağrısından SONRA CatalogBuilder.Run tarafından BuildReport'a okunuyor — kaç ham
    // kaydın, oyun ADI temiz göründüğü hâlde SADECE ROM dosya adındaki bir etiket (ör. "(Unl)")
    // yüzünden elendiğini ayrı raporlayabilmek için (bkz. ContainsExcludedTag çağrısı aşağıda).
    // İstatistiksel görünürlük amaçlı; Group() her çağrıldığında sıfırlanır.
    public static int LastRunRomFileNameExclusionCount { get; private set; }

    public static List<CatalogGame> Group(IEnumerable<DatGameEntry> entries)
    {
        LastRunRomFileNameExclusionCount = 0;

        // Gruplama anahtarı normalize edilmiş başlık (CompareTitle) üzerinden yapılır, ham
        // CleanTitle üzerinden değil — aynı oyunun USA/Europe/Japan sürümleri arasında noktalama,
        // büyük/küçük harf ya da alt başlık yazım farkı olması çok yaygın (ör. "Mega Man 2" /
        // "Mega Man II"'nin farklı DAT'larda yazım biçimi değişebilir); ham başlık üzerinden
        // gruplamak bu durumlarda aynı oyunu yanlışlıkla iki ayrı Games satırına bölüyordu (1G1R
        // hedefini bozan asıl kök neden). CompareTitle zaten LaunchBox'ın kendi oyun-kimliği
        // normalizasyonunu taklit ettiği için bu amaç için de doğru anahtar.
        var games = new Dictionary<(string Platform, string CompareTitle), CatalogGame>();

        foreach (var entry in entries)
        {
            var parsed = DatNameParser.Parse(entry.Name, entry.PlatformName);

            // Oyun adı (entry.Name) "temiz" görünse bile, o oyunun ROM dosya adı(ları) ayrı bir
            // alan ve "(Aftermarket) (Unl)" gibi ek etiketler taşıyabiliyor (bkz. DatNameParser.
            // ContainsExcludedTag yorumu, kullanıcı geri bildirimi: "üstte released yazıyor ama
            // sürüm adında Unl yazıyor") — dosya adlarından biri bile dışlanan bir etiket
            // taşıyorsa kayıt yine tamamen elenir.
            if (!parsed.ShouldExclude && entry.Roms.Any(r => DatNameParser.ContainsExcludedTag(r.FileName)))
                LastRunRomFileNameExclusionCount++;

            if (parsed.ShouldExclude || entry.Roms.Any(r => DatNameParser.ContainsExcludedTag(r.FileName)))
                continue;

            var compareTitle = NormalizeForCompare(parsed.CleanTitle);
            var key = (entry.PlatformName, compareTitle);

            if (!games.TryGetValue(key, out var game))
            {
                game = new CatalogGame
                {
                    PlatformName = entry.PlatformName,
                    Title = parsed.CleanTitle,
                    CompareTitle = compareTitle,
                };
                games[key] = game;
            }

            // Filtreyi geçen (yani gerçek/resmi olduğu düşünülen) ama başlığında USA/Europe/Japan/
            // World gibi net bir bölge etiketi bulunmayan kayıtlar yeni bir oyun oluşturmaz — aynı
            // CompareTitle altında "Unknown" bölgeli bir sürüm olarak kalır, böylece ileride UI'da
            // filtrelenebilir. Sınıflandırılamayan kayıtlarla daha fazla uğraşılmıyor (kullanıcı isteği).
            var regions = parsed.Regions.Length > 0 ? parsed.Regions : new[] { "Unknown" };

            // No-Intro aynı sürümü genelde iki ayrı game() bloğu olarak listeler: "headered" (.nes)
            // ve "headerless" (.unh) ROM dökümü — ikisi de birebir aynı isimle (RawDatName), farklı
            // CRC/boyutla. Bunlar gerçekte TEK sürümdür, sadece dosya formatı farklıdır; bu yüzden
            // aynı RawDatName ikinci kez görüldüğünde yeni bir GameVersion açmak yerine mevcut
            // sürümün Roms listesine ekleniyor (1 Game / 1 GameVersion / birden fazla Hash).
            var existingVersion = game.Versions.FirstOrDefault(v => v.RawDatName == entry.Name);
            if (existingVersion is not null)
            {
                existingVersion.Roms.AddRange(entry.Roms);
                continue;
            }

            game.Versions.Add(new CatalogGameVersion
            {
                RawDatName = entry.Name,
                CleanTitle = parsed.CleanTitle,
                SourceDat = entry.SourceCategory,
                Regions = regions,
                VersionLabel = parsed.VersionLabel,
                Roms = new List<DatRomEntry>(entry.Roms),
            });
        }

        foreach (var game in games.Values)
            ResolvePreferred(game);

        return games.Values.ToList();
    }

    // Tercih sırası (kullanıcı kararı): USA > Europe > World > Japan > diğer bölge; hayatta kalan
    // tüm sürümler zaten "resmi" olduğu için (ShouldExclude filtresini geçtiler) ek bir normal/
    // alt-sürüm ayrımına gerek yok — sadece bölge önceliği ve deterministik bir son çare
    // (en kısa/alfabetik, "en temiz" sürümü seçer) yeterli.
    private static void ResolvePreferred(CatalogGame game)
    {
        var best = game.Versions
            .OrderBy(v => RegionRank(v.Regions))
            .ThenBy(v => v.RawDatName.Length)
            .ThenBy(v => v.RawDatName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (best is null)
            return;

        best.IsPreferred = true;
        // Ana listede gösterilecek başlık, gruptaki ilk görülen (dosya sırasına bağlı, rastgele)
        // kaydın değil, tercih edilen (USA öncelikli) sürümün kendi CleanTitle'ı olsun.
        game.Title = best.CleanTitle;
    }

    // internal: CatalogBuilder.MergeRegionVariants de (birleştirilmiş bir oyunun sürümleri arasından
    // yeni bir "tercih edilen" seçerken) aynı öncelik sırasını kullanıyor — tek bir yerde tanımlı.
    internal static int RegionRank(string[] regions)
    {
        if (regions.Contains("USA", StringComparer.OrdinalIgnoreCase)) return 0;
        if (regions.Contains("Europe", StringComparer.OrdinalIgnoreCase)) return 1;
        if (regions.Contains("World", StringComparer.OrdinalIgnoreCase)) return 2;
        if (regions.Contains("Japan", StringComparer.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    // "The"/"A"/"An"/"And" gibi bağlaç/tanımlıkları ayrı birer TOKEN olarak (kelime sınırı gözetilerek)
    // atar — LaunchBox'ın gerçek CompareName üretiminde bu kelimeler her yerde (sadece başlıkta değil,
    // ":" sonrası alt başlıklarda da) siliniyor. Kısaltmaların içindeki tek harfler (ör. "H.A.W.X"
    // -> "HAWX") token olarak AYRI SAYILMADIĞI için (nokta zaten aşağıda boşluksuz siliniyor) burada
    // yanlışlıkla elenmezler.
    private static readonly HashSet<string> CompareStopWords = new(StringComparer.Ordinal)
    {
        "THE", "A", "AN", "AND",
    };

    // Sadece "X" harfi İÇERMEYEN Roma rakamları (II-VIII / 2-8) rakama çevrilir — LaunchBox
    // gözlemlerinde IX/X/XI/XII/XIII/XIV/XX hep Roma rakamı olarak KALIYOR (muhtemelen "X" harfi
    // oyun başlıklarında o kadar yaygın bir stilistik harf ki (Mega Man X, X-Men, Final Fantasy X)
    // içinde X geçen herhangi bir rakamı çevirmek yanlış pozitif riskini çok artırırdı).
    private static readonly Dictionary<string, string> CompareRomanNumerals = new(StringComparer.Ordinal)
    {
        ["II"] = "2", ["III"] = "3", ["IV"] = "4", ["V"] = "5", ["VI"] = "6", ["VII"] = "7", ["VIII"] = "8",
    };

    // LaunchBox.Metadata.db'nin kendi CompareName üretim kuralını taklit eder — gerçek LaunchBox
    // veritabanındaki 183 bin Name/CompareName çiftinin TAMAMI karşılaştırılarak doğrulandı (bkz.
    // oturum notları): kesme işareti VE nokta (ör. "S.T.A.L.K.E.R." -> "STALKER", "R.C. Pro-Am" ->
    // "RC PRO AM") boşluksuz silinir; diğer noktalama boşluğa çevrilir; "The"/"A"/"An"/"And" ayrı
    // birer kelime olarak (başlığın HERHANGİ bir yerinde, sadece başında değil) atılır; sadece "X"
    // harfi içermeyen Roma rakamları (II-VIII) rakama çevrilir. Bu, örneğin "1943 - The Battle of
    // Valhalla" (DAT) ile LaunchBox'ın "1943 BATTLE OF VALHALLA" AltNameCompareValue'sinin
    // eşleşmesini sağlıyor — eski algoritma "The"yi attırmadığı için bu eşleşme SESSİZCE
    // başarısız oluyordu (kullanıcı tarafından fark edildi: "Valhalla" tabloda ayrı, eşleşmemiş
    // bir satır olarak duruyordu).
    public static string NormalizeForCompare(string title)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in title)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is '\'' or '’' or '.')
                continue; // kesme işareti VE nokta: sil, boşluk ekleme
            else
                sb.Append(' '); // diğer tüm noktalama: boşluğa çevir
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToUpperInvariant();
        if (collapsed.Length == 0)
            return collapsed;

        var tokens = collapsed.Split(' ');
        var result = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (CompareStopWords.Contains(token))
                continue;
            result.Add(CompareRomanNumerals.TryGetValue(token, out var digit) ? digit : token);
        }

        return string.Join(' ', result);
    }
}
