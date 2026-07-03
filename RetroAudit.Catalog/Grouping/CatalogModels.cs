using RetroAudit.Catalog.Dat;

namespace RetroAudit.Catalog.Grouping;

// DAT'tan gelen, henüz ayrıştırılmış tek bir sürüm kaydı (ör. "Super Mario World (Europe) (Rev A)").
// GameHashes tablosuna 1-e-çok giden Roms listesi burada tutulur (ör. başlıklı/başlıksız ikilisi).
public class CatalogGameVersion
{
    public string RawDatName { get; set; } = string.Empty;
    public string SourceDat { get; set; } = string.Empty; // "no-intro", "redump", "tosec", ...
    public string[] Regions { get; set; } = Array.Empty<string>();
    public string? VersionLabel { get; set; }
    public string[] Flags { get; set; } = Array.Empty<string>();
    public bool IsPreferred { get; set; }
    public List<DatRomEntry> Roms { get; set; } = new();

    // Beta/Proto/Demo/Test/Pirate/Hack/Aftermarket/Unlicensed etiketlerinden en az biri varsa true.
    public bool IsAltVersion => Flags.Length > 0;
}

// Ana listede gösterilecek "parent" oyun satırı — (Platform, temiz başlık) çiftine göre gruplanır.
// Tüm ham DAT kayıtları Versions altında saklanır; sadece bu satır Games tablosuna yazılır.
public class CatalogGame
{
    public string PlatformName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CompareTitle { get; set; } = string.Empty;
    public List<CatalogGameVersion> Versions { get; } = new();

    public CatalogGameVersion? Preferred => Versions.FirstOrDefault(v => v.IsPreferred);

    // LaunchBox.Metadata.db'den doldurulan alanlar (Builder'ın ikinci aşaması).
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Overview { get; set; }
    public int? MaxPlayers { get; set; }
    public List<string> Genres { get; } = new();
    public bool MatchedMetadata { get; set; }
}
