using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroAudit.Models;
using RetroAudit.Services;

namespace RetroAudit.ViewModels;

// Kullanıcının kendi ROM arşivinden toplu içe aktarma penceresinin ViewModel'i. Taşıma/kopyalama
// gibi I/O ağır işlemler Task.Run üzerinde çalışır (bu kod tabanındaki diğer her şey senkron —
// burası bilinçli bir istisna, çünkü gerçek bir arşivde GB'larca veri taşınabilir ve bu, UI'yi
// donduramaz; bkz. bu oturumdaki önceki dondurma/performans düzeltmeleri).
public partial class RomImportViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _allGames;
    private readonly string _retroAuditDataPath;
    private readonly Action _onImportCompleted;

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

    [ObservableProperty]
    private RomImportMode selectedMode = RomImportMode.ReferenceInPlace;

    public ObservableCollection<RomMatch> Matches { get; } = new();

    public int SelectedCount => Matches.Count(m => m.IsSelected);

    public event Action<string>? RequestShowMessage;

    public RomImportViewModel(IReadOnlyList<Game> allGames, string retroAuditDataPath, Action onImportCompleted)
    {
        _allGames = allGames;
        _retroAuditDataPath = retroAuditDataPath;
        _onImportCompleted = onImportCompleted;
    }

    [RelayCommand]
    private void BrowseSourceFolder()
    {
        var dialog = new OpenFolderDialog { Title = "İçe aktarılacak ROM arşiv klasörünü seçin" };
        if (dialog.ShowDialog() == true)
            SourceFolder = dialog.FolderName;
    }

    [RelayCommand]
    private void Scan()
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
        try
        {
            var found = RomImportService.ScanFolder(SourceFolder, _allGames, _retroAuditDataPath);
            foreach (var match in found)
                Matches.Add(match);

            RequestShowMessage?.Invoke(found.Count == 0
                ? "Kaynak klasörde katalogla eşleşen dosya bulunamadı."
                : $"{found.Count} eşleşme bulundu.");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var selected = Matches.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
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
        }
        finally
        {
            IsImporting = false;
            ProgressText = string.Empty;
        }

        _onImportCompleted();

        var summary = $"{imported} / {selected.Count} dosya içe aktarıldı.";
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
                UserDataService.SaveFilePathOverride(match.Game.GameKey, match.SourcePath);
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
            UserDataService.SaveFilePathOverride(match.Game.GameKey, match.SourcePath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(match.DestinationPath)!);
        RomImportService.ExtractZipEntry(match.SourcePath, match.ZipEntryName!, match.DestinationPath);

        if (SelectedMode == RomImportMode.Move && RomImportService.CountZipEntries(match.SourcePath) == 1)
            File.Delete(match.SourcePath);
    }
}
