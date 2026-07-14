using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
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

    [ObservableProperty]
    private string videoUrl;

    [ObservableProperty]
    private string releaseYearText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomRegionEntry))]
    private string region;

    public ObservableCollection<string> RegionOptions { get; } = new();

    // Kullanıcı isteği: "şuraya bide others ekle ona tıklayınca manuel girilsin o listeye gelsin"
    // → sonra netleşti: "manuel girilenlerde mevcut kayıtlı regionlardan olsun, bayrakları ile ...
    // bi nevi hazır listeden" — serbest metin DEĞİL, kataloktaki TÜM bölgelerin (bu oyunun kendi
    // sürümleriyle sınırlı olmayan, bkz. CatalogDatabaseService.GetAllRegionNames) tam listesi.
    // "Diğer..." seçilince ikinci bir (bayraklı) ComboBox açılır (bkz. EditMetadataWindow.xaml).
    public const string CustomRegionSentinel = "Diğer...";

    public bool IsCustomRegionEntry => Region == CustomRegionSentinel;

    public ObservableCollection<string> AllRegionOptions { get; } = new();

    // İkinci (tam) listeden bir bölge seçilince (ör. "Brazil") o değer RegionOptions'ta (üstteki,
    // kısa liste) yoksa ÜST ComboBox'ın SelectedItem'ı eşleşme bulamayıp boş görünürdü — bu yüzden
    // seçilen değer kalıcı olarak RegionOptions'a da (sentinel'den önce) ekleniyor.
    partial void OnRegionChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || value == CustomRegionSentinel)
            return;
        if (!RegionOptions.Contains(value, StringComparer.OrdinalIgnoreCase))
            RegionOptions.Insert(RegionOptions.Count - 1, value);
    }

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
        videoUrl = game.VideoUrl;
        releaseYearText = game.HasReleaseYear ? game.ReleaseYear.ToString() : string.Empty;
        // Kullanıcı geri bildirimi: "region kısmı boş gözüküyor ... açılır listesinde var ama seçili
        // değil gibi" — game.Region, toolbar'daki USA/EU/Japan bayrak filtresine göre YENİDEN
        // hesaplanan (bkz. MainViewModel.RecomputeRegionDisplay), filtreye göre BOŞ da kalabilen
        // görüntüleme amaçlı bir alan — Edit Metadata için güvenilir değil. Boşsa, gerçek Preferred
        // sürümün bölgesine (katalogdan doğrudan, filtreden bağımsız) düşülüyor.
        region = game.Region;
        if (string.IsNullOrWhiteSpace(region))
        {
            var versions = CatalogDatabaseService.GetVersions(game.GameId, game.GameKey);
            var preferred = versions.FirstOrDefault(v => v.IsPreferred) ?? versions.FirstOrDefault();
            region = preferred?.Region ?? string.Empty;
        }

        foreach (var option in BuildRegionOptions(game))
            RegionOptions.Add(option);
        RegionOptions.Add(CustomRegionSentinel);

        // Kullanıcı geri bildirimi: "scandinavia'yı kaldır sistemden bayrağı falan gözükmüyor" —
        // Images/Flags altında karşılığı olmayan (bkz. FlagResolver) bölgeler bu bayraklı listede
        // hiç gösterilmiyor ("Unknown" kasıtlı olarak bayraksız olduğu için istisna).
        foreach (var name in CatalogDatabaseService.GetAllRegionNames())
        {
            if (string.Equals(name, "Unknown", StringComparison.OrdinalIgnoreCase) || FlagResolver.Resolve(name) is not null)
                AllRegionOptions.Add(name);
        }
    }

    [RelayCommand]
    private void Save()
    {
        // Kullanıcı "Diğer..."yi seçip hiçbir şey yazmadan Save'e basarsa sentinel metni gerçek bir
        // bölge değeriymiş gibi kaydedilmesin.
        if (Region == CustomRegionSentinel)
            Region = string.Empty;

        int? releaseYear = int.TryParse(ReleaseYearText, out var parsedYear) ? parsedYear : null;
        // Kullanıcı isteği: "büyük küçük uğraşmayım" — elle yazılan türler (küçük/karışık harfle
        // girilse bile) katalogdaki gerçek türlerle aynı görünüme (ör. "Role-Playing") gelsin diye
        // her kelimenin ilk harfi otomatik büyütülüyor.
        Genre = NormalizeGenreCasing(Genre);
        var overrideValue = new MetadataOverride(Title, Genre, Description, Notes, Publisher, Developer, VideoUrl, releaseYear, Region, null);
        UserDataService.SaveMetadataOverride(_game.GameKey, overrideValue);

        _game.Title = Title;
        _game.Genres = Genre;
        _game.Description = Description;
        _game.Notes = Notes;
        _game.Publisher = Publisher;
        _game.Developer = Developer;
        _game.VideoUrl = VideoUrl;
        _game.ReleaseYear = releaseYear ?? 0;
        _game.Region = Region;

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    // Virgülle ayrılmış her tür token'ının içindeki her kelimenin ilk harfini büyütür (ör.
    // "role-playing" -> "Role-Playing", "shoot em up" -> "Shoot Em Up") — kataloktaki gerçek
    // türlerin görünümüyle tutarlı olsun diye.
    private static string NormalizeGenreCasing(string genres)
    {
        if (string.IsNullOrWhiteSpace(genres))
            return genres;

        var tokens = genres.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(CapitalizeWords);
        return string.Join(", ", tokens);
    }

    private static string CapitalizeWords(string token)
    {
        token = token.ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Replace(token, @"(^|[\s\-])([a-z])",
            m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
    }

    private static IEnumerable<string> BuildRegionOptions(Game game)
    {
        var options = new List<string>();
        var common = new[] { "USA", "Europe", "Japan", "World", "Unknown" };

        foreach (var region in game.AllVersions.Select(v => v.Region)
                     .Concat(common)
                     .Append(game.Region)
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            options.Add(region);
        }

        return options;
    }
}
