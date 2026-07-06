namespace RetroAudit.Models;

// Sağ paneldeki başlığa tıklanınca açılan "ALTERNATE NAMES" menüsü için — LaunchBox'ın kendi web
// sitesindeki gösterimle birebir aynı: isim + hangi bölgeye ait olduğu (bkz. CatalogDatabaseService.
// GetAlternateNames, RetroAudit.Catalog AlternateNames tablosu).
public record GameAlternateName(string Name, string Region);
