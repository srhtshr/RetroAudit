# RetroAudit

Retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM (CommunityToolkit.Mvvm) ile geliştiriliyor.

📝 [Changelog](CHANGELOG.md) • 🤖 [AI Guide](AGENTS.md) • 📄 [License](LICENSE)

## Mevcut durum (v0.28)

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
- **Emülatör indirme altyapısı**: tüm standalone emülatörler (PCSX2, RPCS3, Xemu, Cemu, melonDS, Vita3K, Xenia Canary, Dolphin, Ryujinx, Azahar) tek, veri tabanlı bir "İndir & Kur" sistemini paylaşıyor — Ayarlar > Komutlar'daki **Emülatör İndirme Kaynakları** listesinden her biri için Kaynak Türü (GitHub Releases API / Direct URL / RPCS3'e özel Yerleşik API) ve Kaynak adresi düzenlenebiliyor, değişiklik programı yeniden derlemeden Kaydet ile hemen etkin oluyor. Core Adı dropdown'ında seçim artık gerçekten kullanılan core/exe'yi belirliyor; silinmiş/eksik core'lar listede silik gösterilip yeniden indirilebiliyor.
- **Eksik görsel bulucu (Media Provider)**: artık gerçek kütüphaneden hesaplanan eksik Box/Clear Logo/Ekran Görüntüsü listesiyle çalışıyor — "Otomatik İndir" ana tablonun "Görsel Getir"iyle aynı mekanizmayı kullanıyor (çoklu seçim destekli), "Ara" uygulama içi (embedded WebView2) bir arama penceresi açıyor. Junk oyunları isteğe bağlı dahil edilebiliyor.
- **Tıklanabilir Tür rozetleri + aktif filtre rozetleri**: Tablodaki Türler sütunu rozet olarak gösteriliyor, tıklanınca o türe göre filtreliyor (birden fazlası birikiyor); üstteki "Görünen/Toplam" satırında o an uygulanan her filtre değeri ayrı bir rozet olarak görünüp tek tıkla kaldırılabiliyor.
- **Manuel bağlama / standalone oyunlar**: Kataloğun hiçbir sürümüyle eşleşmeyen ROM'lar (Eşleşmeyenler sekmesinden "+ Yeni Oyun" ile, toplu "Eşleşmeyenleri de aktar" ile, ya da ana tablodaki kapsül menüsündeki "Bağla" ile tablodan başka bir oyuna taşınarak) katalogdan bağımsız birer oyun olarak kütüphaneye eklenebiliyor — CRC32'si hesaplanıp gösteriliyor, "manuel"/sarı rozetle işaretleniyor (CRC doğrulanmış gibi sunulmuyor), aynı isimli kayıtlar otomatik tek satırda toplanıyor.
- **Kalıcı Sil, gerçek dosyaları da temizleyebiliyor**: hangi dosya türlerinin (ROM/Box/SS/Logo) de silineceğini seçebileceğiniz bir onay penceresiyle, işaretli dosyalar Windows Çöp Kutusu'na taşınıyor (kalıcı `File.Delete` değil) — Çöp Kutusu'nda çoklu seçimle de yapılabiliyor.
- **Gameplay videosundan anlık görüntü**: Embedded YouTube oynatıcısında bir kamera butonu, o an oynayan kareyi Screenshot (SS) olarak kaydediyor; video gerçekten oynamıyorsa kare yakalanmıyor.
- **"Bağla" artık ROM'suz da çalışıyor**: dosyası olmayan bir oyun da hedefe sahipsiz (kırmızı çarpı) bir sürüm kartı olarak eklenip kaynak satır tablodan gizlenebiliyor; sürüm kartındaki "Ayır" butonuyla geri alınabiliyor.
- Üst arama kutusu alternatif isimlerde de arıyor ve aktif arama metni kaldırılabilir bir filtre rozeti olarak görünüyor; Durum filtresinde manuel bağlı oyunlar için ayrı bir kategori var.
- **Filtre menüleri tıklanabilir rozet/hap görünümünde**: Durum/Platform/Türler/Yayıncı/Bölge ve Actions popup'ı dahil tüm filtre dropdown'ları — çoklu seçime izin veriyor, tıklama anında uygulayıp popup'ı kapatıyor. "Bağla" arama kutusu artık alternatif isimlerde de arıyor. Custom oyunlar artık aynı dosya yolu/CRC32/alternatif isim eşleşmesiyle de gerçek katalog karşılığına otomatik katlanıyor.

### Pencereler
- **MainWindow** — Platform listesi, sanallaştırılmış oyun tablosu (DataGrid), playlist chip şeridi, seçili oyun detay paneli ve BAŞLAT butonu.
- **RomSearchWindow** — "Ara" butonuyla açılan, uygulama içi (WebView2) ROM arama penceresi; indirmeler otomatik olarak doğru platform klasörüne yönlendirilir.
- **RomImportWindow** — Kendi ROM arşivinizi tarayıp toplu Taşı/Kopyala/Referans ile içe aktarma, opsiyonel hash doğrulaması.
- **MediaProviderWindow** — Kütüphanedeki eksik Box/Clear Logo/Ekran Görüntüsü öğelerinin gerçek listesi; her öğe için Otomatik İndir (LaunchBox kaynağı) veya Ara (embedded arama) ile tek tek çözülebiliyor.
- **MediaSearchWindow** — "Ara" ile açılan, uygulama içi (WebView2) görsel arama penceresi; indirilen dosya doğrudan ilgili oyunun ROM dosya adıyla eşleşen konuma kaydediliyor.
- **CropEditorDialog** — Görsel kırpma oranı seçim arayüzü.
- **EditMetadataWindow** — Bir oyunun Başlık/Tür/Açıklama/Notlar/Yayıncı/Geliştirici alanlarını elle düzenleme.
- **ManualLinkWindow** — Bir ROM dosyasını kataloktaki bir oyuna (isterse belirli bir sürümüne), yeni bir standalone oyuna ("+ Yeni Oyun") ya da tablodaki başka bir oyuna manuel bağlama; arama kutulu, varsayılan olarak ilgili platforma sınırlı oyun listesi.
- **PermanentDeleteConfirmationDialog** — Çöp Kutusu'ndan kalıcı silmeden önce hangi dosya türlerinin (ROM/Box/SS/Logo) de Windows Çöp Kutusu'na taşınacağını seçme.
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
