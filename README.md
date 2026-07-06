# RetroAudit

Retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM (CommunityToolkit.Mvvm) ile geliştiriliyor.

📝 [Changelog](CHANGELOG.md) • 🤖 [AI Guide](AGENTS.md) • 📄 [License](LICENSE)

## Mevcut durum (v0.21)

Uygulama artık **gerçek bir DAT tabanlı katalogla** çalışıyor — `Services/MockDataService.cs` tamamen kaldırıldı. Sistem iki ayrı parçadan oluşuyor:

1. **RetroAudit.Builder** (komut satırı) — No-Intro/Redump/TOSEC DAT dosyalarını + LaunchBox metadata veritabanını okuyup tek bir offline katalog (`RetroAudit.db`, SQLite) üretir. Platform başına tek kaynak kuralı uygulanır (kartuş/el konsolları No-Intro, optik disk platformları Redump, ev bilgisayarları TOSEC), 1G1R (bölge önceliği USA > Europe > World > Japan) mantığıyla tekilleştirir, dağıtım varyantlarını (3DS Digital, PSN, vb.) ana platforma birleştirir ve Casino/Quiz/Board Game gibi oyun-olmayan içerikleri varsayılan gizli işaretler.
2. **RetroAudit** (WPF) — Builder'ın ürettiği `RetroAudit.db`'yi salt-okunur okur ve kullanıcı etkileşimlerini (favori, gizle, çöp kutusu, playlist, metadata düzenleme) **ayrı bir veritabanında** (`RetroAuditUserData.db`) tutar — çünkü `RetroAudit.db` her Builder çalıştırmasında tamamen silinip yeniden üretiliyor ve `GameId` kalıcı değil. Oyunlar `Platform|CompareTitle` şeklinde sabit bir anahtarla eşleştirilir, böylece kullanıcı verisi bir sonraki Builder koşusunda kaybolmaz.

Tasarım dili: Visual Studio / Obsidian tarzı koyu tema (bkz. `RetroAudit/Themes/ObsidianDark.xaml`).

### Özellikler
- Sol panelde kategoriye göre gruplanmış ~40 platform (sade isimlerle, ör. "Nintendo 64"), sanallaştırılmış oyun tablosu (67 binin üzerinde oyun).
- Sütun başlığına **sol tık**: arama kutulu filtre + "Sırala A-Z/Z-A" dropdown'ı açılır (Title/File gibi neredeyse hiç tekrarlamayan sütunlarda sadece arama, Enter'a basınca uygulanır). **Sağ tık**: sütun görünürlüğünü aç/kapa ve sola/sağa sabitle (pin) — tercihler diske kaydedilip hatırlanır. Sütunlar sürükleyip yeniden sıralanabilir, bu sıra da kalıcıdır.
- Favori/Ara-Başlat birleşik **Actions** sütunu ve her zaman en solda sabit ayrı bir **Gizle** sütunu; playlist chip şeridinde Favorites/Hidden/Recycle Bin'in yanında **Ready to Play** / **Needs Search** hızlı filtreleri.
- ROM'u eksik bir oyun için tabloda doğrudan **uygulama içi (WebView2) arama penceresi** açılıyor; kullanıcının başlattığı indirme otomatik olarak oyunun platform klasörüne iniyor. Kendi ROM arşivinizi (farklı bir klasör düzeninde) **toplu içe aktarma** penceresiyle tarayıp Taşı/Kopyala/Referans modlarından biriyle (opsiyonel CRC32 doğrulamayla) tek seferde ekleyebilirsiniz.
- LaunchBox'tan Release Date, Topluluk Puanı (+ oy sayısı), YouTube/Wikipedia bağlantıları, Steam App ID ve Cooperative bilgisi de okunup detay panelinde gösteriliyor.
- **Görsel Getir**: eksik Box/Screenshot/Clear Logo görselleri tekli veya toplu olarak indirilip yerel `Images\` klasörüne yazılıyor (otomatik küçültme + sıkıştırma, Ayarlar'dan boyut seçilebilir). Fanart/Background artwork türü tamamen kaldırıldı. Platform logoları sol panelde değil, detay panelinde (tıklanınca ROM klasörünü açan bir rozet olarak) gösteriliyor.
- **Kırpma/yeniden boyutlandırma**: Box art ve Clear Logo'ya sol tıklayınca gerçek bir kırpma editörü açılıyor (sürüklenebilir kırpma dikdörtgeni, isteğe bağlı en-boy kilidi, piksel küçültme seçenekleri); sağ tık ile o görselin dosya konumu açılabiliyor.
- **Top 250 / Top 100 / Top 25**: her platform için LaunchBox oy sayısına göre ağırlıklandırılmış (IMDb tarzı) sıralama; playlist chip'i olarak filtrelenebiliyor, uygun oyunlarda detay panelinde rozet olarak görünüyor.
- Sağ detay paneli: başlık + YouTube/Wikipedia ikon butonları üstte, altında Clear Logo, gameplay screenshot alanının altında Topluluk Puanı, görseli olmayan alanlar için sabit yer tutucular. Panel genişliği sabit — sadece toolbar düğmesiyle açılıp kapanıyor, bir oyun seçilene kadar gizli.
- **Embedded YouTube oynatıcı**: bir oyunun YouTube linki varsa gameplay screenshot alanında bir Play overlay'i beliriyor; tıklanınca dış tarayıcı AÇILMADAN aynı alanda (WebView2 + Plyr.io) video oynatılıyor, kapat butonuna basılınca veya başka oyun seçilince otomatik duruyor.
- Bölge/ülke bayrakları (LaunchBox kaynaklı) tabloda Region sütununda, Sürümler kartlarında ve Alternate Names satırlarında gösteriliyor; Region sütununun görünümü (Bayrak+Text/Text+Bayrak/Sadece Bayrak/Sadece Text) Ayarlar > Arayüz'den seçilebiliyor.
- Ayarlar penceresindeki değişiklikler artık **canlı** yansıyor (Kaydet'e basmadan ana pencerede anında görünüyor); Kaydet hâlâ sadece diske kalıcı yazmak için gerekli.
- Sağ tık ile açılan kapsül biçimli komut menüsü (Ayarlar > Arayüz'den İkon / İkon+Metin seçilebilir): Sil (önce çöp kutusuna taşır), Gizle, Metadata Düzenle, Dosya Konumunu Aç, Web'de Ara, Sürüm listesinden "Preferred yap", Metadata'yı Yeniden Eşleştir. Çoklu seçimde sadece çakışmayan toplu eylemler (favori/gizle/sil/playlist) gösterilir.
- Kullanıcının oluşturduğu sınırsız playlist + sabit Favorites/Hidden/Recycle Bin, tümü tablonun üstünde tıklanabilir chip/etiket olarak.
- Sağ detay panelinde seçili oyunun tüm sürümleri (bölge/kaynak/hash) ve tercih edilen sürümü değiştirme; Ayarlar > Emülatörler'de platform başına tanımlanan emülatörle BAŞLAT butonu üzerinden doğrudan oynatma.

### Pencereler
- **MainWindow** — Platform listesi, sanallaştırılmış oyun tablosu (DataGrid), playlist chip şeridi, seçili oyun detay paneli ve BAŞLAT butonu.
- **RomSearchWindow** — "Ara" butonuyla açılan, uygulama içi (WebView2) ROM arama penceresi; indirmeler otomatik olarak doğru platform klasörüne yönlendirilir.
- **RomImportWindow** — Kendi ROM arşivinizi tarayıp toplu Taşı/Kopyala/Referans ile içe aktarma, opsiyonel hash doğrulaması.
- **MediaProviderWindow** — Eksik medya (kutu/arkaplan/ekran görüntüsü) için arama sonucu kartları; kartlar sürükle-bırak ile eksik öğe listesine uygulanabiliyor.
- **CropEditorDialog** — Görsel kırpma oranı seçim arayüzü.
- **EditMetadataWindow** — Bir oyunun Başlık/Tür/Açıklama/Notlar/Yayıncı/Geliştirici alanlarını elle düzenleme.
- **SettingsWindow** — Veri klasörü (salt-okunur, her zaman .exe'nin yanında), görsel indirme boyutu, LaunchBox metadata veritabanı yolu, platform başına emülatör yolu/parametreleri, bölge önceliği, arayüz tercihleri (komut menüsü görünümü, satır yüksekliği, platform listesi görünümü/kategorileri) — hepsi **Kaydet** düğmesiyle diske yazılıyor. Export/Import Config (JSON).

## Gereksinimler

- .NET 9 SDK (`winget install Microsoft.DotNet.SDK.9`)
- Windows (WPF)
- WPF uygulamasını anlamlı veriyle çalıştırmak için: No-Intro/Redump/TOSEC DAT klasörü + bir LaunchBox kurulumu (`LaunchBox.Metadata.db`) — bkz. aşağıdaki "Çalıştırma" bölümü.

## Çalıştırma

Önce katalogu üret (bir kere, veya DAT/LaunchBox verisi güncellendiğinde tekrar):

```bash
dotnet run --project RetroAudit.Builder -- --dat-folder "<DAT klasörü yolu>" --launchbox-db "<LaunchBox.Metadata.db yolu>"
```

Argümanlar verilmezse Builder bu makinedeki varsayılan yolları dener; `--platform "Nintendo - Nintendo Entertainment System"` ile tek platform, `--output <yol>` ile çıktı konumu override edilebilir. Varsayılan çıktı, taşınabilir veri düzeni gereği WPF uygulamasının kendi `.exe`'sinin yanındaki `Metadata\RetroAudit.db` (bkz. `AppPaths`).

Sonra WPF uygulamasını çalıştır (aynı `Metadata\RetroAudit.db`'yi okur):

```bash
dotnet build
dotnet run --project RetroAudit
```

## Proje yapısı

```text
RetroAudit.Catalog/    DAT ayrıştırma, 1G1R gruplama, LaunchBox eşleştirme, SQLite şeması (Builder ve WPF'nin ortak referansı)
RetroAudit.Builder/    RetroAudit.db'yi üreten komut satırı aracı (yukarıdaki "Çalıştırma" bölümüne bkz.)
RetroAudit/            WPF uygulaması
  Models/              Game, Platform, AppSettings, ColumnFilter, PlaylistChip gibi veri modelleri
  Services/            CatalogDatabaseService (RetroAudit.db okuma), UserDataService (RetroAuditUserData.db), config servisi
  ViewModels/          MVVM ViewModel'ler (CommunityToolkit.Mvvm ObservableObject/RelayCommand)
  Views/               XAML pencereleri
  Converters/          Value converter'lar
  Themes/              Koyu tema (Obsidian) kaynak sözlüğü
```

## Yol haritası

- [x] DAT (No-Intro/Redump/TOSEC) tabanlı RetroAudit.db üretimi (Builder)
- [x] WPF'nin gerçek RetroAudit.db'yi okuması + kalıcı kullanıcı verisi (favori/gizle/playlist/metadata)
- [x] Emülatör başlatma mantığı (Ayarlar > Emülatörler'de tanımlanan yol ile BAŞLAT butonu)
- [x] Toplu ROM içe aktarma (Taşı/Kopyala/Referans + opsiyonel CRC32 hash doğrulama)
- [ ] Günlük "dosya var mı" kontrolü hâlâ sadece dosya adına bakıyor — sürekli/otomatik hash
      doğrulaması yok, hash kontrolü şu an sadece toplu içe aktarma sırasında opsiyonel bir adım
- [ ] Medya (kutu/arkaplan/ekran görüntüsü) için de ROM içe aktarmaya benzer toplu bir yerel
      klasör taraması yok — MediaProviderWindow şu an sadece çevrimiçi arama sonucu kartlarıyla çalışıyor

## Lisans

[MIT](LICENSE)
