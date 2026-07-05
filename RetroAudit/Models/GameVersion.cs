namespace RetroAudit.Models;

// RetroAudit.db'deki tek bir GameHashes satırı — bir sürümün bir dosyasının (ör. headered/
// headerless ikilisi) hash bilgisi.
public class GameHash
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Crc32 { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string Sha1 { get; set; } = string.Empty;
}

// Sağ paneldeki "Versions" listesinde gösterilen tek bir satır — RetroAudit.db'deki bir
// GameVersions kaydına (ör. "Super Mario Bros. 3 (USA)") karşılık gelir. Bir Game birden fazla
// GameVersion'a sahip olabilir (Region bazlı); her GameVersion birden fazla Hash'e sahip olabilir
// (headered/headerless).
public class GameVersion
{
    public string Region { get; set; } = string.Empty;
    public string AllRegionsRaw { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public bool IsPreferred { get; set; }
    public string SourceDat { get; set; } = string.Empty;
    public string RawDatName { get; set; } = string.Empty;
    public List<GameHash> Hashes { get; set; } = new();

    // Bu sürümün dosyalarından biri (Hashes'teki herhangi bir FileName) diskte var mı — sağ
    // paneldeki Sürümler listesinde Play/çarpı ikonunu belirler (bkz. MainViewModel.IsVersionOwned).
    // CatalogDatabaseService.GetVersions'ın döndürdüğü ham veriden bağımsız, MainViewModel tarafından
    // yükleme sırasında doldurulur.
    public bool IsOwned { get; set; }
}
