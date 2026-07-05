using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class RomImportWindow : Window
{
    public RomImportWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new RomImportViewModel(mainVm.AllGames, mainVm.RetroAuditDataPath, mainVm.ReloadAppSettings);
        vm.RequestShowMessage += message =>
            MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
        DataContext = vm;
    }
}
