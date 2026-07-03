namespace RetroAudit.Catalog.Dat;

// No-Intro, Redump, TOSEC ve çoğu MAME/FBNeo DAT dosyasının kullandığı ortak, parantez tabanlı
// metin formatını okur:
//   clrmamepro ( name "..." description "..." version "..." )
//   game ( name "Title (USA) (Rev A)" region "USA" [year "..."] [developer "..."] [serial "..."]
//       rom ( name "..." size 123 crc XXXXXXXX md5 ... sha1 ... )
//   )
// Değerler tırnaklı ("...") ya da çıplak (boşluksuz kelime, ör. bir rom adı ya da sayısal alan)
// olabilir; her ikisi de aynı tokenizer'dan geçer.
public class ClrMameProDatParser : IDatParser
{
    public bool CanParse(string filePath, string firstNonWhitespaceChars)
        => !firstNonWhitespaceChars.StartsWith('<');

    public IEnumerable<DatGameEntry> Parse(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tokens = Tokenize(text);
        var pos = 0;
        var fileName = Path.GetFileName(filePath);

        while (pos < tokens.Count)
        {
            var keyword = tokens[pos].Value;
            pos++;

            if (pos >= tokens.Count || tokens[pos].Type != TokenType.LParen)
                continue; // beklenmedik biçim — bu token'ı atla, dosyanın geri kalanını okumaya devam et

            var block = ParseBlock(tokens, ref pos);

            if (string.Equals(keyword, "game", StringComparison.OrdinalIgnoreCase))
                yield return BuildGameEntry(block, fileName);
        }
    }

    private static DatGameEntry BuildGameEntry(DatBlock block, string sourceFile)
    {
        var entry = new DatGameEntry
        {
            Name = block.GetScalar("name") ?? string.Empty,
            Region = block.GetScalar("region"),
            Year = block.GetScalar("year"),
            Developer = block.GetScalar("developer"),
            Serial = block.GetScalar("serial"),
            SourceFile = sourceFile,
        };

        foreach (var romBlock in block.Children.Where(c => string.Equals(c.Keyword, "rom", StringComparison.OrdinalIgnoreCase)))
        {
            entry.Roms.Add(new DatRomEntry
            {
                FileName = romBlock.GetScalar("name") ?? string.Empty,
                Size = long.TryParse(romBlock.GetScalar("size"), out var size) ? size : 0,
                Crc32 = romBlock.GetScalar("crc") ?? string.Empty,
                Md5 = romBlock.GetScalar("md5"),
                Sha1 = romBlock.GetScalar("sha1"),
            });
        }

        return entry;
    }

    // Bir "( ... )" bloğunu, açan parantezin hemen öncesinde durarak okur; kapanan parantezi
    // tüketip döner. İç içe bloklar (ör. game içindeki rom) Children listesine, düz alanlar
    // (name, region, size, crc, ...) Scalars sözlüğüne yazılır.
    private static DatBlock ParseBlock(List<Token> tokens, ref int pos)
    {
        pos++; // '(' tüket
        var block = new DatBlock();

        while (pos < tokens.Count && tokens[pos].Type != TokenType.RParen)
        {
            var key = tokens[pos].Value;
            pos++;

            if (pos < tokens.Count && tokens[pos].Type == TokenType.LParen)
            {
                var child = ParseBlock(tokens, ref pos);
                child.Keyword = key;
                block.Children.Add(child);
            }
            else if (pos < tokens.Count)
            {
                block.Scalars[key] = tokens[pos].Value;
                pos++;
            }
        }

        if (pos < tokens.Count)
            pos++; // ')' tüket

        return block;
    }

    private enum TokenType { LParen, RParen, Word }

    private readonly record struct Token(TokenType Type, string Value);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { tokens.Add(new Token(TokenType.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenType.RParen, ")")); i++; continue; }

            if (c == '"')
            {
                i++; // açılış tırnağını tüket
                var start = i;
                var sb = new System.Text.StringBuilder();
                while (i < n && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < n)
                    {
                        sb.Append(text[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                }
                i++; // kapanış tırnağını tüket (varsa)
                _ = start;
                tokens.Add(new Token(TokenType.Word, sb.ToString()));
                continue;
            }

            // çıplak (tırnaksız) kelime: boşluk veya parantezle karşılaşana kadar oku
            var wordStart = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')')
                i++;
            tokens.Add(new Token(TokenType.Word, text[wordStart..i]));
        }

        return tokens;
    }

    private class DatBlock
    {
        public string Keyword = string.Empty;
        public Dictionary<string, string> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DatBlock> Children { get; } = new();

        public string? GetScalar(string key) => Scalars.TryGetValue(key, out var value) ? value : null;
    }
}
