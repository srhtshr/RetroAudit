using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

// Sürükle-bırak (drag & drop) mantığı bilinçli olarak code-behind'da tutuluyor:
// bu tamamen bir View/gesture sorumluluğu (fare hareketi izleme, hit-testing ile hangi
// satırın üzerine bırakıldığını bulma). Asıl veri değişikliği (öğeyi listeden kaldırma vb.)
// ViewModel.ApplyDrop(...) içinde yapılıyor, code-behind sadece o metodu çağırıyor.
public partial class MediaProviderWindow : Window
{
    private Point _dragStartPoint;

    public MediaProviderWindow()
    {
        InitializeComponent();

        // ViewModel MessageBox'a doğrudan bağımlı olmasın diye basit bir bilgi olayı üzerinden haberleşiyoruz.
        if (DataContext is MediaProviderViewModel vm)
        {
            vm.RequestShowMessage += message =>
                MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Sürüklemenin başlangıç noktasını hatırlar; gerçek sürükleme MouseMove'da eşik aşılınca başlar.
    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    // Fare sol tuşu basılıyken yeterince hareket edildiğinde asıl WPF sürükleme işlemini başlatır.
    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: MediaSearchResult result } element)
            return;

        var currentPosition = e.GetPosition(null);
        var movedFarEnough =
            Math.Abs(currentPosition.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;

        if (movedFarEnough)
            DragDrop.DoDragDrop(element, result, DragDropEffects.Copy);
    }

    // Sürüklenen kart, eksik öğe listesinin üzerine geldiğinde "kopyala" imlecini göstermek için izin verir.
    private void MissingList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(MediaSearchResult)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Kart bırakıldığında: bırakılan noktanın hangi ListBoxItem'ın (yani hangi eksik öğenin) üzerine
    // denk geldiğini bulur ve ViewModel.ApplyDrop(...) ile eşleştirmeyi simüle eder.
    private void MissingList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox { DataContext: MediaProviderViewModel vm } listBox)
            return;

        if (!e.Data.GetDataPresent(typeof(MediaSearchResult)))
            return;

        var droppedResult = (MediaSearchResult)e.Data.GetData(typeof(MediaSearchResult))!;

        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is ListBoxItem { DataContext: MissingMediaItem targetItem })
            vm.ApplyDrop(targetItem, droppedResult);
    }
}
