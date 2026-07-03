using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.ViewModels;

public partial class CropEditorViewModel : ObservableObject
{
    public ObservableCollection<string> AspectRatios { get; } = new()
    {
        "Serbest", "Orijinal", "Kare", "4:3", "16:9", "2:3", "3:4",
    };

    [ObservableProperty]
    private string selectedAspectRatio = "Serbest";

    [ObservableProperty]
    private string imagePath = string.Empty;

    public event Action? RequestClose;

    [RelayCommand]
    private void Save() => RequestClose?.Invoke();

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
