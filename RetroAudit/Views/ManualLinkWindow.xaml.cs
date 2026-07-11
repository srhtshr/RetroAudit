using System.Windows;
using RetroAudit.Models;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class ManualLinkWindow : Window
{
    // ShowDialog() == true olduğunda dolu — bkz. ArtworkTypeSelectionDialog ile AYNI desen.
    public Game? SelectedGame { get; private set; }
    public GameVersion? SelectedVersion { get; private set; }

    public ManualLinkWindow(IReadOnlyList<Game> allGames, string scannedFolderName, string sourceFileName)
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        var vm = new ManualLinkViewModel(allGames, scannedFolderName, sourceFileName);
        vm.RequestClose += confirmed =>
        {
            if (confirmed)
            {
                SelectedGame = vm.SelectedGame;
                SelectedVersion = vm.SelectedVersion;
            }
            DialogResult = confirmed;
            Close();
        };
        DataContext = vm;
    }
}
