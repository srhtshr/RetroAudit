using System;
using System.Windows;

namespace RetroAudit;

// Uygulama giriş noktası. StartupUri App.xaml içinde MainWindow'a işaret ediyor.
// Global hata yakalayıcıları ekleyerek çökme ayrıntılarını görmek için OnStartup ezildi.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            var ex = args.Exception;
            MessageBox.Show($"Beklenmedik bir hata olustu:\n\nMessage: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner: {ex.InnerException?.Message}", "RetroAudit Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            if (ex != null)
            {
                MessageBox.Show($"AppDomain Unhandled Exception:\n\nMessage: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner: {ex.InnerException?.Message}", "RetroAudit Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        base.OnStartup(e);
    }
}
