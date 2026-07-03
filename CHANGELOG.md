# Changelog

Bu proje küçük, sık sürümlerle ilerler (0.01, 0.02, ...). Henüz bir SemVer/1.0 taahhüdü yoktur.

## [0.07] - 2026-07-03

### Added
- Sol "Platforms" panelinde başlığın yanına **(+)** butonu eklendi; açılan popup'tan hangi
  platformların panelde görüneceği çoklu seçimle (checkbox listesi) belirlenebiliyor.
- `Platform.IsVisible` alanı ve `MainViewModel.VisiblePlatforms`/`SelectablePlatforms` — sol panel
  artık tam platform listesi yerine kullanıcının seçtiği alt kümeyi gösteriyor.
- Platform satırlarına yatay ayırıcı çizgi (satır altı) ve logo/isim arasında ince dikey ayırıcı
  eklendi; oyun sayısı rozeti (badge) korunarak daha düzenli bir liste görünümü sağlandı.

### Changed
- Orta paneldeki oyun `DataGrid`'inde satır/sütunları ayıran ince grid çizgileri açıldı
  (`GridLinesVisibility="All"`), mevcut tema renkleri (Brush.Border) kullanılarak okunabilirlik
  artırıldı; genel görünüm ve renk paleti değişmedi.

## [0.06] - 2026-07-03

### Added
- `SettingsWindow` sekmeli (TabControl) yapıya geçti: **Genel**, **Emülatörler**, **Bölge Önceliği**, **Komutlar**
  — her kategori ayrı sekmede, kendi açıklama metniyle birlikte.
- Her ayar alanının altına "bu alan ne işe yarar" açıklaması eklendi.
- Yeni **Komutlar** sekmesi: ana penceredeki toolbar butonlarının (Import, Rescan, Temizle,
  Refresh Media, Metadata Yenile, LB Taşı, Apply Resolver, BAŞLAT) açıklaması ve düzenlenebilir
  parametresi; kategoriye göre gruplu gösteriliyor (Veri Yönetimi / Medya / Organizasyon / Oynatma).
- `CommandSetting` modeli ve `AppSettings.Commands` — Export/Import Config (JSON) akışına dahil.
- Tema: `TabControl`/`TabItem` stilleri eklendi (`ObsidianDark.xaml`).

## [0.05] - 2026-07-03

### Added
- `LICENSE` — MIT lisansı eklendi.
- README'ye lisans bölümü ve güncel pencere listesi eklendi.

## [0.04] - 2026-07-03

### Changed
- Tüm ViewModel'lere, modellere, converter'lara ve XAML görünümlerine "ne işe yarıyor" ve
  "neden bu şekilde yapıldı" açıklamalı satır içi yorumlar eklendi (Değişmez Sistem Kuralı 2).
  Kapsam: `MainViewModel`, `MediaProviderViewModel`, `CropEditorViewModel`, `Models/*`,
  `Converters/*`, `MockDataService`, ve tüm `Views/*` XAML/code-behind dosyaları.

## [0.03] - 2026-07-03

### Added
- `MediaProviderWindow`: sağdaki arama sonucu kartları artık ortadaki eksik öğe listesine
  sürükle-bırak (drag & drop) ile taşınabiliyor; bırakılan kart, üzerine geldiği satırı
  "çözüldü" kabul edip listeden kaldırıyor (simülasyon — gerçek indirme yok).
- Aynı eşleştirme, "Uygula" butonuyla da (seçili satır + seçili kart) tetiklenebiliyor.
- `MediaProviderViewModel.ApplyDrop(...)` — hem drag&drop hem buton akışının paylaştığı ortak mantık.

## [0.02] - 2026-07-03

### Added
- `AppSettings` / `EmulatorConfig` modelleri ve `ConfigService` (JSON export/import).
- `SettingsWindow` (Admin/Ayarlar paneli): LaunchBox kök dizini, platform başına emülatör yolu/parametreleri,
  bölge önceliği sıralaması (USA > EU > JP), Export/Import Config (JSON) butonları.
- `SettingsViewModel` — tüm ayar alanları RelayCommand'larla (Gözat, Ekle, Sil, Taşı, Export, Import) yönetiliyor.
- Ana penceredeki `Tools` menüsüne "Ayarlar..." seçeneği eklendi.

## [0.01] - 2026-07-03

### Added
- Proje iskeleti: `RetroAudit.sln` + `RetroAudit.csproj` (net9.0-windows, WPF, CommunityToolkit.Mvvm).
- Obsidian koyu tema kaynak sözlüğü (`Themes/ObsidianDark.xaml`).
- `MainWindow`: toolbar, platform listesi, sanallaştırılmış oyun DataGrid'i, detay paneli, BAŞLAT butonu.
- `MediaProviderWindow` ve `CropEditorDialog` pencereleri.
- Mock veri servisi (`MockDataService`) — placeholder platform ve oyun listesi.
- MVVM ViewModel'leri: `MainViewModel`, `MediaProviderViewModel`, `CropEditorViewModel`.
