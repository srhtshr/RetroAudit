using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroAudit.Models;

// "Sütunlar" açılır listesindeki tek bir satır — hangi DataGrid sütununun gösterilip
// gösterilmeyeceğini seçer. Key, MainWindow.xaml.cs'teki DataGridColumn'ları eşlemek için
// kullanılan sabit bir tanımlayıcıdır (x:Name değil, çünkü kod-arkası burada View katmanına ait).
public partial class ColumnVisibilityOption : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Header { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isVisible = true;
}
