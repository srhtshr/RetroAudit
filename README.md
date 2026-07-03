# RetroAudit

Retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM (CommunityToolkit.Mvvm) ile geliştiriliyor.

## Mevcut durum (v0.01)

Bu aşama **yalnızca UI prototipi**: gerçek SQLite veritabanı veya XML tarama mantığı yok, tüm veriler `Services/MockDataService.cs` içindeki placeholder verilerden geliyor.

Tasarım dili: Visual Studio / Obsidian tarzı koyu tema (bkz. `Themes/ObsidianDark.xaml`).

### Pencereler
- **MainWindow** — Platform listesi, sanallaştırılmış oyun tablosu (DataGrid), seçili oyun detay paneli ve BAŞLAT butonu.
- **MediaProviderWindow** — Eksik medya (kutu/arkaplan/ekran görüntüsü) için arama sonucu kartları.
- **CropEditorDialog** — Görsel kırpma oranı seçim arayüzü.

## Gereksinimler

- .NET 9 SDK (`winget install Microsoft.DotNet.SDK.9`)
- Windows (WPF)

## Çalıştırma

```
dotnet build
dotnet run --project RetroAudit
```

## Klasör yapısı

```
Core/Interfaces/   İleride kullanılacak boş arayüz tanımları (ör. IMediaProvider)
Models/            Game, Platform, AppSettings gibi veri modelleri
Services/          Mock veri servisi, config (JSON) servisi
ViewModels/        MVVM ViewModel'ler (CommunityToolkit.Mvvm ObservableObject/RelayCommand)
Views/             XAML pencereleri
Themes/            Koyu tema (Obsidian) kaynak sözlüğü
```

## Yol haritası

- [ ] Gerçek ROM tarama / XML-SQLite entegrasyonu
- [ ] LaunchBox içe aktarma
- [ ] Emülatör başlatma mantığı
