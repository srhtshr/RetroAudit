using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly List<Game> _allGames;

    public ObservableCollection<Platform> Platforms { get; }
    public ObservableCollection<Game> Games { get; } = new();
    public ObservableCollection<string> ToolMenuItems { get; } = new() { "Tools", "Media Provider...", "Crop Editor..." };

    public event Action? RequestOpenMediaProvider;
    public event Action? RequestOpenCropEditor;

    [ObservableProperty]
    private string selectedToolAction = "Tools";

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private Game? selectedGame;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showReleased = true;

    [ObservableProperty]
    private bool showJunk;

    [ObservableProperty]
    private double rowHeight = 30;

    public int TotalCount => _allGames.Count;
    public int VisibleCount => Games.Count;

    public MainViewModel()
    {
        _allGames = MockDataService.GetGames();
        Platforms = new ObservableCollection<Platform>(MockDataService.GetPlatforms());
        selectedPlatform = Platforms.FirstOrDefault(p => p.Name == "Nintendo") ?? Platforms.First();

        ApplyFilter();
        selectedGame = Games.FirstOrDefault(g => g.Title == "A Week of Garfield") ?? Games.FirstOrDefault();
    }

    partial void OnSelectedToolActionChanged(string value)
    {
        if (value == "Media Provider...")
            OpenMediaProvider();
        else if (value == "Crop Editor...")
            OpenCropEditor();

        if (value != "Tools")
            SelectedToolAction = "Tools";
    }

    partial void OnSelectedPlatformChanged(Platform? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowReleasedChanged(bool value) => ApplyFilter();

    partial void OnShowJunkChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Game> query = _allGames;

        if (SelectedPlatform is { IsAllPlatforms: false })
            query = query.Where(g => g.Platform == SelectedPlatform.Name);

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        query = query.Where(g => (ShowReleased && g.Version == "Released") || (ShowJunk && g.Version == "Junk"));

        Games.Clear();
        foreach (var game in query)
            Games.Add(game);

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    [RelayCommand]
    private void Import() { }

    [RelayCommand]
    private void Rescan() { }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);
        ShowReleased = true;
        ShowJunk = false;
    }

    [RelayCommand]
    private void RefreshMedia() { }

    [RelayCommand]
    private void MetadataRefresh() { }

    [RelayCommand]
    private void LbTasi() { }

    [RelayCommand]
    private void ApplyResolver() { }

    [RelayCommand]
    private void Launch() { }

    [RelayCommand]
    private void OpenMediaProvider() => RequestOpenMediaProvider?.Invoke();

    [RelayCommand]
    private void OpenCropEditor() => RequestOpenCropEditor?.Invoke();
}
