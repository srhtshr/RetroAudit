# Changelog

Bu proje küçük, sık sürümlerle ilerler (0.01, 0.02, ...). Henüz bir SemVer/1.0 taahhüdü yoktur.

## [0.11] - 2026-07-03

### Added
- **Platform bazlı kaynak sistemi**: her platformun RetroAudit.db'de tek bir referans kaynağı var
  (`PlatformSourceMap`) — No-Intro/Redump/TOSEC asla aynı platform için birleştirilmez. PlayStation,
  PS2, GameCube, Wii, Dreamcast, Saturn, Xbox, Xbox 360, PSP artık Redump üzerinden; Amiga, Atari ST
  gibi ev bilgisayarları TOSEC üzerinden kataloğa giriyor (önceden bu platformlar hiç yoktu).
- Xbox 360 Digital/Games on Demand/Title Updates varsayılan olarak katalog dışı (mimari ileride
  açılabilir şekilde tasarlandı).
- **Temiz katalog filtresi**: Beta/Prototype/Demo/Sample/Preview/Kiosk/BadDump/Overdump/Alternate/
  Cracked/Trainer/Fixed/Pirate/Hack/Aftermarket/Unlicensed/BIOS/Utility/Application/Documentation/
  Magazine/Music/Coverdisk/SDK/Update/TestROM ve TOSEC'in `[cr][h][t][f][b][a][o][m][p]` scene/dump
  etiketleri artık GameVersions'a bile girmiyor (önceki "alt sürüm olarak sakla" davranışının yerini
  tam hariç tutma aldı). Bölge/sürümü net sınıflanamayan ama filtreye takılmayan kayıtlar "Unknown"
  olarak işaretleniyor.
- Fuzzy (Levenshtein) eşleştirme + güven skoru: `MatchConfidence`/`MatchMethod`/`NeedsReview` alanları
  Games tablosuna eklendi; düşük güvenli eşleşmeler otomatik onaylanmıyor, "Needs Review" ile işaretleniyor.
- Sayı-çakışması güvenlik freni (ör. "Super Mario Bros 16" ile "Super Mario Bros. 6" gibi farklı
  oyunların sadece rakam benzerliğiyle yanlış eşleşmesini engeller).
- `LaunchBox.Metadata.db`'nin gerçek `CompareName` normalizasyon kuralı örneklerle doğrulanıp taklit
  edildi (kesme işareti silinir, diğer noktalama boşluğa çevrilir) — eşleşme oranını belirgin artırdı.

### Fixed
- Çoklu tireli platform adlarının (ör. "Sega - Mega Drive - Genesis") LaunchBox'ta hiç eşleşmemesine
  neden olan platform-isim eşleştirme hatası.
- TOSEC'in köşeli parantez crack-grup etiketlerinin (`[cr CSL]` vb.) temizlenmemesi yüzünden aynı
  oyunun onlarca varyantının ayrı "oyun" olarak gruplanması (performans + veri kalitesi sorunu).

### Sonuç (tam No-Intro+Redump+TOSEC koşusu)
117 platform, 106.600 oyun, 220.450 sürüm, %56,9 LaunchBox eşleşmesi (54.029 kesin + 6.659 fuzzy,
5.059 Needs Review), duplicate hash çakışması 5.589'dan 159'a düştü.

## [0.10] - 2026-07-03

### Added
- **RetroAudit DAT Builder** (Stage A) — yeni `RetroAudit.Catalog` (kütüphane) ve `RetroAudit.Builder`
  (konsol aracı) projeleri. WPF uygulamasına henüz dokunmuyor; sadece offline katalog üretim
  boru hattı: No-Intro/Redump/TOSEC/MAME/FBNeo DAT dosyalarını (ClrMamePro metin formatı) okur,
  No-Intro parantez etiketleme kuralına göre bölge/sürüm/bayrak (Beta/Proto/Demo/Test/Pirate/
  Hack/Aftermarket/Unl) ayrıştırır, her oyun için tek bir "tercih edilen" sürüm seçer
  (USA > World > Europe > Japan > temiz geri kalan), LaunchBox.Metadata.db'den (Platform +
  CompareName zorunlu, ardından tam isim ve LaunchBox'ın kendi alternatif isim tablosu ile
  yedekli) tek seferlik açıklama/geliştirici/yayıncı/tür zenginleştirmesi yapar ve normalize
  edilmiş bir `RetroAudit.db` (Platforms/Developers/Publishers/Genres/Regions/Games/GameGenres/
  AlternateNames/GameVersions/GameHashes/MetadataSources/UserLibrary) üretir.
- Builder her koşunun sonunda platform/oyun/sürüm/hash sayıları, LaunchBox eşleşen/eşleşmeyen,
  tercih sürümü seçilemeyen ve olası hash çakışması sayılarını içeren bir rapor basar.
- `--platform` (tek platformla hızlı doğrulama) ve `--sources` (varsayılan: sadece `no-intro`;
  Redump/TOSEC/MAME/FBNeo mimarisi hazır ama henüz varsayılan olarak açılmadı) CLI argümanları.
- Doğrulama: NES No-Intro seti (4083 oyun) ve tam No-Intro seti (90 platform, 58.131 oyun,
  90.934 sürüm) ile gerçek verilerle koşuldu; çıktı `%LOCALAPPDATA%\RetroAudit\RetroAudit.db`'ye
  yazıldı (repo'ya asla girmiyor).

## [0.09] - 2026-07-03

### Added
- Sol panelde favori platform kavramı: mevcut 17 platform favori olarak işaretlendi, "+" popup'ında
  görünürlük (checkbox) ve favori (yıldız) ayrı ayrı düzenlenebiliyor; favoriler listede üstte.
- Kullanıcının verdiği geniş platform listesinden 27 yeni platform eklendi (varsayılan gizli).
  Arcade tarafı ayrı kategori açılmadı — CPS1-3 ve genel arcade, MAME satırının Alternatif Core
  alanına (FBNeo) toplandı.
- Emülatörler sekmesi artık "Tercih Edilen Core" / "Alternatif Core" kolonlarıyla, tüm platformlar
  için gerçek emülatör önerileriyle (44 satır) geliyor.

### Changed
- Platform listesi sıklaştırıldı (satır iç boşluğu azaltıldı).
- Program genelinde "LaunchBox" adı kaldırıldı: `LaunchBoxRootPath` → `RetroAuditDataPath`,
  "LB Taşı" → "Kütüphaneye Taşı", ilgili tüm etiket/yorum/mock veriler "RetroAudit Data" olarak
  güncellendi (bkz. veri mimarisi kararı: artık çalışma zamanında LaunchBox'a bağımlılık yok).

## [0.08] - 2026-07-03

### Changed
- Platform listesindeki satır alt çizgisi ve logo/isim arasındaki dikey ayırıcı kaldırıldı;
  tek ayırt edici öğe artık sağdaki oyun sayısı rozeti (kendi arka planı + ince kenarlığıyla).
- Orta paneldeki oyun `DataGrid`'inde dikey (sütunlar arası) grid çizgileri kapatıldı,
  sadece satırlar arası yatay çizgiler kaldı (`GridLinesVisibility="Horizontal"`).

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
  Refresh Media, Metadata Yenile, Kütüphaneye Taşı, Apply Resolver, BAŞLAT) açıklaması ve düzenlenebilir
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
- `SettingsWindow` (Admin/Ayarlar paneli): RetroAudit Data kök dizini, platform başına emülatör yolu/parametreleri,
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
