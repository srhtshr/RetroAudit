using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroAudit.Catalog.Naming;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Kullanıcı isteği: "sayıları yazarsa daha iyi olur" — kategori butonlarında (bkz.
// RomImportViewModel.ExcludedTagGroups) her etiketin kaç dosyayı etkileyeceği açıkça görünsün diye
// (ör. "Beta (117)"), sadece etiket adı değil sayısı da tutuluyor. IsActive — kullanıcı isteği:
// "seçili olan butonlar belli olsun" — bu etikete sahip TÜM satırlar şu an işaretliyse true (bkz.
// RomImportViewModel.OnUnmatchedItemPropertyChanged, satır bazlı elle işaretleme/kaldırma dahil
// HER değişiklikte yeniden hesaplanır, sadece buton tıklamasında değil).
public sealed partial class ExcludedTagGroup : ObservableObject
{
    public required string Tag { get; init; }

    // Kullanıcı bildirdi: "test 1 i sildim buton takılı duruyor" — silme sonrası kalan dosya
    // sayısı DeleteSelectedUnmatchedConfirmedAsync tarafından burada güncelleniyor (count init
    // değil, mutable), 0'a düşerse buton tamamen kaldırılıyor (bkz. ExcludedTagGroups).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private int count;

    public string Label => $"{Tag} ({Count})";

    [ObservableProperty]
    private bool isActive;
}

// Kullanıcının kendi ROM arşivinden toplu içe aktarma penceresinin ViewModel'i. Taşıma/kopyalama
// gibi I/O ağır işlemler Task.Run üzerinde çalışır (bu kod tabanındaki diğer her şey senkron —
// burası bilinçli bir istisna, çünkü gerçek bir arşivde GB'larca veri taşınabilir ve bu, UI'yi
// donduramaz; bkz. bu oturumdaki önceki dondurma/performans düzeltmeleri).
public partial class RomImportViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _allGames;
    private readonly string _retroAuditDataPath;
    private readonly Action _onImportCompleted;

    // MainViewModel.RegisterNewCustomGame'e (bkz. orada) doğrudan bağımlı olmamak için bir Func
    // olarak enjekte ediliyor — ManualLinkWindow'daki "+ Yeni Oyun" seçeneğiyle AYNI mekanizma
    // (bkz. ImportSelectedUnmatchedAsNewGames), sadece pencere açmadan TOPLU çalışıyor.
    private readonly Func<string, string, Game> _registerNewCustomGame;

    [ObservableProperty]
    private string sourceFolder = string.Empty;

    [ObservableProperty]
    private bool isScanning;

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(CanScan));

    [ObservableProperty]
    private bool isImporting;

    partial void OnIsImportingChanged(bool value) => OnPropertyChanged(nameof(CanImport));

    public bool CanScan => !IsScanning;
    public bool CanImport => !IsImporting;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private bool verifyHashOnImport;

    // Kullanıcı isteği: "eşleşmeyenler sekmesine geçmeden seçim yapabilecek ... içe aktarın
    // yanında, seçiliyse eşleşmeyenleri aktaracak değilse aktarmıcak" — Eşleşmeyenler'e hiç
    // geçmeden, TEK bir İçe Aktar tıklamasıyla gerçekten katalogda karşılığı olmayan dosyalar da
    // (bkz. ImportAsync, UnmatchedRomFile.IsIntentionallyExcluded) kendi adlarıyla bağımsız birer
    // oyun olarak eklensin diye — varsayılan işaretli (kullanıcı isteği: "tik işaretli gelsin").
    [ObservableProperty]
    private bool importUnmatchedAsNewGames = true;

    [ObservableProperty]
    private RomImportMode selectedMode = RomImportMode.ReferenceInPlace;

    public ObservableCollection<RomMatch> Matches { get; } = new();

    // Kullanıcı isteği: "önce eklenmeyenleri tespit etmek lazım" — taranan ama katalogdaki hiçbir
    // oyunla eşleştirilemeyen dosyalar artık sessizce atlanmıyor, sebebiyle birlikte burada listeleniyor
    // (bkz. RomImportService.ScanFolder, RomImportWindow "Eşleşmeyenler" sekmesi).
    public ObservableCollection<UnmatchedRomFile> Unmatched { get; } = new();

    // Kullanıcı isteği: "solda seçilenleri sil in olduğu kısımda onları seçebileceğimiz bişey yok
    // tıklayınca betaları işaretlesin mesela ... adam belki betaları tutmak isticek diğerlerini
    // silmek isticek" — taramadan sonra Unmatched'te GERÇEKTEN görülen etiketlerden (bkz.
    // UnmatchedRomFile.ExcludedTag) türetilen, veri odaklı bir hızlı-seçim buton listesi: "Beta",
    // "Unl", "Proto" gibi. Sabit/hardcoded bir kategori listesi DEĞİL — o taramada hiç "Hack"
    // yoksa "Hack" butonu da çıkmaz. "Gerçekten katalogda yok" (ExcludedTag=null) satırlar için
    // buton YOK, kullanıcı onları elle işaretlemek zorunda (ileride farklı bir DAT'la eşleşebilirler).
    public ObservableCollection<ExcludedTagGroup> ExcludedTagGroups { get; } = new();

    public int SelectedCount => Matches.Count(m => m.IsSelected);

    // Kullanıcı isteği: "seçilenlerin altında sayıları yazarsa daha iyi olur" — Sil butonunun
    // altında canlı olarak güncellenen "N dosya seçili" metni için (bkz. RomImportWindow.xaml).
    // Matches.SelectedCount'un aksine bu CANLI: her satırın IsSelected'ı (tek tek tıklamayla veya
    // kategori butonlarıyla) değiştiğinde OnUnmatchedItemPropertyChanged üzerinden anında güncellenir.
    // Kullanıcı isteği: "Seçimi Temizle yide Seçim yoksa hepsini seç olsun ... seçim varsa Seçimi
    // temizle olsun" — HasUnmatchedSelection/ToggleSelectAllUnmatchedLabel bu tek butonun (bkz.
    // ToggleSelectAllUnmatched) durumuna göre değişmesi için.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnmatchedSelection))]
    [NotifyPropertyChangedFor(nameof(ToggleSelectAllUnmatchedLabel))]
    private int selectedUnmatchedCount;

    public bool HasUnmatchedSelection => SelectedUnmatchedCount > 0;
    public string ToggleSelectAllUnmatchedLabel => HasUnmatchedSelection ? "Seçimi Temizle" : "Hepsini Seç";

    // Kullanıcı isteği: "filtreleme butonu ekle bide seçili olanları sadece göstermek için toggle
    // olsun" — MainWindow'daki "Released"/"Junk" ToggleButton filtreleriyle AYNI desen. Gerçek
    // filtreleme CollectionViewSource üzerinden (bkz. RomImportWindow.xaml.cs) — bu sadece
    // ToggleButton'ın IsChecked'ine bağlanan düz bir bayrak, view'ı ne zaman yenileyeceğini
    // RequestRefreshUnmatchedView event'iyle koda bildiriyor (satır bazlı IsSelected değişimlerinde
    // de, bkz. OnUnmatchedItemPropertyChanged — filtre açıkken elle işaretlenen/kaldırılan bir satır
    // anında görünümden düşsün/eklensin diye).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnlySelectedUnmatchedLabel))]
    private bool showOnlySelectedUnmatched;

    partial void OnShowOnlySelectedUnmatchedChanged(bool value) => RequestRefreshUnmatchedView?.Invoke();

    // Kullanıcı isteği: "Sadece Seçilenler Değilde Filtrele ve Hepsi olarak değişsin" —
    // ToggleSelectAllUnmatchedLabel ile AYNI iki-durumlu buton metni deseni.
    public string ShowOnlySelectedUnmatchedLabel => ShowOnlySelectedUnmatched ? "Hepsi" : "Filtrele";

    public event Action? RequestRefreshUnmatchedView;

    public event Action<string>? RequestShowMessage;

    // Kullanıcı isteği: "tıklamalı seçenekli yap onları seçili olanları silsin" — silme komutu
    // doğrudan çalışmaz, önce kaç dosyanın silineceğini gösteren bir onay istenir (bkz.
    // RomImportWindow.xaml.cs — MainWindow'daki "Kalıcı Sil" onayıyla AYNI desen).
    public event Action<int>? RequestDeleteSelectedUnmatchedConfirmation;

    // Kullanıcı isteği: "kataloğunda olmayabilir dat eksik olabilir ... eşleşmese bile açabilmeli
    // bi yerden" — Eşleşmeyenler'deki bir satırı ELLE bir Game'e (istenirse belirli bir
    // GameVersion'ına) bağlamak için (bkz. ManualLinkWindow). Pencereyi açma işi View katmanına ait
    // (bkz. RequestShowMessage ile AYNI desen) — ViewModel doğrudan bir Window tipine bağımlı olmasın.
    public event Action<UnmatchedRomFile>? RequestManualLink;

    [RelayCommand]
    private void ManualLink(UnmatchedRomFile file) => RequestManualLink?.Invoke(file);

    // RomImportWindow.xaml.cs, ManualLinkWindow'dan "Bağla" ile dönünce çağırır. Metadata (kapak/
    // favori/BAŞLAT vb.) ile ROM sahipliği BİLİNÇLİ OLARAK ayrı tutuluyor (kullanıcı isteği) —
    // RetroAudit.db'ye hiçbir yazma yok, sadece RetroAuditUserData.db'deki FilePathOverrides'a
    // (bkz. UserDataService.SaveFilePathOverride). targetVersion null ise Game seviyesinde genel
    // bir bağlantı (kullanıcı "Genel Bağlantı" seçtiyse).
    public async Task CompleteManualLinkAsync(UnmatchedRomFile file, Game game, GameVersion? targetVersion)
    {
        // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan alıp
        // yazamıyormu buraya" — Task.Run: PS2/PS3 boyutundaki dosyalarda UI donmasın diye (bkz.
        // ExportUnmatchedAsync'teki AYNI gerekçe).
        string? crc32;
        try
        {
            crc32 = await Task.Run(() => RomImportService.ComputeCrc32(file));
        }
        catch (Exception)
        {
            crc32 = null;
        }

        UserDataService.SaveFilePathOverride(game.GameKey, file.SourcePath, MatchMethods.ManualLink, targetVersion?.RawDatName, file.ZipEntryName, crc32);
        Unmatched.Remove(file);
        SelectedUnmatchedCount = Unmatched.Count(u => u.IsSelected);
        RefreshExcludedTagGroups();
        _onImportCompleted();
        RequestShowMessage?.Invoke($"\"{file.FileName}\" -> \"{game.Title}\"{(targetVersion is null ? "" : $" ({targetVersion.RawDatName})")} oyununa manuel bağlandı.");
    }

    public RomImportViewModel(IReadOnlyList<Game> allGames, string retroAuditDataPath, Action onImportCompleted, Func<string, string, Game> registerNewCustomGame)
    {
        _allGames = allGames;
        _retroAuditDataPath = retroAuditDataPath;
        _onImportCompleted = onImportCompleted;
        _registerNewCustomGame = registerNewCustomGame;
    }

    [RelayCommand]
    private void BrowseSourceFolder()
    {
        var dialog = new OpenFolderDialog { Title = "İçe aktarılacak ROM arşiv klasörünü seçin" };
        if (dialog.ShowDialog() == true)
            SourceFolder = dialog.FolderName;
    }

    // Artık isim eşleşmeyen HER dosya için CRC32 de hesaplanabiliyor (bkz. RomImportService.
    // ResolveCandidate) — bu gerçek dosya okuma/hash I/O'su gerektirdiğinden (ör. binlerce
    // eşleşmeyen dosyalı bir klasörde) tarama artık Task.Run üzerinde, UI donmadan çalışıyor.
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFolder) || !Directory.Exists(SourceFolder))
        {
            RequestShowMessage?.Invoke("Önce geçerli bir kaynak klasör seçin.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_retroAuditDataPath))
        {
            RequestShowMessage?.Invoke("Önce Ayarlar > Genel'den RetroAudit veri dizinini ayarlayın.");
            return;
        }

        IsScanning = true;
        Matches.Clear();
        Unmatched.Clear();
        ExcludedTagGroups.Clear();
        SelectedUnmatchedCount = 0;
        try
        {
            // Kullanıcı geri bildirimi: "pinball'ı bağlamıştım halbuki, eşleşmeyenlerde gözükmemesi
            // lazım değil mi" — daha önce ELLE (Bağla/Şu anki yoldan kullan) herhangi bir oyuna
            // bağlanmış dosyalar taramada tekrar "Eşleşmeyenler"e düşmesin diye (bkz.
            // RomImportService.ScanFolder yorumu).
            var alreadyLinkedPaths = new HashSet<string>(
                UserDataService.GetAllFilePathOverrides().Values.SelectMany(v => v).Select(o => o.FilePath).Where(p => p is not null)!,
                StringComparer.OrdinalIgnoreCase);
            var result = await Task.Run(() => RomImportService.ScanFolder(SourceFolder, _allGames, _retroAuditDataPath, alreadyLinkedPaths));
            foreach (var match in result.Matches)
                Matches.Add(match);
            foreach (var item in result.Unmatched)
            {
                item.PropertyChanged += OnUnmatchedItemPropertyChanged;
                Unmatched.Add(item);
            }

            foreach (var group in result.Unmatched
                .Where(u => u.ExcludedTag is not null)
                .GroupBy(u => u.ExcludedTag!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                ExcludedTagGroups.Add(new ExcludedTagGroup { Tag = group.Key, Count = group.Count() });
            }

            RequestShowMessage?.Invoke(result.Matches.Count == 0
                ? $"Kaynak klasörde katalogla eşleşen dosya bulunamadı ({result.Unmatched.Count} dosya eşleşmedi)."
                : $"{result.Matches.Count} eşleşme bulundu, {result.Unmatched.Count} dosya eşleşmedi.");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    // Kullanıcı isteği: "seçilenlerin altında sayıları yazarsa daha iyi olur" + "seçili olan
    // butonlar belli olsun" — SelectedUnmatchedCount'u VE (bu satır bir kategoriye aitse) o
    // kategori butonunun IsActive'ini canlı tutmak için: Unmatched'e eklenen her satırın IsSelected
    // değişimini dinler (bkz. ScanAsync) — SADECE buton tıklamasında değil, satır elle işaretlenip/
    // kaldırıldığında da doğru kalsın diye ("aktif" = o etiketin TÜM satırları şu an işaretli).
    private void OnUnmatchedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UnmatchedRomFile.IsSelected))
            return;

        SelectedUnmatchedCount = Unmatched.Count(u => u.IsSelected);

        // "Sadece Seçilenler" filtresi açıkken, tek bir satırın işareti bile değişse görünüm
        // (hangi satırların gösterildiği) anında güncellensin diye (bkz. RomImportWindow.xaml.cs).
        if (ShowOnlySelectedUnmatched)
            RequestRefreshUnmatchedView?.Invoke();

        if (sender is not UnmatchedRomFile { ExcludedTag: { } tag })
            return;

        var group = ExcludedTagGroups.FirstOrDefault(g => string.Equals(g.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
            group.IsActive = Unmatched.Where(u => string.Equals(u.ExcludedTag, tag, StringComparison.OrdinalIgnoreCase)).All(u => u.IsSelected);
    }

    // Kullanıcı isteği: "tıklayınca betaları işaretlesin mesela" + "seçili olana bi daha
    // tıkladığımda seçimi kaldırmıyor" — bu SPESİFİK etikete sahip satırları TOGGLE eder: hepsi
    // zaten işaretliyse (buton "aktif" görünüyorsa, bkz. ExcludedTagGroup.IsActive) hepsini
    // kaldırır; aksi halde (hiçbiri veya bir kısmı işaretliyse) hepsini işaretler. Kullanıcı birden
    // fazla kategori butonuna art arda basıp birleştirebilir, ör. "Unl" + "Proto" ama "Beta" hariç.
    [RelayCommand]
    private void SelectByTag(string tag)
    {
        var items = Unmatched.Where(u => string.Equals(u.ExcludedTag, tag, StringComparison.OrdinalIgnoreCase)).ToList();
        var allSelected = items.Count > 0 && items.All(u => u.IsSelected);
        foreach (var item in items)
            item.IsSelected = !allSelected;
    }

    // Kullanıcı isteği: "Seçimi Temizle yide Seçim yoksa hepsini seç olsun soldakilerin hepsini
    // seçsin yani. seçim varsa Seçimi temizle olsun" — tek bir buton, mevcut duruma göre TERSİNİ
    // yapıyor: hiç işaretli satır yoksa Eşleşmeyenler'in TAMAMINI işaretler, en az bir işaretli
    // satır varsa hepsini kaldırır (bkz. ToggleSelectAllUnmatchedLabel).
    [RelayCommand]
    private void ToggleSelectAllUnmatched()
    {
        var selectAll = !HasUnmatchedSelection;
        foreach (var item in Unmatched)
            item.IsSelected = selectAll;
    }

    // Kullanıcı isteği: "dışarı aktarabilme yapıp aktarıp sana mı yollasam" — eşleşmeyen dosyaların
    // tam listesini (dosya adı + sebep + CRC32 + kaynak yol) bir CSV'ye yazar, böylece hangi
    // dosyaların neden eşleşmediği kopyala-yapıştır yapmadan paylaşılabilir/incelenebilir. CRC32
    // (kullanıcı isteği: "crc32'ler yazmıyor csv'de") gerçek dosya içeriğinden hesaplanıyor — sadece
    // dosya adı DAT'la uyuşmasa bile katalogda AYNI hash'e sahip başka bir kayıt var mı diye
    // aranabilsin diye. Potansiyel olarak büyük dosyalar okunacağı için (PS2/PS3 boyutları) UI
    // donmasın diye Task.Run üzerinde çalışıyor (ImportAsync'teki VerifyHash ile AYNI desen).
    // Varsayılan dosya adı "eşleşmeyenler" yerine kaynak klasörün adını (kullanıcının zaten
    // kullandığı "her platform kendi klasöründe" düzenine göre genelde platform adı) taşıyor.
    [RelayCommand]
    private async Task ExportUnmatchedAsync()
    {
        if (Unmatched.Count == 0)
        {
            RequestShowMessage?.Invoke("Dışa aktarılacak eşleşmeyen dosya yok.");
            return;
        }

        var folderName = Path.GetFileName(SourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var defaultFileName = string.IsNullOrWhiteSpace(folderName) ? "eşleşmeyenler.csv" : $"{folderName} eşleşmeyenler.csv";

        var dialog = new SaveFileDialog
        {
            Title = "Eşleşmeyenleri Dışa Aktar",
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = defaultFileName,
        };
        if (dialog.ShowDialog() != true)
            return;

        var items = Unmatched.ToList();
        var rows = new List<(string FileName, string Reason, string Crc32, string SourcePath)>(items.Count);
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                ProgressText = $"CRC32 hesaplanıyor: {i + 1} / {items.Count} — {item.FileName}";

                string crc32;
                try
                {
                    crc32 = await Task.Run(() => RomImportService.ComputeCrc32(item));
                }
                catch (Exception)
                {
                    crc32 = "?"; // Bozuk/okunamayan dosya CSV'yi durdurmasın.
                }

                rows.Add((item.FileName, item.Reason, crc32, item.SourcePath));
            }
        }
        finally
        {
            ProgressText = string.Empty;
        }

        // Kullanıcı geri bildirimi 1: Excel'de tüm satır tek sütuna dolmuş — Türkçe Windows/Excel
        // yerel ayarında listedeki alanları AYIRAN karakter varsayılan olarak ";" (virgül ondalık
        // ayracı olarak kullanıldığı için) — bu yüzden "," yerine ";" kullanılıyor. Dosya adlarının
        // içinde zaten virgül geçtiği için ("Addams Family, The") virgülle ayırmak ayrıca riskliydi.
        //
        // Kullanıcı geri bildirimi 2: Türkçe karakterler bozuk çıktı (ör. "adıyla" -> "adÄ±yla") —
        // dosyanın GERÇEK baytları kontrol edildi, UTF-8 BOM zaten doğru yazılıyordu (EF BB BF);
        // sorun BOM eksikliği değil, "sep=;" satırının KENDİSİYDİ — Excel'in bazı sürümleri bir
        // "sep=" satırı gördüğünde BOM'u yok sayıp geri kalanını sistem ANSI kod sayfasıyla okuyor
        // (bilinen bir Excel tuhaflığı). Türkçe yerel ayarda Excel zaten VARSAYILAN olarak ";"
        // kullandığı için "sep=;" satırına hiç ihtiyaç yoktu — kaldırıldı, sadece UTF-8 BOM'a
        // (StreamWriter'ın Encoding.UTF8 ile otomatik yazdığı) güveniliyor.
        static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        await using var writer = new StreamWriter(dialog.FileName, append: false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("DosyaAdi;Sebep;Crc32;KaynakYol");
        foreach (var row in rows)
            await writer.WriteLineAsync($"{Csv(row.FileName)};{Csv(row.Reason)};{Csv(row.Crc32)};{Csv(row.SourcePath)}");

        RequestShowMessage?.Invoke($"{rows.Count} eşleşmeyen dosya dışa aktarıldı: {dialog.FileName}");
    }

    // Kullanıcı isteği: "eşleşmeyenlerde beta proto vs olanları silmek için buton ekle klasörden
    // silsin ... tıklamalı seçenekli yap onları seçili olanları silsin" — SADECE işaretli (IsSelected)
    // satırları hedefler (varsayılan olarak sadece Beta/Unlicensed/Prototip gibi kasıtlı dışlananlar
    // işaretli geliyor, bkz. RomImportService.ScanFolder — ama kullanıcı istediğini elle işaretleyip
    // kaldırabilir). Silmeden önce kaç dosyanın etkileneceği bir onay diyaloğuyla gösteriliyor.
    [RelayCommand]
    private void RequestDeleteSelectedUnmatched()
    {
        var count = Unmatched.Count(u => u.IsSelected);
        if (count == 0)
        {
            RequestShowMessage?.Invoke("Silmek için önce en az bir satır işaretleyin.");
            return;
        }
        RequestDeleteSelectedUnmatchedConfirmation?.Invoke(count);
    }

    // Onay diyaloğunda "Evet" denince RomImportWindow.xaml.cs tarafından çağrılır. Dosya
    // sayısı büyük olabileceğinden (Windows Çöp Kutusu'na taşıma gerçek bir dosya sistemi işlemi)
    // Task.Run üzerinde çalışır.
    [RelayCommand]
    private async Task DeleteSelectedUnmatchedConfirmedAsync()
    {
        var selected = Unmatched.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        IsScanning = true;
        try
        {
            var result = await Task.Run(() => RomImportService.DeleteSelectedFiles(selected));

            // Silinen dosyalar listeden kaldırılıyor — çok girdili zip olduğu için ATLANANLAR
            // (dosya hâlâ diskte var) bilerek listede kalır, kullanıcı bunu görsün diye.
            foreach (var item in selected.Where(item => !File.Exists(item.SourcePath)))
                Unmatched.Remove(item);
            SelectedUnmatchedCount = Unmatched.Count(u => u.IsSelected);
            RefreshExcludedTagGroups();

            var summary = $"{result.Deleted} dosya çöp kutusuna taşındı.";
            if (result.SkippedMultiEntryZip > 0)
                summary += $"\n{result.SkippedMultiEntryZip} zip, birden fazla dosya içerdiği için atlandı.";
            if (result.Failed > 0)
                summary += $"\n{result.Failed} dosya silinemedi.";
            RequestShowMessage?.Invoke(summary);
        }
        finally
        {
            IsScanning = false;
        }
    }

    // Kullanıcı isteği: "hiçbişeyle eşleştiremiyoz ... bunuda tabloda gösterebilelim" — sonra
    // netleştirildi: "unknown değilde manuel bağlamaya yönlendirebilirsin, 2 ayrı eşleştirme şekli
    // olmasın" ve "eşleşmeyenler sekmesine geçmeden seçim yapabilecek". Bu yüzden ayrı bir "Unknown"
    // akışı YA DA ayrı bir buton DEĞİL — ManualLinkWindow'daki "+ Yeni Oyun" ile BİREBİR aynı
    // adımları (RegisterNewCustomGame + SaveFilePathOverride, MatchMethods.ManualLink), ImportAsync
    // içinde ImportUnmatchedAsNewGames işaretliyken çağırır.
    private async Task ImportUnmatchedFileAsNewGameAsync(UnmatchedRomFile file)
    {
        // Kullanıcı isteği: "bu eşleşmeyenlerin crc32'sini zip içinden veya dosyadan alıp
        // yazamıyormu buraya" — Task.Run: PS2/PS3 boyutundaki dosyalarda UI donmasın diye.
        string? crc32;
        try
        {
            crc32 = await Task.Run(() => RomImportService.ComputeCrc32(file));
        }
        catch (Exception)
        {
            crc32 = null;
        }

        var folderName = Path.GetFileName(Path.GetDirectoryName(file.SourcePath)) ?? string.Empty;
        var cleanTitle = DatNameParser.Parse(Path.GetFileNameWithoutExtension(file.FileName)).CleanTitle;
        var title = string.IsNullOrWhiteSpace(cleanTitle) ? file.FileName : cleanTitle;

        // NOT: RegisterNewCustomGame ObservableCollection'ları (Platforms/PlatformListItems)
        // doğrudan değiştiriyor, bu WPF çağrı thread'inde (Dispatcher) kalmalı — CRC32'nin aksine
        // burada Task.Run YOK (yukarıdaki await zaten UI thread'ine geri döner).
        var game = _registerNewCustomGame(title, folderName);
        UserDataService.SaveFilePathOverride(game.GameKey, file.SourcePath, MatchMethods.ManualLink, Path.GetFileNameWithoutExtension(file.FileName), file.ZipEntryName, crc32);
    }

    // Silme sonrası her kategori butonunun sayısını Unmatched'daki GERÇEK kalan dosyalara göre
    // yeniden hesaplar; bir kategori tamamen silindiyse (ör. "Test (1)" -> 0) buton tamamen kaldırılır.
    private void RefreshExcludedTagGroups()
    {
        foreach (var group in ExcludedTagGroups.ToList())
        {
            var remaining = Unmatched.Count(u => string.Equals(u.ExcludedTag, group.Tag, StringComparison.OrdinalIgnoreCase));
            if (remaining == 0)
                ExcludedTagGroups.Remove(group);
            else
                group.Count = remaining;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var selected = Matches.Where(m => m.IsSelected).ToList();

        // Kullanıcı isteği: "eşleşmeyenler sekmesine geçmeden seçim yapabilecek ... içe aktarın
        // yanında, seçiliyse eşleşmeyenleri aktaracak değilse aktarmıcak" — Beta/Unl/Proto gibi
        // BİLEREK dışlananlar (bkz. IsIntentionallyExcluded) bu otomatik akışın DIŞINDA kalır,
        // sadece "gerçekten katalogda yok" (ExcludedTag=null) satırlar aday olur. Zaten bir oyuna
        // bağlı dosyalar (kullanıcı isteği: "tablodaki bi oyunun kartına bağlı sonuçta, eşleşmeyenlerde
        // gözükmemesi lazım") Unmatched listesine artık hiç girmiyor (bkz. RomImportService.ScanFolder),
        // bu yüzden burada ayrıca bir kontrole gerek yok.
        var unmatchedToImport = ImportUnmatchedAsNewGames
            ? Unmatched.Where(u => !u.IsIntentionallyExcluded).ToList()
            : new List<UnmatchedRomFile>();

        if (selected.Count == 0 && unmatchedToImport.Count == 0)
        {
            RequestShowMessage?.Invoke("İçe aktarmak için en az bir satır seçin.");
            return;
        }

        IsImporting = true;
        var imported = 0;
        var failed = new List<string>();
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var match = selected[i];
                ProgressText = $"{i + 1} / {selected.Count} — {match.Game.Title}";

                if (VerifyHashOnImport)
                {
                    var verified = await Task.Run(() => RomImportService.VerifyHash(match));
                    match.HashVerified = verified;
                    if (!verified)
                    {
                        failed.Add($"{match.Game.Title} (hash uyuşmadı)");
                        continue;
                    }
                }

                try
                {
                    await Task.Run(() => ApplyImport(match));
                    match.IsImported = true;
                    imported++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{match.Game.Title} ({ex.Message})");
                }
            }

            for (var i = 0; i < unmatchedToImport.Count; i++)
            {
                var file = unmatchedToImport[i];
                ProgressText = $"Yeni oyun olarak ekleniyor: {i + 1} / {unmatchedToImport.Count} — {file.FileName}";

                try
                {
                    await ImportUnmatchedFileAsNewGameAsync(file);
                    Unmatched.Remove(file);
                    imported++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{file.FileName} ({ex.Message})");
                }
            }
        }
        finally
        {
            IsImporting = false;
            ProgressText = string.Empty;
        }

        if (unmatchedToImport.Count > 0)
        {
            SelectedUnmatchedCount = Unmatched.Count(u => u.IsSelected);
            RefreshExcludedTagGroups();
        }

        _onImportCompleted();

        var totalAttempted = selected.Count + unmatchedToImport.Count;
        var summary = $"{imported} / {totalAttempted} dosya içe aktarıldı.";
        if (failed.Count > 0)
            summary += $"\n\nBaşarısız olanlar:\n{string.Join("\n", failed)}";
        RequestShowMessage?.Invoke(summary);
    }

    private void ApplyImport(RomMatch match)
    {
        if (match.ZipEntryName is not null)
        {
            ApplyZipImport(match);
            return;
        }

        switch (SelectedMode)
        {
            case RomImportMode.Move:
                Directory.CreateDirectory(Path.GetDirectoryName(match.DestinationPath)!);
                File.Move(match.SourcePath, match.DestinationPath, overwrite: true);
                break;

            case RomImportMode.Copy:
                Directory.CreateDirectory(Path.GetDirectoryName(match.DestinationPath)!);
                File.Copy(match.SourcePath, match.DestinationPath, overwrite: true);
                break;

            case RomImportMode.ReferenceInPlace:
                UserDataService.SaveFilePathOverride(match.Game.GameKey, match.SourcePath, match.MatchMethod);
                break;
        }
    }

    // Eşleşme bir .zip arşivinin içindeyse: "Şu anki yoldan kullan" arşive hiç dokunmadan sadece
    // zip'in yolunu override olarak kaydeder (çoğu emülatör zip içinden doğrudan ROM okuyabilir).
    // Taşı/Kopyala ise girdiyi düz bir dosya olarak hedef klasöre çıkarır; "Taşı" sadece arşivin
    // TEK oyun içerdiği durumda kaynak zip'i siler — aksi halde arşivdeki başka oyunlar kaybolur.
    private void ApplyZipImport(RomMatch match)
    {
        if (SelectedMode == RomImportMode.ReferenceInPlace)
        {
            UserDataService.SaveFilePathOverride(match.Game.GameKey, match.SourcePath, match.MatchMethod);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(match.DestinationPath)!);
        RomImportService.ExtractZipEntry(match.SourcePath, match.ZipEntryName!, match.DestinationPath);

        if (SelectedMode == RomImportMode.Move && RomImportService.CountZipEntries(match.SourcePath) == 1)
            File.Delete(match.SourcePath);
    }
}
