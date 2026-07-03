using RetroAudit.Catalog.Dat;

namespace RetroAudit.Catalog.Grouping;

// DAT'tan gelen, henüz ayrıştırılmış tek bir sürüm kaydı (ör. "Super Mario World (Europe) (Rev A)").
// GameHashes tablosuna 1-e-çok giden Roms listesi burada tutulur (ör. başlıklı/başlıksız ikilisi).
// Beta/Proto/Demo/Pirate/Cracked/BIOS/Utility/... gibi resmi olmayan kayıtlar buraya hiç ulaşmaz —
// DatNameParser.ShouldExclude bunları VersionResolver'da daha grupianmadan eler.
public class CatalogGameVersion
{
    public string RawDatName { get; set; } = string.Empty;
    // Bu sürümün DatNameParser'dan çıkan kendi temiz başlığı — bölgeler arası yazım farkları
    // (noktalama, büyük/küçük harf, alt başlık) olabileceği için CatalogGame.Title, gruptaki
    // ilk görülen kayıttan değil, tercih edilen sürümün CleanTitle'ından atanır.
    public string CleanTitle { get; set; } = string.Empty;
    public string SourceDat { get; set; } = string.Empty; // "no-intro", "redump", "tosec", ...
    public string[] Regions { get; set; } = Array.Empty<string>();
    public string? VersionLabel { get; set; }
    public bool IsPreferred { get; set; }
    public List<DatRomEntry> Roms { get; set; } = new();
}

// Ana listede gösterilecek "parent" oyun satırı — (Platform, temiz başlık) çiftine göre gruplanır.
// Tüm ham DAT kayıtları Versions altında saklanır; sadece bu satır Games tablosuna yazılır.
public class CatalogGame
{
    public string PlatformName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CompareTitle { get; set; } = string.Empty;
    public List<CatalogGameVersion> Versions { get; } = new();

    public CatalogGameVersion? Preferred => Versions.FirstOrDefault(v => v.IsPreferred);

    // LaunchBox.Metadata.db'den doldurulan alanlar (Builder'ın ikinci aşaması).
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Overview { get; set; }
    public int? MaxPlayers { get; set; }
    public List<string> Genres { get; } = new();
    public bool MatchedMetadata { get; set; }

    // Eşleşmenin nasıl bulunduğu (CompareName/ExactName/AlternateName/Fuzzy) ve ne kadar güvenilir
    // olduğu (1.0 = kesin, <1.0 = fuzzy benzerlik oranı). NeedsReview, Confidence
    // FuzzyAcceptThreshold'un altında kaldığında (ama FuzzyReviewFloor'un üstündeyse) true olur —
    // bu oyunun metadata'sı kullanılabilir ama kullanıcı gözden geçirene kadar şüpheli sayılmalı.
    public string? MatchMethod { get; set; }
    public double? MatchConfidence { get; set; }
    public bool NeedsReview { get; set; }

    // LaunchBox'tan gelen tür (Genre) bilgisi Casino/Gambling/Mahjong/Pachinko/Pachislot/Quiz/
    // Board Game/Tabletop/Card Game/Educational gibi "gerçek video oyunu" sayılmayan bir türe
    // işaret ediyorsa true olur. Kullanıcı kararıyla bu oyunlar silinmiyor (veri kaybı yok),
    // sadece varsayılan ana listede gizlenmek üzere işaretleniyor — kullanıcı isterse UI'dan
    // açabilir.
    public bool HiddenByDefault { get; set; }
}
