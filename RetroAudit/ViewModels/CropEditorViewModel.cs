using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.ViewModels;

// Görsel kırpma diyaloğunun ViewModel'i. Bu aşamada gerçek bir kırpma/kaydetme işlemi
// yapılmıyor; oran seçimi ve Save/Cancel akışı sadece UI iskeleti olarak hazır.
public partial class CropEditorViewModel : ObservableObject
{
    // Üstteki oran seçim şeridinde gösterilen seçenekler.
    public ObservableCollection<string> AspectRatios { get; } = new()
    {
        "Serbest", "Orijinal", "Kare", "4:3", "16:9", "2:3", "3:4",
    };

    [ObservableProperty]
    private string selectedAspectRatio = "Serbest";

    // Kırpılacak görselin dosya yolu (henüz gerçek bir yükleme akışı yok, placeholder alan).
    [ObservableProperty]
    private string imagePath = string.Empty;

    // Save veya Cancel'a basıldığında pencereyi kapatmak için View'a bırakılan olay.
    public event Action? RequestClose;

    // Gerçek kırpma/kaydetme mantığı henüz yok; şimdilik sadece pencereyi kapatır.
    [RelayCommand]
    private void Save() => RequestClose?.Invoke();

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
