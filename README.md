# RetroAudit

Retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM (CommunityToolkit.Mvvm) ile geliştiriliyor.

## Mevcut durum (v0.13)

Uygulama artık **gerçek bir DAT tabanlı katalogla** çalışıyor — `Services/MockDataService.cs` tamamen kaldırıldı. Sistem iki ayrı parçadan oluşuyor:

1. **RetroAudit.Builder** (komut satırı) — No-Intro/Redump/TOSEC DAT dosyalarını + LaunchBox metadata veritabanını okuyup tek bir offline katalog (`RetroAudit.db`, SQLite) üretir. Platform başına tek kaynak kuralı uygulanır (kartuş/el konsolları No-Intro, optik disk platformları Redump, ev bilgisayarları TOSEC), 1G1R (bölge önceliği USA > Europe > World > Japan) mantığıyla tekilleştirir, dağıtım varyantlarını (3DS Digital, PSN, vb.) ana platforma birleştirir ve Casino/Quiz/Board Game gibi oyun-olmayan içerikleri varsayılan gizli işaretler.
2. **RetroAudit** (WPF) — Builder'ın ürettiği `RetroAudit.db`'yi salt-okunur okur ve kullanıcı etkileşimlerini (favori, gizle, çöp kutusu, playlist, metadata düzenleme) **ayrı bir veritabanında** (`RetroAuditUserData.db`) tutar — çünkü `RetroAudit.db` her Builder çalıştırmasında tamamen silinip yeniden üretiliyor ve `GameId` kalıcı değil. Oyunlar `Platform|CompareTitle` şeklinde sabit bir anahtarla eşleştirilir, böylece kullanıcı verisi bir sonraki Builder koşusunda kaybolmaz.

Tasarım dili: Visual Studio / Obsidian tarzı koyu tema (bkz. `RetroAudit/Themes/ObsidianDark.xaml`).

### Özellikler
- Sol panelde kategoriye göre gruplanmış ~40 platform, sanallaştırılmış oyun tablosu (67 binin üzerinde oyun).
- Her sütunda arama kutulu, "Hepsini Seç/Temizle" filtre dropdown'ı + gösterilecek sütunları seçme paneli.
- Yıldız sütunuyla tek tıkla favoriye ekleme; ROM'u eksik oyunlarda tabloda doğrudan görünen "web'de ara" butonu.
- Sağ tık ile açılan kapsül biçimli komut menüsü (Ayarlar > Arayüz'den İkon / İkon+Metin seçilebilir): Sil (önce çöp kutusuna taşır), Gizle, Metadata Düzenle, Dosya Konumunu Aç, Web'de Ara, Sürüm listesinden "Preferred yap", Metadata'yı Yeniden Eşleştir. Çoklu seçimde sadece çakışmayan toplu eylemler (favori/gizle/sil/playlist) gösterilir.
- Kullanıcının oluşturduğu sınırsız playlist + sabit Favorites/Hidden/Recycle Bin, tümü tablonun üstünde tıklanabilir chip/etiket olarak.
- Sağ detay panelinde seçili oyunun tüm sürümleri (bölge/kaynak/hash) ve tercih edilen sürümü değiştirme.

### Pencereler
- **MainWindow** — Platform listesi, sanallaştırılmış oyun tablosu (DataGrid), playlist chip şeridi, seçili oyun detay paneli ve BAŞLAT butonu.
- **MediaProviderWindow** — Eksik medya (kutu/arkaplan/ekran görüntüsü) için arama sonucu kartları; kartlar sürükle-bırak ile eksik öğe listesine uygulanabiliyor.
- **CropEditorDialog** — Görsel kırpma oranı seçim arayüzü.
- **EditMetadataWindow** — Bir oyunun Başlık/Tür/Açıklama/Notlar/Yayıncı/Geliştirici alanlarını elle düzenleme.
- **SettingsWindow** — RetroAudit Data kök dizini, LaunchBox metadata veritabanı yolu, platform başına emülatör yolu/parametreleri, bölge önceliği, arayüz tercihleri (komut menüsü görünümü), Export/Import Config (JSON).

## Gereksinimler

- .NET 9 SDK (`winget install Microsoft.DotNet.SDK.9`)
- Windows (WPF)
- WPF uygulamasını anlamlı veriyle çalıştırmak için: No-Intro/Redump/TOSEC DAT klasörü + bir LaunchBox kurulumu (`LaunchBox.Metadata.db`) — bkz. aşağıdaki "Çalıştırma" bölümü.

## Çalıştırma

Önce katalogu üret (bir kere, veya DAT/LaunchBox verisi güncellendiğinde tekrar):

```
dotnet run --project RetroAudit.Builder -- --dat-folder "<DAT klasörü yolu>" --launchbox-db "<LaunchBox.Metadata.db yolu>"
```

Argümanlar verilmezse Builder bu makinedeki varsayılan yolları dener; `--platform "Nintendo - Nintendo Entertainment System"` ile tek platform, `--output <yol>` ile çıktı konumu override edilebilir. Varsayılan çıktı `%LOCALAPPDATA%\RetroAudit\RetroAudit.db`.

Sonra WPF uygulamasını çalıştır (aynı `%LOCALAPPDATA%\RetroAudit\RetroAudit.db`'yi okur):

```
dotnet build
dotnet run --project RetroAudit
```

## Proje yapısı

```
RetroAudit.Catalog/    DAT ayrıştırma, 1G1R gruplama, LaunchBox eşleştirme, SQLite şeması (Builder ve WPF'nin ortak referansı)
RetroAudit.Builder/    RetroAudit.db'yi üreten komut satırı aracı (yukarıdaki "Çalıştırma" bölümüne bkz.)
RetroAudit/            WPF uygulaması
  Models/              Game, Platform, AppSettings, ColumnFilter, PlaylistChip gibi veri modelleri
  Services/            CatalogDatabaseService (RetroAudit.db okuma), UserDataService (RetroAuditUserData.db), config servisi
  ViewModels/          MVVM ViewModel'ler (CommunityToolkit.Mvvm ObservableObject/RelayCommand)
  Views/               XAML pencereleri
  Converters/           Value converter'lar
  Themes/              Koyu tema (Obsidian) kaynak sözlüğü
```

## Yol haritası

- [x] DAT (No-Intro/Redump/TOSEC) tabanlı RetroAudit.db üretimi (Builder)
- [x] WPF'nin gerçek RetroAudit.db'yi okuması + kalıcı kullanıcı verisi (favori/gizle/playlist/metadata)
- [ ] Gerçek ROM tarama / hash kontrolü (şu an "dosya var mı" kontrolü sadece isim eşleşmesine bakıyor)
- [ ] Emülatör başlatma mantığı (BAŞLAT butonu henüz bağlı değil)

## Lisans

[MIT](LICENSE)
