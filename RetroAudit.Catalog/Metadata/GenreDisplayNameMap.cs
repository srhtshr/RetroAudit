namespace RetroAudit.Catalog.Metadata;

// LaunchBox'ın bazı tür adları tabloda/rozetlerde göstermek için gereksiz uzun (ör. "Construction
// and Management Simulation") — kullanıcı isteği: "şu çok yer kaplıyor bunu Const. olarak değiştir
// databasede de tablodanda". MasterMetadataReader'ın ayrıştırdığı HER genre token'ı buradan
// geçer, bu yüzden kısaltma doğrudan RetroAudit.db'ye (Genres tablosu) yazılır — WPF tarafında
// AYRICA bir eşleme gerekmez, PlatformDisplayNameMap'in aksine bu harita build-time.
public static class GenreDisplayNameMap
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Construction and Management Simulation"] = "Const.",
    };

    public static string Resolve(string rawGenreName) =>
        Names.TryGetValue(rawGenreName, out var displayName) ? displayName : rawGenreName;
}
