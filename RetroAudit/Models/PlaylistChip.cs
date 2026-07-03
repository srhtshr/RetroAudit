using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroAudit.Models;

public enum PlaylistChipKind
{
    Playlist,
    Hidden,
    RecycleBin,
}

// Ana tablonun üstündeki hashtag/chip şeridindeki tek bir satır. Favorites dahil gerçek
// playlist'ler UserDataService.Playlists tablosundan gelir (PlaylistId dolu); Hidden ve Recycle
// Bin gerçek bir playlist değildir, GameState'ten hesaplanan sentetik chip'lerdir (PlaylistId null)
// — kullanıcı isteğiyle aynı görsel/tıklama mantığını paylaşırlar ama ayrı bir tabloya yazılmazlar.
public partial class PlaylistChip : ObservableObject
{
    public int? PlaylistId { get; init; }
    public PlaylistChipKind Kind { get; init; } = PlaylistChipKind.Playlist;
    public bool IsBuiltIn { get; init; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string color = "#3A86FF";

    [ObservableProperty]
    private bool isPopupOpen;

    // MainViewModel.OnSelectedChipChanged tarafından güncellenir — DataGridColumn'daki gibi
    // Value="{Binding}" bir DataTrigger'da geçerli olmadığı için ("seçili miyim?" karşılaştırması
    // XAML'de yapılamaz), seçim durumu doğrudan bu bool'a yansıtılıyor.
    [ObservableProperty]
    private bool isSelected;
}
