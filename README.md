# RetroAudit

Retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM (CommunityToolkit.Mvvm) ile geliştiriliyor.

## Mevcut durum (v0.04)

Bu aşama **yalnızca UI prototipi**: gerçek SQLite veritabanı veya XML tarama mantığı yok, tüm veriler `Services/MockDataService.cs` içindeki placeholder verilerden geliyor.

Tasarım dili: Visual Studio / Obsidian tarzı koyu tema (bkz. `Themes/ObsidianDark.xaml`).

### Pencereler
- **MainWindow** — Platform listesi, sanallaştırılmış oyun tablosu (DataGrid), seçili oyun detay paneli ve BAŞLAT butonu.
- **MediaProviderWindow** — Eksik medya (kutu/arkaplan/ekran görüntüsü) için arama sonucu kartları; kartlar sürükle-bırak ile eksik öğe listesine uygulanabiliyor.
- **CropEditorDialog** — Görsel kırpma oranı seçim arayüzü.
- **SettingsWindow** — Admin/Ayarlar paneli: RetroAudit Data kök dizini, platform başına emülatör yolu/parametreleri, bölge önceliği (USA > EU > JP), Export/Import Config (JSON).

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

- [ ] DAT (No-Intro/Redump) tabanlı RetroAudit.db üretimi (Builder)
- [ ] Gerçek ROM tarama / hash kontrolü
- [ ] Emülatör başlatma mantığı

## Lisans

[MIT](LICENSE)
