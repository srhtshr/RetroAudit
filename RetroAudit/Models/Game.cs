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
    [NotifyPropertyChangedFor(nameof(HasMissingArtwork))]
    [NotifyPropertyChangedFor(nameof(CanFetchArtwork))]
    private string boxPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshot))]
    [NotifyPropertyChangedFor(nameof(ScreenshotDisplayPath))]
    [NotifyPropertyChangedFor(nameof(ClearLogoDisplayPath))]
    [NotifyPropertyChangedFor(nameof(ShowClearLogoArea))]
    [NotifyPropertyChangedFor(nameof(HasMissingArtwork))]
    [NotifyPropertyChangedFor(nameof(CanFetchArtwork))]
    private string screenshotPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClearLogo))]
    [NotifyPropertyChangedFor(nameof(ClearLogoDisplayPath))]
    [NotifyPropertyChangedFor(nameof(ClearLogoThumbnailPath))]
    [NotifyPropertyChangedFor(nameof(ShowClearLogoArea))]
    [NotifyPropertyChangedFor(nameof(HasMissingArtwork))]
    [NotifyPropertyChangedFor(nameof(CanFetchArtwork))]
    private string clearLogoPath = string.Empty;

    public bool HasBox => !string.IsNullOrWhiteSpace(BoxPath);
    public bool HasScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPath);
    public bool HasClearLogo => !string.IsNullOrWhiteSpace(ClearLogoPath);

    // Detay panelindeki tek Download/Search butonlarının görünürlüğü (kullanıcı isteği:
    // "bütün resimler varsa gösterme indirme ve arama butonunu") — üçünden BİRİ bile eksikse true.
    public bool HasMissingArtwork => !HasBox || !HasClearLogo || !HasScreenshot;

    // Kullanıcı geri bildirimi: "tabloda artwork butonuna tıklayıp indirme yaptıktan sonra artwork
    // butonu hala sanki resim indirmemişim gibi gözüküyor canlı güncellemiyor" — tablodaki (grid
    // Actions sütunu) "Görsel Getir" düğmesi eskiden sadece HasArtworkSource'a bağlıydı, indirilip
    // indirilmediğini hiç yansıtmıyordu. Detay panelindeki AYNI düğme (HasMissingArtwork) zaten
    // tüm görseller tamamsa gizleniyordu — burada satır düzeni bozulmasın diye gizlemek yerine
    // devre dışı bırakılıp soluklaşıyor (bkz. MainWindow.xaml grid Actions sütunu).
    public bool CanFetchArtwork => HasArtworkSource && HasMissingArtwork;

    // Detay panelinde gösterilecek gerçek yol — Has* bayrakları (yukarıda) gerçek görselin var
    // olup olmadığını (grid noktaları, "Görsel Getir" gibi yerlerde) yansıtmaya devam eder, bu
    // alanlar SADECE görüntüleme amaçlı: görsel yoksa Images/NoImage altındaki sabit yer
    // tutucuya düşer (bkz. AppPaths.NoImageCover/NoImageBackground).
    public string BoxDisplayPath => HasBox ? BoxPath : AppPaths.NoImageCover;
    public string ScreenshotDisplayPath => HasScreenshot ? ScreenshotPath : AppPaths.NoImageBackground;

    // Kullanıcı isteği (düzeltildi — "clearlogosunu indirdim var ama background.png'yi
    // gösteriyor... karıştırdın heralde"): Clear Logo VARSA gameplay olsun ya da olmasın HER ZAMAN
    // gerçek Clear Logo gösterilir. Yoksa: gameplay varsa Logo.png, gameplay de yoksa alan
    // tamamen gizlenir (bkz. ShowClearLogoArea).
    public bool ShowClearLogoArea => HasScreenshot || HasClearLogo;

    public string ClearLogoDisplayPath => HasClearLogo
        ? ClearLogoPath
        : (HasScreenshot ? AppPaths.NoImageLogo : AppPaths.NoImageBackground);

    // Tablodaki (DataGrid) Logo sütunu için — detay panelindeki ClearLogoDisplayPath'in aksine
    // gameplay'e bağlı değil (kullanıcı isteği: "tabloda clear logosu olmayanlarda da bizim
    // logo.png gözükecek"): sadece gerçek logo var mı yok mu, yoksa her zaman Logo.png.
    public string ClearLogoThumbnailPath => HasClearLogo ? ClearLogoPath : AppPaths.NoImageLogo;

    // Crop Editor'da kırpıp AYNI dosya yoluna kaydettikten sonra çağrılır (bkz. MainWindow.xaml.cs
    // BoxArt/ClearLogo click handler'ları, CropEditorViewModel.Saved) — path'in KENDİSİ
    // değişmediği için [ObservableProperty]'nin ürettiği setter hiçbir şey yapmaz (eşit değer,
    // PropertyChanged tetiklenmez); ThumbnailImageConverter.Invalidate zaten çağrıldığı için burada
    // sadece WPF'e "yeniden oku" demek yeterli.
    public void RefreshImageDisplayPaths()
    {
        OnPropertyChanged(nameof(BoxDisplayPath));
        OnPropertyChanged(nameof(ScreenshotDisplayPath));
        OnPropertyChanged(nameof(ClearLogoDisplayPath));
        OnPropertyChanged(nameof(ClearLogoThumbnailPath));
    }

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

    // Kullanıcı isteği: "manuel eklenenlerde oyuncu sayısını otomatik 1 yazıyor" — MaxPlayers==0
    // zaten "bilinmiyor" anlamına geliyordu (bkz. MainViewModel'in MaxPlayersFilter'ı, 0'ı boş
    // string'e çeviriyor), ama DataGrid sütunu doğrudan MaxPlayers'a bağlıydı ve 0'ı olduğu gibi
    // "0" yazardı — bu yüzden Publisher/ReleaseYear'daki AYNI "boş kalsın" deseni burada da: 0 ise
    // boş, değilse sayı (bkz. Views/MainWindow.xaml MaxPlayersColumn, RetroAudit.Models.Game
    // BuildCustomGame'de custom oyunlar için MaxPlayers=0 set edilir).
    public string MaxPlayersDisplay => MaxPlayers > 0 ? MaxPlayers.ToString() : string.Empty;

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

    // LaunchBox'tan gelen VideoUrl genelde standart bir "watch?v=" linki ama youtu.be/embed/
    // shorts formatları da görülüyor — hepsinden 11 karakterlik video ID'sini çıkarır. Gameplay
    // alanındaki embedded YouTube player (bkz. MainWindow.xaml Grid.Row="4", MainWindow.xaml.cs
    // PlayYouTubeEmbedAsync) bunu kullanır; ID çıkarılamazsa (bilinmeyen/bozuk bir URL) embed
    // hiç denenmez, buton pasif kalır.
    private static readonly System.Text.RegularExpressions.Regex YouTubeIdPattern =
        new(@"(?:v=|youtu\.be/|embed/|shorts/)([A-Za-z0-9_-]{11})", System.Text.RegularExpressions.RegexOptions.Compiled);

    public string? YouTubeVideoId
    {
        get
        {
            if (string.IsNullOrWhiteSpace(VideoUrl))
                return null;

            var match = YouTubeIdPattern.Match(VideoUrl);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    public bool HasYouTubeEmbed => YouTubeVideoId is not null;
    public bool HasWikipediaUrl => !string.IsNullOrWhiteSpace(WikipediaUrl);
    public bool HasCommunityRating => CommunityRating.HasValue;
    public bool HasSteamAppId => SteamAppId.HasValue;
    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);
    public bool HasReleaseYear => ReleaseYear > 0;

    // Kullanıcı isteği: "yayıncı ve yıl boşta olsa gözüksün hepsinde... bilinmiyor veya boşsa
    // Unknown yılda - yazsın şablon sabit" — satır artık HİÇ gizlenmiyor, boşken bu yer tutucular
    // gösteriliyor (Steam AppID gibi diğer koşullu satırların aksine, burada "şablon sabit" kalsın
    // diye tercih böyle).
    public string PublisherDisplay => HasPublisher ? Publisher : "Unknown";
    public string ReleaseYearDisplay => HasReleaseYear ? ReleaseYear.ToString() : "-";

    // Detay panelindeki Tür rozeti için: Genres tek bir virgülle-ayrılmış string (bkz. yukarıdaki
    // Genres alanı), rozet + açılır menü bunun ayrıştırılmış tek tek parçalarını kullanır.
    public string[] GenreTokens => string.IsNullOrWhiteSpace(Genres)
        ? Array.Empty<string>()
        : Genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public bool HasGenres => GenreTokens.Length > 0;
    public bool HasMultipleGenres => GenreTokens.Length > 1;
    public string PrimaryGenre => GenreTokens.Length > 0 ? GenreTokens[0] : string.Empty;

    // Oyun modu rozeti: LaunchBox'ın kesin bir "oyun modu" alanı yok (bkz. Cooperative alanı ve
    // CatalogDatabaseService.GetGames'teki yorum) — sadece MaxPlayers + Cooperative (ikili:
    // kooperatif var/yok) alanlarından türetilebiliyor. Bu yüzden sadece Single/Co-op/Multiplayer
    // ayrımı yapılabiliyor; Versus/Alternating için hiç veri yok, o yüzden bilinçli olarak
    // üretilmiyor (yanlış bilgi vermektense üç kategoriyle sınırlı kalmak tercih edildi).
    public string GameModeIcon => MaxPlayers <= 1 ? "👤" : (Cooperative == true ? "🤝" : "👥");
    public string GameModeLabel => MaxPlayers <= 1 ? "Single" : (Cooperative == true ? "Co-op" : "Multiplayer");

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

    // Başlangıçta tercih edilen (preferred) sürümün bölgesi ve kaynağı — DataGrid'de sütun/filtre
    // olarak gösterilebilsin diye Game seviyesine taşındı. Toolbar'daki USA/EU/Japan bayrak
    // filtresi işaretli region'lara göre bunları YENİDEN hesaplayıp değiştirebilir (bkz.
    // MainViewModel.RecomputeRegionDisplay) — ör. sadece Japan işaretliyken ve oyunun Japan
    // sürümü varsa, Region/SourceDat/File o sürümün bilgisine döner. Sürüm etiketi (Rev A, Beta
    // vb.) bilinçli olarak burada yok — sağ paneldeki Sürümler listesinde (GameVersion.
    // VersionLabel) zaten her sürüm için ayrı ayrı gösteriliyor.
    public string Region { get; set; } = string.Empty;
    public string SourceDat { get; set; } = string.Empty;

    // Bu oyunun TÜM sürümlerinin (Region/SourceDat/FileName) özet listesi — bkz.
    // CatalogDatabaseService.GetGames, sadece toolbar'daki USA/EU/Japan bayrak filtresinin
    // görünürlük/görüntüleme kararı için kullanılıyor (RecomputeRegionDisplay). Detay panelindeki
    // tam Sürümler listesi (hash/CRC dahil) hâlâ ayrıca, talep üzerine sorgulanıyor.
    public List<GameVersionSummary> AllVersions { get; set; } = new();

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

    // Kullanıcı isteği: "manuel bağlanan kayıtlar detay panelinde ve ana tabloda açıkça 'Manual
    // Link' rozetiyle gösterilsin" — ROM İçe Aktar'ın Eşleşmeyenler sekmesinden ELLE bağlanmış
    // (bkz. ManualLinkViewModel, MatchMethods.ManualLink) bir dosyaya sahipse true. Normal
    // otomatik eşleşmelerden (Tier 1-5) veya "Şu anki yoldan kullan"dan AYRI — CRC doğrulanmış
    // bir eşleşme gibi SUNULMAMASI gerektiği için ayrı bir bayrak (bkz. MainViewModel.
    // ApplyManualLinkInfo, HasLocalFile ile aynı yükleme noktalarında dolduruluyor).
    public bool IsManuallyLinked { get; set; }

    // Doluysa manuel bağlantı BELİRLİ bir sürüme yapılmış (bkz. GameVersion.RawDatName) — boşsa
    // Game seviyesinde genel bir bağlantı.
    public string ManualLinkTargetVersionLabel { get; set; } = string.Empty;
    public bool HasManualLinkTargetVersion => !string.IsNullOrWhiteSpace(ManualLinkTargetVersionLabel);
}
