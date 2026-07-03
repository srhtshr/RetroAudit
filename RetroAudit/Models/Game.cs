namespace RetroAudit.Models;

public class Game
{
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Version { get; set; } = "Released";
    public string Genres { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;

    public bool StatusOk { get; set; }
    public bool HasBox { get; set; }
    public bool HasBackground { get; set; }
    public bool HasScreenshot { get; set; }

    public string CoverImagePath { get; set; } = string.Empty;
    public string ScreenshotImagePath { get; set; } = string.Empty;

    public int ReleaseYear { get; set; }
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Single Player";
    public int MaxPlayers { get; set; } = 1;
    public string Description { get; set; } = string.Empty;
}
