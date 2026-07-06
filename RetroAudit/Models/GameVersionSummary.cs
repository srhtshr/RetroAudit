namespace RetroAudit.Models;

// Bir oyunun TEK bir sürümünün özet bilgisi — sağ paneldeki tam GameVersion listesinden farklı
// olarak (talep üzerine, seçili oyun için ayrıca sorgulanır) bu, TÜM oyunlar için DataGrid
// yüklenirken bir kerede toplanır (bkz. CatalogDatabaseService.GetGames). Sadece toolbar'daki
// USA/EU/Japan bayrak filtresinin ihtiyaç duyduğu üç alanı taşır — hash/CRC gibi detaylar burada
// yok, onlar için hâlâ GameVersion kullanılıyor.
public record GameVersionSummary(string Region, string SourceDat, string FileName);
