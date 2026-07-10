using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Edit Metadata penceresinin ViewModel'i. Kaydetme, RetroAudit.db'ye değil (Builder'ın disposable
// çıktısı — bkz. UserDataService üstündeki yorum) RetroAuditUserData.db'deki MetadataOverrides
// tablosuna yazar; canlı Game nesnesi de doğrudan güncellenir ki DataGrid/detay paneli pencereyi
// kapatır kapatmaz (MainViewModel.RefreshGamesView ile) güncel değeri göstersin.
public partial class EditMetadataViewModel : ObservableObject
{
    private readonly Game _game;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string genre;

    [ObservableProperty]
    private string description;

    [ObservableProperty]
    private string notes;

    [ObservableProperty]
    private string publisher;

    [ObservableProperty]
    private string developer;

    [ObservableProperty]
    private string videoUrl;

    [ObservableProperty]
    private string releaseYearText;

    [ObservableProperty]
    private string region;

    public ObservableCollection<string> RegionOptions { get; } = new();

    // true: kaydedildi, false: iptal edildi — View bunu DialogResult'a çevirir.
    public event Action<bool>? RequestClose;

    public EditMetadataViewModel(Game game)
    {
        _game = game;
        title = game.Title;
        genre = game.Genres;
        description = game.Description;
        notes = game.Notes;
        publisher = game.Publisher;
        developer = game.Developer;
        videoUrl = game.VideoUrl;
        releaseYearText = game.HasReleaseYear ? game.ReleaseYear.ToString() : string.Empty;
        region = game.Region;

        foreach (var option in BuildRegionOptions(game))
            RegionOptions.Add(option);
    }

    [RelayCommand]
    private void Save()
    {
        int? releaseYear = int.TryParse(ReleaseYearText, out var parsedYear) ? parsedYear : null;
        var overrideValue = new MetadataOverride(Title, Genre, Description, Notes, Publisher, Developer, VideoUrl, releaseYear, Region, null);
        UserDataService.SaveMetadataOverride(_game.GameKey, overrideValue);

        _game.Title = Title;
        _game.Genres = Genre;
        _game.Description = Description;
        _game.Notes = Notes;
        _game.Publisher = Publisher;
        _game.Developer = Developer;
        _game.VideoUrl = VideoUrl;
        _game.ReleaseYear = releaseYear ?? 0;
        _game.Region = Region;

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    private static IEnumerable<string> BuildRegionOptions(Game game)
    {
        var options = new List<string>();
        var common = new[] { "USA", "Europe", "Japan", "World", "Unknown" };

        foreach (var region in game.AllVersions.Select(v => v.Region)
                     .Concat(common)
                     .Append(game.Region)
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            options.Add(region);
        }

        return options;
    }
}
