namespace RetroAudit.Catalog.Dat;

// Bir DAT dosyasının içeriğine (ilk birkaç anlamlı karakterine) bakarak hangi IDatParser
// implementasyonunun kullanılacağına karar verir. Yeni bir format eklemek, burada yeni bir
// IDatParser kaydetmekten ibarettir.
public static class DatParserFactory
{
    private static readonly IDatParser[] Parsers =
    {
        new LogiqxXmlDatParser(),
        new ClrMameProDatParser(), // varsayılan/yakalayıcı: XML değilse ClrMamePro formatı say
    };

    public static IDatParser? Resolve(string filePath)
    {
        string probe;
        try
        {
            using var reader = new StreamReader(filePath);
            var buffer = new char[64];
            var read = reader.Read(buffer, 0, buffer.Length);
            probe = new string(buffer, 0, read).TrimStart();
        }
        catch (IOException)
        {
            return null;
        }

        return Parsers.FirstOrDefault(p => p.CanParse(filePath, probe));
    }
}
