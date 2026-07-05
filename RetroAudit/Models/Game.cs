using CommunityToolkit.Mvvm.ComponentModel;

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
    public string PlatformDisplayName { get; set; } = string.Empty;

    // "Released" / "Junk" — toolbar'daki filtre butonlarıyla eşleşir. Gerçek veride
    // Games.HiddenByDefault'a karşılık gelir (Casino/Board Game/Educational gibi türler "Junk").
    public string Version { get; set; } = "Released";
    public string Genres { get; set; } = string.Empty;

    // Tercih edilen (preferred) sürümün ilk ROM dosyasının adı — bir oyunun birden fazla dosyası
    // (headered/headerless gibi) olabilir, tam liste sağ paneldeki Versions listesindedir.
    public string File { get; set; } = string.Empty;

    // Orta paneldeki DataGrid'de mor tik / kırmızı çarpı ikonunu belirleyen genel durum bayrağı.
    public bool StatusOk { get; set; }

    // Box/BG/SS kolonlarındaki nokta göstergelerini besleyen medya varlık bayrakları.
    public bool HasBox { get; set; }
    public bool HasBackground { get; set; }
    public bool HasScreenshot { get; set; }

    public string CoverImagePath { get; set; } = string.Empty;
    public string ScreenshotImagePath { get; set; } = string.Empty;

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
