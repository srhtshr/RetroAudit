using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class CropEditorDialog : Window
{
    public CropEditorDialog()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        // Save/Cancel ViewModel'de RequestClose olayını tetikler; pencereyi kapatma (Window.Close)
        // View'a özgü bir işlem olduğu için burada, code-behind'da bağlanıyor.
        if (DataContext is CropEditorViewModel vm)
        {
            vm.RequestClose += Close;
        }
    }
}
