namespace RetroAudit.Models;

public class Platform
{
    public string Name { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
    public int GameCount { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsAllPlatforms { get; set; }
}
