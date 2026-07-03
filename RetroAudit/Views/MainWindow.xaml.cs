using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (DataContext is MainViewModel vm)
        {
            vm.RequestOpenMediaProvider += () => new MediaProviderWindow { Owner = this }.Show();
            vm.RequestOpenCropEditor += () => new CropEditorDialog { Owner = this }.ShowDialog();
            vm.RequestOpenSettings += () => new SettingsWindow { Owner = this }.Show();
        }
    }
}
