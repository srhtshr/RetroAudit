using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Ortadaki "Eksik Öğeler" listesindeki tek bir satırı temsil eder (ör. kutusu eksik bir oyun).
public class MissingMediaItem
{
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string MissingType { get; set; } = string.Empty; // "Box" / "Background" / "Screenshot"
}

// Sağdaki kart grid'inde gösterilen tek bir internet arama sonucunu temsil eder.
public class MediaSearchResult
{
    public string ThumbnailPath { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty; // ör. "1280x720" — kart üzerindeki rozet metni
    public string Source { get; set; } = string.Empty; // ör. "RetroAudit Data", "TheGamesDB" — kaynağı gösteren etiket
}

// Media Provider penceresinin ViewModel'i.
// Bu aşamada gerçek bir internet araması yapılmıyor; SearchResults mock kartlarla dolduruluyor.
// Kullanıcı bir kartı ortadaki eksik öğe listesine sürükleyip bırakarak (veya seçip "Uygula" diyerek)
// eşleştirmeyi simüle edebilir.
public partial class MediaProviderViewModel : ObservableObject
{
    public ObservableCollection<Platform> Platforms { get; }
    public ObservableCollection<string> MediaTypeFilters { get; } = new() { "All", "Box", "Background", "Screenshot" };
    public ObservableCollection<MissingMediaItem> MissingItems { get; } = new();
    public ObservableCollection<MediaSearchResult> SearchResults { get; } = new();

    // Sol paneldeki platform filtresi.
    [ObservableProperty]
    private Platform? selectedPlatform;

    // Sol paneldeki "eksik medya türü" filtresi (Box/Background/Screenshot/All).
    [ObservableProperty]
    private string selectedMediaTypeFilter = "All";

    // Ortadaki listede seçili eksik öğe; "Uygula"/"Atla" komutları bunu hedef alır.
    [ObservableProperty]
    private MissingMediaItem? selectedMissingItem;

    // Sağdaki kart grid'inde seçili arama sonucu.
    [ObservableProperty]
    private MediaSearchResult? selectedSearchResult;

    // Uygulama/atlama sonrası kullanıcıya kısa bir bilgi mesajı göstermek için View'a bırakılan olay.
    public event Action<string>? RequestShowMessage;

    public MediaProviderViewModel()
    {
        Platforms = new ObservableCollection<Platform>(CatalogDatabaseService.GetPlatforms());
        selectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);

        // Placeholder eksik-medya listesi (gerçek tarama mantığı sonraki aşamada gelecek).
        MissingItems.Add(new MissingMediaItem { Title = "Kid Niki: Radical Ninja", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "Kirby's Son in Fantasia", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "King Nothing", Platform = "Nintendo", MissingType = "Box" });
        MissingItems.Add(new MissingMediaItem { Title = "Sonic Chaos", Platform = "Game Gear", MissingType = "Background" });
        MissingItems.Add(new MissingMediaItem { Title = "Missile Command", Platform = "Atari", MissingType = "Screenshot" });

        // Placeholder arama sonuçları — çözünürlük ve kaynak alanları görsel çeşitlilik için değişken üretiliyor.
        for (var i = 1; i <= 12; i++)
        {
            SearchResults.Add(new MediaSearchResult
            {
                ThumbnailPath = string.Empty,
                Resolution = i % 3 == 0 ? "1280x720" : "512x512",
                Source = i % 2 == 0 ? "RetroAudit Data" : "TheGamesDB",
            });
        }
    }

    // Mock aramayı tetikler (şu an no-op; gerçek internet araması ileride buraya bağlanacak).
    [RelayCommand]
    private void Search() { }

    // Seçili kartı, seçili eksik öğeye "Uygula" butonuyla eşler (sürükle-bırakın buton eşdeğeri).
    [RelayCommand]
    private void ApplySelectedResult()
    {
        if (SelectedMissingItem is not null && SelectedSearchResult is not null)
            ApplyDrop(SelectedMissingItem, SelectedSearchResult);
    }

    // Seçili eksik öğeyi hiçbir eşleştirme yapmadan listeden çıkarır (kullanıcı bu öğeyi atlamak istiyor).
    [RelayCommand]
    private void SkipItem()
    {
        if (SelectedMissingItem is not null)
            MissingItems.Remove(SelectedMissingItem);
    }

    // Sürükle-bırak simülasyonunun ortak mantığı: hem MediaProviderWindow'daki Drop olayından,
    // hem de "Uygula" butonundan çağrılır. Gerçek indirme/kaydetme olmadığından öğeyi doğrudan
    // "çözüldü" kabul edip listeden kaldırıyoruz.
    public void ApplyDrop(MissingMediaItem target, MediaSearchResult result)
    {
        MissingItems.Remove(target);
        RequestShowMessage?.Invoke($"\"{target.Title}\" için {result.Source} kaynaklı görsel uygulandı (simülasyon).");
    }
}
