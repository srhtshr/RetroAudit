using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Catalog.Grouping;
using RetroAudit.Catalog.Metadata;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

public class MissingMetadataItem
{
    public required Game Game { get; init; }
    public required string MissingType { get; init; }
    private string? _crc32;

    public string Title => Game.Title;
    public string Platform => Game.PlatformDisplayName;
    public string Crc32 => _crc32 ??= LoadPrimaryCrc32(Game);
    public string MissingTypeLabel => MissingType switch
    {
        "Match" => "Metadata Eşleşmesi",
        "Genres" => "Tür",
        "Publisher" => "Yayıncı",
        "Description" => "Açıklama",
        "Year" => "Yıl",
        "Version" => "Sürüm",
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

// Sol paneldeki platform kartı listesi filtre popup'ının her satırı. Kullanıcı isteği:
// "göstermeyi istemediklerimi göstermemem için seçmeli filtre" — işaret kaldırılan platform
// AppSettings.MetadataProviderHiddenPlatforms'a yazılıp kart listesinden çıkarılır.
public partial class PlatformVisibilityOption : ObservableObject
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    [ObservableProperty]
    private bool isVisible = true;

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke(this);

    public event Action<PlatformVisibilityOption>? VisibilityChanged;
}

public partial class MetadataProviderViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _allGames;
    private readonly Action<Game> _onMetadataUpdated;
    private readonly Func<IReadOnlyList<Platform>> _getOrderedPlatforms;
    private HashSet<string> _hiddenPlatforms = new();
    private bool _suppressPlatformVisibilityChanged;

    public bool UseModernLayout { get; }

    public ObservableCollection<PlatformMetadataAuditSummary> PlatformAuditSummaries { get; } = new();
    public ObservableCollection<PlatformVisibilityOption> PlatformVisibilityOptions { get; } = new();
    public ObservableCollection<MissingMetadataItem> MissingItems { get; } = new();

    [ObservableProperty]
    private bool isPlatformFilterOpen;

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private PlatformMetadataAuditSummary? selectedPlatformSummary;

    [ObservableProperty]
    private bool showMissingGenres = true;

    [ObservableProperty]
    private bool showMissingPublisher = true;

    [ObservableProperty]
    private bool showMissingDescription = true;

    [ObservableProperty]
    private bool showMissingYear = true;

    [ObservableProperty]
    private bool showMissingVersion = true;

    // Kullanıcı isteği: "bağlanmayanları tespit etmek için filtre ekleyelim... tekrar aynı
    // şeyleri yollamayım geminiye" — BİRLEŞTİR/SÜRÜM ile elle bağlanmış (bkz. Game.
    // HasManualMetadataSourceOverride) kayıtlar, hedef LaunchBox kaydı o alanda veri
    // taşımıyorsa hâlâ "eksik" görünüp dışa aktarmada tekrar tekrar çıkabiliyordu. Varsayılan
    // KAPALI (diğerlerinin tersine) — kullanıcı zaten bu kayıtlar için bir karar vermiştir.
    [ObservableProperty]
    private bool showManuallyLinked;

    [ObservableProperty]
    private MissingMetadataItem? selectedMissingItem;

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
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action<string> CompletedCallback, Game Game)>? RequestSearchArtwork;

    public MetadataProviderViewModel(IReadOnlyList<Game> allGames, Action<Game> onMetadataUpdated, Func<IReadOnlyList<Platform>> getOrderedPlatforms, bool useModernLayout = false)
    {
        _allGames = allGames;
        _onMetadataUpdated = onMetadataUpdated;
        _getOrderedPlatforms = getOrderedPlatforms;
        UseModernLayout = useModernLayout;
        _hiddenPlatforms = new HashSet<string>(ConfigService.LoadDefault().MetadataProviderHiddenPlatforms);

        RebuildPlatformAuditSummaries();
        SelectedPlatformSummary = PlatformAuditSummaries.FirstOrDefault();
        RebuildMissingItems();
    }

    partial void OnSelectedPlatformSummaryChanged(PlatformMetadataAuditSummary? value)
    {
        SelectedPlatform = value?.Platform;
    }

    partial void OnSelectedPlatformChanged(Platform? value) => RebuildMissingItems();
    partial void OnShowMissingGenresChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingPublisherChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingDescriptionChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingYearChanged(bool value) => RebuildMissingItems();
    partial void OnShowMissingVersionChanged(bool value) => RebuildMissingItems();
    partial void OnShowManuallyLinkedChanged(bool value) => RebuildMissingItems();
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
        var visibleGames = _allGames.Where(g => !g.IsEffectivelyHidden && !g.IsDeleted).ToList();
        var orderedPlatforms = _getOrderedPlatforms();

        SyncPlatformVisibilityOptions(orderedPlatforms);

        PlatformAuditSummaries.Clear();
        foreach (var platform in orderedPlatforms)
        {
            // Aggregate "RetroAudit" (All Platforms) kartı her zaman görünür kalır — sadece
            // gerçek platform kartları filtre listesine göre gizlenebilir.
            if (!platform.IsAllPlatforms && _hiddenPlatforms.Contains(platform.Name))
                continue;

            var games = platform.IsAllPlatforms
                ? visibleGames
                : visibleGames.Where(g => g.Platform == platform.Name).ToList();
            var totalGames = games.Count;
            var matchedCount = games.Count(HasMatchedMetadata);
            // Kullanıcı isteği: "y sürümünü x'e bağladığımızda y'nin yılı eksikse metadata
            // provider uyarmasın, x'e bağladık zaten" — elle bağlanmış (BİRLEŞTİR/SÜRÜM, bkz.
            // Game.HasManualMetadataSourceOverride) kayıtlar için karar zaten verilmiş sayılır;
            // hedef LaunchBox kaydı bir alanda veri taşımasa bile kart rozetlerinde "eksik"
            // sayılmazlar (aksi hâlde alt listedeki "Bağlı" filtresiyle tutarsız kalırdı).
            var manuallyLinkedCount = games.Count(g => g.HasManualMetadataSourceOverride
                && (!g.HasGenres || !g.HasPublisher || string.IsNullOrWhiteSpace(g.Description) || !g.HasReleaseYear || string.IsNullOrWhiteSpace(g.MatchMethod)));
            var missingGenresCount = games.Count(g => !g.HasManualMetadataSourceOverride && !g.HasGenres);
            var missingPublisherCount = games.Count(g => !g.HasManualMetadataSourceOverride && !g.HasPublisher);
            var missingDescriptionCount = games.Count(g => !g.HasManualMetadataSourceOverride && string.IsNullOrWhiteSpace(g.Description));
            var missingYearCount = games.Count(g => !g.HasManualMetadataSourceOverride && !g.HasReleaseYear);
            var missingVersionCount = games.Count(g => !g.HasManualMetadataSourceOverride && string.IsNullOrWhiteSpace(g.MatchMethod));

            var healthPercent = totalGames == 0
                ? 0
                : (int)Math.Round(
                    (matchedCount / (double)totalGames * 30d) +
                    ((totalGames - missingGenresCount) / (double)totalGames * 14d) +
                    ((totalGames - missingPublisherCount) / (double)totalGames * 14d) +
                    ((totalGames - missingDescriptionCount) / (double)totalGames * 14d) +
                    ((totalGames - missingYearCount) / (double)totalGames * 14d) +
                    ((totalGames - missingVersionCount) / (double)totalGames * 14d),
                    MidpointRounding.AwayFromZero);

            PlatformAuditSummaries.Add(new PlatformMetadataAuditSummary
            {
                Platform = platform,
                TotalGames = totalGames,
                MatchedCount = matchedCount,
                MissingGenresCount = missingGenresCount,
                MissingPublisherCount = missingPublisherCount,
                MissingDescriptionCount = missingDescriptionCount,
                MissingYearCount = missingYearCount,
                MissingVersionCount = missingVersionCount,
                ManuallyLinkedCount = manuallyLinkedCount,
                HealthPercent = Math.Clamp(healthPercent, 0, 100),
            });
        }

        SelectedPlatformSummary = PlatformAuditSummaries.FirstOrDefault(s => s.Platform.Name == previousPlatformName)
            ?? PlatformAuditSummaries.FirstOrDefault();
    }

    // Platform filtre popup'ındaki checkbox listesini orderedPlatforms ile senkron tutar.
    // Var olan seçenekleri (ve dolayısıyla event handler'larını) yeniden oluşturmadan korur —
    // sadece yeni eklenen/kaldırılan platformları senkronlar.
    private void SyncPlatformVisibilityOptions(IReadOnlyList<Platform> orderedPlatforms)
    {
        var currentNames = orderedPlatforms.Where(p => !p.IsAllPlatforms).Select(p => p.Name).ToHashSet();

        for (var i = PlatformVisibilityOptions.Count - 1; i >= 0; i--)
        {
            if (!currentNames.Contains(PlatformVisibilityOptions[i].Name))
                PlatformVisibilityOptions.RemoveAt(i);
        }

        var existingNames = PlatformVisibilityOptions.Select(o => o.Name).ToHashSet();
        foreach (var platform in orderedPlatforms.Where(p => !p.IsAllPlatforms))
        {
            if (existingNames.Contains(platform.Name))
                continue;

            var option = new PlatformVisibilityOption
            {
                Name = platform.Name,
                DisplayName = platform.DisplayName,
                IsVisible = !_hiddenPlatforms.Contains(platform.Name),
            };
            option.VisibilityChanged += OnPlatformVisibilityOptionChanged;
            PlatformVisibilityOptions.Add(option);
        }
    }

    private void OnPlatformVisibilityOptionChanged(PlatformVisibilityOption option)
    {
        if (_suppressPlatformVisibilityChanged)
            return;

        if (option.IsVisible)
            _hiddenPlatforms.Remove(option.Name);
        else
            _hiddenPlatforms.Add(option.Name);

        var settings = ConfigService.LoadDefault();
        settings.MetadataProviderHiddenPlatforms = _hiddenPlatforms.ToList();
        ConfigService.SaveDefault(settings);

        RebuildPlatformAuditSummaries();
    }

    [RelayCommand]
    private void TogglePlatformFilterPopup() => IsPlatformFilterOpen = !IsPlatformFilterOpen;

    [RelayCommand]
    private void ShowAllPlatforms()
    {
        _hiddenPlatforms.Clear();

        _suppressPlatformVisibilityChanged = true;
        foreach (var option in PlatformVisibilityOptions)
            option.IsVisible = true;
        _suppressPlatformVisibilityChanged = false;

        var settings = ConfigService.LoadDefault();
        settings.MetadataProviderHiddenPlatforms = new List<string>();
        ConfigService.SaveDefault(settings);

        RebuildPlatformAuditSummaries();
    }

    private static bool HasMatchedMetadata(Game game) => game.MetadataSourceId.HasValue || game.StatusOk;

    private void RebuildMissingItems()
    {
        MissingItems.Clear();

        IEnumerable<Game> games = _allGames.Where(g => !g.IsEffectivelyHidden && !g.IsDeleted);
        if (!ShowManuallyLinked)
            games = games.Where(g => !g.HasManualMetadataSourceOverride);
        if (SelectedPlatform is { IsAllPlatforms: false })
            games = games.Where(g => g.Platform == SelectedPlatform.Name);
        if (!string.IsNullOrWhiteSpace(SearchText))
            games = games.Where(g => g.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

        foreach (var game in games)
        {
            if (!game.HasGenres && ShowMissingGenres)
                MissingItems.Add(new MissingMetadataItem { Game = game, MissingType = "Genres" });
            if (!game.HasPublisher && ShowMissingPublisher)
                MissingItems.Add(new MissingMetadataItem { Game = game, MissingType = "Publisher" });
            if (string.IsNullOrWhiteSpace(game.Description) && ShowMissingDescription)
                MissingItems.Add(new MissingMetadataItem { Game = game, MissingType = "Description" });
            if (!game.HasReleaseYear && ShowMissingYear)
                MissingItems.Add(new MissingMetadataItem { Game = game, MissingType = "Year" });
            if (string.IsNullOrWhiteSpace(game.MatchMethod) && ShowMissingVersion)
                MissingItems.Add(new MissingMetadataItem { Game = game, MissingType = "Version" });
        }
    }

    [RelayCommand]
    private void ToggleMissingType(string type)
    {
        switch (type)
        {
            case "Genres":
                ShowMissingGenres = !ShowMissingGenres;
                break;
            case "Publisher":
                ShowMissingPublisher = !ShowMissingPublisher;
                break;
            case "Description":
                ShowMissingDescription = !ShowMissingDescription;
                break;
            case "Year":
                ShowMissingYear = !ShowMissingYear;
                break;
            case "Version":
                ShowMissingVersion = !ShowMissingVersion;
                break;
        }
    }

    [RelayCommand]
    private void SkipItem()
    {
        if (SelectedMissingItem is { } item)
            MissingItems.Remove(item);
    }

    [RelayCommand]
    private void SearchSelected()
    {
        if (SelectedMissingItem is not { } item)
            return;

        var query = $"{item.Game.Title} {item.Game.PlatformDisplayName} {item.MissingTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
        
        var targetFolder = Path.Combine(AppPaths.Images, item.Game.PlatformDisplayName, "Box");
        var baseFileName = GetMediaBaseFileName(item.Game);

        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, item.Game.Title, item.MissingTypeLabel, _ =>
        {
            _onMetadataUpdated(item.Game);
            RebuildPlatformAuditSummaries();
            RebuildMissingItems();
        }, item.Game));
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedMissingItem is not { } item)
            return;

        RequestEditMetadata?.Invoke((item.Game, () =>
        {
            _onMetadataUpdated(item.Game);
            RebuildPlatformAuditSummaries();
            RebuildMissingItems();
            SelectedMissingItem = MissingItems.FirstOrDefault(x => x.Game == item.Game);
        }));
    }

    [RelayCommand]
    private void ReMatchSelected()
    {
        if (SelectedMissingItem is not { } item)
            return;

        var appSettings = ConfigService.LoadDefault();
        if (string.IsNullOrWhiteSpace(appSettings.MasterMetadataDbPath) || !File.Exists(appSettings.MasterMetadataDbPath))
        {
            RequestShowMessage?.Invoke("MasterMetadata.db yolu Ayarlar > Genel'de tanımlı değil ya da bulunamadı.");
            return;
        }

        using var reader = new MasterMetadataReader(appSettings.MasterMetadataDbPath);
        if (!reader.IsPlatformKnown(item.Game.Platform))
        {
            RequestShowMessage?.Invoke("Bu platform master metadata veritabanında tanınmadı.");
            return;
        }

        var compareTitle = VersionResolver.NormalizeForCompare(item.Game.Title);
        var match = reader.FindMatch(item.Game.Platform, compareTitle, item.Game.Title);
        if (match is null)
        {
            RequestShowMessage?.Invoke("Bu oyun için yeni bir metadata eşleşmesi bulunamadı.");
            return;
        }

        ApplyMetadataMatch(item.Game, match);
        _onMetadataUpdated(item.Game);
        RebuildPlatformAuditSummaries();
        RebuildMissingItems();
        SelectedMissingItem = MissingItems.FirstOrDefault(x => x.Game == item.Game);
        RequestShowMessage?.Invoke($"\"{item.Game.Title}\" için metadata güncellendi.");
    }

    private static void ApplyMetadataMatch(Game game, MetadataMatch match)
    {
        game.Developer = match.Developer ?? game.Developer;
        game.Publisher = match.Publisher ?? game.Publisher;
        game.ReleaseYear = match.ReleaseYear ?? match.ReleaseDate?.Year ?? game.ReleaseYear;
        game.Description = match.Overview ?? game.Description;
        game.MaxPlayers = match.MaxPlayers ?? game.MaxPlayers;
        if (match.Genres.Length > 0)
            game.Genres = string.Join(", ", match.Genres);
        game.MatchMethod = match.MatchMethod;
        game.NeedsReview = match.Confidence < MasterMetadataReader.FuzzyAcceptThreshold;
        game.ReleaseDate = match.ReleaseDate ?? game.ReleaseDate;
        game.CommunityRating = match.CommunityRating ?? game.CommunityRating;
        game.VideoUrl = match.VideoUrl ?? game.VideoUrl;
        game.WikipediaUrl = match.WikipediaUrl ?? game.WikipediaUrl;
        game.SteamAppId = match.SteamAppId ?? game.SteamAppId;
        game.Cooperative = match.Cooperative ?? game.Cooperative;
        game.GameMode = game.Cooperative switch
        {
            true => "Kooperatif",
            false => "Kooperatif Değil",
            null => game.GameMode,
        };
        game.MetadataSourceId = match.MetadataSourceId;
    }

    private static string GetMediaBaseFileName(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.File))
            return Path.GetFileNameWithoutExtension(game.File);

        var invalid = Path.GetInvalidFileNameChars();
        return new string(game.Title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
