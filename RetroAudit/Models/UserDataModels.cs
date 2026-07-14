namespace RetroAudit.Models;

// RetroAudit.db'nin Games.GameId'si Builder her koşuda yeniden üretildiği için (AUTOINCREMENT
// sıfırdan başlar) kalıcı değildir — kullanıcı verisini (favori, gizli, playlist, düzenlenmiş
// metadata) buna bağlarsak bir sonraki Builder koşusunda hepsi kaybolur. Bunun yerine
// VersionResolver.Group'un zaten oyun kimliği için kullandığı (Platform, CompareTitle) çiftinden
// türetilen sabit bir anahtar kullanılıyor — bu, DAT/LaunchBox verisi değişmediği sürece
// rebuild'ler arasında aynı kalır.
public static class GameKeyHelper
{
    public static string Compute(string platformName, string compareTitle) => $"{platformName}|{compareTitle}";
}

// GameState tablosundan tek bir oyun için okunan durum (bkz. UserDataService.GetAllGameStates).
// VersionOverride: kullanıcı isteği: "istenilen oyunu junk'a atabilelim veya junktaki bi oyunu
// release'e çekebilelim ... tek tuş toggle" — Builder'ın kataloğun kendi sınıflandırmasını (ör.
// LaunchBox'ın belirsiz bir alternatif isim çakışması yüzünden yanlış Junk'a düşürmesi) geçersiz
// kılar: null = geçersiz kılma yok (kataloğun kendi Version'ı geçerli), "Released"/"Junk" = kalıcı
// olarak zorlanan değer — katalog yeniden inşa edilse bile (GameKey sabit kaldığı sürece) kaybolmaz.
public record GameStateInfo(bool IsHidden, bool IsDeleted, bool IsPermanentlyDeleted, string? VersionOverride = null);

// MetadataOverrides tablosundan tek bir oyun için okunan, kullanıcının elle düzenlediği alanlar.
// Null olan alanlar "düzenlenmedi, katalog değeri geçerli" anlamına gelir.
public record MetadataOverride(
    string? Title,
    string? Genre,
    string? Description,
    string? Notes,
    string? Publisher,
    string? Developer,
    string? VideoUrl,
    int? ReleaseYear,
    string? Region,
    string? PreferredVersionRawName);

// Playlists tablosundaki tek bir satır. IsBuiltIn=true olan (Favorites) yeniden adlandırılamaz/
// silinemez — bkz. UserDataService.DeletePlaylist/RenamePlaylist.
public record PlaylistRecord(int PlaylistId, string Name, string Color, bool IsBuiltIn, int SortOrder);

// FilePathOverrides tablosundan tek bir oyun için okunan dosya-yolu override'ı (bkz.
// UserDataService.SaveFilePathOverride/GetAllFilePathOverrides). MatchMethod — RomMatch.
// MatchMethod ile AYNI kavram, artık kalıcı hale getirildi (kullanıcı isteği: "eşleşme türünü ...
// sakla"): "Exact Name Match"/"CRC Match"/... otomatik katmanlardan biri, ya da MatchMethods.
// ManualLink (bkz. aşağıda) — kullanıcının ROM İçe Aktar'ın Eşleşmeyenler sekmesinden ELLE bir
// oyuna (isterse belirli bir sürümüne) bağladığı kayıtlar. TargetVersionRawName doluysa (bkz.
// GameVersion.RawDatName — GameVersionId gibi ham sayısal ID DEĞİL, çünkü o da Builder
// koşusundan koşusuna kalıcı değil) bu override SADECE o spesifik sürüm için geçerlidir (bkz.
// MainViewModel.ResolveVersionFilePath); null ise Game seviyesinde genel bir bağlantıdır.
// ZipEntryName/Crc32 — kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan
// alıp yazamıyormu buraya" — manuel bağlantılar da (bkz. MainViewModel.LinkFile,
// RomImportViewModel.CompleteManualLinkAsync/ImportUnmatchedFileAsNewGameAsync) Sürümler
// kartında gerçek CRC32'yi gösterebilsin diye. ZipEntryName doluysa FilePath bir .zip'tir ve CRC32
// arşivin TAMAMINDAN değil bu girdiden hesaplanmıştır (bkz. RomImportService.ComputeCrc32) — Crc32
// null olabilir (hesaplama henüz yapılmadı ya da başarısız oldu), bu durumda kart boş gösterir.
// FilePath de null olabilir — kullanıcı isteği: "rom unun olup olmaması fark etmemeli" — bir
// oyunu dosyası olmadan da başka bir oyuna "sürüm" olarak bağlayabilmek için (bkz.
// MainViewModel.LinkGameFileToGameAsync "dosyasız birleştirme" dalı) — bu durumda kart kırmızı
// çarpılı (sahipsiz) gösterilir. SourceGameKey — kullanıcı isteği: "merge'i kaldırınca tabloya
// geri gelmiyor" — Bağla ile hangi oyundan taşındığını (varsa gizlenen kaynağı "Ayır" ile geri
// açabilmek, bkz. MainViewModel.RemoveVersionLink) VE fileless kartın beklenen dosya adlarını
// kaynağın kendi katalog verisinden gösterebilmek için (bkz. LoadSelectedGameVersions) tutulur.
public record FilePathOverrideInfo(string? FilePath, string? MatchMethod, string? TargetVersionRawName, string? ZipEntryName = null, string? Crc32 = null, string? SourceGameKey = null);

// Kullanıcı isteği: "manuel bağlanan kayıtlar ... CRC doğrulanmış gibi davranılmasın" — bu sabit,
// FilePathOverrideInfo.MatchMethod'da kullanılan tek "manuel" değeri tek bir yerde tutar (bkz.
// ManualLinkViewModel'in yazdığı yer, MainViewModel'in Game.IsManuallyLinked için okuduğu yer) —
// iki ayrı yerde aynı string'i elle tekrarlayıp yazım hatasıyla senkronsuz kalma riskini önler.
public static class MatchMethods
{
    public const string ManualLink = "Manuel Bağlantı (Manual Link)";
}

// CustomGames tablosundan tek bir satır (bkz. UserDataService.GetAllCustomGames/AddCustomGame) —
// kullanıcı isteği: "unknown değilde manuel bağlamaya yönlendirebilirsin, 2 ayrı eşleştirme şekli
// olmasın, bağladığımızda manuel olarak kaydediyorya buda aynı yani" — kataloktaki (RetroAudit.db)
// HİÇBİR Game'e karşılık gelmeyen, kullanıcının ROM İçe Aktar'ın Eşleşmeyenler sekmesinden "+ Yeni
// Oyun" ile kendi dosyasından türettiği bağımsız bir oyun. Bu SADECE Title/Platform kimliğini
// (RetroAudit.db yeniden üretilse bile hayatta kalması gereken) taşır — dosya sahipliği yine
// AYNI FilePathOverrides mekanizmasıyla (MatchMethods.ManualLink) kaydedilir, ayrı bir sistem
// DEĞİL (bkz. MainViewModel.RegisterNewCustomGame).
public record CustomGameInfo(string GameKey, string Title, string Platform, string PlatformDisplayName);
