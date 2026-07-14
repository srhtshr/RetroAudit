using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using RetroAudit.Catalog.Naming;
using RetroAudit.Models;

namespace RetroAudit.Services;

// Kullanıcının kendi ROM arşivini (RetroAudit'in {Platform}\{File} kuralına uymayan, serbest
// klasör yapısındaki) tarayıp katalogdaki oyunlarla dosya adı üzerinden eşleştirir. Bulk indirme
// akışındaki (RomSearchWindow) tek-oyun eşleştirmesinin aksine burada kullanıcı zaten sahip olduğu
// dosyaları RetroAudit'e "tanıtıyor" — dosyalara UI'dan ayrı, saf ve test edilebilir bir katman.
public static class RomImportService
{
    // Kullanıcı geri bildirimi: "bazı oyunları almıyor, mesela Addams Family" — kök neden ARAŞTIRILDI
    // (gerçek RetroAudit.db + gerçek zip dosyaları karşılaştırılarak): eskiden SADECE her oyunun
    // tercih edilen (grid'in Dosya sütununda görünen) sürümünün dosya adına göre eşleşiyordu; aynı
    // oyunun Europe/Japan gibi alternatif bölge dosyaları (katalogda GERÇEKTEN kayıtlı olmalarına
    // rağmen) hiçbir zaman eşleşemiyordu. Artık CatalogDatabaseService.GetAllVersionRecordsByGame
    // ile TÜM sürüm/dump varyantlarının dosya adları da eşleşme anahtarı olarak kullanılıyor.
    //
    // İKİNCİ, bağımsız bir sorun daha bulundu: eşleştirme sözlüğü platform ayrımı yapmadan TEK bir
    // düz "dosya adı -> oyun" haritasıydı — katalogda 1900'ün üzerinde dosya adı ÇAKIŞMASI var
    // (özellikle çok diskli platformlarda "Manual.pdf", "Arkanoid.ipf" gibi jenerik adlar
    // tekrarlanıyor), son yazan öncekini SESSİZCE eziyordu. Artık her dosya adı için TÜM aday
    // oyunlar tutuluyor; birden fazla aday varsa, kullanıcının zaten kullandığı "her platform kendi
    // alt klasöründe" düzenine göre dosyanın BULUNDUĞU klasörün adı adayların platform adlarıyla
    // (bkz. PlatformNameMatchesFolder) karşılaştırılıp tek bir eşleşme varsa O seçilir.
    //
    // ÜÇÜNCÜ bulgu (kullanıcı geri bildirimi: "eşleşmeyenler ile ilgili ne yapacağız" — gerçek
    // export edilmiş CRC32 verisi katalogla karşılaştırılarak analiz edildi): dosya adı DAT
    // revizyonları arasında değişebiliyor (ör. "Rev A" -> "Rev 1") ama İÇERİK aynı kalıyor. Dosya
    // adı hiçbir adayla eşleşmezse (veya adaylar arasında belirsizse), dosyanın GERÇEK CRC32'si
    // hesaplanıp katalogdaki TÜM hash'lere karşı da denenir — sadece bu durumda (isim eşleşmesi
    // zaten başarısız olduğunda) dosya okunur, ucuz isim kontrolünü YAVAŞLATMAZ.
    //
    // DÖRDÜNCÜ bulgu (kullanıcı geri bildirimi: "crc'si eşleşmeyenleri kendi crc'si ile programa
    // eklesek ne olur" — gerçek CSV verisiyle doğrulandı: NES setinde 446 eşleşmeyenin 246'sı,
    // yani %55'i bu şekilde kurtarılıyor): isim VE CRC32 ikisi de tutmasa bile, dosya adı
    // DatNameParser.Parse ile ayrıştırılıp temiz başlık+bölge+revizyon kataloktaki TEK bir
    // GameVersion'a denk geliyorsa yine de eşleşme kabul edilir (bkz. ResolveCandidate "Tier 3").
    // Metadata zaten GameId üzerinden RetroAudit.db'den geliyor (dosyanın CRC'sinden bağımsız),
    // eşleşme SADECE "hangi Game/sürüm" sorusuna cevap veriyor — bu yüzden içerik farklı bir dump
    // nesli olsa da kütüphanede sorunsuz görünür.
    //
    // BEŞİNCİ bulgu (kullanıcı isteği + ChatGPT önerisi, gerçek DB verisiyle doğrulandı: 111
    // "başlık hiç tutmuyor" dosyasından 43'ü normalize edilince TEK bir Game'e denk geliyor, 42'si
    // sürüm seviyesinde de kesinleşiyor, 4 GERÇEK çakışma testinde [Final Fantasy I,II / III gibi]
    // YANLIŞ eşleşme SIFIR): dosya adı Tier 3'ün aradığı TAM başlıkla bile tutmayabilir (ör.
    // "Bomber Man" vs kataloktaki "Bomberman", "2010 - Street Fighter" vs "2010 Street Fighter").
    // Bu durumda başlık agresif normalize edilip (boşluk/tire/noktalama silinip küçük harfe
    // çevrilip, bkz. NormalizeTitleAggressive) SEÇİLİ PLATFORM içinde TEK bir Game'e denk geliyorsa
    // (bkz. ResolveCandidate "Tier 4") yine denenir — sonra AYNI bölge/revizyon/tie-break kuralları
    // uygulanır (bkz. TryResolveVersion, Tier 3 ile paylaşılıyor). Normalize edilmiş başlık birden
    // fazla FARKLI Game'e denk geliyorsa (ör. "Final Fantasy I, II" ve "Final Fantasy III" ikisi de
    // "finalfantasyiii" oluyor) KESİNLİKLE otomatik seçim yapılmaz. Bu katmanla eşleşen kayıtlar
    // RomMatch.MatchMethod'da "Normalize Edilmiş Başlık (Normalized Title Match)" olarak ayrıca
    // işaretlenir — CRC ile doğrulanmış bir eşleşme gibi GÖSTERİLMEZ.
    //
    // Hiçbir dosya artık sessizce ATLANMIYOR — "Eşleşmeyenler" listesine sebebiyle birlikte
    // ekleniyor (kullanıcı isteği: "önce eklenmeyenleri tespit etmek lazım"); sebep de artık
    // "kasıtlı dışlanan" (Beta/Unlicensed/Prototip — bkz. DatNameParser.ContainsExcludedTag, bu
    // etiketli DAT kayıtları katalog inşa edilirken zaten hiç alınmıyor) ile "gerçekten
    // katalogda/DAT'ta yok" (ör. farklı/doğrulanmamış bir dump nesli) arasında ayrım yapıyor.
    // alreadyLinkedPaths — kullanıcı geri bildirimi: "pinball'ı bağlamıştım halbuki, eşleşmeyenlerde
    // gözükmemesi lazım değil mi" ... sonra "tarama esnasında klasörde ne varsa gösterse daha
    // anlaşılır olmazmı" (rozetle gösterilmeye çevrildi) ... sonra netleşti: "zaten bağlı olanlar
    // eşleşmeyenlerde neden gözüküyor ... tablodaki bi oyunun kartına bağlı sonuçta" — bu tarama
    // SADECE otomatik DAT eşleşmesine bakıyor, kullanıcının ELLE (Bağla/Şu anki yoldan kullan/
    // "+ Yeni Oyun") zaten bir oyuna bağladığı dosyaların hiç haberi yoktu. SON karar: Eşleşenler'de
    // (RomMatch) rozetle görünmeye devam eder (o listede "bu tür DAT eşleşmesi var" bilgisi hâlâ
    // faydalı) ama Eşleşmeyenler'e (UnmatchedRomFile) HİÇ eklenmiyor — zaten bir oyunun kartına
    // bağlıysa "gerçekten eşleşmeyen/aksiyon bekleyen" değildir. Zip'ler için yol, arşivin KENDİSİ
    // (girdi değil) — bkz. FilePathOverrides.FilePath'in de aynı şekilde zip'in tam yolunu tutması.
    public static RomScanResult ScanFolder(string sourceFolder, IReadOnlyList<Game> allGames, string retroAuditDataPath, IReadOnlySet<string>? alreadyLinkedPaths = null)
    {
        var matches = new List<RomMatch>();
        var unmatched = new List<UnmatchedRomFile>();
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            return new RomScanResult(matches, unmatched);

        var indexes = BuildCandidateIndexes(allGames);

        foreach (var filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var alreadyLinked = alreadyLinkedPaths is not null && alreadyLinkedPaths.Contains(filePath);

            if (string.Equals(Path.GetExtension(filePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                ScanZipEntries(filePath, indexes, retroAuditDataPath, matches, unmatched, alreadyLinked);
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var resolved = ResolveCandidate(fileName, filePath, filePath, null, indexes,
                out var reason, out var excludedTag, out var destinationFileNameOverride, out var matchMethod);
            if (resolved is null)
            {
                if (!alreadyLinked)
                    unmatched.Add(new UnmatchedRomFile { FileName = fileName, SourcePath = filePath, Reason = reason!, ExcludedTag = excludedTag });
                continue;
            }

            var destinationPath = Path.Combine(retroAuditDataPath, resolved.Platform, destinationFileNameOverride ?? resolved.File);
            // Kullanıcı geri bildirimi: "Zaten Bağlı olsa bile TÜMÜ varsayılan işaretli gelsin" —
            // rozet (IsAlreadyLinked, bkz. resolved.HasLocalFile) sadece BİLGİ amaçlı kalıyor,
            // hangi satırların içe aktarılacağını ETKİLEMİYOR — o zaten hemen hemen her satırı
            // işaretsiz bırakıp "içe aktarmak için en az bir satır seçin" hatasına yol açıyordu.
            var alreadyOwned = alreadyLinked || File.Exists(destinationPath) || resolved.HasLocalFile;

            matches.Add(new RomMatch
            {
                Game = resolved,
                SourcePath = filePath,
                DestinationPath = destinationPath,
                IsAlreadyLinked = alreadyOwned,
                MatchMethod = matchMethod,
            });
        }

        return new RomScanResult(matches, unmatched);
    }

    private sealed record CandidateIndexes(
        Dictionary<string, List<Game>> ByFile,
        Dictionary<string, List<Game>> ByCrc32,
        Dictionary<string, List<Game>> ByTitle,
        Dictionary<string, List<Game>> ByNormalizedTitle,
        Dictionary<string, List<Game>> ByAlternateName,
        Dictionary<string, List<Game>> ByNormalizedAlternateName,
        Dictionary<int, List<CatalogVersionRecord>> VersionDetailsByGame);

    // Her dosya adı/CRC32/başlık için katalogdaki TÜM aday oyunları toplar — hem tercih edilen
    // sürümün dosya adı (game.File, GetGames'te zaten yüklü) hem de aynı oyunun diğer sürüm/dump
    // varyantlarının dosya adları VE CRC32'leri (bkz. dosya başı yorum). Aynı oyun birden fazla
    // dosya adına/hash'e (kendi versiyonları) sahip olabildiği için anahtar dosya adı/CRC32/başlık,
    // değer ise ona uyan DİSTİNCT oyun listesi.
    private static CandidateIndexes BuildCandidateIndexes(IReadOnlyList<Game> allGames)
    {
        var byFile = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        var byCrc32 = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        var byTitle = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedTitle = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        var byAlternateName = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedAlternateName = new Dictionary<string, List<Game>>(StringComparer.OrdinalIgnoreCase);

        static void AddCandidate(Dictionary<string, List<Game>> index, string key, Game game)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<Game>();
                index[key] = list;
            }
            if (!list.Contains(game))
                list.Add(game);
        }

        var gamesById = new Dictionary<int, Game>();
        foreach (var game in allGames)
        {
            gamesById[game.GameId] = game;
            AddCandidate(byFile, game.File, game);
            AddCandidate(byTitle, game.Title, game);
            AddCandidate(byNormalizedTitle, NormalizeTitleAggressive(game.Title), game);
        }

        foreach (var (gameId, records) in CatalogDatabaseService.GetAllVersionRecordsByGame())
        {
            if (!gamesById.TryGetValue(gameId, out var game))
                continue; // Kaldırılmış platform (RemovedPlatformName) — GetGames'te zaten yok.
            foreach (var (fileName, crc32) in records)
            {
                AddCandidate(byFile, fileName, game);
                AddCandidate(byCrc32, crc32, game);
            }
        }

        foreach (var (gameId, altNames) in CatalogDatabaseService.GetAllAlternateNames())
        {
            if (!gamesById.TryGetValue(gameId, out var game))
                continue;
            foreach (var altName in altNames)
            {
                AddCandidate(byAlternateName, altName, game);
                AddCandidate(byNormalizedAlternateName, NormalizeTitleAggressive(altName), game);
            }
        }

        return new CandidateIndexes(byFile, byCrc32, byTitle, byNormalizedTitle, byAlternateName, byNormalizedAlternateName, CatalogDatabaseService.GetAllVersionDetailsByGame());
    }

    // Tier 3/4'ün "başlık başlangıçta bulundu" ADIMINDAN SONRAKİ ORTAK kısmı: aday oyun(lar)ı
    // gerektiğinde platform klasörüne göre daraltır, ayrıştırılan bölge(ler)/revizyon etiketiyle
    // (Rev harf<->sayı normalizasyonu dahil) kataloktaki GameVersion'lara karşı dener, birden fazla
    // sürüm çakışırsa yeniden-yayın etiketi tie-break'ini uygular (bkz. FindReissueTag). Tier 3 ve
    // Tier 4 arasında BİREBİR aynı mantık — kod tekrarını önlemek için tek metoda çıkarıldı.
    private enum VersionResolution { Resolved, Ambiguous, NoMatch }

    private static VersionResolution TryResolveVersion(List<Game> gameCandidates, ParsedDatName parsed, string fileName, string containingPath,
        Dictionary<int, List<CatalogVersionRecord>> versionDetailsByGame, out Game? matchedGame, out CatalogVersionRecord? matchedVersion)
    {
        var platformCandidates = gameCandidates.Count == 1
            ? gameCandidates
            : (DisambiguateByFolder(gameCandidates, containingPath) is { } single ? new List<Game> { single } : gameCandidates);

        var normalizedLabel = DatNameParser.NormalizeVersionLabel(parsed.VersionLabel);
        var regionSet = new HashSet<string>(parsed.Regions, StringComparer.OrdinalIgnoreCase);
        var versionMatches = new List<(Game Game, CatalogVersionRecord Version)>();
        foreach (var game in platformCandidates)
        {
            if (!versionDetailsByGame.TryGetValue(game.GameId, out var versions))
                continue;
            foreach (var version in versions)
            {
                var dbRegionSet = string.IsNullOrWhiteSpace(version.AllRegionsRaw)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(version.AllRegionsRaw.Split(',', StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                if (!dbRegionSet.SetEquals(regionSet))
                    continue;
                if (!string.Equals(DatNameParser.NormalizeVersionLabel(version.VersionLabel), normalizedLabel, StringComparison.OrdinalIgnoreCase))
                    continue;
                versionMatches.Add((game, version));
            }
        }

        // Gerçek veriyle doğrulandı: aynı başlık+bölge+revizyona sahip birden fazla sürüm sık sık
        // "Virtual Console"/"GameCube Edition"/"Retro-Bit Generations" gibi yeniden-yayın
        // etiketleriyle ayrılıyor (bölge/revizyon alanlarında görünmüyor, sadece RawDatName'de).
        // Dosyanın KENDİ adında bu etiketlerden biri varsa AYNI etiketi taşıyan sürüm seçilir; yoksa
        // hiçbir yeniden-yayın etiketi TAŞIMAYAN "düz" (orijinal kartuş) sürüm tercih edilir —
        // kişisel ROM arşivleri genelde dijital servis rip'i değil, orijinal kartuş dump'ıdır. Bu
        // ikisinin dışında kalan (ör. iki "düz" sürüm birbirinden ayırt edilemiyor, ya da farklı
        // yayıncı/ürün-kodu varyantları) GERÇEK belirsizlik — tahmin YÜRÜTÜLMEZ.
        if (versionMatches.Count > 1)
        {
            var sourceReissueTag = FindReissueTag(Path.GetFileNameWithoutExtension(fileName));
            var tieBroken = sourceReissueTag is not null
                ? versionMatches.Where(m => string.Equals(FindReissueTag(m.Version.RawDatName), sourceReissueTag, StringComparison.OrdinalIgnoreCase)).ToList()
                : versionMatches.Where(m => FindReissueTag(m.Version.RawDatName) is null).ToList();

            if (tieBroken.Count == 1)
                versionMatches = tieBroken;
        }

        if (versionMatches.Count == 1)
        {
            (matchedGame, matchedVersion) = versionMatches[0];
            return VersionResolution.Resolved;
        }

        matchedGame = null;
        matchedVersion = null;
        return versionMatches.Count > 1 ? VersionResolution.Ambiguous : VersionResolution.NoMatch;
    }

    // Bir dosya için tek, kesin bir oyun bulur — sırasıyla dosya adı (Tier 1), GERÇEK içerik CRC32'si
    // (Tier 2), tam başlık+bölge/revizyon (Tier 3), agresif normalize edilmiş başlık+bölge/revizyon
    // (Tier 4) denenir. Hiçbiri sonuç vermezse null döner (reason parametresi kullanıcıya
    // gösterilecek Türkçe sebep; excludedTag — bkz. UnmatchedRomFile — sadece Beta/Unlicensed/
    // Prototip vb. etiketli, kasıtlı dışlanan dosyalarda dolu, spesifik etiket adı; matchMethod —
    // bkz. RomMatch — kullanıcıya HANGİ katmanla eşleştiğini gösterir, CRC doğrulanmış gibi
    // sunulmasın diye).
    private static Game? ResolveCandidate(string fileName, string containingPath, string sourcePathForHash, string? zipEntryNameForHash,
        CandidateIndexes indexes, out string? reason, out string? excludedTag, out string? destinationFileNameOverride, out string matchMethod)
    {
        excludedTag = null;
        destinationFileNameOverride = null;
        matchMethod = string.Empty;

        var nameCandidates = indexes.ByFile.TryGetValue(fileName, out var byName) ? byName : null;
        if (nameCandidates is { Count: 1 })
        {
            reason = null;
            matchMethod = "Dosya Adı (Exact Name Match)";
            return nameCandidates[0];
        }

        if (nameCandidates is { Count: > 1 } && DisambiguateByFolder(nameCandidates, containingPath) is { } byNameResolved)
        {
            reason = null;
            matchMethod = "Dosya Adı (Exact Name Match)";
            return byNameResolved;
        }

        // İsim eşleşmedi/belirsizdi — dosyanın GERÇEK içeriğine (CRC32) bakılıyor (bkz. dosya başı
        // yorum, 3. bulgu). Bozuk/okunamayan bir dosya burada taramayı DURDURMASIN.
        string? crc32 = null;
        try
        {
            crc32 = ComputeCrc32(sourcePathForHash, zipEntryNameForHash);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (crc32 is not null && indexes.ByCrc32.TryGetValue(crc32, out var byCrc) && byCrc.Count > 0)
        {
            var resolved = byCrc.Count == 1 ? byCrc[0] : DisambiguateByFolder(byCrc, containingPath);
            if (resolved is not null)
            {
                reason = null;
                matchMethod = "İçerik (CRC Match)";
                return resolved;
            }
        }

        var parsed = DatNameParser.Parse(Path.GetFileNameWithoutExtension(fileName));

        // Gerçek log ile doğrulandı (kullanıcı geri bildirimi: "Akumajou Dracula/Batman/Castle
        // Excellent/Ikari/Xevious gibi tabloda olması gereken oyunlar neden eşleşmiyor" — DB analiziyle
        // kesinleşti): aynı başlık BAŞKA bir platformda da varsa (ör. "Batman" NES'te değil sadece
        // MSX/PC-Engine'de tam bu adla geçiyor — NES'inki "Batman - The Video Game"), eski davranışta
        // titleCandidates SEÇİLİ PLATFORMA hiç daraltılmadan TryResolveVersion'a veriliyordu; yanlış
        // platformun kendi sürüm çakışması "Ambiguous" dönüp Tier 5'in (AlternateNames, doğru NES
        // eşleşmesini zaten bulabilecek olan) hiç denenmesini ENGELLİYORDU. Artık Tier 3'e SADECE
        // seçili platformdaki adaylar giriyor; bu platformda hiç aday yoksa Tier 3 hiç denenmeden
        // (yanlış bir "Ambiguous" vermeden) doğrudan Tier 4/5'e düşülüyor.
        if (!parsed.ShouldExclude && indexes.ByTitle.TryGetValue(parsed.CleanTitle, out var titleCandidates))
        {
            var titleFolderName = Path.GetFileName(Path.GetDirectoryName(containingPath)) ?? string.Empty;
            var platformScopedTitleCandidates = titleCandidates.Where(g => PlatformNameMatchesFolder(g, titleFolderName)).ToList();

            if (platformScopedTitleCandidates.Count > 0)
            {
                switch (TryResolveVersion(platformScopedTitleCandidates, parsed, fileName, containingPath, indexes.VersionDetailsByGame, out var matchedGame, out var matchedVersion))
                {
                    case VersionResolution.Resolved:
                        var sourceExtension = Path.GetExtension(fileName);
                        destinationFileNameOverride = matchedVersion!.FileNames.FirstOrDefault(f =>
                            string.Equals(Path.GetExtension(f), sourceExtension, StringComparison.OrdinalIgnoreCase)) ?? matchedVersion.FileNames[0];
                        matchMethod = "Başlık + Bölge/Revizyon (Revision Normalize)";
                        reason = null;
                        return matchedGame;
                    case VersionResolution.Ambiguous:
                        reason = "Bu dosya, katalogda aynı başlık/bölgeye sahip birden fazla sürümle eşleşiyor — otomatik seçilemedi.";
                        return null;
                }
            }
        }

        if (nameCandidates is { Count: > 1 })
        {
            reason = $"Bu dosya adı birden fazla oyunla eşleşiyor ({string.Join(", ", nameCandidates.Select(g => g.PlatformDisplayName).Distinct())}) — otomatik seçilemedi.";
            return null;
        }

        // BEŞİNCİ bulgu (bkz. dosya başı yorum) — tam başlık da tutmadı; agresif normalize edilmiş
        // başlık SEÇİLİ PLATFORM içinde TEK bir Game'e denk geliyorsa yine denenir. "Yalnızca seçili
        // platform içindeki kayıtları karşılaştır" kuralı gereği, platforma göre daraltma boş
        // sonuç verirse (bu platformda hiç aday yoksa) BAŞKA platformlara DÜŞÜLMEZ — Tier 3'ün
        // aksine burada belirsizlik payı bırakılmıyor, çünkü normalize edilmiş başlık zaten daha
        // gevşek bir eşleşme.
        if (!parsed.ShouldExclude && indexes.ByNormalizedTitle.TryGetValue(NormalizeTitleAggressive(parsed.CleanTitle), out var normalizedCandidates))
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(containingPath)) ?? string.Empty;
            var platformScoped = normalizedCandidates.Where(g => PlatformNameMatchesFolder(g, folderName)).Distinct().ToList();

            if (platformScoped.Count == 1)
            {
                switch (TryResolveVersion(platformScoped, parsed, fileName, containingPath, indexes.VersionDetailsByGame, out var matchedGame, out var matchedVersion))
                {
                    case VersionResolution.Resolved:
                        var sourceExtension = Path.GetExtension(fileName);
                        destinationFileNameOverride = matchedVersion!.FileNames.FirstOrDefault(f =>
                            string.Equals(Path.GetExtension(f), sourceExtension, StringComparison.OrdinalIgnoreCase)) ?? matchedVersion.FileNames[0];
                        matchMethod = "Normalize Edilmiş Başlık (Normalized Title Match)";
                        reason = null;
                        return matchedGame;
                    case VersionResolution.Ambiguous:
                        reason = "Bu dosya, normalize edilmiş başlıkla katalogda birden fazla sürümle eşleşiyor — otomatik seçilemedi.";
                        return null;
                }
            }
        }

        // ALTINCI bulgu (kullanıcı isteği: "AlternateNames'i Tier 5 olarak ekleyebiliriz" — gerçek
        // veriyle doğrulandı: doğru katalogda 138 "başlık hiç tutmuyor" dosyasından 81'i LaunchBox'ın
        // AlternateNames'inde TEK bir Game'e, 70'i sürüm seviyesinde de kesin eşleşiyor): önceki
        // TÜM katmanlar (isim/CRC/tam başlık/normalize başlık) başarısız olduysa, dosya adı bu kez
        // LaunchBox'ın "alternatif isim" listesine (bölgesel/yeniden isimlendirilmiş sürümler — ör.
        // "Kage" (Japan) = "Shadow of the Ninja" (USA); DAT bunları AYRI Game kayıtları olarak
        // tutar, sadece LaunchBox bu bağlantıyı bilir) karşı denenir — önce TAM, sonra normalize
        // edilmiş biçimde (Tier 3/4 ile AYNI iki aşama). Tier 4 ile AYNI güvenlik kuralı: SEÇİLİ
        // PLATFORM içinde TEK bir Game'e denk gelmiyorsa (birden fazla farklı oyun aynı alternatif
        // adı paylaşıyorsa) KESİNLİKLE otomatik seçim yapılmaz — ardından yine AYNI bölge/revizyon/
        // tie-break kuralları (TryResolveVersion) uygulanır.
        if (!parsed.ShouldExclude)
        {
            var altNameCandidates = indexes.ByAlternateName.TryGetValue(parsed.CleanTitle, out var exactAlt)
                ? exactAlt
                : (indexes.ByNormalizedAlternateName.TryGetValue(NormalizeTitleAggressive(parsed.CleanTitle), out var normalizedAlt) ? normalizedAlt : null);

            if (altNameCandidates is not null)
            {
                var folderName = Path.GetFileName(Path.GetDirectoryName(containingPath)) ?? string.Empty;
                var platformScoped = altNameCandidates.Where(g => PlatformNameMatchesFolder(g, folderName)).Distinct().ToList();

                if (platformScoped.Count == 1)
                {
                    switch (TryResolveVersion(platformScoped, parsed, fileName, containingPath, indexes.VersionDetailsByGame, out var matchedGame, out var matchedVersion))
                    {
                        case VersionResolution.Resolved:
                            var sourceExtension = Path.GetExtension(fileName);
                            destinationFileNameOverride = matchedVersion!.FileNames.FirstOrDefault(f =>
                                string.Equals(Path.GetExtension(f), sourceExtension, StringComparison.OrdinalIgnoreCase)) ?? matchedVersion.FileNames[0];
                            matchMethod = "Alternatif İsim (Alternate Name Match)";
                            reason = null;
                            return matchedGame;
                        case VersionResolution.Ambiguous:
                            reason = "Bu dosya, alternatif isimle katalogda birden fazla sürümle eşleşiyor — otomatik seçilemedi.";
                            return null;
                    }
                }
            }
        }

        // "Katalogda yok" iki farklı anlama gelebilir: (a) RetroAudit'in KASITLI olarak
        // dışladığı bir DAT etiketi (Beta/Unlicensed/Prototip/Hack vb. — bkz. DatNameParser.
        // TryGetExcludedTag, bunlar katalog inşa edilirken zaten hiç alınmıyor, düzeltilecek bir
        // şey yok) — (b) gerçekten farklı/doğrulanmamış bir dump nesli VE o bölge/revizyonun
        // kataloktaki karşılığı da yok (yukarıdaki Tier 3/4/5 de başarısız oldu) — yeni bir DAT
        // kaynağı olmadan koddan düzeltilemez.
        excludedTag = DatNameParser.TryGetExcludedTag(fileName);
        reason = excludedTag is not null
            ? $"Bu dosya (\"{excludedTag}\" etiketli) RetroAudit kataloğunda kasıtlı olarak yer almıyor."
            : "Katalogda bu dosya adıyla, içeriğiyle (CRC32), başlık/bölge/revizyonuyla (tam ya da normalize edilmiş) veya alternatif isimle eşleşen bir kayıt yok.";
        return null;
    }

    // Aynı dosya adı/CRC32/başlık birden fazla platformda/oyunda geçiyorsa (bkz. dosya başı yorum),
    // dosyanın içinde bulunduğu klasörün adı bir adayın platform adıyla (bkz.
    // PlatformNameMatchesFolder) TEK bir şekilde eşleşiyorsa o seçilir — aksi halde null (belirsiz).
    private static Game? DisambiguateByFolder(List<Game> candidates, string containingPath)
    {
        var folderName = Path.GetFileName(Path.GetDirectoryName(containingPath)) ?? string.Empty;
        var byFolder = candidates.Where(g => PlatformNameMatchesFolder(g, folderName)).ToList();
        return byFolder.Count == 1 ? byFolder[0] : null;
    }

    // Gerçek log ile doğrulandı (tier3-debug.log, "10-Yard Fight" örneği: NES'te VE MSX'te aynı
    // isimde ayrı bir oyun var): kullanıcının klasörü "Nintendo Entertainment System" ama
    // PlatformDisplayName sadece "Nintendo" (bkz. PlatformDisplayNameMap) olduğu için ikisi de
    // eşleşmiyordu, DisambiguateByFolder hiçbirini seçemeyip TÜM adayları (MSX dahil) geri
    // düşürüyordu. Kişisel ROM klasörleri genelde ham DAT'ın "{Üretici} - {Konsol}" kalıbının
    // SADECE konsol kısmını kullanıyor (No-Intro/RetroArch klasör kuralı) — bu yüzden klasör adı
    // artık PlatformDisplayName'e, ham Platform adına VE o ham adın ilk " - "den sonraki kısmına
    // karşı deneniyor.
    // İç kullanımın yanı sıra ManualLinkViewModel de "hangi platformun oyunları önce gösterilsin"
    // kararı için AYNI klasör-platform karşılaştırmasını kullanıyor (kullanıcı isteği: "manuel
    // bağlama penceresi varsayılan olarak yalnızca taranan platformun oyunlarını göstersin") — kod
    // tekrarını/uyuşmazlığını önlemek için public.
    public static bool PlatformNameMatchesFolder(Game game, string folderName)
    {
        if (string.Equals(game.PlatformDisplayName, folderName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(game.Platform, folderName, StringComparison.OrdinalIgnoreCase))
            return true;

        var dashIndex = game.Platform.IndexOf(" - ", StringComparison.Ordinal);
        return dashIndex >= 0 && string.Equals(game.Platform[(dashIndex + 3)..], folderName, StringComparison.OrdinalIgnoreCase);
    }

    // Tier 4 (bkz. ResolveCandidate, dosya başı BEŞİNCİ bulgu) için: başlığı harf/rakam DIŞINDAKİ
    // her şeyi (boşluk, tire, virgül, kesme işareti, ünlem vb.) atıp küçük harfe çevirerek "kaba"
    // bir karşılaştırma anahtarı üretir — "Bomber Man" ve "Bomberman" ya da "2010 - Street Fighter"
    // ve "2010 Street Fighter" aynı anahtara düşer. Regex KULLANILMIYOR (67 bin oyun için tek
    // seferlik ama basit bir filtre, performans/okunabilirlik açısından yeterli).
    // internal: MainViewModel.FoldCustomGamesIntoMatchingCatalogGames de AYNI normalizasyonu
    // alternatif isim eşleştirmesinde kullanıyor (ör. "Dragon Quest III: ..." / "... - ...").
    internal static string NormalizeTitleAggressive(string title) =>
        new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Tier 3/4 tie-break'te (bkz. TryResolveVersion) aynı bölge/revizyona sahip sürümleri ayıran
    // yeniden-yayın/derleme etiketleri — gerçek katalog verisinde (No-Intro DAT) GÖZLEMLENDİ,
    // kapsamlı bir liste DEĞİL (yeni bir etiket görülürse eklenebilir; eksik kalması sadece o
    // dosyanın belirsiz kalıp Eşleşmeyenler'de kalmasına yol açar, YANLIŞ bir eşleşmeye değil).
    private static readonly string[] ReissueTagMarkers =
    {
        "Virtual Console", "GameCube Edition", "Switch Online", "3D Classics", "Arcade Archives",
        "Wii U", "Classic Mini", "Animal Crossing", "e-Reader", "Namcot Collection",
        "FamicomBox", "Namco Anthology", "Zelda Collection", "SGB Enhanced", "Retro-Bit Generations",
        "Datach", "ArchiMENdes Hen", "Genteiban",
    };

    private static string? FindReissueTag(string text) =>
        ReissueTagMarkers.FirstOrDefault(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void ScanZipEntries(string zipPath, CandidateIndexes indexes, string retroAuditDataPath, List<RomMatch> matches, List<UnmatchedRomFile> unmatched, bool alreadyLinked = false)
    {
        // Bozuk/parola korumalı/gerçekte zip olmayan bir dosya taramayı durdurmasın — sadece atlanır.
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // dizin girdisi

                var resolved = ResolveCandidate(entry.Name, zipPath, zipPath, entry.Name, indexes,
                    out var reason, out var excludedTag, out var destinationFileNameOverride, out var matchMethod);
                if (resolved is null)
                {
                    if (!alreadyLinked)
                        unmatched.Add(new UnmatchedRomFile { FileName = entry.Name, SourcePath = zipPath, Reason = reason!, ZipEntryName = entry.Name, ExcludedTag = excludedTag });
                    continue;
                }

                var destinationPath = Path.Combine(retroAuditDataPath, resolved.Platform, destinationFileNameOverride ?? resolved.File);
                // Bkz. ScanFolder'daki AYNI gerekçe — hedefte zaten aynı isimli dosya varsa (standart
                // kuralla zaten sahip olunan bir sürüm) YA DA bu OYUNUN başka bir dosyası zaten varsa
                // (bkz. resolved.HasLocalFile) da "zaten bağlı" sayılır.
                var alreadyOwned = alreadyLinked || File.Exists(destinationPath) || resolved.HasLocalFile;

                matches.Add(new RomMatch
                {
                    Game = resolved,
                    SourcePath = zipPath,
                    DestinationPath = destinationPath,
                    ZipEntryName = entry.Name,
                    IsAlreadyLinked = alreadyOwned,
                    MatchMethod = matchMethod,
                });
            }
        }
        catch (InvalidDataException)
        {
        }
    }

    // Bir .zip arşivindeki tek bir girdiyi hedef yola çıkarır (arşivin tamamını değil).
    public static void ExtractZipEntry(string zipPath, string entryName, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName)
            ?? throw new FileNotFoundException($"Zip içinde bulunamadı: {entryName}", zipPath);
        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    // "Taşı" seçiliyken bir zip'in kaynaktan tamamen silinmesi ancak o zip'in TEK girdisi
    // içe aktarılan oyunsa güvenlidir — aksi halde zip içindeki başka oyunların verisi kaybolur.
    public static int CountZipEntries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
    }

    // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan alıp yazamıyormu" —
    // MainViewModel.LinkFile (ana tablodaki kapsül menüsündeki "Bağla") bilgisayardan seçilen
    // dosyanın kendisinin bir zip olup olmadığını, ZipEntryName'i elle bilmeden anlayabilsin diye —
    // TEK girdili zip'lerde (ki kişisel ROM arşivlerinde en yaygın durum) o girdinin adını döner,
    // birden fazla girdi varsa (hangisinin ROM olduğu belirsiz) null döner.
    public static string? GetSoleZipEntryName(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".zip", StringComparison.OrdinalIgnoreCase))
            return null;

        using var archive = ZipFile.OpenRead(filePath);
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        return entries.Count == 1 ? entries[0].FullName : null;
    }

    // Kullanıcı isteği: "eşleşmeyenlerde beta proto vs olanları silmek için buton ekle klasörden
    // silsin ... tıklamalı seçenekli yap onları seçili olanları silsin" — SADECE çağıran tarafın
    // (RomImportViewModel) IsSelected=true diye zaten filtrelediği dosyaları hedefler, kategoriye
    // göre otomatik toplu silme YOK. Kalıcı File.Delete YERİNE Windows Çöp Kutusu'na taşınıyor
    // (bkz. RecycleBinService) — yanlışlıkla silinen bir dosya geri alınabilsin diye. Zip içindeki
    // bir girdiyse (bkz. ZipEntryName) TEK BİR girdiyi arşivden çıkarmanın güvenli bir yolu yok —
    // bu yüzden SADECE o zip'in TEK girdisi buysa (CountZipEntries == 1, "Taşı" modundaki AYNI
    // güvenlik kuralı) arşivin TAMAMI çöp kutusuna taşınır; birden fazla girdisi varsa
    // dokunulmadan atlanır (aksi halde zip içindeki başka oyunların verisi de kaybolurdu).
    public static RomDeleteResult DeleteSelectedFiles(IEnumerable<UnmatchedRomFile> files)
    {
        var deleted = 0;
        var skippedMultiEntryZip = 0;
        var failed = 0;
        var handledZipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                if (file.ZipEntryName is not null)
                {
                    if (!handledZipPaths.Add(file.SourcePath))
                        continue; // Aynı zip'in başka bir eşleşmeyen girdisi için zaten işlendi.

                    if (CountZipEntries(file.SourcePath) != 1)
                    {
                        skippedMultiEntryZip++;
                        continue;
                    }
                }

                if (!File.Exists(file.SourcePath))
                    continue; // Zaten silinmiş/taşınmış.

                if (RecycleBinService.MoveToRecycleBin(file.SourcePath))
                    deleted++;
                else
                    failed++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return new RomDeleteResult(deleted, skippedMultiEntryZip, failed);
    }

    // Eşleşen dosyayı (düz dosya veya bir zip girdisini) GameHashes'teki kayıtlı CRC32 ile
    // karşılaştırır. İçerik, belleğe tamamen alınmadan (Append(Stream) ile) akış olarak okunur —
    // PS2/PS3 boyutundaki dosyalarda bile düşük bellek.
    public static bool VerifyHash(RomMatch match)
    {
        var computedHex = ComputeCrc32(match.SourcePath, match.ZipEntryName);
        return CatalogDatabaseService.GetVersions(match.Game.GameId, match.Game.GameKey)
            .SelectMany(v => v.Hashes)
            .Any(h => string.Equals(h.Crc32, computedHex, StringComparison.OrdinalIgnoreCase));
    }

    // Kullanıcı isteği: "crc32'ler yazmıyor csv'de" — eşleşmeyen bir dosyanın GERÇEK CRC32'sini
    // hesaplar, böylece dışa aktarılan liste (bkz. RomImportViewModel.ExportUnmatchedAsync) sadece
    // dosya adına değil, gerçek içerik hash'ine göre de incelenebilir.
    public static string ComputeCrc32(UnmatchedRomFile file) => ComputeCrc32(file.SourcePath, file.ZipEntryName);

    // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan alıp yazamıyormu
    // buraya" — manuel bağlanan kayıtların (bkz. MainViewModel.LinkFile, RomImportViewModel.
    // CompleteManualLinkAsync/ImportUnmatchedFileAsNewGameAsync) Sürümler kartında da CRC32
    // gösterilebilsin diye, UnmatchedRomFile'a bağımlı olmayan genel hali public.
    public static string ComputeCrc32(string sourcePath, string? zipEntryName)
    {
        using var archive = zipEntryName is not null ? ZipFile.OpenRead(sourcePath) : null;
        using var stream = zipEntryName is not null
            ? (archive!.GetEntry(zipEntryName) ?? throw new FileNotFoundException($"Zip içinde bulunamadı: {zipEntryName}", sourcePath)).Open()
            : File.OpenRead(sourcePath);

        var crc32 = new Crc32();
        crc32.Append(stream);

        // System.IO.Hashing.Crc32.GetCurrentHash() sonucu, geleneksel CRC32 hex gösteriminin
        // ters bayt sırasında döner — hex'e çevirmeden önce ters çevrilmesi gerekiyor.
        var hashBytes = crc32.GetCurrentHash();
        Array.Reverse(hashBytes);
        return Convert.ToHexString(hashBytes);
    }
}
