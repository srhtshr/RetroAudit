namespace RetroAudit.Catalog;

public class BuildOptions
{
    public required string DatRoot { get; init; }

    // "metadat" altında taranacak alt klasör adları — CLI varsayılanı sadece {"no-intro"}.
    public required IReadOnlyList<string> SourceCategories { get; init; }

    // Verilirse (ör. "Nintendo - Nintendo Entertainment System"), sadece o platformun DAT'ı işlenir.
    public string? PlatformFilter { get; init; }

    public required string MasterMetadataDbPath { get; init; }

    public required string OutputDbPath { get; init; }
}
