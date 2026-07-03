namespace RetroAudit.Catalog.Dat;

// Farklı DAT formatlarını (ClrMamePro metin formatı, Logiqx XML formatı, ...) tek bir arayüz
// arkasında soyutlar. DatParserFactory hangi implementasyonun kullanılacağına dosya içeriğine
// bakarak karar verir; yeni bir kaynak formatı eklemek yeni bir IDatParser yazmaktan ibarettir.
public interface IDatParser
{
    // Verilen dosyanın bu parser tarafından okunup okunamayacağını (format olarak) bildirir.
    bool CanParse(string filePath, string firstNonWhitespaceChars);

    IEnumerable<DatGameEntry> Parse(string filePath);
}
