namespace RetroAudit.Catalog.Metadata;

// Fuzzy eşleştirme için basit, bağımlılıksız bir Levenshtein-tabanlı benzerlik oranı.
// 1.0 = birebir aynı metin, 0.0 = hiçbir ortak karakter dizisi yok.
public static class StringSimilarity
{
    public static double Ratio(string a, string b)
    {
        if (a == b)
            return 1.0;
        if (a.Length == 0 || b.Length == 0)
            return 0.0;

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
