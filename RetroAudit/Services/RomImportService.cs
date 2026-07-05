using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using RetroAudit.Models;

namespace RetroAudit.Services;

// Kullanıcının kendi ROM arşivini (RetroAudit'in {Platform}\{File} kuralına uymayan, serbest
// klasör yapısındaki) tarayıp katalogdaki oyunlarla dosya adı üzerinden eşleştirir. Bulk indirme
// akışındaki (RomSearchWindow) tek-oyun eşleştirmesinin aksine burada kullanıcı zaten sahip olduğu
// dosyaları RetroAudit'e "tanıtıyor" — dosyalara UI'dan ayrı, saf ve test edilebilir bir katman.
public static class RomImportService
{
    // v1 sınırlaması: sadece her oyunun o an tercih edilen (grid'in Dosya sütununda görünen)
    // sürümünün dosya adına göre eşleşir; GameHashes'teki alternatif dump/hash varyantlarının
    // tümüne karşı arama yapmaz (bkz. plan — kapsamlı hash-varyant eşleştirmesi gelecek iş).
    // Kişisel arşivlerde yaygın olan "her oyun kendi .zip'i içinde" düzenini de destekler:
    // düz dosyalar adına göre, .zip'ler ise içindeki girdi adına göre eşleştirilir.
    public static List<RomMatch> ScanFolder(string sourceFolder, IReadOnlyList<Game> allGames, string retroAuditDataPath)
    {
        var result = new List<RomMatch>();
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            return result;

        var gamesByFile = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in allGames)
        {
            if (!string.IsNullOrWhiteSpace(game.File))
                gamesByFile[game.File] = game;
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetExtension(filePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                ScanZipEntries(filePath, gamesByFile, retroAuditDataPath, result);
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            if (!gamesByFile.TryGetValue(fileName, out var matchedGame))
                continue;

            result.Add(new RomMatch
            {
                Game = matchedGame,
                SourcePath = filePath,
                DestinationPath = Path.Combine(retroAuditDataPath, matchedGame.Platform, matchedGame.File),
            });
        }

        return result;
    }

    private static void ScanZipEntries(string zipPath, Dictionary<string, Game> gamesByFile, string retroAuditDataPath, List<RomMatch> result)
    {
        // Bozuk/parola korumalı/gerçekte zip olmayan bir dosya taramayı durdurmasın — sadece atlanır.
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // dizin girdisi

                if (!gamesByFile.TryGetValue(entry.Name, out var matchedGame))
                    continue;

                result.Add(new RomMatch
                {
                    Game = matchedGame,
                    SourcePath = zipPath,
                    DestinationPath = Path.Combine(retroAuditDataPath, matchedGame.Platform, matchedGame.File),
                    ZipEntryName = entry.Name,
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

    // Eşleşen dosyayı (düz dosya veya bir zip girdisini) GameHashes'teki kayıtlı CRC32 ile
    // karşılaştırır. İçerik, belleğe tamamen alınmadan (Append(Stream) ile) akış olarak okunur —
    // PS2/PS3 boyutundaki dosyalarda bile düşük bellek.
    public static bool VerifyHash(RomMatch match)
    {
        using var archive = match.ZipEntryName is not null ? ZipFile.OpenRead(match.SourcePath) : null;
        using var stream = match.ZipEntryName is not null
            ? (archive!.GetEntry(match.ZipEntryName) ?? throw new FileNotFoundException($"Zip içinde bulunamadı: {match.ZipEntryName}", match.SourcePath)).Open()
            : File.OpenRead(match.SourcePath);

        var crc32 = new Crc32();
        crc32.Append(stream);

        // System.IO.Hashing.Crc32.GetCurrentHash() sonucu, geleneksel CRC32 hex gösteriminin
        // ters bayt sırasında döner — hex'e çevirmeden önce ters çevrilmesi gerekiyor.
        var hashBytes = crc32.GetCurrentHash();
        Array.Reverse(hashBytes);
        var computedHex = Convert.ToHexString(hashBytes);

        return CatalogDatabaseService.GetVersions(match.Game.GameId, match.Game.GameKey)
            .SelectMany(v => v.Hashes)
            .Any(h => string.Equals(h.Crc32, computedHex, StringComparison.OrdinalIgnoreCase));
    }
}
