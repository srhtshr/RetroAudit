using System.Windows;

namespace RetroAudit.Views;

// DataGridColumn (ve Style/Popup gibi diğer görsel-ağaç-dışı nesneler) normal {Binding} ile
// DataContext'i miras alamaz — ElementName ve RelativeSource da çalışmaz, çünkü ikisi de
// NameScope/visual tree üzerinden çalışır ve DataGridColumn hiçbirinin parçası değildir.
// Freezable'lar farklıdır: bir ResourceDictionary'ye eklendiklerinde WPF onlara sahibi
// FrameworkElement'in InheritanceContext'ini otomatik atar, bu da DataContext mirasının
// (dolaylı olarak) çalışmasını sağlar. Bu, DataGridColumn.Header gibi alanları ViewModel'e
// bağlamanın standart WPF çözümüdür (bkz. MainWindow.xaml: Source={StaticResource ViewModelProxy}).
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
}
