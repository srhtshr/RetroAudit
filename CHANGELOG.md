# Changelog

Bu proje küçük, sık sürümlerle ilerler (0.01, 0.02, ...). Henüz bir SemVer/1.0 taahhüdü yoktur.

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
