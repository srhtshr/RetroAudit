using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class CropEditorDialog : Window
{
    private CropEditorViewModel? Vm => DataContext as CropEditorViewModel;

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

    // Kırpılacak görselin yolunu ve (dolaylı olarak) kaynak piksel boyutunu kurar — pencere
    // Show/ShowDialog'dan ÖNCE, çağıran taraf (MainWindow.xaml.cs) tarafından çağrılır.
    public void LoadImage(string imagePath) => Vm?.Initialize(imagePath);

    // Canvas her boyut değiştiğinde (pencere sabit boyutlu olduğu için pratikte sadece İLK
    // layout geçişinde) Image'ı Canvas'ı dolduracak şekilde büyütüp gerçek Stretch=Uniform
    // letterbox alanını hesaplıyor — WPF'in Image kontrolü Width/Height'i "kutu" olarak raporlar,
    // içindeki gerçek (letterbox'lı) çizim alanını KENDİMİZ hesaplamamız gerekiyor.
    private void ImageCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Vm is not { SourcePixelWidth: > 0, SourcePixelHeight: > 0 } vm)
            return;

        SourceImage.Width = ImageCanvas.ActualWidth;
        SourceImage.Height = ImageCanvas.ActualHeight;

        var containerWidth = ImageCanvas.ActualWidth;
        var containerHeight = ImageCanvas.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0)
            return;

        var imageAspect = (double)vm.SourcePixelWidth / vm.SourcePixelHeight;
        var containerAspect = containerWidth / containerHeight;

        double displayWidth, displayHeight, displayLeft, displayTop;
        if (imageAspect > containerAspect)
        {
            displayWidth = containerWidth;
            displayHeight = containerWidth / imageAspect;
            displayLeft = 0;
            displayTop = (containerHeight - displayHeight) / 2;
        }
        else
        {
            displayHeight = containerHeight;
            displayWidth = containerHeight * imageAspect;
            displayTop = 0;
            displayLeft = (containerWidth - displayWidth) / 2;
        }

        vm.SetDisplayBounds(displayLeft, displayTop, displayWidth, displayHeight);
    }

    // Kırpma dikdörtgeninin GÖVDESİ sürüklenince: sadece KONUM değişir (boyut aynı kalır),
    // görselin display sınırları dışına taşmaması için clamp uygulanır.
    private void CropBody_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        vm.CropLeft = Math.Clamp(vm.CropLeft + e.HorizontalChange, vm.DisplayLeft, vm.DisplayLeft + vm.DisplayWidth - vm.CropWidth);
        vm.CropTop = Math.Clamp(vm.CropTop + e.VerticalChange, vm.DisplayTop, vm.DisplayTop + vm.DisplayHeight - vm.CropHeight);
    }

    // 4 köşe tutamacı: her biri kendi köşesini sürükler, karşı köşe SABİT kalır. Oran kilitliyse
    // (bkz. CropEditorViewModel.CurrentLockedAspectRatio — "Serbest" dışındaki her seçenek)
    // yükseklik genişliğe göre yeniden hesaplanır, genişlik yön değiştirmeye göre belirlenir.
    private void TopLeftHandle_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromCorner(e, anchorRight: true, anchorBottom: true);
    private void TopRightHandle_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromCorner(e, anchorRight: false, anchorBottom: true);
    private void BottomLeftHandle_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromCorner(e, anchorRight: true, anchorBottom: false);
    private void BottomRightHandle_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromCorner(e, anchorRight: false, anchorBottom: false);

    // anchorRight/anchorBottom: sürüklenen köşenin KARŞISINDAKİ (sabit kalması gereken) kenarı
    // belirtir — ör. sol-üst köşe sürüklenirken sağ ve alt kenarlar sabit kalmalı (anchorRight=true,
    // anchorBottom=true).
    private void ResizeFromCorner(DragDeltaEventArgs e, bool anchorRight, bool anchorBottom)
    {
        if (Vm is not { } vm)
            return;

        var right = vm.CropLeft + vm.CropWidth;
        var bottom = vm.CropTop + vm.CropHeight;

        var newLeft = anchorRight ? vm.CropLeft + e.HorizontalChange : vm.CropLeft;
        var newRight = anchorRight ? right : right + e.HorizontalChange;
        var newTop = anchorBottom ? vm.CropTop + e.VerticalChange : vm.CropTop;
        var newBottom = anchorBottom ? bottom : bottom + e.VerticalChange;

        // Minimum boyut + görsel sınırları içinde kal.
        const double minSize = 20;
        newLeft = Math.Clamp(newLeft, vm.DisplayLeft, newRight - minSize);
        newTop = Math.Clamp(newTop, vm.DisplayTop, newBottom - minSize);
        newRight = Math.Clamp(newRight, newLeft + minSize, vm.DisplayLeft + vm.DisplayWidth);
        newBottom = Math.Clamp(newBottom, newTop + minSize, vm.DisplayTop + vm.DisplayHeight);

        var newWidth = newRight - newLeft;
        var newHeight = newBottom - newTop;

        if (vm.CurrentLockedAspectRatio is { } ratio && ratio > 0)
        {
            // Oran kilitliyken yüksekliği genişliğe göre yeniden türet, sabit kalması gereken
            // kenardan (anchor) itibaren hesapla.
            newHeight = newWidth / ratio;
            newTop = anchorBottom ? newBottom - newHeight : newTop;
            if (!anchorBottom)
                newBottom = newTop + newHeight;
            else
                newTop = newBottom - newHeight;
        }

        vm.CropLeft = newLeft;
        vm.CropTop = newTop;
        vm.CropWidth = newWidth;
        vm.CropHeight = newHeight;
    }
}
