namespace RetroAudit.Catalog.Dat;

// Bazı MAME varyantları (ör. "MAME 2003 XML.xml") Logiqx tarzı XML DAT formatında dağıtılıyor.
// Bu format bu aşamada desteklenmiyor — mimari (IDatParser + DatParserFactory) hazır olduğu için
// gerçek implementasyon eklemek ileride tek dosyalık bir iş olacak. Şimdilik DatSourceScanner bu
// dosyaları "desteklenmiyor" olarak raporlayıp atlıyor.
public class LogiqxXmlDatParser : IDatParser
{
    public bool CanParse(string filePath, string firstNonWhitespaceChars)
        => firstNonWhitespaceChars.StartsWith('<');

    public IEnumerable<DatGameEntry> Parse(string filePath)
        => throw new NotSupportedException(
            $"Logiqx XML DAT formatı henüz desteklenmiyor: {Path.GetFileName(filePath)}. " +
            "ClrMamePro formatındaki No-Intro/Redump/TOSEC/MAME dosyalarını kullanın.");
}
