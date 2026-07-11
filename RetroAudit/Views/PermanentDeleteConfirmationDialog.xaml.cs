using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class PermanentDeleteConfirmationDialog : Window
{
    public bool DeleteRom { get; private set; }
    public bool DeleteBox { get; private set; }
    public bool DeleteScreenshot { get; private set; }
    public bool DeleteLogo { get; private set; }

    public PermanentDeleteConfirmationDialog(string gameTitle)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new PermanentDeleteConfirmationViewModel(gameTitle);
        vm.RequestClose += confirmed =>
        {
            if (confirmed)
            {
                DeleteRom = vm.DeleteRom;
                DeleteBox = vm.DeleteBox;
                DeleteScreenshot = vm.DeleteScreenshot;
                DeleteLogo = vm.DeleteLogo;
            }
            DialogResult = confirmed;
            Close();
        };
        DataContext = vm;
    }
}
