using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // ViewModel MessageBox'a doğrudan bağımlı olmasın diye basit bir bilgi olayı üzerinden haberleşiyoruz.
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestShowMessage += message =>
                MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
