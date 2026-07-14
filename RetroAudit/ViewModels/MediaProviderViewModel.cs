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
    // tazele" demek için (bkz. MainWindow.xaml.cs: vm.RegisterDownloadedMedia + NotifyArtworkDownloaded
    // + ApplyFilter). Kullanıcı geri bildirimi: "media provider da otomatik indirme çalışmıyor
    // heralde" — sadece Game göndermek yetmiyordu, MainViewModel'in KENDİ medya sözlüklerini
    // (_boxByPlatform vb.) güncelleyebilmesi için tür/dosya adı/hedef yol da gerekiyor (bkz.
    // MainViewModel.RegisterDownloadedMedia) — aksi halde dosya diske gerçekten inse bile
    // NotifyArtworkDownloaded onu bulamıyor, restart'a kadar boş görünüyordu.
    private readonly Action<Game, string, string, string> _onArtworkResolved;
    private readonly Func<IReadOnlyList<Platform>> _getOrderedPlatforms;

    public bool UseModernLayout { get; }

    public ObservableCollection<PlatformAuditSummary> PlatformAuditSummaries { get; } = new();
    public ObservableCollection<MissingMediaItem> MissingItems { get; } = new();

    [ObservableProperty]
    private Platform? selectedPlatform;

    [ObservableProperty]
    private PlatformAuditSummary? selectedPlatformSummary;

    // Kullanıcı isteği: "uygun bir yere junkları filtreleme ekle junklar dahil yansıyor provider a
    // ... yüzde hesaplamalarınıda junklar seçiliyse ona göre ver seçili değilse ona göre ver" —
    // ana tablodaki Released/Junk chip'leriyle AYNI Game.Version alanı (bkz. MainViewModel.
    // ShowReleased/ShowJunk) — Media Provider'da tek bir "dahil et" anahtarı yeterli (varsayılan
    // KAPALI, ana tablonun varsayılanıyla AYNI: ShowJunk=false), hem eksik öğe listesini hem
    // platform kartlarındaki % sağlık hesaplamasını (bkz. RebuildPlatformAuditSummaries — health
    // percent zaten bu filtrelenmiş listeden hesaplanıyor) etkiler.
    [ObservableProperty]
    private bool showJunk;

    partial void OnShowJunkChanged(bool value) => RefreshAll();

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
    [NotifyPropertyChangedFor(nameof(CanAutoDownload))]
    private MissingMediaItem? selectedMissingItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAutoDownload))]
    private bool isBusy;

    // Kullanıcı isteği: "videolar ile ilgili filtre tek başına filtrelendiğinde veya seçilen
    // satırın eksiği video olduğunda o komut pasif gözükmeli videoyla alakası yok çünkü" —
    // BulkFetchArtworkForGamesAsync sadece Box/Logo/SS indirebiliyor (bkz. MainViewModel.
    // DownloadArtworkAsync), Video/Wiki için hiçbir işe yaramıyor. Bir satır seçiliyse ONUN türüne
    // bakılır; hiç seçim yoksa (ör. sadece "Video" filtresi açıkken tüm liste video'dan ibaretse)
    // o an GÖRÜNEN listede en az bir Box/Logo/SS eksiği var mı diye bakılır.
    public bool CanAutoDownload
    {
        get
        {
            if (IsBusy)
                return false;
            if (SelectedMissingItem is { } selected)
                return selected.MissingType is not ("Video" or "Wiki");
            return MissingItems.Any(i => i.MissingType is not ("Video" or "Wiki"));
        }
    }

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
    public event Action<(string Url, string TargetFolder, string TargetFileNameWithoutExtension, string GameTitle, string MediaTypeLabel, Action<string> CompletedCallback, Game Game)>? RequestSearchArtwork;

    public MediaProviderViewModel(IReadOnlyList<Game> allGames, Action<Game, string, string, string> onArtworkResolved, Func<IReadOnlyList<Platform>> getOrderedPlatforms, bool useModernLayout = false)
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
        var visibleGames = _allGames.Where(g => !g.IsHidden && !g.IsDeleted && (ShowJunk || g.Version == "Released")).ToList();
        var orderedPlatforms = _getOrderedPlatforms();

        // Kullanıcı geri bildirimi: "providerda junkları dahil et dediğimde geç hesaplıyor" —
        // eskiden HER platform için visibleGames'in TAMAMI yeniden taranıyordu (O(oyun × platform),
        // Junk dahilken ~67 bin oyun × ~40 platform = milyonlarca karşılaştırma). Tek seferlik
        // ToLookup ile platform başına O(1) erişim + toplamda sadece O(oyun) materyalizasyon.
        var gamesByPlatform = visibleGames.ToLookup(g => g.Platform);

        PlatformAuditSummaries.Clear();
        foreach (var platform in orderedPlatforms)
        {
            var games = platform.IsAllPlatforms
                ? visibleGames
                : gamesByPlatform[platform.Name].ToList();
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

        IEnumerable<Game> games = _allGames.Where(g => !g.IsHidden && !g.IsDeleted && (ShowJunk || g.Version == "Released"));
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

        OnPropertyChanged(nameof(CanAutoDownload));
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

    // Kullanıcı isteği: "otomatik indir butonuna basıyorum indirmiyor ... normal otomatik indirme
    // komutuna bağlasana bunuda çalışmıyor şuanda ... görsel getir butonu varya ona bağlıcan ...
    // toplu seçimlerede uygun olacak" — bu ViewModel'in kendi (ayrı, hatalı) tek-satır indirme
    // mantığı TAMAMEN kaldırıldı. "Otomatik İndir" düğmesi artık doğrudan MainViewModel.
    // BulkFetchArtworkForGamesAsync'i (ana tablonun "Görsel Getir"iyle AYNI mekanizma) çağırıyor —
    // bkz. MediaProviderWindow.xaml.cs AutoDownloadButton_Click (seçili satır(lar)ı toplar, XAML'de
    // artık Command binding YOK).

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
            "Box" => "cover",
            "Logo" => "clear logo",
            "SS" => "gameplay",
            "Video" => "video bağlantısı",
            "Wiki" => "Wikipedia bağlantısı",
            _ => item.MissingType,
        };

        if (item.MissingType is "Video" or "Wiki")
        {
            RequestShowMessage?.Invoke("Video ve Wiki eksikleri otomatik görsel arama ile tamamlanmıyor.");
            return;
        }

        // Clear Logo genelde platformdan bağımsız (kullanıcı isteği: "clearlogoda platformu
        // yazmana gerek yok") — bkz. MainViewModel.SearchArtwork, aynı gerekçe.
        var query = item.MissingType == "Logo"
            ? $"{item.Game.Title} {mediaTypeLabel}"
            : $"{item.Game.Title} {item.Game.PlatformDisplayName} {mediaTypeLabel}";
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) + "&tbm=isch";
        var targetFolder = Path.Combine(AppPaths.Images, item.Game.PlatformDisplayName, item.MissingType);
        var baseFileName = GetMediaBaseFileName(item.Game);

        // Kullanıcı bulgusu (bkz. MainViewModel.SearchArtwork'teki AYNI düzeltme): hedef dosya
        // adının uzantısı burada TAHMİN edilmemeli — MediaSearchWindow'un GERÇEKTEN yazdığı yol
        // (kaynak görselin/tarayıcının uzantısıyla, ör. ".webp") kullanılmalı, yoksa BoxPath var
        // olmayan bir dosyaya işaret edip Crop Editor'da "Could not find file" ile çöküyordu.
        RequestSearchArtwork?.Invoke((url, targetFolder, baseFileName, item.Game.Title, mediaTypeLabel, actualPath =>
        {
            MissingItems.Remove(item);
            _onArtworkResolved(item.Game, item.MissingType, baseFileName, actualPath);
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
