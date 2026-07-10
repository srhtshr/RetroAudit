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
public record GameStateInfo(bool IsHidden, bool IsDeleted, bool IsPermanentlyDeleted);

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
