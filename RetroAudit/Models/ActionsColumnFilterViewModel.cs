using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RetroAudit.Models;

// DataGrid'deki birleşik "Actions" sütununun (Favori + Ara/Başlat butonları tek hücrede, bkz.
// MainWindow.xaml ActionsColumn — Gizle artık ayrı bir sütun, HideColumn) başlığındaki filtre
// dropdown'ı — Favori ve Durum (Ara/Başlat) filtrelerini TEK bir popup'ta birleştirir, ikisini de
// aynı anda uygular/iptal eder.
public partial class ActionsColumnFilterViewModel : ObservableObject
{
    public ColumnFilterViewModel FavoriteFilter { get; }
    public ColumnFilterViewModel HasLocalFileFilter { get; }

    [ObservableProperty]
    private bool isPopupOpen;

    // İkon rengini değiştirmek için: alttaki iki filtreden herhangi biri aktifse true.
    [ObservableProperty]
    private bool isActive;

    public ActionsColumnFilterViewModel(ColumnFilterViewModel favoriteFilter, ColumnFilterViewModel hasLocalFileFilter)
    {
        FavoriteFilter = favoriteFilter;
        HasLocalFileFilter = hasLocalFileFilter;

        favoriteFilter.PropertyChanged += (_, e) => RecomputeIsActive(e.PropertyName);
        hasLocalFileFilter.PropertyChanged += (_, e) => RecomputeIsActive(e.PropertyName);
    }

    private void RecomputeIsActive(string? propertyName)
    {
        if (propertyName == nameof(ColumnFilterViewModel.IsActive))
            IsActive = FavoriteFilter.IsActive || HasLocalFileFilter.IsActive;
    }

    // Alttaki iki filtrenin kendi Open()'ını çağırıyor (sadece Cancel için gereken anlık durumu
    // (snapshot) almak amacıyla) — onların KENDİ popup'ları hiç render edilmiyor, sadece Options
    // listeleri bu birleşik popup'a doğrudan bağlanıyor.
    [RelayCommand]
    private void Open()
    {
        FavoriteFilter.OpenCommand.Execute(null);
        HasLocalFileFilter.OpenCommand.Execute(null);
        IsPopupOpen = true;
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        FavoriteFilter.ApplyFilterCommand.Execute(null);
        HasLocalFileFilter.ApplyFilterCommand.Execute(null);
        IsPopupOpen = false;
    }

    [RelayCommand]
    private void CancelFilter()
    {
        FavoriteFilter.CancelFilterCommand.Execute(null);
        HasLocalFileFilter.CancelFilterCommand.Execute(null);
        IsPopupOpen = false;
    }

    // Tek "Temizle" düğmesi iki grubu da tam işaretli hale getirip her iki filtreyi de kaldırır.
    [RelayCommand]
    private void ClearAll()
    {
        foreach (var option in FavoriteFilter.Options)
            option.IsChecked = true;
        foreach (var option in HasLocalFileFilter.Options)
            option.IsChecked = true;
    }
}
