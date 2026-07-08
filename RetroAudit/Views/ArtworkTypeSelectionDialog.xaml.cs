using System.Windows;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class ArtworkTypeSelectionDialog : Window
{
    // ShowDialog() == true olduğunda dolu — DialogResult ile aynı anda set edilir (bkz. ctor).
    public HashSet<string> SelectedTypes { get; private set; } = new();

    public ArtworkTypeSelectionDialog(bool alreadyHasBox = false, bool alreadyHasClearLogo = false, bool alreadyHasGameplay = false)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new ArtworkTypeSelectionViewModel(alreadyHasBox, alreadyHasClearLogo, alreadyHasGameplay);
        vm.RequestClose += confirmed =>
        {
            if (confirmed)
                SelectedTypes = vm.GetSelectedTypes();
            DialogResult = confirmed;
            Close();
        };
        DataContext = vm;
    }
}
