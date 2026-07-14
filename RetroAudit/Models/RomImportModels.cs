using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroAudit.Models;

// Toplu ROM içe aktarma penceresinde taranan kaynak klasördeki bir dosyanın, katalogdaki bir
// Game ile eşleşmesi (bkz. RomImportService.ScanFolder). ObservableObject: IsSelected (satır
// bazlı onay kutusu) ve IsImported (içe aktarma sırasında dolan mavi tik) canlı olarak DataGrid'e
// yansısın diye.
public partial class RomMatch : ObservableObject
{
    public required Game Game { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }

    // Hangi eşleştirme katmanı bu satırı bulduğu (bkz. RomImportService.ResolveCandidate) —
    // kullanıcı isteği: "eşleşme türünü ... sakla veya logla; CRC doğrulanmış gibi gösterme". Ör.
    // "Dosya Adı (Tam Eşleşme)", "İçerik (CRC32)", "Başlık + Bölge/Revizyon (farklı dump)",
    // "Normalize Edilmiş Başlık (Normalized Title Match)" — sonuncusu en gevşek eşleşme, dosyanın
    // GERÇEK CRC'si kataloğunkinden farklı olabilir (VerifyHashCommand ayrıca doğrulayabilir).
    public required string MatchMethod { get; init; }

    [ObservableProperty]
    private bool isSelected = true;

    // Null ise SourcePath doğrudan ROM dosyasıdır. Doluysa SourcePath bir .zip arşividir ve asıl
    // eşleşen ROM bu isimdeki bir arşiv girdisidir (birçok kişisel arşiv, her oyunu ayrı bir
    // .zip içinde tutar — bkz. RomImportService.ScanFolder).
    public string? ZipEntryName { get; init; }

    // İçe aktarma turu bu satırı başarıyla işledikten sonra true olur — RomImportWindow'daki
    // "Durum" sütunundaki mavi tik bu alana bağlı.
    [ObservableProperty]
    private bool isImported;

    // Sadece "hash ile doğrula" seçiliyse RomImportViewModel tarafından doldurulur; null =
    // doğrulama hiç çalıştırılmadı, true/false = çalıştı ve sonucu.
    [ObservableProperty]
    private bool? hashVerified;

    // Kullanıcı isteği: "tarama esnasında klasörde ne varsa gösterse daha anlaşılır olmazmı ...
    // daha önceden eşleştirilen varsa eşleştirilenlerde gösterebilir" — bu dosyanın yolu ZATEN bir
    // oyuna bağlı (bkz. RomImportService.ScanFolder alreadyLinkedPaths) — satır GİZLENMİYOR, sadece
    // işaretlenip (bkz. RomImportWindow "Zaten Bağlı" rozeti) varsayılan olarak işaretsiz geliyor,
    // tekrar aynı işlemi yanlışlıkla tekrarlamasın diye.
    public bool IsAlreadyLinked { get; init; }
}

// Kullanıcının bir içe aktarma turunda seçtiği toplu (satır bazlı değil) dosya işlemi modu.
public enum RomImportMode
{
    Move,
    Copy,
    ReferenceInPlace,
}

// Taranan ama katalogdaki hiçbir oyunla eşleştirilemeyen bir dosya/zip girdisi (bkz.
// RomImportService.ScanFolder) — kullanıcı isteği: "önce eklenmeyenleri tespit etmek lazım", sessizce
// atlamak yerine SEBEBİYLE birlikte ayrı bir listede gösterilir (bkz. RomImportWindow "Eşleşmeyenler"
// sekmesi). ObservableObject: IsSelected — kullanıcı isteği: "tıklamalı seçenekli yap ... kullanıcı
// isteğine göre silebilsin ... adam belki betaları tutmak isticek diğerlerini silmek isticek" —
// hiçbir satır TARAMADA otomatik işaretli GELMİYOR (silme bkz. RomImportService.DeleteSelectedFiles
// SADECE işaretli satırları hedefler); kullanıcı RomImportWindow'daki kategori butonlarıyla (bkz.
// RomImportViewModel.ExcludedTagGroups, SelectByTagCommand) "Unl", "Proto" gibi istediği etiketleri
// tek tek işaretleyip Beta'yı dışarıda bırakabilir, ya da satırları elle tek tek işaretleyebilir.
public partial class UnmatchedRomFile : ObservableObject
{
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }
    public required string Reason { get; init; }

    // RomMatch.ZipEntryName ile AYNI anlam — null ise SourcePath doğrudan dosyadır, doluysa
    // SourcePath bir .zip'tir ve FileName o zip'in İÇİNDEKİ girdinin adıdır (bkz.
    // RomImportService.ComputeCrc32 — CRC32'yi doğru akıştan okuyabilmek için gerekli).
    public string? ZipEntryName { get; init; }

    // Doluysa bu dosya DatNameParser.TryGetExcludedTag'e göre bu SPESİFİK etikete (ör. "Beta",
    // "Unl", "Proto") sahip — RetroAudit'in katalogu ASLA içermeyecek (kasıtlı tasarım). Null ise
    // "gerçekten katalogda yok" (ör. tanınmayan dump) — bu satırlar ileride farklı bir DAT
    // kaynağıyla eşleşebileceğinden kategori butonlarının hedefi DEĞİL.
    public required string? ExcludedTag { get; init; }

    public bool IsIntentionallyExcluded => ExcludedTag is not null;

    [ObservableProperty]
    private bool isSelected;
}

// CatalogDatabaseService.GetAllVersionDetailsByGame'in tek bir GameVersion'ı — RomImportService'in
// ÜÇÜNCÜ eşleştirme katmanı (bkz. RomImportService.ResolveCandidate "Tier 3") için: dosya adı VE
// CRC32 tutmasa bile, dosyanın adından ayrıştırılan (DatNameParser.Parse) temiz başlık+bölge+
// revizyon etiketi bu sürümlerden BİRİNE denk geliyorsa, o sürümün FileNames'inden biri hedef
// dosya adı olarak kullanılır (Game.File DEĞİL — aksi halde "Sürümler" panelindeki IsVersionOwned
// yanlış sürümü sahiplenilmiş gösterirdi, bkz. MainViewModel.ResolveVersionFilePath). RawDatName —
// gerçek veriyle doğrulandı: aynı bölge+revizyona sahip birden fazla sürüm sık sık "Virtual
// Console"/"GameCube Edition" gibi yeniden-yayın etiketleriyle ayrılıyor (bkz. ResolveCandidate
// "Tier 3 tie-break") — bu etiket sadece RawDatName'de var, region/label alanlarında yok.
public sealed record CatalogVersionRecord(string? AllRegionsRaw, string? VersionLabel, string RawDatName, List<string> FileNames);

// RomImportService.ScanFolder'ın tam sonucu — hem eşleşenler hem (sebebiyle birlikte) eşleşmeyenler.
public sealed record RomScanResult(List<RomMatch> Matches, List<UnmatchedRomFile> Unmatched);

// RomImportService.DeleteSelectedFiles'ın özeti — RomImportViewModel bunu kullanıcıya tek bir
// mesajda özetliyor (kaç dosya silindi, kaçı çok girdili zip olduğu için atlandı, kaçı başarısız oldu).
public sealed record RomDeleteResult(int Deleted, int SkippedMultiEntryZip, int Failed);
