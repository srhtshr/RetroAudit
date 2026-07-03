using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    }

    [RelayCommand]
    private void Save()
    {
        var overrideValue = new MetadataOverride(Title, Genre, Description, Notes, Publisher, Developer, null);
        UserDataService.SaveMetadataOverride(_game.GameKey, overrideValue);

        _game.Title = Title;
        _game.Genres = Genre;
        _game.Description = Description;
        _game.Notes = Notes;
        _game.Publisher = Publisher;
        _game.Developer = Developer;

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
