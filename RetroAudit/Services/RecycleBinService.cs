using System.Runtime.InteropServices;

namespace RetroAudit.Services;

// Kullanıcı isteği: "eşleşmeyenlerde beta proto vs olanları silmek için buton ekle klasörden
// silsin" — kalıcı File.Delete YERİNE Windows Çöp Kutusu'na taşıyan bir yol gerekiyordu (yanlışlıkla
// silinen bir dosya geri alınabilsin diye). Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile (en
// yaygın .NET yöntemi) denendi ama UseWindowsForms gerektiriyor — bu da TÜM projede Application/
// Button/MouseEventArgs/DragEventArgs gibi WPF tiplerinin WinForms'unkilerle ÇAKIŞMASINA yol açıp
// build'i tamamen bozdu (App.xaml.cs, MainWindow.xaml.cs, MediaSearchWindow.xaml.cs). Bu yüzden
// doğrudan klasik Shell32 SHFileOperationW P/Invoke'u kullanılıyor — hiçbir ek proje ayarı/paket
// gerektirmiyor, mevcut kodu etkilemiyor.
public static class RecycleBinService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT fileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040; // Kalıcı silme DEĞİL — Çöp Kutusu'na taşı.
    private const ushort FOF_NOCONFIRMATION = 0x0010; // RetroAudit zaten kendi onay diyaloğunu gösteriyor.
    private const ushort FOF_SILENT = 0x0004; // Windows'un kendi ilerleme diyaloğu gösterilmez.

    // pFrom, Win32 API gereksinimi: çift NULL ile bitmeli. Tek dosya siler (klasör DEĞİL) —
    // RomImportService.DeleteSelectedFiles zaten tek tek dosya yolu veriyor.
    public static bool MoveToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
        };
        var result = SHFileOperationW(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }
}
