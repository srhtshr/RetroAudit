using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Ortadaki "Eksik Öğeler" listesindeki tek bir satır: gerçek bir Game + eksik olan TEK görsel
// türü (Box/Logo/SS). Game referansı MainViewModel.AllGames'ten AYNEN paylaşılıyor (kopya değil)
// — böylece burada bir görsel indirilip Game.BoxPath vb. güncellenince, arkadaki MainWindow'un
// grid/detay paneli de (aynı nesne olduğu için) otomatik güncel kalır.
public class MissingMediaItem
{
    public required Game Game { get; init; }
    public required string MissingType { get; init; } // "Box" / "Logo" / "SS"

    public string Title => Game.Title;
    public string Platform => Game.PlatformDisplayName;
    public string MissingTypeLabel => MissingType switch
    {
        "Box" => "Kapak",
        "Logo" => "Clear Logo",
        "SS" => "Ekran Görüntüsü",
        _ => MissingType,
    };
}

// Media Provider penceresinin ViewModel'i. Kullanıcı isteği: "media provider tool'u mevcut
// yapıya entegre et, eksik olanları görsün, ordan da indirme yapabilelim" — önceki tamamen mock
// (sabit sahte liste + sahte arama sonucu kartları + "simülasyon" uygulama) hâli kaldırıldı.
// Artık MissingItems gerçek kütüphaneden (MainViewModel.AllGames) hesaplanıyor, "Otomatik İndir"
// gerçekten LaunchBox kaynağından indiriyor, "Ara" gerçek bir embedded arama penceresi açıyor
// (bkz. MediaSearchWindow, MainViewModel'deki SearchBoxArt/SearchClearLogoArt/SearchScreenshotArt
// ile AYNI mekanizma).
public partial class MediaProviderViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _allGames;

    // Bir görsel indirilip/aranıp bulununca MainWindow'daki asıl MainViewModel'e "bu oyunu
    // tazele" demek için (bkz. MainWindow.xaml.cs: vm.NotifyArtworkDownloaded + vm.ApplyFilter).
    private readonly Action<Game> _onArtworkResolved;

    public ObservableCollection<Platform> Platforms { get; }
    public ObservableCollection<string> MediaTypeFilters { get; } = new() { "Hepsi", "Kapak", "Clear Logo", "Ekran Görüntüsü" };
    public ObservableCollection<MissingMediaItem> MissingItems { get; } = new();

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private string selectedMediaTypeFilter = "Hepsi";

    [ObservableProperty]
    private MissingMediaItem? selectedMissingItem;

    [ObservableProperty]
    private bool isBusy;

    public event Action<string>? RequestShowMessage;

    // Embedded arama penceresi isteği — MainWindow.xaml.cs, MainViewModel.RequestSearchArtwork
    // İLE AYNI handler'ı kullanıyor (bkz. orada), tek fark burada tuple'ın son elemanı olan
    // completedCallback ayrıca MissingItems'tan da çıkarma yapıyor.
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action CompletedCallback)>? RequestSearchArtwork;

    public MediaProviderViewModel(IReadOnlyList<Game> allGames, Action<Game> onArtworkResolved)
    {
        _allGames = allGames;
        _onArtworkResolved = onArtworkResolved;

        Platforms = new ObservableCollection<Platform>(CatalogDatabaseService.GetPlatforms());
        selectedPlatform = Platforms.FirstOrDefault(p => p.IsAllPlatforms);

        RebuildMissingItems();
    }

    partial void OnSelectedPlatformChanged(Platform? value) => RebuildMissingItems();
    partial void OnSelectedMediaTypeFilterChanged(string value) => RebuildMissingItems();

    private void RebuildMissingItems()
    {
        MissingItems.Clear();

        IEnumerable<Game> games = _allGames.Where(g => !g.IsHidden && !g.IsDeleted);
        if (SelectedPlatform is { IsAllPlatforms: false })
            games = games.Where(g => g.Platform == SelectedPlatform.Name);

        foreach (var game in games)
        {
            if (!game.HasBox && MatchesTypeFilter("Kapak"))
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Box" });
            if (!game.HasClearLogo && MatchesTypeFilter("Clear Logo"))
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Logo" });
            if (!game.HasScreenshot && MatchesTypeFilter("Ekran Görüntüsü"))
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "SS" });
        }
    }

    private bool MatchesTypeFilter(string label) => SelectedMediaTypeFilter is "Hepsi" || SelectedMediaTypeFilter == label;

    // "Atla": bu öğeyle şu an ilgilenmek istemiyor, hiçbir işlem yapmadan listeden çıkar (bir
    // sonraki pencere açılışında MissingItems yeniden hesaplandığı için, oyun gerçekten
    // eşleşmiş bir görsel bulmadıkça tekrar görünecektir — kalıcı bir "yok say" değildir).
    [RelayCommand]
    private void SkipItem()
    {
        if (SelectedMissingItem is { } item)
            MissingItems.Remove(item);
    }

    // "Otomatik İndir": FetchArtwork'ün MainViewModel'deki tek-tür karşılığıyla aynı mantık
    // (LaunchBox kaynağından indir) — burada Media Provider kendi başına, MainViewModel'e
    // bağımlı olmadan çalışabilsin diye ArtworkService/CatalogDatabaseService'i doğrudan çağırıyor.
    [RelayCommand]
    private async Task FetchSelectedAsync()
    {
        if (SelectedMissingItem is not { } item)
            return;

        if (!item.Game.HasArtworkSource)
        {
            RequestShowMessage?.Invoke("Bu oyun için eşleşmiş bir metadata kaydı yok, görsel aranamadı.");
            return;
        }

        IsBusy = true;
        try
        {
            var asset = CatalogDatabaseService.GetArtworkAssets(item.Game.GameId)
                .FirstOrDefault(kv => kv.Key == item.MissingType);
            if (asset.Key is null)
            {
                RequestShowMessage?.Invoke("LaunchBox'ta bu görsel için kaynak bulunamadı — \"Ara\" ile deneyebilirsiniz.");
                return;
            }

            var preserveTransparency = item.MissingType == "Logo";
            var baseFileName = GetMediaBaseFileName(item.Game);
            var destination = ArtworkService.BuildLocalPath(AppPaths.Images, item.Game.PlatformDisplayName, item.MissingType, baseFileName, preserveTransparency);
            var maxDimension = ConfigService.LoadDefault().ArtworkMaxDimension switch
            {
                Models.ArtworkMaxDimension.Px800 => 800,
                Models.ArtworkMaxDimension.Original => int.MaxValue,
                _ => 600,
            };

            var success = await ArtworkService.DownloadAsync(asset.Value, destination, preserveTransparency, maxDimension);
            if (success)
            {
                MissingItems.Remove(item);
                _onArtworkResolved(item.Game);
                RequestShowMessage?.Invoke($"\"{item.Title}\" için {item.MissingTypeLabel} indirildi.");
            }
            else
            {
                RequestShowMessage?.Invoke("Görsel indirilemedi — \"Ara\" ile deneyebilirsiniz.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // "Ara": otomatik indirme kaynak bulamazsa/başarısız olursa kullanıcının kendi bulup
    // indirebilmesi için embedded arama penceresi (bkz. MediaSearchWindow, MainViewModel'deki
    // AYNI mekanizma).
    [RelayCommand]
    private void SearchSelected()
    {
        if (SelectedMissingItem is not { } item)
            return;

        var mediaTypeLabel = item.MissingType switch
        {
            "Box" => "kapak resmi (box art)",
            "Logo" => "clear logo (şeffaf png)",
            "SS" => "oynanış ekran görüntüsü",
            _ => item.MissingType,
        };

        var query = $"{item.Game.Title} {item.Game.PlatformDisplayName} {mediaTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&tbm=isch";
        var targetFolder = Path.Combine(AppPaths.Images, item.Game.PlatformDisplayName, item.MissingType);
        var baseFileName = GetMediaBaseFileName(item.Game);

        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, item.Game.Title, mediaTypeLabel, () =>
        {
            MissingItems.Remove(item);
            _onArtworkResolved(item.Game);
        }));
    }

    // MainViewModel.GetMediaBaseFileName ile AYNI kural (ROM dosya adı, yoksa başlıktan
    // türetilmiş güvenli bir dosya adı) — burada kopyalanmış, çünkü MainViewModel'in kendisi
    // private ve bu ViewModel MainViewModel'e bağımlı olmadan çalışabilmeli.
    private static string GetMediaBaseFileName(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.File))
            return Path.GetFileNameWithoutExtension(game.File);

        var invalid = Path.GetInvalidFileNameChars();
        return new string(game.Title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
