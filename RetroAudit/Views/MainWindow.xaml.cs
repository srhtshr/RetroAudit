using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Pencere açma, bilinçli olarak ViewModel'in RelayCommand'ları yerine burada, View
        // katmanında yapılıyor: MainViewModel doğrudan Window tiplerine bağımlı olmasın diye
        // ViewModel sadece bir olay (event) tetikliyor, gerçek "new Window().Show()" çağrısı burada.
        if (DataContext is MainViewModel vm)
        {
            vm.RequestOpenMediaProvider += () => new MediaProviderWindow { Owner = this }.Show();
            vm.RequestOpenCropEditor += () => new CropEditorDialog { Owner = this }.ShowDialog();
            vm.RequestOpenSettings += () => new SettingsWindow { Owner = this }.Show();
        }
    }
}
