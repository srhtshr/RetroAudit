namespace RetroAudit.Catalog.Dat;

// Tek bir ROM dosyasının DAT kaydındaki hash bilgisi (bir "game" bloğu birden fazla rom içerebilir,
// ör. headered/unheadered çift ya da çok diskli bir oyunun disk 1/disk 2 dosyaları).
public class DatRomEntry
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Crc32 { get; set; } = string.Empty;
    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }
}

// DAT dosyasındaki ham "game" bloğu — henüz isim ayrıştırması (region/version/flag) yapılmamış,
// DatNameParser bu aşamadan sonra devreye girer.
public class DatGameEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Year { get; set; }
    public string? Developer { get; set; }
    public string? Serial { get; set; }
    public List<DatRomEntry> Roms { get; set; } = new();

    // Hangi DAT dosyasından geldiği (ör. "Nintendo - Nintendo Entertainment System.dat") — tanılama
    // ve SourceDat sütunu için saklanıyor.
    public string SourceFile { get; set; } = string.Empty;

    // Hangi kaynak klasöründen geldiği: "no-intro", "redump", "tosec", "mame", "fbneo-member" vb.
    // DatSourceScanner tarafından, dosyanın bulunduğu klasör adından çıkarılır.
    public string SourceCategory { get; set; } = string.Empty;

    // DAT dosya adından türetilen platform adı (ör. "Nintendo - Nintendo Entertainment System").
    public string PlatformName { get; set; } = string.Empty;
}
