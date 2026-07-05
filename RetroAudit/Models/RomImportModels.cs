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
}

// Kullanıcının bir içe aktarma turunda seçtiği toplu (satır bazlı değil) dosya işlemi modu.
public enum RomImportMode
{
    Move,
    Copy,
    ReferenceInPlace,
}
