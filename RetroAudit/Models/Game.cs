using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroAudit.Services;

namespace RetroAudit.Models;

// Kütüphanedeki tek bir oyunu temsil eden veri modeli. RetroAudit.db'nin Games tablosundaki
// "parent" (1G1R) satırına karşılık gelir — bkz. Services/CatalogDatabaseService. ObservableObject
// tabanı sadece IsFavorite için gerekli: DataGrid'deki yıldız sütunu tüm listeyi yeniden
// filtrelemeden anında güncellensin diye (bkz. MainViewModel.ToggleFavoriteCommand).
public partial class Game : ObservableObject
{
    // RetroAudit.db'deki Games.GameId — sağ paneldeki Versions listesini bu oyun için ayrıca
    // sorgulamak (GetVersions) için gerekli. Builder her koşuda yeniden üretildiği için KALICI
    // DEĞİLDİR — kullanıcı verisi (favori/gizli/playlist/override) bu yüzden GameKey kullanır.
    public int GameId { get; set; }

    // Kullanıcı verisi (UserDataService) için sabit kimlik — bkz. GameKeyHelper.
    public string GameKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    // Ham DAT platform adı ("Atari - Jaguar" gibi) — filtreleme/GameKey/dosya yolu hep bunu kullanır.
    public string Platform { get; set; } = string.Empty;

    // DataGrid'in Platform sütununda gösterilen sade isim ("Jaguar") — bkz. PlatformDisplayNameMap.
    // Sütun hücresinde artık bu metin değil PlatformLogoPath gösteriliyor (bkz. MainWindow.xaml
    // PlatformColumn) — arama/filtre/sıralama hâlâ bu alanı kullanmaya devam ediyor.
    public string PlatformDisplayName { get; set; } = string.Empty;

    // Images/Platforms/{PlatformDisplayName}.png varsa tam yolu (bkz. CatalogDatabaseService.
    // GetGames/ResolveLogoPath) — yoksa boş, bu durumda DataGrid hücresi metne geri düşer.
    public string PlatformLogoPath { get; set; } = string.Empty;
    public bool HasPlatformLogo => !string.IsNullOrWhiteSpace(PlatformLogoPath);

    // "Released" / "Junk" — toolbar'daki filtre butonlarıyla eşleşir. Gerçek veride
    // Games.HiddenByDefault'a karşılık gelir (Casino/Board Game/Educational gibi türler "Junk").
    public string Version { get; set; } = "Released";
    public string Genres { get; set; } = string.Empty;

    // Tercih edilen (preferred) sürümün ilk ROM dosyasının adı — bir oyunun birden fazla dosyası
    // (headered/headerless gibi) olabilir, tam liste sağ paneldeki Versions listesindedir.
    public string File { get; set; } = string.Empty;

    // Orta paneldeki DataGrid'de mor tik / kırmızı çarpı ikonunu belirleyen genel durum bayrağı.
    public bool StatusOk { get; set; }

    // Box/BG/SS/Logo görsellerinin tam yolu — MainViewModel.BuildLocalFileIndex tarafından
    // yükleme sırasında hesaplanır (bkz. o metodun yorumu), Box/BG/SS kolonlarındaki nokta
    // göstergeleri VE sağ detay panelindeki gerçek küçük resimler hepsi buradan besleniyor.
    // ObservableProperty: "Görsel Getir" tamamlandığında MainViewModel.NotifyArtworkDownloaded
    // bunları YENİDEN set ediyor — restart olmadan grid/detay paneli anında güncellensin diye
    // (eskiden düz get/set'ti, bu yüzden değişiklik uygulama kapatılıp açılana kadar görünmüyordu).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBox))]
    [NotifyPropertyChangedFor(nameof(BoxDisplayPath))]
    private string boxPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackground))]
    [NotifyPropertyChangedFor(nameof(BackgroundDisplayPath))]
    private string backgroundPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshot))]
    [NotifyPropertyChangedFor(nameof(ScreenshotDisplayPath))]
    private string screenshotPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClearLogo))]
    private string clearLogoPath = string.Empty;

    public bool HasBox => !string.IsNullOrWhiteSpace(BoxPath);
    public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundPath);
    public bool HasScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPath);
    public bool HasClearLogo => !string.IsNullOrWhiteSpace(ClearLogoPath);

    // Detay panelinde gösterilecek gerçek yol — Has* bayrakları (yukarıda) gerçek görselin var
    // olup olmadığını (grid noktaları, "Görsel Getir" gibi yerlerde) yansıtmaya devam eder, bu
    // alanlar SADECE görüntüleme amaçlı: görsel yoksa Images/NoImage altındaki sabit yer
    // tutucuya düşer (bkz. AppPaths.NoImageCover/NoImageBackground).
    public string BoxDisplayPath => HasBox ? BoxPath : AppPaths.NoImageCover;
    public string BackgroundDisplayPath => HasBackground ? BackgroundPath : AppPaths.NoImageBackground;
    public string ScreenshotDisplayPath => HasScreenshot ? ScreenshotPath : AppPaths.NoImageBackground;

    // Zenginleştirme kaynağındaki bu oyunun sayısal kimliği — eşleşme yoksa null. "Görsel Getir"
    // butonunun etkin olup olmadığını belirler (bkz. MainViewModel.FetchArtwork).
    public int? MetadataSourceId { get; set; }
    public bool HasArtworkSource => MetadataSourceId.HasValue;

    // Bu oyunun KENDİ platformu içindeki ağırlıklı Topluluk Puanı sıralamasına göre en yüksek
    // rozeti — "Top 25" / "Top 100" / "Top 250" ya da hiçbiri (bkz. MainViewModel.
    // ComputeTopRankBadges, uygulama açılışında bir kez hesaplanır). Değer, Images/Badges/
    // altındaki dosya adıyla birebir aynı ("Top 25.png" gibi) — ayrı bir eşleme tablosuna gerek yok.
    public string TopRankBadge { get; set; } = string.Empty;
    public bool HasTopRankBadge => !string.IsNullOrEmpty(TopRankBadge);
    public string TopRankBadgePath => HasTopRankBadge ? Path.Combine(AppPaths.Images, "Badges", $"{TopRankBadge}.png") : string.Empty;

    // Sağ detay panelinde gösterilen ek bilgiler.
    public int ReleaseYear { get; set; }
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Single Player";
    public int MaxPlayers { get; set; } = 1;
    public string Description { get; set; } = string.Empty;

    // LaunchBox'tan gelen ek zenginleştirme alanları (bkz. CatalogDatabaseService.GetGames).
    // ReleaseDate, ReleaseYear'dan daha kesin (gün/ay dahil) ama her kayıtta yok — detay panelinde
    // varsa tam tarih, yoksa yıl gösterilir (bkz. ReleaseDateDisplay).
    public DateTime? ReleaseDate { get; set; }
    public double? CommunityRating { get; set; }

    // Top 250 için ağırlıklı sıralamada kullanılıyor (bkz. MainViewModel.ComputeTopRated) — kaç
    // kişinin oyladığını bilmeden ham CommunityRating'e göre sıralamak yanıltıcı olurdu (2 oyla
    // 5.0 alan bir oyun, 5000 oyla 4.6 alandan "daha iyi" görünürdü).
    public int? CommunityRatingCount { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public string WikipediaUrl { get; set; } = string.Empty;
    public long? SteamAppId { get; set; }
    public bool? Cooperative { get; set; }

    public string ReleaseDateDisplay => ReleaseDate is { } date ? date.ToString("d") : (ReleaseYear > 0 ? ReleaseYear.ToString() : string.Empty);
    public bool HasVideoUrl => !string.IsNullOrWhiteSpace(VideoUrl);
    public bool HasWikipediaUrl => !string.IsNullOrWhiteSpace(WikipediaUrl);
    public bool HasCommunityRating => CommunityRating.HasValue;
    public bool HasSteamAppId => SteamAppId.HasValue;

    // 5 yıldız üzerinden dolu/boş yıldız gösterimi (Favori yıldızıyla aynı görsel dil, ★/☆) +
    // sayısal değer. LaunchBox'ın CommunityRating'i zaten 0-5 aralığında.
    public string CommunityRatingDisplay
    {
        get
        {
            if (CommunityRating is not { } rating)
                return string.Empty;

            var filled = Math.Clamp((int)Math.Round(rating), 0, 5);
            return $"{new string('★', filled)}{new string('☆', 5 - filled)}  ({rating:0.0})";
        }
    }

    // Tercih edilen (preferred) sürümün bölgesi ve kaynağı — DataGrid'de sütun/filtre olarak
    // gösterilebilsin diye Game seviyesine taşındı (GameVersion listesi sadece seçili oyun için
    // ayrıca sorgulanıyor, DataGrid'de tüm oyunlar için tek bakışta bölge/kaynak lazım). Sürüm
    // etiketi (Rev A, Beta vb.) bilinçli olarak burada yok — sağ paneldeki Sürümler listesinde
    // (GameVersion.VersionLabel) zaten her sürüm için ayrı ayrı gösteriliyor, Game seviyesinde
    // tekilleştirmek (sadece preferred sürümü yansıtır) yanıltıcı olur.
    public string Region { get; set; } = string.Empty;
    public string SourceDat { get; set; } = string.Empty;

    // LaunchBox metadata eşleşme bilgisi — "CompareName"/"ExactName"/"AlternateName"/"Fuzzy" veya
    // eşleşme yoksa boş.
    public string MatchMethod { get; set; } = string.Empty;
    public bool NeedsReview { get; set; }

    // UserDataService'ten (RetroAuditUserData.db, RetroAudit.db'den AYRI) gelen kullanıcı durumu.
    [ObservableProperty]
    private bool isFavorite;

    // Gizli/Çöp kutusu durumu değiştiğinde satır zaten MainViewModel.ApplyFilter tarafından
    // listeden çıkarılıp yeniden filtrelendiği için bunların ObservableProperty olmasına gerek
    // yok — sadece hangi chip'in altında göründüğünü belirlemek için okunuyorlar.
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }

    public string Notes { get; set; } = string.Empty;

    // MainViewModel.HasLocalFile ile yükleme sırasında bir kere hesaplanır (henüz gerçek bir ROM
    // taraması yok, bkz. o metodun yorumu) — DataGrid'deki "eksik ROM'u ara" sütunu buna bakar.
    public bool HasLocalFile { get; set; }
}
