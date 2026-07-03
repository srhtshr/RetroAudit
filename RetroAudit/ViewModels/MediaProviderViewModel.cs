using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

public class MissingMediaItem
{
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string MissingType { get; set; } = string.Empty;
}

public class MediaSearchResult
{
    public string ThumbnailPath { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public partial class MediaProviderViewModel : ObservableObject
{
    public ObservableCollection<Platform> Platforms { get; }
    public ObservableCollection<string> MediaTypeFilters { get; } = new() { "All", "Box", "Background", "Screenshot" };
    public ObservableCollection<MissingMediaItem> MissingItems { get; } = new();
    public ObservableCollection<MediaSearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private string selectedMediaTypeFilter = "All";

    [ObservableProperty]
    private MissingMediaItem? selectedMissingItem;

    [ObservableProperty]
    private MediaSearchResult? selectedSearchResult;

    public MediaProviderViewModel()
    {
        Platforms = new ObservableCollection<Platform>(MockDataService.GetPlatforms());
        selectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);

        MissingItems.Add(new MissingMediaItem { Title = "Kid Niki: Radical Ninja", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "Kirby's Son in Fantasia", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "King Nothing", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "Sonic Chaos", Platform = "Game Gear", MissingType = "Background" });
        MissingItems.Add(new MissingMediaItem { Title = "Missile Command", Platform = "Atari", MissingType = "Screenshot" });

        for (var i = 1; i <= 12; i++)
        {
            SearchResults.Add(new MediaSearchResult
            {
                ThumbnailPath = string.Empty,
                Resolution = i % 3 == 0 ? "1280x720" : "512x512",
                Source = i % 2 == 0 ? "LaunchBox" : "TheGamesDB",
            });
        }
    }

    [RelayCommand]
    private void Search() { }

    [RelayCommand]
    private void ApplySelectedResult() { }

    [RelayCommand]
    private void SkipItem() { }
}
