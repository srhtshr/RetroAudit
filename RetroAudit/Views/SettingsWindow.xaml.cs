using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RetroAudit.ViewModels;

namespace RetroAudit.Views;

public partial class SettingsWindow : Window
{
    // Emülatörler DataGrid'indeki her sütun için sabit bir anahtar (kullanıcı isteği: "sütunları
    // ayarlıyom kapatınca ayar bozuluyor genişlik ve konum ayarı onuda hallet") — MainWindow.xaml.cs'teki
    // WireColumnWidths/GamesGrid_ColumnReordered ile AYNI desen, burada daha basit: pinleme yok.
    private readonly Dictionary<string, DataGridColumn> _emulatorColumnsByKey = new();

    public SettingsWindow()
    {
        InitializeComponent();
        DarkTitleBarHelper.Apply(this);

        // ViewModel MessageBox'a doğrudan bağımlı olmasın diye basit bir bilgi olayı üzerinden haberleşiyoruz.
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestShowMessage += message =>
                MessageBox.Show(this, message, "RetroAudit", MessageBoxButton.OK, MessageBoxImage.Information);

            WireEmulatorGridColumns(vm);
        }
    }

    private void WireEmulatorGridColumns(SettingsViewModel vm)
    {
        _emulatorColumnsByKey["Platform"] = PlatformColumn;
        _emulatorColumnsByKey["CoreName"] = CoreNameColumn;
        _emulatorColumnsByKey["ResolvedFileName"] = ResolvedFileNameColumn;
        _emulatorColumnsByKey["Parameters"] = ParametersColumn;

        foreach (var (key, column) in _emulatorColumnsByKey)
        {
            if (vm.EmulatorGridColumnWidths.TryGetValue(key, out var savedWidth))
                column.Width = new DataGridLength(savedWidth);
        }

        // Kayıtlı sıra varsa VE mevcut sütun anahtarlarıyla tam eşleşiyorsa uygulanır — eşleşmiyorsa
        // (ör. bir güncellemede sütun eklenip çıkarıldıysa) yok sayılıp koddaki varsayılan sıraya
        // dönülür (bkz. AppSettings.ColumnOrder'daki aynı gerekçe).
        var savedOrder = vm.EmulatorGridColumnOrder;
        if (savedOrder.Count == _emulatorColumnsByKey.Count && savedOrder.All(_emulatorColumnsByKey.ContainsKey))
        {
            for (var i = 0; i < savedOrder.Count; i++)
                _emulatorColumnsByKey[savedOrder[i]].DisplayIndex = i;
        }

        var saveWidthsTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(400) };
        saveWidthsTimer.Tick += (_, _) =>
        {
            saveWidthsTimer.Stop();
            vm.SaveEmulatorGridColumnWidths(_emulatorColumnsByKey.ToDictionary(kv => kv.Key, kv => kv.Value.ActualWidth));
        };

        var widthDescriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
        foreach (var column in _emulatorColumnsByKey.Values)
        {
            widthDescriptor?.AddValueChanged(column, (_, _) =>
            {
                saveWidthsTimer.Stop();
                saveWidthsTimer.Start();
            });
        }
    }

    // Kullanıcı bir sütun başlığını sürükleyip bıraktığında (bkz. EmulatorsGrid.ColumnReordered
    // XAML'de) tam sırayı diske yazar — MainWindow.xaml.cs GamesGrid_ColumnReordered ile aynı
    // gerekçe, pinleme burada olmadığı için çok daha basit.
    private void EmulatorsGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var currentOrder = EmulatorsGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => _emulatorColumnsByKey.FirstOrDefault(kv => kv.Value == c).Key)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();

        vm.SaveEmulatorGridColumnOrder(currentOrder);
    }
}
