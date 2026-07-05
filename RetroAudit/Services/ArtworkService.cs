using System.IO;
using System.Net.Http;
using System.Text;

namespace RetroAudit.Services;

// Katalogdaki (bkz. CatalogDatabaseService.GetArtworkAssets) görsel varlık dosya adlarını gerçek
// bir görsele çevirip diske indirir. Saf, UI'dan bağımsız bir servis — RomImportService.cs ile
// aynı tarzda statik.
public static class ArtworkService
{
    // Kaynak sunucu adresi genel depoda düz metin arama/görünürlük olmasın diye Base64 ile
    // saklanıyor. Bu gerçek bir gizlilik SAĞLAMIYOR — ağ isteği zaten düz metin gider, paket
    // izleyen herkes adresi görür — sadece kaynak kodun kendisinde plaintext bir literal olarak
    // görünmesin diye (bu depo public).
    private const string EncodedHost = "aW1hZ2VzLmxhdW5jaGJveC1hcHAuY29t";
    private static readonly string Host = Encoding.UTF8.GetString(Convert.FromBase64String(EncodedHost));

    private static readonly HttpClient Http = new();

    private static string BuildUrl(string fileName) => $"https://{Host}/{fileName}";

    // {RetroAuditDataPath}\{Platform}\Media\{typeFolder}\{baseFileName}{indirilen dosyanın uzantısı}.
    // baseFileName, oyunun ROM dosya adıyla aynı kimliği kullanır (uzantısız) — böylece görsel ile
    // ROM arasında 1-e-1 bir karşılık kalır, aynı GameKey/File çakışma kurallarını miras alır.
    public static string BuildLocalPath(string retroAuditDataPath, string platform, string typeFolder, string baseFileName, string sourceFileName) =>
        Path.Combine(retroAuditDataPath, platform, "Media", typeFolder, baseFileName + Path.GetExtension(sourceFileName));

    // fileName, ArtworkAssets.FileName'den gelir (ör. bir GUID + uzantı). Başarısızlıkta (ağ
    // hatası, 404, disk hatası) false döner — RomImportViewModel'deki per-item try/catch deseninin
    // aynısı, çağıran taraf bunu bir "N / Toplam başarısız" özetine topluyor.
    public static async Task<bool> DownloadAsync(string fileName, string destinationPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var bytes = await Http.GetByteArrayAsync(BuildUrl(fileName));
            await File.WriteAllBytesAsync(destinationPath, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
