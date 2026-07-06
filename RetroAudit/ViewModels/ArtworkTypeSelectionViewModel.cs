using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.ViewModels;

// "Görsel Getir" öncesi hangi türlerin indirileceğini soran küçük onay penceresinin ViewModel'i.
// Fanart kullanıcı isteğiyle tamamen kaldırıldı (Box/Clear Logo/Gameplay kaldı). Hem tekli
// (FetchArtwork) hem toplu (BulkFetchArtwork) indirme bu pencereyi bir kez gösterip seçilen
// türlerle sınırlıyor — bkz. MainViewModel.RequestArtworkTypeSelection.
public partial class ArtworkTypeSelectionViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySelected))]
    private bool includeBox = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySelected))]
    private bool includeClearLogo = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySelected))]
    private bool includeGameplay = true;

    // İndir butonunun etkin olması için en az bir tür seçili olmalı.
    public bool HasAnySelected => IncludeBox || IncludeClearLogo || IncludeGameplay;

    // true: indir, false: iptal — View bunu DialogResult'a çevirir.
    public event Action<bool>? RequestClose;

    // ArtworkAssets/ArtworkService'in kullandığı kısa tür kodlarına (Box/BG/Logo/SS) eşler.
    public HashSet<string> GetSelectedTypes()
    {
        var types = new HashSet<string>();
        if (IncludeBox) types.Add("Box");
        if (IncludeClearLogo) types.Add("Logo");
        if (IncludeGameplay) types.Add("SS");
        return types;
    }

    [RelayCommand]
    private void Download() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
