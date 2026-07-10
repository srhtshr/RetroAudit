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
    private string? _crc32;

    public string Title => Game.Title;
    public string Platform => Game.PlatformDisplayName;
    public string Crc32 => _crc32 ??= LoadPrimaryCrc32(Game);
    public string MissingTypeLabel => MissingType switch
    {
        "Box" => "Kapak",
        "Logo" => "Clear Logo",
        "SS" => "Ekran Görüntüsü",
        "Video" => "Video",
        "Wiki" => "Wiki",
        _ => MissingType,
    };

    private static string LoadPrimaryCrc32(Game game)
    {
        try
        {
            return CatalogDatabaseService.GetVersions(game.GameId, game.GameKey)
                .SelectMany(v => v.Hashes)
                .Select(h => h.Crc32)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
            ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
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
    private readonly Func<IReadOnlyList<Platform>> _getOrderedPlatforms;

    public bool UseModernLayout { get; }

    public ObservableCollection<PlatformAuditSummary> PlatformAuditSummaries { get; } = new();
    public ObservableCollection<MissingMediaItem> MissingItems { get; } = new();

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private PlatformAuditSummary? selectedPlatformSummary;

    [ObservableProperty]
    private bool showMissingLogo = true;

    [ObservableProperty]
    private bool showMissingBox = true;

    [ObservableProperty]
    private bool showMissingScreenshot = true;

    [ObservableProperty]
    private bool showMissingVideo = true;

    [ObservableProperty]
    private bool showMissingWiki = true;

    [ObservableProperty]
    private MissingMediaItem? selectedMissingItem;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string searchText = string.Empty;

    public bool ShowAsTable
    {
        get => ConfigService.LoadDefault().ProviderShowAsTable;
        set
        {
            var settings = ConfigService.LoadDefault();
            if (settings.ProviderShowAsTable != value)
            {
                settings.ProviderShowAsTable = value;
                ConfigService.SaveDefault(settings);
                OnPropertyChanged(nameof(ShowAsTable));
                OnPropertyChanged(nameof(ShowAsList));
            }
        }
    }

    public bool ShowAsList
    {
        get => !ShowAsTable;
        set => ShowAsTable = !value;
    }

    public event Action<string>? RequestShowMessage;
    public event Action<(Game Game, Action CompletedCallback)>? RequestEditMetadata;

    // Embedded arama penceresi isteği — MainWindow.xaml.cs, MainViewModel.RequestSearchArtwork
    // İLE AYNI handler'ı kullanıyor (bkz. orada), tek fark burada tuple'ın son elemanı olan
    // completedCallback ayrıca MissingItems'tan da çıkarma yapıyor.
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action CompletedCallback, Game Game)>? RequestSearchArtwork;

    public MediaProviderViewModel(IReadOnlyList<Game> allGames, Action<Game> onArtworkResolved, Func<IReadOnlyList<Platform>> getOrderedPlatforms, bool useModernLayout = false)
    {
        _allGames = allGames;
        _onArtworkResolved = onArtworkResolved;
        _getOrderedPlatforms = getOrderedPlatforms;
        UseModernLayout = useModernLayout;

        RebuildPlatformAuditSummaries();
        SelectedPlatformSummary = PlatformAuditSummaries.FirstOrDefault();
        RebuildMissingItems();
    }

    partial void OnSelectedPlatformSummaryChanged(PlatformAuditSummary? value)
    {
        SelectedPlatform = value?.Platform;
    }

    partial void OnSelectedPlatformChanged(Platform? value) => RebuildMissingItems();
    partial void OnShowMissingLogoChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingBoxChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingScreenshotChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingVideoChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingWikiChanged(bool value) => RebuildMissingItems();
    partial void OnSearchTextChanged(string value) => RebuildMissingItems();

    public void RefreshPlatformOrder() => RebuildPlatformAuditSummaries();
    public void RefreshAll()
    {
        RebuildPlatformAuditSummaries();
        RebuildMissingItems();
    }

    private void RebuildPlatformAuditSummaries()
    {
        var previousPlatformName = SelectedPlatformSummary?.Platform.Name;
        var visibleGames = _allGames.Where(g => !g.IsHidden && !g.IsDeleted).ToList();
        var orderedPlatforms = _getOrderedPlatforms();

        PlatformAuditSummaries.Clear();
        foreach (var platform in orderedPlatforms)
        {
            var games = platform.IsAllPlatforms
                ? visibleGames
                : visibleGames.Where(g => g.Platform == platform.Name).ToList();
            var totalGames = games.Count;
            var matchedCount = games.Count(HasMatchedMetadata);
            var missingLogoCount = games.Count(g => !g.HasClearLogo);
            var missingBoxCount = games.Count(g => !g.HasBox);
            var missingScreenshotCount = games.Count(g => !g.HasScreenshot);
            var missingVideoCount = games.Count(g => !g.HasVideoUrl);
            var missingWikipediaCount = games.Count(g => !g.HasWikipediaUrl);

            var healthPercent = totalGames == 0
                ? 0
                : (int)Math.Round(
                    (matchedCount / (double)totalGames * 50d) +
                    ((totalGames - missingLogoCount) / (double)totalGames * 15d) +
                    ((totalGames - missingBoxCount) / (double)totalGames * 20d) +
                    ((totalGames - missingScreenshotCount) / (double)totalGames * 15d),
                    MidpointRounding.AwayFromZero);

            PlatformAuditSummaries.Add(new PlatformAuditSummary
            {
                Platform = platform,
                TotalGames = totalGames,
                MatchedCount = matchedCount,
                MissingLogoCount = missingLogoCount,
                MissingBoxCount = missingBoxCount,
                MissingScreenshotCount = missingScreenshotCount,
                MissingVideoCount = missingVideoCount,
                MissingWikipediaCount = missingWikipediaCount,
                HealthPercent = Math.Clamp(healthPercent, 0, 100),
            });
        }

        SelectedPlatformSummary = PlatformAuditSummaries.FirstOrDefault(s => s.Platform.Name == previousPlatformName)
            ?? PlatformAuditSummaries.FirstOrDefault();
    }

    private static bool HasMatchedMetadata(Game game) => game.MetadataSourceId.HasValue || game.StatusOk;

    private void RebuildMissingItems()
    {
        MissingItems.Clear();

        IEnumerable<Game> games = _allGames.Where(g => !g.IsHidden && !g.IsDeleted);
        if (SelectedPlatform is { IsAllPlatforms: false })
            games = games.Where(g => g.Platform == SelectedPlatform.Name);
        if (!string.IsNullOrWhiteSpace(SearchText))
            games = games.Where(g => g.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

        foreach (var game in games)
        {
            if (!game.HasBox && ShowMissingBox)
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Box" });
            if (!game.HasClearLogo && ShowMissingLogo)
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Logo" });
            if (!game.HasScreenshot && ShowMissingScreenshot)
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "SS" });
            if (!game.HasVideoUrl && ShowMissingVideo)
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Video" });
            if (!game.HasWikipediaUrl && ShowMissingWiki)
                MissingItems.Add(new MissingMediaItem { Game = game, MissingType = "Wiki" });
        }
    }

    [RelayCommand]
    private void ToggleMissingType(string type)
    {
        switch (type)
        {
            case "Logo":
                ShowMissingLogo = !ShowMissingLogo;
                break;
            case "Box":
                ShowMissingBox = !ShowMissingBox;
                break;
            case "SS":
                ShowMissingScreenshot = !ShowMissingScreenshot;
                break;
            case "Video":
                ShowMissingVideo = !ShowMissingVideo;
                break;
            case "Wiki":
                ShowMissingWiki = !ShowMissingWiki;
                break;
        }
    }

    // "Atla": bu öğeyle şu an ilgilenmek istemiyor, hiçbir işlem yapmadan listeden çıkar (bir
    // sonraki pencere açılışında MissingItems yeniden hesaplandığı için, oyun gerçekten
    // eşleşmiş bir görsel bulmadıkça tekrar görünecektir — kalıcı bir "yok say" değildir).
    [RelayCommand]
    private void SkipItem()
    {
        if (SelectedMissingItem is { } item)
            MissingItems.Remove(item);
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedMissingItem is not { } item)
            return;

        RequestEditMetadata?.Invoke((item.Game, () =>
        {
            RefreshAll();
            SelectedMissingItem = MissingItems.FirstOrDefault(x => x.Game == item.Game && x.MissingType == item.MissingType)
                ?? MissingItems.FirstOrDefault(x => x.Game == item.Game);
        }));
    }

    // "Otomatik İndir": FetchArtwork'ün MainViewModel'deki tek-tür karşılığıyla aynı mantık
    // (LaunchBox kaynağından indir) — burada Media Provider kendi başına, MainViewModel'e
    // bağımlı olmadan çalışabilsin diye ArtworkService/CatalogDatabaseService'i doğrudan çağırıyor.
    [RelayCommand]
    private async Task FetchSelectedAsync()
    {
        if (SelectedMissingItem is not { } item)
            return;

        if (item.MissingType is "Video" or "Wiki")
        {
            RequestShowMessage?.Invoke("Video ve Wiki eksikleri için otomatik indirme desteklenmiyor.");
            return;
        }

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
                RebuildPlatformAuditSummaries();
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
            "Video" => "video bağlantısı",
            "Wiki" => "Wikipedia bağlantısı",
            _ => item.MissingType,
        };

        if (item.MissingType is "Video" or "Wiki")
        {
            RequestShowMessage?.Invoke("Video ve Wiki eksikleri otomatik görsel arama ile tamamlanmıyor.");
            return;
        }

        var query = $"{item.Game.Title} {item.Game.PlatformDisplayName} {mediaTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&tbm=isch";
        var targetFolder = Path.Combine(AppPaths.Images, item.Game.PlatformDisplayName, item.MissingType);
        var baseFileName = GetMediaBaseFileName(item.Game);

        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, item.Game.Title, mediaTypeLabel, () =>
        {
            MissingItems.Remove(item);
            _onArtworkResolved(item.Game);
            RebuildPlatformAuditSummaries();
        }, item.Game));
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
