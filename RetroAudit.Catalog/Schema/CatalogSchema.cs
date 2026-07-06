namespace RetroAudit.Catalog.Schema;

// RetroAudit.db'nin normalize edilmiş şeması. Games tablosu sadece "parent" (ana listede
// gösterilecek) satırları tutar; her oyunun tüm bölge/sürüm/hash varyasyonu GameVersions ve
// GameHashes altında saklanır. UserLibrary Builder tarafından boş oluşturulur — ROM içe aktarma
// akışı (Stage B, çalışma zamanı) buraya yazar.
public static class CatalogSchema
{
    public const string CreateTablesSql = """
        -- Category: WPF sol panelindeki taksonomiye (CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC/
        -- OTHERS) karşılık gelir (bkz. PlatformCategoryMap). Sadece bir sunum/UI bilgisidir —
        -- Builder bu yüzden hiçbir platformu atmaz, haritada olmayanlar OTHERS'a düşer.
        CREATE TABLE Platforms (
            PlatformId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Category TEXT NOT NULL DEFAULT 'OTHERS'
        );

        CREATE TABLE Developers (
            DeveloperId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE Publishers (
            PublisherId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE Genres (
            GenreId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE Regions (
            RegionId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE Games (
            GameId INTEGER PRIMARY KEY AUTOINCREMENT,
            PlatformId INTEGER NOT NULL REFERENCES Platforms(PlatformId),
            Title TEXT NOT NULL,
            CompareTitle TEXT NOT NULL,
            DeveloperId INTEGER REFERENCES Developers(DeveloperId),
            PublisherId INTEGER REFERENCES Publishers(PublisherId),
            ReleaseYear INTEGER,
            Overview TEXT,
            MaxPlayers INTEGER,
            -- LaunchBox'tan gelen ek zenginleştirme alanları (bkz. LaunchBoxMetadataReader,
            -- CatalogGame). ReleaseDate, ReleaseYear'dan daha kesin (gün/ay dahil) ama LaunchBox'ta
            -- bazı kayıtlarda hiç yok — bu yüzden ikisi de ayrı ayrı saklanıyor.
            ReleaseDate TEXT,
            CommunityRating REAL,
            CommunityRatingCount INTEGER,
            VideoUrl TEXT,
            WikipediaUrl TEXT,
            SteamAppId INTEGER,
            Cooperative INTEGER,
            PreferredVersionId INTEGER REFERENCES GameVersions(GameVersionId),
            -- Bu oyun LaunchBox.Metadata.db'de gerçekten bulundu mu? Developer/Publisher/Overview/
            -- ReleaseYear/MaxPlayers'ın hepsi LaunchBox'ta da boş olabileceği için (nadir ama mümkün),
            -- eşleşme durumunu bu alanların doluluğundan çıkarmak yerine ayrı bir bayrakla saklıyoruz.
            MatchedMetadata INTEGER NOT NULL DEFAULT 0,
            -- Eşleşme nasıl bulundu: "CompareName" / "ExactName" / "AlternateName" (hepsi kesin,
            -- Confidence=1.0) veya "Fuzzy" (Confidence<1.0, benzerlik oranı). NeedsReview=1 olan
            -- kayıtların metadata'sı kullanılabilir ama kullanıcı onaylayana kadar şüpheli sayılmalı.
            MatchMethod TEXT,
            MatchConfidence REAL,
            NeedsReview INTEGER NOT NULL DEFAULT 0,
            -- LaunchBox türü Casino/Gambling/Mahjong/Pachinko/Pachislot/Quiz/Board Game/Tabletop/
            -- Card Game/Educational gibi bir kategoriye işaret ediyorsa 1. Satır silinmez (veri
            -- kaybı yok), sadece WPF'in varsayılan ana listesi bu satırları gizler.
            HiddenByDefault INTEGER NOT NULL DEFAULT 0,
            -- Zenginleştirme kaynağındaki bu oyunun sayısal kimliği (bkz. ArtworkAssets) — eşleşme
            -- yoksa NULL.
            MetadataSourceId INTEGER
        );
        CREATE INDEX IX_Games_Platform_CompareTitle ON Games(PlatformId, CompareTitle);

        CREATE TABLE GameGenres (
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            GenreId INTEGER NOT NULL REFERENCES Genres(GenreId),
            PRIMARY KEY (GameId, GenreId)
        );

        -- Bu oyun için önceden çözülmüş (bölge önceliğine göre tek satıra indirgenmiş, bkz.
        -- LaunchBoxMetadataReader.GetArtwork) görsel varlık dosya adları. WPF tarafı bunu okuyup
        -- FileName'i bir CDN URL'sine çevirip isteğe bağlı olarak indiriyor — RetroAudit.db sadece
        -- "hangi görsel mevcut" bilgisini taşır, gerçek görsel verisini asla içermez.
        CREATE TABLE ArtworkAssets (
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            Type TEXT NOT NULL, -- 'Box' | 'Screenshot' | 'ClearLogo' (Fanart/Background kaldırıldı)
            FileName TEXT NOT NULL,
            PRIMARY KEY (GameId, Type)
        );

        CREATE TABLE AlternateNames (
            AlternateNameId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            Name TEXT NOT NULL,
            Region TEXT,
            Source TEXT
        );

        -- Tek bir DAT kaydı (ör. "Super Mario World (Europe) (Rev A)"). AllRegionsRaw, name
        -- etiketinde birden fazla bölge geçtiğinde ("(USA, Europe)") tam listeyi korur; RegionId
        -- sorgu/badge kolaylığı için sadece birincil (ilk) bölgeye işaret eder. Beta/Proto/Demo/
        -- Pirate/Cracked/BIOS/Utility/... gibi resmi olmayan kayıtlar bu tabloya hiç girmez
        -- (DatNameParser.ShouldExclude ile daha gruplanmadan elenir) — bu yüzden ayrı bir Flags
        -- sütununa gerek kalmadı, hayatta kalan her satır tanım gereği resmi bir sürümdür.
        CREATE TABLE GameVersions (
            GameVersionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            RegionId INTEGER REFERENCES Regions(RegionId),
            AllRegionsRaw TEXT,
            VersionLabel TEXT,
            IsPreferred INTEGER NOT NULL DEFAULT 0,
            RawDatName TEXT NOT NULL,
            SourceDat TEXT NOT NULL
        );
        CREATE INDEX IX_GameVersions_GameId ON GameVersions(GameId);

        CREATE TABLE GameHashes (
            GameHashId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameVersionId INTEGER NOT NULL REFERENCES GameVersions(GameVersionId),
            FileName TEXT NOT NULL,
            Size INTEGER NOT NULL,
            Crc32 TEXT,
            Md5 TEXT,
            Sha1 TEXT
        );
        CREATE INDEX IX_GameHashes_GameVersionId ON GameHashes(GameVersionId);
        CREATE INDEX IX_GameHashes_Crc32 ON GameHashes(Crc32);

        CREATE TABLE MetadataSources (
            MetadataSourceId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Version TEXT,
            ImportedAt TEXT NOT NULL
        );

        -- Çalışma zamanında (Stage B) ROM içe aktarıldığında doldurulur: hash eşleşen GameVersion
        -- "owned/verified" işaretlenir. Builder bu tabloyu boş oluşturur, hiç veri yazmaz.
        CREATE TABLE UserLibrary (
            OwnedRomId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameVersionId INTEGER NOT NULL REFERENCES GameVersions(GameVersionId),
            FilePath TEXT NOT NULL,
            VerifiedAt TEXT,
            VerificationStatus TEXT
        );

        -- Bu RetroAudit.db'nin hangi Builder/şema sürümüyle, ne zaman, hangi kaynaklardan
        -- üretildiğini kaydeden tek satırlık bir denetim tablosu. WPF tarafı (Stage B) açılışta
        -- SchemaVersion'ı kontrol ederek uyumsuz bir veritabanını erkenden reddedebilir.
        CREATE TABLE BuildInfo (
            SchemaVersion TEXT NOT NULL,
            CatalogVersion TEXT NOT NULL,
            BuildDate TEXT NOT NULL,
            BuilderVersion TEXT NOT NULL,
            SourceSummary TEXT NOT NULL
        );
        """;
}
