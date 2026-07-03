namespace RetroAudit.Catalog.Schema;

// RetroAudit.db'nin normalize edilmiş şeması. Games tablosu sadece "parent" (ana listede
// gösterilecek) satırları tutar; her oyunun tüm bölge/sürüm/hash varyasyonu GameVersions ve
// GameHashes altında saklanır. UserLibrary Builder tarafından boş oluşturulur — ROM içe aktarma
// akışı (Stage B, çalışma zamanı) buraya yazar.
public static class CatalogSchema
{
    public const string CreateTablesSql = """
        CREATE TABLE Platforms (
            PlatformId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE
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
            PreferredVersionId INTEGER REFERENCES GameVersions(GameVersionId)
        );
        CREATE INDEX IX_Games_Platform_CompareTitle ON Games(PlatformId, CompareTitle);

        CREATE TABLE GameGenres (
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            GenreId INTEGER NOT NULL REFERENCES Genres(GenreId),
            PRIMARY KEY (GameId, GenreId)
        );

        CREATE TABLE AlternateNames (
            AlternateNameId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            Name TEXT NOT NULL,
            Source TEXT
        );

        -- Tek bir DAT kaydı (ör. "Super Mario World (Europe) (Rev A)"). AllRegionsRaw, name
        -- etiketinde birden fazla bölge geçtiğinde ("(USA, Europe)") tam listeyi korur; RegionId
        -- sorgu/badge kolaylığı için sadece birincil (ilk) bölgeye işaret eder.
        CREATE TABLE GameVersions (
            GameVersionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId INTEGER NOT NULL REFERENCES Games(GameId),
            RegionId INTEGER REFERENCES Regions(RegionId),
            AllRegionsRaw TEXT,
            VersionLabel TEXT,
            Flags TEXT,
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
        """;
}
