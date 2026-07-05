using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RetroAudit.Views;

// WPF hiçbir zaman kendiliğinden Windows'un immersive koyu başlık çubuğuna geçmez — başlık
// çubuğu/kenarlıklar DWM tarafından, WPF'in kendi çizim hattının tamamen dışında (non-client
// area) çiziliyor. Sistem koyu temada olsa bile, bu DWM attribute'ünü elle set etmeyen her WPF
// penceresi hep beyaz/açık başlık çubuğuyla açılır — WindowStyle/AllowsTransparency/WindowChrome
// ile hiçbir ilgisi yok (bu projede zaten hiçbiri kullanılmıyor), sadece bu çağrı hiç yapılmamış.
// Native başlık çubuğunu, Aero Snap'i, sistem menüsünü ve pencere düğmelerini olduğu gibi
// korur — sadece DWM'e bu chrome'u koyu renkte çizmesini söyler.
//
// DWMWA_CAPTION_COLOR/DWMWA_TEXT_COLOR (title bar'ı uygulamanın kendi rengine boyayan DWM
// attribute'leri) BİLİNÇLİ OLARAK kullanılmıyor — bu makine Windows 10 22H2 (build 19045)
// çalıştırıyor ve bu iki attribute Windows 11 22H2 (build 22621) öncesinde işletim sistemi
// tarafından desteklenmiyor; DwmSetWindowAttribute onlarla çağrılınca sessizce başarısız
// oluyor. Windows 10'da native title bar için erişilebilen tek özelleştirme, aşağıdaki
// immersive dark mode anahtarı (sabit, OS tanımlı bir koyu renk verir, uygulamanın kendi
// paletiyle birebir eşleşmez). Uygulamanın tam #252526 rengiyle eşleşen bir title bar
// istenirse tek yol özel (custom-drawn) bir title bar'dır — kullanıcı kararıyla bu proje
// için tercih edilmedi (native chrome/Aero Snap/sistem menüsü korunuyor).
internal static class DarkTitleBarHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20; // Windows 11 / 10 20H1+
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19; // Windows 10 1809-1909

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int attributeSize);

    // InitializeComponent()'ten hemen sonra çağrılmalı. DİKKAT: WindowInteropHelper.Handle
    // PASİF bir getter'dır — HWND henüz yoksa (Show()/ShowDialog() çağrılmadıysa) sadece
    // IntPtr.Zero döner, kendisi HWND OLUŞTURMAZ. HWND'yi zorla erken oluşturan asıl metot
    // EnsureHandle() — bu olmadan DwmSetWindowAttribute çağrısı sessizce hiçbir şey
    // yapmıyordu (ilk denemede tam olarak bu yüzden başlık çubuğu hâlâ beyaz kalmıştı).
    public static void Apply(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var useDarkMode = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));
    }
}
