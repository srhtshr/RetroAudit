using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAudit.Catalog.Naming;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// ROM İçe Aktar'ın "Eşleşmeyenler" sekmesinden bir dosyayı ELLE bir Game'e (istenirse belirli bir
// GameVersion'ına) bağlamak için kullanılan küçük seçim penceresinin ViewModel'i (kullanıcı isteği:
// "manuel bağlama ... Game seviyesinde değil, istenirse GameVersion seviyesinde de yapılabilsin").
// Metadata (kapak/açıklama/favori vb.) ile ROM sahipliği BİLİNÇLİ OLARAK ayrı tutuluyor — bu
// pencere sadece "hangi dosya hangi Game/GameVersion'a bağlanacak" sorusuna cevap veriyor, katalog
// hiçbir şekilde değişmiyor (bkz. UserDataService.SaveFilePathOverride — ayrı, kullanıcı verisi
// veritabanı).
public partial class ManualLinkViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _allGames;
    private readonly string _scannedFolderName;
    private readonly string _sourceFileName;

    // Kullanıcı isteği: "unknown değilde manuel bağlamaya yönlendirebilirsin ... 2 ayrı eşleştirme
    // şekli olmasın, bağladığımızda manuel olarak kaydediyorya buda aynı yani" — kataloktaki
    // HİÇBİR Game'e karşılık gelmeyen dosyalar için, oyun listesinin EN ÜSTÜNE her zaman eklenen,
    // "dosyanın kendi adıyla YENİ bir oyun oluştur" seçeneği (bkz. GameKey == NewGameSentinelKey
    // kontrolü, OnSelectedGameChanged, MainViewModel.RegisterNewCustomGame). TEK bir sabit örnek
    // olarak tutuluyor (her FilteredGames çağrısında yeniden ÜRETİLMİYOR) çünkü WPF'in
    // ListBox.SelectedItem'ı referans eşitliğiyle çalışıyor — arama kutusu her tuş vuruşunda
    // FilteredGames'i yeniden hesaplarken yeni bir örnek üretilseydi, varsayılan seçim görsel
    // olarak kaybolurdu.
    public const string NewGameSentinelKey = "__new_custom_game__";
    private readonly Game _newGameSentinel;

    // Kullanıcı isteği: "şu listede detaylardaki gibi alternatif nameleri çıkmıyor" — arama artık
    // sadece başlığa değil, LaunchBox'ın alternatif isim listesine de bakıyor (ör. "Utsurun Desu"
    // gibi bir Japonca romanizasyon, o oyunun ana başlığında değil sadece AlternateNames'inde
    // olabilir); liste öğesinde de (ManualLinkWindow.xaml) küçük gri alt satır olarak gösteriliyor.
    public IReadOnlyDictionary<int, List<string>> AlternateNamesByGameId { get; } = CatalogDatabaseService.GetAllAlternateNames();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGames))]
    private string searchText = string.Empty;

    // Kullanıcı isteği: "varsayılan olarak yalnızca taranan platformun oyunları göstersin; başka
    // platformlar kullanıcı özellikle istemedikçe listelenmesin."
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGames))]
    private bool showAllPlatforms;

    public IEnumerable<Game> FilteredGames
    {
        get
        {
            var query = ShowAllPlatforms ? _allGames : _allGames.Where(g => RomImportService.PlatformNameMatchesFolder(g, _scannedFolderName));
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(g =>
                    g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || (AlternateNamesByGameId.TryGetValue(g.GameId, out var names)
                        && names.Any(n => n.Contains(SearchText, StringComparison.OrdinalIgnoreCase))));
            }
            return new[] { _newGameSentinel }.Concat(query.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).Take(200));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    private Game? selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;

    public ObservableCollection<GameVersion> SelectedGameVersions { get; } = new();

    // Kullanıcı isteği: "genel bağlantı olayını unut kafa karıştırmasın ... sürümlerin içine yeni
    // kart açsın o kadar" — artık HER bağlantı bir SÜRÜME özel (bkz. dosya başı yorum); "genel/Game
    // seviyesi" seçeneği kaldırıldı. Oyun seçilince kullanıcı elle seçmek zorunda kalmasın diye
    // tercih edilen (yoksa ilk) sürüm otomatik seçili gelir, istersen listeden başka birini seçebilirsin.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private GameVersion? selectedVersion;

    public bool CanConfirm => SelectedGame is not null && SelectedVersion is not null;

    partial void OnSelectedGameChanged(Game? value)
    {
        SelectedGameVersions.Clear();
        SelectedVersion = null;
        if (value is not null)
        {
            // Kullanıcı isteği: "yeni olsun bi tane ona tıklayınca dosya ismi neyse onunla açsın" —
            // kataloktaki gerçek sürümlerin ÜSTÜNE, dosyanın kendi adını taşıyan sentetik bir "Yeni"
            // seçeneği eklenir (bkz. GameVersion.IsCustomEntry) — kataloğun bilmediği bir dump/
            // revizyon olsa bile kullanıcı zorlanmadan bağlayabilsin diye. "+ Yeni Oyun" (sentinel)
            // seçiliyken kataloktan gerçek sürüm ARANMAZ (GameId=0, hiçbir GameVersion'a karşılık
            // gelmez) — bu tek kart, standalone oyunun KENDİ (tek) sürümü olur.
            SelectedGameVersions.Add(new GameVersion
            {
                RawDatName = System.IO.Path.GetFileNameWithoutExtension(_sourceFileName),
                IsCustomEntry = true,
            });
            if (value.GameKey != NewGameSentinelKey)
            {
                foreach (var version in CatalogDatabaseService.GetVersions(value.GameId, value.GameKey))
                    SelectedGameVersions.Add(version);
            }

            // Kullanıcı isteği: "şurda yeni olan seçili gelsin hep" — Eşleşmeyenler'e düşen dosyalar
            // zaten genelde kataloğun tam karşılığı olmayan dump'lar, bu yüzden varsayılan seçim
            // "Yeni" (dosyanın kendi adı, bkz. yukarıdaki ekleme) — kullanıcı isterse listeden gerçek
            // bir katalog sürümüne değiştirebilir.
            SelectedVersion = SelectedGameVersions.FirstOrDefault();
        }
    }

    public ManualLinkViewModel(IReadOnlyList<Game> allGames, string scannedFolderName, string sourceFileName)
    {
        _allGames = allGames;
        _scannedFolderName = scannedFolderName;
        _sourceFileName = sourceFileName;

        var cleanTitle = DatNameParser.Parse(Path.GetFileNameWithoutExtension(sourceFileName)).CleanTitle;
        _newGameSentinel = new Game
        {
            GameKey = NewGameSentinelKey,
            Title = string.IsNullOrWhiteSpace(cleanTitle) ? sourceFileName : cleanTitle,
            Platform = scannedFolderName,
            PlatformDisplayName = scannedFolderName,
        };

        // Kullanıcı isteği: "seçenek olsun orda tik işaretli gelsin eşleşmeyenleride import etmek
        // için" — bu senaryoda kataloğa uyan bir aday genelde YOK, bu yüzden varsayılan olarak
        // "+ Yeni Oyun" (kendi dosya adıyla standalone bir oyun oluştur) zaten seçili gelir; sadece
        // "Bağla"ya basmak yeterli. Kullanıcı isterse listeden gerçek bir katalog oyunu seçebilir.
        SelectedGame = _newGameSentinel;
    }

    // true: bağla, false: iptal — View bunu DialogResult'a çevirir (bkz. ArtworkTypeSelectionDialog
    // ile AYNI desen).
    public event Action<bool>? RequestClose;

    [RelayCommand]
    private void Link() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
