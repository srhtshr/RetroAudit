using System.Windows;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class EditMetadataWindow : Window
{
    public EditMetadataWindow(Game game)
    {
        InitializeComponent();

        var vm = new EditMetadataViewModel(game);
        vm.RequestClose += saved =>
        {
            DialogResult = saved;
            Close();
        };
        DataContext = vm;
    }
}
