using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.ViewModels;

// Kullanıcı isteği: "yani rom u resmi vs herşeyiyle silmeli çöp kutusundan temizlediğimde ...
// hatta sorabilir son temizlikte rom box ss vs silinecekleri tikle işaretleyebilmeli kullanıcı
// işaretli gelmeli hepsi" — "Kalıcı Sil" artık düz bir MessageBox onayı DEĞİL, hangi dosya
// türlerinin (ROM + her görsel türü) diskten de silineceğini (Windows Çöp Kutusu'na taşınarak,
// bkz. RecycleBinService) seçebileceğiniz bir onay penceresi. Oyunun kendisi (RetroAudit
// kütüphanesinden kaldırma) bu seçimlerden BAĞIMSIZ her zaman gerçekleşir — checkbox'lar sadece
// hangi GERÇEK dosyaların da silineceğini belirler (bkz. ArtworkTypeSelectionViewModel ile AYNI
// desen, ama burada "Sil" en az bir seçim gerektirmiyor).
public partial class PermanentDeleteConfirmationViewModel : ObservableObject
{
    public string GameTitle { get; }

    [ObservableProperty]
    private bool deleteRom = true;

    [ObservableProperty]
    private bool deleteBox = true;

    // NOT: "Arkaplan" ayrı bir görsel türü DEĞİL — bu uygulamada detay panelinin arkaplanı zaten
    // Screenshot/"SS" dosyasının kendisi (bkz. Game.ScreenshotDisplayPath), ayrı bir "BG" dosyası
    // hiç yazılmıyor/izlenmiyor (bkz. MainViewModel._screenshotByPlatform). Bu yüzden burada
    // sadece gerçekten var olan 3 tür var: Box/SS/Logo.
    [ObservableProperty]
    private bool deleteScreenshot = true;

    [ObservableProperty]
    private bool deleteLogo = true;

    public PermanentDeleteConfirmationViewModel(string gameTitle)
    {
        GameTitle = gameTitle;
    }

    // true: sil, false: iptal — View bunu DialogResult'a çevirir (bkz. ArtworkTypeSelectionDialog
    // ile AYNI desen).
    public event Action<bool>? RequestClose;

    [RelayCommand]
    private void Delete() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
