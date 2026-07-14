using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using RetroAudit.Models;
using RetroAudit.Services;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class MainWindow : Window
{
    // Sol paneldeki platform satırlarını sürüklemenin başlangıç noktası (bkz. PlatformRow_MouseMove) —
    // MediaProviderWindow'daki kart sürükleme deseniyle aynı yaklaşım. Tam nitelikli System.Windows.Point:
    // bu sınıfta zaten WM_GETMINMAXINFO P/Invoke'u için ayrı bir iç içe "Point" struct'ı var.
    private System.Windows.Point _platformDragStartPoint;

    // Sütun başlığı sağ tık popup'ının üstündeki "Sola/Sağa Sabitle" düğmelerinin hedeflediği
    // sütun (bkz. GamesGrid_PreviewMouseRightButtonUp) ve Key<->DataGridColumn eşlemesi (bkz.
    // WireColumnVisibility) — ApplyColumnPinning/PinColumn tarafından paylaşılıyor.
    private DataGridColumn? _lastRightClickedColumn;
    private readonly Dictionary<string, DataGridColumn> _columnsByKey = new();
    private readonly List<string> _pinnedLeftKeys = new();
    private readonly List<string> _pinnedRightKeys = new();

    // Gameplay alanındaki embedded YouTube player'ın sanal host eşlemesi bir kez kurulduktan
    // sonra tekrar kurulmaya çalışılırsa SetVirtualHostNameToFolderMapping istisna fırlatıyor —
    // bu yüzden sadece ilk PlayYouTubeEmbedAsync çağrısında kuruluyor (bkz. o metot).
    private bool _youTubeVirtualHostMapped;
    private const string YouTubeVirtualHost = "retroaudit.embed";

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        // Maximized'ken WindowChrome'un bilinen bir sorunu: pencere, ekranin gercek "calisma
        // alani" (work area) sinirlarini birkac piksel asiyor, bu da kenarda/ustte native
        // chrome'un ince bir dilimini (beyaz) acika cikariyor. Kenar bosluguyla telafi etmeye
        // calismak (onceki deneme) yanlis yontemdi -- asil dogru cozum, WM_GETMINMAXINFO'yu
        // yakalayip maximize boyut/konumunu dogrudan monitorun work area'sina esitlemek
        // (bkz. WmGetMinMaxInfo). Bu, tasmayi kokunden ortadan kaldiriyor.
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WindowProc);

        StateChanged += (_, _) =>
        {
            MaximizeRestoreGlyph.Text = WindowState == WindowState.Maximized ? "" : "";
            ApplyMaximizedBodyInset();
        };

        // Pencere açma, bilinçli olarak ViewModel'in RelayCommand'ları yerine burada, View
        // katmanında yapılıyor: MainViewModel doğrudan Window tiplerine bağımlı olmasın diye
        // ViewModel sadece bir olay (event) tetikliyor, gerçek "new Window().Show()" çağrısı burada.
        if (DataContext is MainViewModel vm)
        {
            // Gameplay screenshot alanındaki embedded YouTube player (bkz. MainWindow.xaml
            // Grid.Row="4", YouTubePlayer). WebView2.Navigate imperatif bir çağrı olduğu için
            // saf XAML binding ile ifade edilemiyor — MainViewModel.IsPlayingVideo değişince
            // burada gerçek navigasyon/durdurma yapılıyor.
            vm.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName != nameof(MainViewModel.IsPlayingVideo))
                    return;

                if (vm.IsPlayingVideo)
                    await PlayYouTubeEmbedAsync(vm.SelectedGame);
                else
                    StopYouTubeEmbed();
            };

            vm.RequestOpenMediaProvider += () => new MediaProviderWindow(vm) { Owner = this }.Show();
            vm.RequestOpenMetadataProvider += () => new MetadataProviderWindow(vm) { Owner = this }.Show();
            vm.RequestOpenCropEditor += () => new CropEditorDialog { Owner = this }.ShowDialog();
            vm.RequestOpenSettings += () =>
            {
                var settingsWindow = new SettingsWindow { Owner = this };

                // Ayarlar penceresi açıkken yapılan her değişikliği (Kaydet'e basmadan) canlı
                // olarak ana pencereye yansıtır — kullanıcı isteği: "değişikliklerin yansımasını
                // live yap". Kategori görünürlüğü checkbox'ları SettingsViewModel'in kendi
                // property'si değil, CategoryOptions koleksiyonundaki ayrı öğeler olduğu için
                // ayrıca dinleniyor.
                if (settingsWindow.DataContext is SettingsViewModel settingsVm)
                {
                    settingsVm.PropertyChanged += (_, _) => vm.ApplyLiveSettings(settingsVm);
                    foreach (var option in settingsVm.CategoryOptions)
                        option.PropertyChanged += (_, _) => vm.ApplyLiveSettings(settingsVm);
                }

                // "Kaydet" sonrası MessageBox.Show(this /* SettingsWindow */, ...) ile gösterilen
                // "Ayarlar kaydedildi." bilgi kutusu kapatılıp ardından Ayarlar penceresi X ile
                // kapatılınca, WindowChrome tabanlı özel başlık çubuğu (bkz. DarkTitleBarHelper)
                // ile sahip (Owner) zincirindeki bir etkileşim yüzünden MainWindow bazen
                // aktifleşmek yerine simge durumuna küçülüyor/arkada kalıyor (kullanıcı: "çarpıya
                // basınca minimize yapıyor"). Kapanışta MainWindow'u açıkça eski haline getirip
                // öne almak bunu güvenilir şekilde düzeltiyor.
                settingsWindow.Closed += (_, _) =>
                {
                    vm.ReloadAppSettings();
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                };
                settingsWindow.Show();
            };
            vm.RequestPermanentDeleteConfirmation += game =>
            {
                var dialog = new PermanentDeleteConfirmationDialog(game.Title) { Owner = this };
                if (dialog.ShowDialog() == true)
                    vm.PermanentlyDeleteGame(game, dialog.DeleteRom, dialog.DeleteBox, dialog.DeleteScreenshot, dialog.DeleteLogo);
            };
            vm.RequestBulkPermanentDeleteConfirmation += games =>
            {
                var dialog = new PermanentDeleteConfirmationDialog($"{games.Count} oyun") { Owner = this };
                if (dialog.ShowDialog() == true)
                    vm.BulkPermanentlyDeleteGames(games, dialog.DeleteRom, dialog.DeleteBox, dialog.DeleteScreenshot, dialog.DeleteLogo);
            };
            vm.RequestEditMetadata += game =>
            {
                var dialog = new EditMetadataWindow(game) { Owner = this };
                if (dialog.ShowDialog() == true)
                    vm.ApplyFilter(); // Title/Genre/... ObservableProperty değil, satırı tazelemek gerekiyor
            };
            // Kullanıcı isteği: "tablodan bağlama yapmak istediğimde klasörden değil tablodaki
            // oyunların listesinden seçmeli" — ROM İçe Aktar'daki ManualLinkWindow'un AYNI arama
            // kutulu oyun listesi burada da kullanılıyor (kaynak oyunun kendisi listeden hariç
            // tutulur, bir oyun kendine bağlanamaz); "+ Yeni Oyun" seçilirse (bkz.
            // ManualLinkViewModel.NewGameSentinelKey) RegisterNewCustomGame ile AYNI yol.
            vm.RequestLinkToGameSelection += async (game, filePath) =>
            {
                var candidates = vm.AllGames.Where(g => g != game).ToList();
                var dialog = new ManualLinkWindow(candidates, game.PlatformDisplayName, Path.GetFileName(filePath)) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.SelectedGame is not { } selectedGame)
                    return;

                var targetGame = selectedGame.GameKey == ManualLinkViewModel.NewGameSentinelKey
                    ? vm.RegisterNewCustomGame(selectedGame.Title, game.PlatformDisplayName)
                    : selectedGame;
                await vm.LinkGameFileToGameAsync(game, targetGame, dialog.SelectedVersion);
            };
            vm.RequestShowMessage += message => MessageBox.Show(message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

            // "Görsel Getir" öncesi hangi türlerin (Box/Clear Logo/Gameplay) indirileceğini
            // sorar (kullanıcı isteği) — iptal edilirse null döner, indirme hiç başlamaz. Zaten
            // mevcut olan türler diyalogda varsayılan işaretsiz geliyor (bkz. ArtworkTypeSelectionViewModel).
            vm.RequestArtworkTypeSelection += alreadyHas =>
            {
                var dialog = new ArtworkTypeSelectionDialog(alreadyHas.HasBox, alreadyHas.HasClearLogo, alreadyHas.HasScreenshot) { Owner = this };
                return dialog.ShowDialog() == true ? dialog.SelectedTypes : null;
            };

            // Uygulama içi (embedded) ROM arama penceresi — kullanıcı tamamen kendi kontrolünde
            // geziniyor, pencerenin tek otomasyonu WebView2'nin resmi DownloadStarting olayı
            // üzerinden indirmenin hedef klasörünü oyunun platform klasörüne yönlendirmek (bkz.
            // RomSearchWindow, MainViewModel.SearchWeb). completedCallback, bir indirme bitince
            // (Completed) o TEK oyunun "dosya var mı" durumunu tazeliyor.
            vm.RequestSearchRom += request =>
            {
                var (url, targetFolder, forcedFileName, game) = request;
                new RomSearchWindow(url, targetFolder, game.Title, _ => vm.NotifyRomDownloaded(game), forcedFileName)
                {
                    Owner = this,
                }.Show();
            };

            // Detay panelindeki tek-görsel "Ara" butonları (bkz. MainViewModel.SearchBoxArt/
            // SearchClearLogoArt/SearchScreenshotArt) — aynı embedded WebView2 deseni, sadece
            // hedef dosya adı ROM'la eşleşecek şekilde zorlanıyor (bkz. MediaSearchWindow).
            vm.RequestSearchArtwork += request =>
            {
                var (url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game) = request;
                new MediaSearchWindow(url, targetFolder, targetFileNameWithoutExtension, gameTitle, mediaTypeLabel, completedCallback, game)
                {
                    Owner = this,
                }.Show();
            };

            // Kullanıcının kendi ROM arşivinden toplu içe aktarma penceresi (bkz. RomImportWindow/
            // RomImportViewModel). Kapanmasını beklemeye gerek yok: pencere her başarılı içe
            // aktarma turunun sonunda kendi içinde vm.ReloadAppSettings'i zaten çağırıyor.
            vm.RequestOpenRomImport += () =>
            {
                var romImportWindow = new RomImportWindow(vm) { Owner = this };

                // SettingsWindow'daki AYNI kullanıcı geri bildirimi ("çarpıya basınca minimize
                // yapıyor") burada da yaşandı — WindowChrome tabanlı özel başlık çubuğu (bkz.
                // DarkTitleBarHelper) ile sahip (Owner) zincirindeki etkileşim yüzünden pencere X
                // ile kapanınca MainWindow bazen aktifleşmek yerine simge durumuna küçülüyor/arkada
                // kalıyor. Kapanışta MainWindow'u açıkça eski haline getirip öne almak (bkz. yukarıda
                // settingsWindow.Closed ile AYNI desen) bunu güvenilir şekilde düzeltiyor.
                romImportWindow.Closed += (_, _) =>
                {
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                };
                romImportWindow.Show();
            };

            WireColumnVisibility(vm);
            WireDetailPanelWidth(vm);
        }

        // Kapsül menü her açıldığında (Popup.Opened her IsOpen=true geçişinde tetiklenir, ilk
        // oluşturmada değil) küçük bir ölçek+opaklık animasyonu oynatır — kullanıcı isteği
        // "Animasyonlu açılsın/kapansın", sade tutmak için sadece açılışta (~120ms).
        ContextMenuPopup.Opened += (_, _) =>
        {
            if (ContextMenuBorder.RenderTransform is not ScaleTransform scale)
                return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(120);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1.0, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1.0, duration) { EasingFunction = ease });
            ContextMenuBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, duration));
        };
    }

    // Özel başlık çubuğundaki üç düğme — sürükleme/çift-tık-büyüt/sağ-tık-sistem-menüsü zaten
    // WindowChrome.CaptionHeight üzerinden native Win32 mekanizmasıyla çalışıyor (bkz.
    // MainWindow.xaml), bu üçü sadece düğmelerin kendi eylemlerini gerçekleştiriyor.
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // Pencere Maximized iken WindowChrome'un klasik "görünmez resize/frame kenarlığı" sorunu:
    // WM_GETMINMAXINFO (bkz. WmGetMinMaxInfo) pencerenin DIŞ boyutunu work area'ya birebir
    // eşitliyor, ama Windows'un kenar sürükleyerek yeniden boyutlandırmak için ayırdığı görünmez
    // kenarlık (DPI'a göre değişir) WPF içeriği tarafında hâlâ hesaba katılmıyor. Bu telafi SADECE
    // BodyGrid'e (Grid.Row="4" — pencerenin gerçek sol/sağ/alt kenarlarına değen TEK katman)
    // uygulanıyor, EN DIŞTAKİ RootLayoutGrid'e DEĞİL ve ÜST kenara DA DEĞİL:
    //   - RootLayoutGrid'e (veya başlık çubuğuna) margin verildiğinde (denendi, geri alındı)
    //     Windows'un pencere kenarına çizdiği native accent/border rengi açığa çıkıyordu (üstte
    //     ince renkli bir çizgi) — RootLayoutGrid ve başlık çubuğu her zaman WM_GETMINMAXINFO'nun
    //     sığdırdığı gerçek pencere sınırına JİLET GİBİ (0 margin) otursun; sadece bunun bir SEVİYE
    //     İÇİNDEKİ BodyGrid (RootLayoutGrid'in kendi arkaplanı hâlâ o kenarı kaplarken) geri çekiliyor.
    //   - BodyGrid'in ÜST kenarı zaten pencerenin fiziksel üst sınırına değmiyor (üstünde başlık
    //     çubuğu/araç çubukları var), bu yüzden Top her zaman 0.
    private void ApplyMaximizedBodyInset()
    {
        if (WindowState != WindowState.Maximized)
        {
            BodyGrid.Margin = new Thickness(0);
            return;
        }

        var border = SystemParameters.WindowResizeBorderThickness;
        var frame = SystemParameters.WindowNonClientFrameThickness;
        BodyGrid.Margin = new Thickness(
            border.Left + frame.Left,
            0,
            border.Right + frame.Right,
            border.Bottom + frame.Bottom);
    }

    // Sol paneldeki platform listesinde tut-sürükle ile manuel sıralama. Kullanıcı isteği:
    // sürüklerken CANLI konumlansın, sadece bırakınca değil — bu yüzden her DragOver'da
    // vm.MoveInPlatformListItems ile görünür liste anında güncelleniyor (bkz. o metodun yorumu).
    // DragDrop.DoDragDrop KENDİ mesaj döngüsünü pompaladığı için çağrı senkron blok olarak
    // bırakılana/iptal edilene kadar dönmüyor — döndüğü an sürükleme kesin olarak bitmiştir,
    // bu yüzden kalıcı kayıt (CommitPlatformOrder) ayrı bir Drop olayı yerine burada yapılıyor.
    private void PlatformRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _platformDragStartPoint = e.GetPosition(null);
    }

    private void PlatformRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: PlatformListItem { Platform: { } platform } } element)
            return;

        var currentPosition = e.GetPosition(null);
        var movedFarEnough =
            Math.Abs(currentPosition.X - _platformDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPosition.Y - _platformDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;

        if (!movedFarEnough)
            return;

        DragDrop.DoDragDrop(element, platform, DragDropEffects.Move);

        if (DataContext is MainViewModel vm)
            vm.CommitPlatformOrder();
    }

    private void PlatformRow_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel vm ||
            e.Data.GetData(typeof(Platform)) is not Platform draggedPlatform ||
            sender is not FrameworkElement { DataContext: PlatformListItem { Platform: { } targetPlatform } })
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        if (draggedPlatform != targetPlatform)
            vm.MoveInPlatformListItems(draggedPlatform, targetPlatform);
    }

    // DataGrid'in kendi seçim/sağ tık davranışı yok (WPF DataGrid sağ tıkta satırı otomatik
    // seçmez) — bu yüzden tıklanan noktadaki DataGridRow'u görsel ağaçta yukarı doğru arayıp
    // bulunca ViewModel'e "bu oyun için menü aç" deniyor (bkz. MainViewModel.OpenContextMenuFor).
    // Down değil UP'ta açılıyor: StaysOpen="False" bir Popup'ı onu açan tıklamanın KENDİSİ
    // içinde (Down anında) senkron açmak WPF'te bilinen bir sorun — Popup'ın "dışarı tıklandı,
    // kapat" mekanizması aynı tıklamayı dışarıda sayıp anında kapatıyor. Up'ta açmak bu yarış
    // durumunu ortadan kaldırıyor çünkü o an itibariyle tıklama jesti zaten tamamlanmış oluyor.
    private void GamesGrid_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // Herhangi bir sütun başlığına sağ tıklamak, ayrı bir "Sütunlar" düğmesine gerek
        // kalmadan sütun görünürlüğü seçicisini açar (bkz. kullanıcı isteği: "fazladan gereksiz
        // buton olmaz"). Satır kontrolünden ÖNCE kontrol edilmeli — başlık, satırların üstünde
        // ayrı bir öğe. Tıklanan spesifik sütun _lastRightClickedColumn'a kaydediliyor ki popup'ın
        // üstündeki "Sola/Sağa Sabitle" düğmeleri hangi sütunu hedefleyeceğini bilsin.
        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is { Column: { } clickedColumn })
        {
            e.Handled = true;
            _lastRightClickedColumn = clickedColumn;
            vm.IsColumnPickerOpen = true;
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: Game game })
        {
            e.Handled = true;

            // Sağ tıklanan satır, Ctrl/Shift ile yapılmış mevcut bir çoklu seçimin parçasıysa
            // (WPF DataGrid varsayılan SelectionMode="Extended" — sağ tık seçimi bozmaz) toplu
            // menü açılır; aksi halde her zamanki gibi sadece o satır hedeflenir.
            var selectedGames = GamesGrid.SelectedItems.Cast<Game>().ToList();
            if (selectedGames.Count > 1 && selectedGames.Contains(game))
                vm.OpenBulkContextMenuFor(selectedGames);
            else
                vm.OpenContextMenuFor(game);
        }
    }

    // Sağ paneldeki Sürümler listesinde bir kart çift tıklanınca, o SÜRÜMÜN kendi dosyasını
    // (genel HasLocalFile'ın aksine, o an tercih edilen sürümle sınırlı değil) tanımlı emülatörle
    // başlatır (bkz. MainViewModel.LaunchVersionCommand). Tek tık seçimi zaten ListBox'ın kendi
    // SelectedItem/IsSelected mekanizmasıyla (bkz. MainWindow.xaml ItemContainerStyle) sağlanıyor.
    private void VersionsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (sender is ListBox { SelectedItem: GameVersion version })
            vm.LaunchVersionCommand.Execute(version);
    }

    // Sürümler (Region) tek-kart popup'ındaki listede bir karta TEK tıklanınca (bkz. MainWindow.xaml
    // OtherVersionCards ListBox'ı) o sürümü tek-kart alanına taşır, popup'ı kapatır. Çift tık zaten
    // VersionsList_MouseDoubleClick ile doğrudan başlatıyor (aynı ListBox'a her ikisi de bağlı).
    private void OtherVersionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (sender is ListBox { SelectedItem: GameVersion version })
            vm.SelectVersionCardCommand.Execute(version);
    }

    // Detay panelindeki platform logosu rozetine tıklanınca o platformun ROM klasörünü açar
    // (bkz. MainWindow.xaml Border.MouseLeftButtonDown, MainViewModel.OpenPlatformFolderCommand).
    private void PlatformLogoBadge_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedGame: { } game } vm)
            vm.OpenPlatformFolderCommand.Execute(game);
    }

    // Detay panelindeki Box art'a sol tıklanınca (kullanıcı isteği: "kapağa sol tık ... media
    // provider tool u açar kırpma kesme pixel küçültme işleri yapılır ordan") gerçek Crop Editor'ü
    // açar — SADECE gerçek bir Box art varsa (HasBox=false ise gösterilen Images/NoImage/Cover.png
    // paylaşılan bir yer tutucu dosya, onu kırpmak tüm oyunları etkiler, bu yüzden hiçbir şey
    // yapılmaz). Kaydetme başarılı olursa (bkz. CropEditorViewModel.Saved) Game.
    // RefreshImageDisplayPaths ile detay paneli restart olmadan yeni (kırpılmış) görseli gösterir.
    private void BoxArt_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedGame: { HasBox: true } game })
            return;

        OpenCropEditor(game.BoxPath, game);
    }

    // Clear Logo için aynı davranış (kullanıcı isteği: "clear logo içinde aynı şekilde").
    private void ClearLogo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedGame: { HasClearLogo: true } game })
            return;

        OpenCropEditor(game.ClearLogoPath, game);
    }

    // Kullanıcı bulgusu: "Could not find file '...jpg'" çökmesi — HasBox/HasClearLogo (bkz.
    // Game.cs) sadece BoxPath/ClearLogoPath'in BOŞ olmadığını kontrol ediyor, dosyanın diskte
    // GERÇEKTEN var olduğunu değil (kayıt tarafındaki bir eşleşmezlik — bkz. MainViewModel.
    // SearchArtwork'teki düzeltme — ya da dosyanın sonradan silinmesi/taşınması hâlâ aynı çökmeye
    // yol açabilirdi). Artık burada da bir güvenlik ağı var: dosya yoksa çökmek yerine kullanıcıya
    // anlaşılır bir mesaj gösteriliyor.
    private void OpenCropEditor(string imagePath, Game game)
    {
        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this,
                $"Görsel dosyası bulunamadı:\n{imagePath}\n\nYeniden indirmeyi deneyin.",
                "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new CropEditorDialog { Owner = this };
        dialog.LoadImage(imagePath);
        if (dialog.DataContext is CropEditorViewModel vm)
            vm.Saved += game.RefreshImageDisplayPaths;
        dialog.ShowDialog();
    }

    // player.html + Plyr (plyr.css/plyr.js) STATİK dosyalar (bkz. AppPaths.YouTubePlayerAssets,
    // RetroAudit/Assets/YouTubePlayer/) — SetVirtualHostNameToFolderMapping ile "https://retroaudit.embed/"
    // sanal adresi altında sunuluyor. YouTube'un embed doğrulaması gerçek (biçimsel) bir https
    // origin'i şart koşuyor, "null" origin'e (ör. NavigateToString) "Hata 153" ile reddediyor.
    // Plyr, YouTube'un kendi arayüzünü (başlık/kanal logosu/öneriler) tamamen kaldırıp kendi sade
    // kontrollerini gösteriyor (kullanıcı isteği). Video ID'si player.html'e ?v= query string'iyle
    // geçiliyor (bkz. Navigate çağrısı aşağıda).
    //
    // Chromium'un varsayılan autoplay politikası sesli oynatma için gerçek bir DOM tıklaması
    // şartı koşuyor; Play overlay'imiz WebView2'nin DOM'u dışında bir WPF Button olduğu için bu
    // şart hiç karşılanmıyordu. --autoplay-policy=no-user-gesture-required bu şartı tarayıcı
    // seviyesinde kaldırıyor. Ayrı bir UserDataFolder kullanılıyor ki bu özel argüman
    // RomSearchWindow'un kendi (varsayılan ortamlı) WebView2'siyle çakışmasın — aynı user data
    // folder'ı paylaşan WebView2'ler aynı tarayıcı sürecini (ve argümanlarını) paylaşmak zorunda.
    private CoreWebView2Environment? _youTubeWebViewEnvironment;

    private async Task<CoreWebView2Environment> GetYouTubeEnvironmentAsync()
    {
        if (_youTubeWebViewEnvironment is not null)
            return _youTubeWebViewEnvironment;

        var userDataFolder = Path.Combine(Path.GetTempPath(), "RetroAudit", "YouTubePlayerWebView2");
        var options = new CoreWebView2EnvironmentOptions
        {
            // --disable-features=DirectCompositionVideoOverlays: kullanıcı geri bildirimi — "ilk
            // sahneyi aldı ... hangi saniyedeysem onu alacak şekilde ayarlayamıyor muyuz" — video
            // Chromium'da performans için donanım "overlay" katmanına (DirectComposition) çiziliyor,
            // bu katman TARAYICININ KENDİ compositor'ının (CapturePreviewAsync'in okuduğu yer) TAMAMEN
            // DIŞINDA — bu yüzden yakalanan kare oynatma ilerlese de hep overlay'e geçilmeden ÖNCEKİ
            // (genelde videonun ilk karesi) donmuş görüntü oluyordu. Bu bayrak video'yu normal
            // compositor'a çizdiriyor, CapturePreviewAsync artık GERÇEK anlık kareyi görebiliyor.
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-features=DirectCompositionVideoOverlays",
        };
        _youTubeWebViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
        return _youTubeWebViewEnvironment;
    }

    // Gameplay screenshot alanındaki embedded YouTube player (bkz. MainWindow.xaml
    // Grid.Row="4", YouTubePlayer, MainViewModel.IsPlayingVideo/PlayVideoCommand). Video
    // İNDİRİLMİYOR — WebView2 sadece youtube.com/embed/... adresini gösteriyor (kullanıcı
    // isteği: "sadece YouTube embed olarak oynatılacak").
    private async Task PlayYouTubeEmbedAsync(Game? game)
    {
        if (game?.YouTubeVideoId is not { } videoId)
            return;

        try
        {
            var environment = await GetYouTubeEnvironmentAsync();
            await YouTubePlayer.EnsureCoreWebView2Async(environment);
        }
        catch
        {
            // WebView2 Runtime kurulu değilse: gameplay alanı boş kalmasın diye "Open on
            // YouTube" fallback'i tetikle (bkz. MainViewModel.VideoEmbedFailed).
            if (DataContext is MainViewModel vm)
                vm.VideoEmbedFailed = true;
            return;
        }

        if (!_youTubeVirtualHostMapped)
        {
            YouTubePlayer.CoreWebView2.SetVirtualHostNameToFolderMapping(
                YouTubeVirtualHost, AppPaths.YouTubePlayerAssets, CoreWebView2HostResourceAccessKind.Allow);
            _youTubeVirtualHostMapped = true;
        }

        YouTubePlayer.CoreWebView2.NavigationCompleted -= YouTubePlayer_NavigationCompleted;
        YouTubePlayer.CoreWebView2.NavigationCompleted += YouTubePlayer_NavigationCompleted;
        YouTubePlayer.CoreWebView2.Navigate($"https://{YouTubeVirtualHost}/player.html?v={videoId}");
    }

    // Sadece SARMALAYICI player.html sayfasının kendisi yüklenemezse (iframe değil) fallback
    // gösterilir. İçindeki YouTube iframe'inin kendi hatalarını (video kaldırılmış/bölge kısıtlı
    // gibi) bu olay yakalamıyor — kabul edilen bir sınırlama, o durumda kullanıcı sadece siyah/boş
    // bir oynatıcı görür.
    private void YouTubePlayer_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.VideoEmbedFailed = !e.IsSuccess;
    }

    // Kapat butonuna basılınca VEYA başka bir oyun seçilince (bkz. MainViewModel.OnSelectedGameChanged)
    // çağrılır. Sadece WebView2'yi gizlemek yetmez — video sesi/oynatması Visibility=Collapsed
    // olsa bile arka planda devam eder, bu yüzden about:blank'e navigasyon ile gerçekten durduruluyor.
    private void StopYouTubeEmbed()
    {
        if (YouTubePlayer.CoreWebView2 is not null)
            YouTubePlayer.CoreWebView2.Navigate("about:blank");
    }

    // Kullanıcı isteği: "detaylardaki videodan gameplay resmi alma yapabilirmiyiz ... o butonla
    // videodan gameplay resmi snapshot alıp kaydedebilirmiyiz" — CapturePreviewAsync, WebView2'nin
    // o an EKRANA ÇİZİLMİŞ görüntüsünü yakalar (piksel tabanlı, gerçek bir ekran görüntüsü gibi) —
    // içerik YouTube'un kendi iframe'i olsa da cross-origin/CORS kısıtlaması SÖZ KONUSU DEĞİL,
    // çünkü DOM/JS seviyesinde değil render edilmiş çıktı seviyesinde çalışıyor.
    private async void CaptureVideoSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedGame is not { } game)
            return;
        if (YouTubePlayer.CoreWebView2 is null)
            return;

        // Kullanıcı geri bildirimi: "bazılarında youtubedaki yazıları falan alıyor bazılarında
        // almıyor süreye göremi screenshot alıyor acaba" — evet, tam olarak bu: start:60 saniyeye
        // atlarken YouTube kısa bir süre arabelleğe alıyor (buffering) ve bu sırada kendi
        // başlık/kapak ekranını gösteriyor, ama Plyr'ın window.player.playing bayrağı bu buffering
        // anında bile TRUE dönebiliyor (Plyr, YT'nin BUFFERING durumunu da "oynuyor" sayıyor) — bu
        // yüzden önceki bayrak kontrolü bazen bu ekranı yakalamayı engelleyemedi. Bunun yerine
        // YouTube IFrame API'nin KENDİ ham durumuna (window.player.embed.getPlayerState — Plyr
        // YouTube provider'da ham YT.Player'ı "embed" olarak dışarı veriyor) bakıyoruz: sadece
        // state === 1 (PLAYING) gerçekten karenin oynanış olduğunu garanti eder, 3 (BUFFERING) DEĞİL.
        // Buffering geçici olduğu için hemen pes etmek yerine birkaç kez kısa aralıklarla tekrar
        // deniyoruz (~1.8sn) — çoğu buffering bu sürede biter, kullanıcı tekrar tıklamak zorunda kalmaz.
        var isPlaying = false;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var stateRaw = await YouTubePlayer.CoreWebView2.ExecuteScriptAsync(
                "window.player && window.player.embed && typeof window.player.embed.getPlayerState === 'function' ? window.player.embed.getPlayerState() : -99");
            if (int.TryParse(stateRaw, out var state) && state == 1)
            {
                isPlaying = true;
                break;
            }
            await Task.Delay(300);
        }
        if (!isPlaying)
        {
            MessageBox.Show(this, "Video şu an oynatılmıyor (YouTube'un kapak ekranında/arabelleğe almada kalmış olabilir) — kare yakalamadan önce videoyu gerçekten başlatın.", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var stream = new MemoryStream();
        try
        {
            await YouTubePlayer.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Kare yakalanamadı:\n{ex.Message}", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var success = await vm.SaveVideoSnapshotAsync(game, stream.ToArray());
        if (!success)
            MessageBox.Show(this, "Görsel kaydedilemedi.", "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Kullanıcı geri bildirimi: "genel olarak kartın üstündeyken çalışmıyor ... scrollbar'a getirince
    // çalışıyor" — ListBox seviyesindeki önlemler (CanContentScroll=False, bu handler'ın kendisi)
    // yeterli gelmedi; alttaki ListBoxItem'lar tekerlek olayını daha derinde tüketip yukarı hiç
    // çıkarmıyordu. Asıl düzeltme artık dıştaki ScrollViewer'ın KENDİSİNDE (bkz.
    // GameDetailScrollViewer_PreviewMouseWheel, MainWindow.xaml) — bu metot orada zaten
    // "Handled=true" olarak işaretlendiği için burası çoğu zaman hiç çalışmaz, sadece ekstra bir
    // güvence olarak duruyor.
    private void VersionsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject element)
            return;

        var scrollViewer = FindVisualParent<ScrollViewer>(element);
        if (scrollViewer is null)
            return;

        e.Handled = true;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
    }

    // Kullanıcı geri bildirimi: "kartın üstündeyken çalışmıyor, scrollbar'a getirince çalışıyor" —
    // bkz. MainWindow.xaml GameDetailScrollViewer yorumu: tünelleme (Preview) olayı yukarıdan
    // aşağıya inerken bu ScrollViewer'ın KENDİSİNDE yakalanıp elle kaydırılıyor, altındaki
    // ListBox/ListBoxItem/sanal panel gibi elemanların olayı DAHA DERİNDE tüketmesine hiç fırsat
    // kalmıyor — sağlam ve dolaylı olmayan tek nokta çözümü.
    private void GameDetailScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        // Kullanıcı geri bildirimi: "kapsüldekinde çalışıyor ama şu alanda çalışmıyor" — "Sürümler"
        // tek-kart görünümündeki "▾" popup'ı (bkz. MainWindow.xaml VersionCardMenuScrollViewer)
        // XAML'de bu ScrollViewer'ın İÇİNDE tanımlı (görsel olarak ayrı bir katmanda render olsa da) —
        // bu yüzden tekerlek olayı popup'ın KENDİ PreviewMouseWheel'ına hiç ulaşmadan burada
        // (dıştaki ana panelde) tüketiliyordu. Popup açıkken olay BURAYA değil, doğrudan popup'ın
        // kendi ScrollViewer'ına yönlendiriliyor.
        if (VersionCardMenuScrollViewer is { IsVisible: true } popupScrollViewer)
        {
            popupScrollViewer.ScrollToVerticalOffset(popupScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // Kullanıcı geri bildirimi: "kartları açtığımdaki açılır listede kaydırmıyor yani arkasındaki
    // detaylar panelini kaydırıyor" — Sürümler'in İKİ ayrı Popup'ı (bkz. MainWindow.xaml
    // IsVersionsPopupOpen ve IsVersionCardMenuOpen) kendi ScrollViewer'larına rağmen tekerlek
    // olayını ALTTAKİ ana pencereye (GameDetailScrollViewer) sızdırıyordu — GameDetailScrollViewer_
    // PreviewMouseWheel ile AYNI "burada dur, elle kaydır" çözümü, sadece Popup'ın KENDİ
    // ScrollViewer'ına uygulanıyor.
    private void PopupScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // Kullanıcı isteği: "tek scrollla inme hızını artırabiliyor muyuz" — VirtualizingPanel.
    // ScrollUnit="Pixel"e geçince (bkz. GamesGrid'in kendi yorumu) varsayılan tekerlek başına
    // piksel miktarı küçük kalıyordu. Diğer ScrollViewer'larda (bkz. GameDetailScrollViewer_
    // PreviewMouseWheel) zaten kullanılan AYNI "elle ScrollToVerticalOffset" deseni, sadece e.Delta
    // burada bir çarpanla büyütülüyor. İçteki ScrollViewer'ı her tekerlek olayında yeniden aramamak
    // için bir kez bulunup alanda (_gamesGridScrollViewer) saklanıyor.
    private ScrollViewer? _gamesGridScrollViewer;
    private const double GamesGridWheelSpeedMultiplier = 2.5;

    private void GamesGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _gamesGridScrollViewer ??= FindVisualChild<ScrollViewer>(GamesGrid);
        if (_gamesGridScrollViewer is null)
            return;

        _gamesGridScrollViewer.ScrollToVerticalOffset(_gamesGridScrollViewer.VerticalOffset - e.Delta * GamesGridWheelSpeedMultiplier);
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null && element is not T)
            element = VisualTreeHelper.GetParent(element);
        return element as T;
    }

    private static T? FindVisualChild<T>(DependencyObject? element) where T : DependencyObject
    {
        if (element is null)
            return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match)
                return match;

            if (FindVisualChild<T>(child) is T found)
                return found;
        }

        return null;
    }

    // Sütun filtresi popup'ı açıldığında arama kutusuna otomatik odaklanır ve içindeki (varsa
    // önceki) metni seçili getirir — önceden kullanıcı popup'ı açtıktan sonra kutuya ayrıca
    // tıklaması gerekiyordu.
    private void FilterPopup_Opened(object sender, EventArgs e)
    {
        if (sender is not Popup { Child: DependencyObject child } || FindVisualChild<TextBox>(child) is not { } textBox)
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    // Sıralama artık DataGridColumnHeader'a tıklamakla değil (o artık filtre dropdown'ını açıyor —
    // bkz. FilterableColumnHeader, CanUserSortColumns="False"), bu dropdown'ın en üstündeki
    // "Sırala A-Z"/"Sırala Z-A" düğmeleriyle yapılıyor. Düğme, filtre şablonunun içinde olduğu için
    // görsel ağaçta yukarı çıkıp hangi DataGridColumnHeader'a (dolayısıyla hangi DataGridColumn'a)
    // ait olduğunu buluyoruz — WPF'in kendi otomatik sıralamasının kullandığı SortMemberPath +
    // ICollectionView.SortDescriptions mekanizmasının aynısı, sadece manuel tetikleniyor.
    private void SortAscending_Click(object sender, RoutedEventArgs e) => ApplyHeaderSort(sender, ListSortDirection.Ascending);

    private void SortDescending_Click(object sender, RoutedEventArgs e) => ApplyHeaderSort(sender, ListSortDirection.Descending);

    private void ApplyHeaderSort(object sender, ListSortDirection direction)
    {
        if (sender is not DependencyObject element)
            return;

        if (FindVisualParent<DataGridColumnHeader>(element)?.Column is not { } column ||
            string.IsNullOrEmpty(column.SortMemberPath))
            return;

        var view = CollectionViewSource.GetDefaultView(GamesGrid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));

        foreach (var col in GamesGrid.Columns)
            col.SortDirection = null;
        column.SortDirection = direction;

        if (element is FrameworkElement { DataContext: ColumnFilterViewModel filterVm })
            filterVm.IsPopupOpen = false;
    }

    // Sağ detay panelinin genişliğini ARTIK bir {Binding} değil, elle yönetiyoruz (bkz. MainWindow.xaml
    // ColumnDefinition yorumu) — GridSplitter'ın kendi sürüklemesi sırasında Width'i doğrudan
    // SetValue ile değiştirmesi, aktif bir OneWay Binding'i kalıcı olarak koparıyordu (sürükledikten
    // SONRA "tabloyu tam genişliğe getir" düğmesi bir daha hiç işe yaramıyordu). Bunun yerine
    // IsDetailPanelExpanded/DetailPanelWidth PropertyChanged'ı dinlenip Width burada set ediliyor;
    // GridSplitter ile aralarında kalıcı bir bağ olmadığı için birbirlerini bozmuyorlar.
    private void WireDetailPanelWidth(MainViewModel vm)
    {
        // UpdateLayout: sütun genişliği GridSplitter'ın piksel piksel sürüklemesi yerine TEK ANDA
        // (tam ekran düğmesiyle) büyük bir sıçrama yapınca, DataGrid'in sütun sanallaştırması
        // (EnableColumnVirtualization) genişlik/scroll hesaplarını hemen tazelemiyor — dikey
        // scrollbar bir önceki (dar) genişliğe göre konumlanmış kalıp pencere dışına taşıyordu.
        // Senkron UpdateLayout, DataGrid'i bu sıçramadan hemen sonra zorla yeniden ölçüp diziyor.
        // SelectedGame null iken (platform listesinde gezinirken, henüz bir oyun seçilmemişken)
        // panel her zaman gizli — kullanıcı geri bildirimi: "boş detaylar paneli gözüküyor".
        void ApplyDetailPanelWidth()
        {
            var shouldShow = vm.IsDetailPanelExpanded && vm.SelectedGame is not null;
            DetailPanelColumnDef.Width = new GridLength(shouldShow ? vm.DetailPanelWidth : 0);
            GamesGrid.UpdateLayout();
        }

        ApplyDetailPanelWidth();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsDetailPanelExpanded) or nameof(MainViewModel.DetailPanelWidth) or nameof(MainViewModel.SelectedGame))
                ApplyDetailPanelWidth();
        };
    }

    // "Sütunlar" seçicisindeki her satır bir DataGridColumn'a karşılık gelir (Key ile eşleşir).
    // DataGridColumn görsel ağacın parçası olmadığı için Visibility'si XAML'de doğrudan
    // bağlanamıyor — bu yüzden ColumnVisibilityOption.IsVisible değiştiğinde ilgili sütunu burada,
    // kod-arkasında güncelliyoruz.
    private void WireColumnVisibility(MainViewModel vm)
    {
        var columnsByKey = new Dictionary<string, DataGridColumn>
        {
            ["Hide"] = HideColumn,
            ["Matched"] = MatchedColumn,
            ["Logo"] = LogoColumn,
            ["Actions"] = ActionsColumn,
            ["Title"] = TitleColumn,
            ["Box"] = BoxColumn,
            ["Screenshot"] = ScreenshotColumn,
            ["File"] = FileColumn,
            ["Platform"] = PlatformColumn,
            ["Genres"] = GenresColumn,
            ["Publisher"] = PublisherColumn,
            ["CommunityRating"] = CommunityRatingColumn,
            ["MaxPlayers"] = MaxPlayersColumn,
            ["Region"] = RegionColumn,
            ["Source"] = SourceColumn,
            ["MatchMethod"] = MatchMethodColumn,
        };

        foreach (var option in vm.ColumnOptions)
        {
            if (!columnsByKey.TryGetValue(option.Key, out var column))
                continue;

            column.Visibility = option.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(option.IsVisible))
                    return;

                column.Visibility = option.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                vm.SaveColumnVisibility();
            };
        }

        WireColumnWidths(vm, columnsByKey);

        // PinColumn/ApplyColumnPinning için Key<->DataGridColumn eşlemesi field'a da kopyalanıyor.
        foreach (var (key, column) in columnsByKey)
            _columnsByKey[key] = column;
        ApplyColumnPinning(vm);
    }

    // Sağ tıklanan sütunu (_lastRightClickedColumn) sola/sağa sabitler ya da sabitlemeyi kaldırır
    // (pinLeft: true/false/null), sonra son durumu kaydedip pozisyonları yeniden uygular.
    private void PinColumn(bool? pinLeft)
    {
        if (_lastRightClickedColumn is null || DataContext is not MainViewModel vm)
            return;

        var entry = _columnsByKey.FirstOrDefault(kv => kv.Value == _lastRightClickedColumn);
        if (entry.Key is not { } key)
            return;

        _pinnedLeftKeys.Remove(key);
        _pinnedRightKeys.Remove(key);
        if (pinLeft == true)
            _pinnedLeftKeys.Add(key);
        else if (pinLeft == false)
            _pinnedRightKeys.Add(key);

        ApplyColumnPinningPositions(vm);
        vm.SavePinnedColumns(_pinnedLeftKeys.ToList(), _pinnedRightKeys.ToList());
        vm.IsColumnPickerOpen = false;
    }

    private void PinColumnLeft_Click(object sender, RoutedEventArgs e) => PinColumn(true);
    private void PinColumnRight_Click(object sender, RoutedEventArgs e) => PinColumn(false);
    private void UnpinColumn_Click(object sender, RoutedEventArgs e) => PinColumn(null);

    // Detay panelindeki Tür rozeti (bkz. XAML): tek türü olan bir oyunda tıklama doğrudan o türe
    // göre filtreler; birden fazla türü olan bir oyunda ise Button.ContextMenu'yü (XAML'de tanımlı,
    // her tür için bir MenuItem) manuel açar. ContextMenu normalde sadece sağ tıkta kendiliğinden
    // açıldığı için buradaki asıl amaç SOL tıkla da (rozete "tıklamak" doğal beklenti) açılmasını
    // sağlamak — PlacementTarget'ı burada set etmek şart, XAML'deki bindingler (PlacementTarget.
    // DataContext...) buna bağlı.
    private void GenreBadge_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.DataContext is not MainViewModel { SelectedGame: { } game } vm)
            return;

        if (game.HasMultipleGenres)
        {
            if (button.ContextMenu is { } menu)
            {
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            }
        }
        else
        {
            vm.FilterByGenreTokenCommand.Execute(game.PrimaryGenre);
        }
    }

    // ALTERNATE NAMES bölümündeki "▾ diğerleri" düğmesi (bkz. XAML) — ilk 2'nin ötesindeki
    // alternatif isimleri listeleyen ContextMenu'yü açar (2 satır sınırının üzerinde her zaman
    // en az 1 öğe olduğu için burada tek/çoklu ayrımı yok, her tıklama menüyü açar).
    private void AlternateNamesOverflowToggle_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    // Başlık satırındaki tek "Ara" düğmesi (bkz. XAML, kullanıcı isteği: "search butonuna
    // tıklayınca Kapak/Logo/Gameplay seçenekleri açılsın") — Kapsül menüsünü açar, hangi türün
    // aranacağına kullanıcı orada karar verir.
    private void ArtworkSearchBadge_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    // Kullanıcı bir sütun başlığını sürükleyip bırakarak sırasını değiştirdiğinde (pinleme dışı, düz
    // sürükle-bırak) tetiklenir — tam sırayı diske yazar (bkz. AppSettings.ColumnOrder). Bu olmadan
    // sürükleyerek yapılan sıralama hiç kalıcı olmuyor, uygulama her açılışta ColumnDefinitions'ın
    // sabit koddaki sırasına dönüyordu ("sütun konumları kapatınca bozuluyor" şikayeti buradan
    // geliyordu).
    private void GamesGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // "Gizle" her zaman en solda sabit kalmalı (bkz. ApplyColumnPinningPositions) — kullanıcı
        // onu sürükleyip başka bir yere taşımışsa hemen geri uygulanıyor.
        if (_columnsByKey.TryGetValue("Hide", out var hideColumn) && hideColumn.DisplayIndex != 0)
            ApplyColumnPinningPositions(vm);

        var currentOrder = GamesGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => _columnsByKey.FirstOrDefault(kv => kv.Value == c).Key)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();

        // WPF'te sabitleme (FrozenColumnCount) tamamen KONUMSAL: "ilk N sütun donuk" — hangi
        // anahtarların o N konumda durduğu değil, sadece SAYI kalıcı bir kavram. Bu yüzden pinlenmiş
        // kümeyi anahtar bazlı sabit tutmak yerine, her sürüklemeden sonra sayıyı koruyup içeriği
        // GÜNCEL sıradan yeniden okuyoruz (Excel/Sheets'teki "bölmeleri dondur" ile aynı zihniyet):
        // pinli bir sütun N. konumun dışına sürüklenirse doğal olarak sabitlemeden çıkar, pinli
        // alan İÇİNDE yeniden sıralanırsa (ör. iki pinli sütunun yerini değiştirmek) sabit kalır.
        // Önceki sürüm "sürüklenen sütun pin listesindeyse direkt kaldır" diyordu — bu, kullanıcının
        // az önce bilerek sabitleyip sonra pinli alan içinde konumunu ince ayar yapmak için
        // sürüklediği bir sütunun bile anında sabitlemeden çıkmasına yol açıyordu.
        var newPinnedLeft = currentOrder.Take(_pinnedLeftKeys.Count).ToList();
        var newPinnedRight = _pinnedRightKeys.Count > 0
            ? currentOrder.Skip(currentOrder.Count - _pinnedRightKeys.Count).ToList()
            : new List<string>();

        if (!newPinnedLeft.SequenceEqual(_pinnedLeftKeys) || !newPinnedRight.SequenceEqual(_pinnedRightKeys))
        {
            _pinnedLeftKeys.Clear();
            _pinnedLeftKeys.AddRange(newPinnedLeft);
            _pinnedRightKeys.Clear();
            _pinnedRightKeys.AddRange(newPinnedRight);
            vm.SavePinnedColumns(_pinnedLeftKeys.ToList(), _pinnedRightKeys.ToList());
        }

        vm.SaveColumnOrder(currentOrder);
    }

    // Kayıtlı sabitleme durumunu (bkz. AppSettings.PinnedLeftColumns/PinnedRightColumns) açılışta
    // uygular — sonraki her değişiklik PinColumn üzerinden ApplyColumnPinningPositions'ı çağırır.
    private void ApplyColumnPinning(MainViewModel vm)
    {
        _pinnedLeftKeys.Clear();
        _pinnedLeftKeys.AddRange(vm.PinnedLeftColumns);
        _pinnedRightKeys.Clear();
        _pinnedRightKeys.AddRange(vm.PinnedRightColumns);
        ApplyColumnPinningPositions(vm);
    }

    // Sola sabitlenenler DisplayIndex 0'dan başlayarak sırayla dizilir ve DataGrid.FrozenColumnCount
    // bu sayıya eşitlenir — gerçekten yatay kaydırmadan bağışık kalırlar (WPF'in native "freeze"
    // desteği). WPF DataGrid'in SAĞDAN dondurma desteği yok; bu yüzden sağa sabitleme sadece
    // DisplayIndex'i en sona TAŞIR — kullanıcı yatay kaydırırsa bu sütun da diğerleriyle birlikte
    // kayar, gerçek bir "sticky" davranış değildir (bilinçli, belgelenmiş bir sınırlama).
    //
    // Her çağrıda TÜM sütunların DisplayIndex'i, sabit bir "doğal sıra" (vm.ColumnOptions'ın
    // tanım sırası) + sol-sabit + sağ-sabit listelerinden BAŞTAN hesaplanıyor. Önceki sürüm sadece
    // pinlenen sütunları taşıyıp pinlenmeyenlere hiç dokunmuyordu — WPF, bir sütunun DisplayIndex'ini
    // değiştirince diğerlerini otomatik kaydırdığı için, sabitleme kaldırıldığında sütun eski
    // (pinlenmeden önceki) konumuna DÖNMÜYOR, kaldığı yerde kalıp komşu sütunların sırasını kalıcı
    // olarak bozuyordu ("box'ın sabitlemesini kaldırınca başka bir sütun sabitlenmiş gibi görünüyor"
    // şikayeti buradan geliyordu). Tam yeniden hesaplama bu birikimli kaymayı imkansız kılıyor.
    private void ApplyColumnPinningPositions(MainViewModel vm)
    {
        // Kullanıcının sürükleyerek belirlediği sıra (bkz. AppSettings.ColumnOrder) varsa VE mevcut
        // sütun anahtar kümesiyle birebir eşleşiyorsa (bir kod güncellemesi sütun eklemediyse/
        // çıkarmadıysa) o kullanılır; yoksa ColumnDefinitions'daki koddaki varsayılan sıraya dönülür.
        var defaultOrder = vm.ColumnOptions.Select(o => o.Key).ToList();
        var savedOrder = vm.ColumnOrder;
        var naturalOrder = savedOrder.Count > 0 && new HashSet<string>(savedOrder).SetEquals(defaultOrder)
            ? savedOrder.ToList()
            : defaultOrder;
        var validKeys = new HashSet<string>(naturalOrder);

        // Sütun birleştirme/kaldırma sonrası ayarlarda kalmış olabilecek artık var olmayan Key'ler
        // (ör. eski "Search"/"Status" sütunları) burada süzülüyor — yoksa toplam sütun sayısını aşan
        // bir DisplayIndex atanıp XAML yüklenirken ArgumentOutOfRangeException fırlatıyordu.
        var staleRemoved = _pinnedLeftKeys.RemoveAll(k => !validKeys.Contains(k)) > 0;
        staleRemoved |= _pinnedRightKeys.RemoveAll(k => !validKeys.Contains(k)) > 0;
        if (staleRemoved)
            vm.SavePinnedColumns(_pinnedLeftKeys.ToList(), _pinnedRightKeys.ToList());

        // "Gizle" sütunu her zaman en sola sabit (kullanıcı isteği: "en sola sabitle onu değişmesin
        // yeri") — normal kullanıcı pin/unpin akışının (PinColumn) DIŞINDA, burada zorla en başa
        // konuyor. Kullanıcı sağ-tıklayıp "Sabitlemeyi Kaldır" seçse bile bu, çağrıldığı her seferde
        // geri uygulanıyor, yani kalıcı olarak sabit kalıyor.
        if (validKeys.Contains("Hide"))
        {
            _pinnedLeftKeys.Remove("Hide");
            _pinnedRightKeys.Remove("Hide");
            _pinnedLeftKeys.Insert(0, "Hide");
        }

        var middleKeys = naturalOrder.Where(k => !_pinnedLeftKeys.Contains(k) && !_pinnedRightKeys.Contains(k));
        var orderedKeys = _pinnedLeftKeys.Concat(middleKeys).Concat(_pinnedRightKeys).ToList();

        for (var i = 0; i < orderedKeys.Count; i++)
        {
            if (_columnsByKey.TryGetValue(orderedKeys[i], out var column))
                column.DisplayIndex = i;
        }
        GamesGrid.FrozenColumnCount = _pinnedLeftKeys.Count;

        // Sabitlenen bölgenin sınırını ince bir dikey çizgiyle işaretle (bkz. MainWindow.xaml
        // PinBoundary*CellStyle'lar) — sadece HÜCRELERDE, başlıkta DEĞİL. HeaderStyle'ı buradan
        // değiştirmek denendi ama "Actions" gibi kendi özel HeaderTemplate'i (birleşik filtre
        // popup'ı) olan sütunlarda başlığın varsayılan (temasız/beyaz) görünüme dönmesine yol
        // açtı — bu yüzden başlık bilerek dokunulmadan bırakıldı, sınır çizgisi sadece veri
        // satırlarında görünüyor (istenen "sabitlenen alanı ayırt etme" amacı için yeterli).
        // Önce hepsi sıfırlanır ki eski sınır sütunu kalıcı işaretli kalmasın.
        foreach (var column in _columnsByKey.Values)
            column.ClearValue(DataGridColumn.CellStyleProperty);

        if (_pinnedLeftKeys.Count > 0 && _columnsByKey.TryGetValue(_pinnedLeftKeys[^1], out var lastLeft))
            lastLeft.CellStyle = (Style)FindResource("PinBoundaryRightCellStyle");

        if (_pinnedRightKeys.Count > 0 && _columnsByKey.TryGetValue(_pinnedRightKeys[0], out var firstRight))
            firstRight.CellStyle = (Style)FindResource("PinBoundaryLeftCellStyle");
    }

    // Kullanıcı bir sütun başlığının kenarını sürükleyip genişliğini değiştirdiğinde son ayarı
    // kaydeder. WPF DataGrid'in doğrudan bir "sütun genişliği değişti" olayı yok — DataGridColumn.
    // WidthProperty için bir DependencyPropertyDescriptor ile dinleniyor. Sürükleme sırasında piksel
    // piksel çok sık tetiklendiği için her defasında diske yazmak yerine DispatcherTimer ile
    // debounce ediliyor (son değişiklikten ~400ms sonra tek seferlik kayıt, tüm sütunlar birlikte).
    private void WireColumnWidths(MainViewModel vm, Dictionary<string, DataGridColumn> columnsByKey)
    {
        foreach (var (key, column) in columnsByKey)
        {
            if (vm.ColumnWidths.TryGetValue(key, out var savedWidth))
                column.Width = new DataGridLength(savedWidth);
        }

        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            vm.SaveColumnWidths(columnsByKey.ToDictionary(kv => kv.Key, kv => kv.Value.ActualWidth));
        };

        var widthDescriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
        foreach (var column in columnsByKey.Values)
        {
            widthDescriptor?.AddValueChanged(column, (_, _) =>
            {
                saveTimer.Stop();
                saveTimer.Start();
            });
        }
    }

    // --- WindowChrome maximize düzeltmesi (WM_GETMINMAXINFO) ---
    // WindowChrome kullanan pencerelerde, Windows'un maximize için hesapladığı boyut/konum
    // varsayılan olarak MONİTÖRÜN TAMAMINI (görev çubuğunun altını da) kapsayabiliyor ve
    // kenarlarda gerçek "çalışma alanı" sınırını birkaç piksel aşabiliyor — bu da native
    // chrome'un ince bir diliminin (ör. üstte beyaz bir çizgi) açığa çıkmasına yol açıyor.
    // Bu, doğru yöntem: WM_GETMINMAXINFO mesajını yakalayıp maximize boyut/konumunu doğrudan
    // ilgili monitörün work area'sına eşitlemek — kenar boşluğuyla telafi etmeye çalışmaktan
    // (önceki, terk edilen deneme) çok daha güvenilir.
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
            WmGetMinMaxInfo(hwnd, lParam);

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        const int MonitorDefaultToNearest = 0x00000002;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.ptMaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.ptMaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.ptMaxSize.Y = workArea.Bottom - workArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
