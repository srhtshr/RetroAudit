using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class CropEditorDialog : Window
{
    public CropEditorDialog()
    {
        InitializeComponent();

        if (DataContext is CropEditorViewModel vm)
        {
            vm.RequestClose += Close;
        }
    }
}
