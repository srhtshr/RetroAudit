# Changelog

Bu proje küçük, sık sürümlerle ilerler (0.01, 0.02, ...). Henüz bir SemVer/1.0 taahhüdü yoktur.

## [0.24] - 2026-07-10

### Added
- **Gameplay ve Kapak Görselini Sağ Tık Menüsünden Silme**: Klasör yollarına gidip manuel silme zahmetini ortadan kaldırmak amacıyla, oyun detaylarındaki Kapak ve Gameplay görselleri için doğrudan sağ tık menüsüne silme komutu eklendi.

### Changed
- **Provider Pencerelerinin Düzeni ve Genişliği**: Görsel taşmaları önlemek ve sağdaki eksik öğeler tablosuna daha geniş bir alan açmak amacıyla sol panel genişlikleri 780px olarak eşitlendi; kart içi boşluklar ve rozetler arası mesafeler daraltılarak butonların tek satıra sığması sağlandı.
- **Akıcı Pencere Geçişleri (Seamless Transition)**: Media ve Metadata Provider pencereleri arasındaki geçişlerdeki sarsıntı ve yanıp sönmeyi sıfırlamak için, eski pencere ancak yeni pencere tamamen ekrana çizilip Windows animasyonu bittikten sonra kapatılacak şekilde (120ms gecikmeli) güncellendi.

### Fixed
- **Kapatma Sonrası Odak Kaybı**: Provider pencereleri kapatıldığında uygulamanın minimize olduğu veya arkaya düştüğü izlenimini önlemek amacıyla, kapanma anında ana pencerenin (MainWindow) aktif edilip öne getirilmesi sağlandı.
- **Görsel Arama Kaydetme Kilitlenmesi**: Görsel indirildikten sonra kütüphanenin taze görseli hemen render edememesi ve dosyanın kullanımda olması (kilitlenme) sebebiyle silinememe hatası, WPF görsel önbelleğinin kapatılması (`BitmapCacheOption.OnLoad`) ile giderildi.
- **Resmi Kaydet/Videoyu Kaydet Butonları**: Arama penceresinde indirme butonlarının pasif kalması ve çalışmama hatası düzeltildi; URL kopyalama butonu gibi pulse efekti kazandırılarak görünürlüğü artırıldı.

## [0.23] - 2026-07-09

### Added
- **Arayüzde CRC32 Görünürlüğü**: Sürümler panelindeki dosyaların kimliğini ve doğruluğunu netleştirmek amacıyla CRC32 hash bilgileri eklendi (dosyaların kopyalama/doğrulama kontrolünü kolaylaştırmak için).
- **Otomatik Arayüz Yenilemesi**: Toplu ROM içe aktarma işlemi sonrasında kütüphanedeki oyunların durumunu görmek için uygulamayı kapatıp açma zorunluluğunu ortadan kaldırmak amacıyla, içe aktarım tamamlandığında kütüphane indeksini ve oynanabilirlik durumlarını canlı olarak tazeleyen yenileme mekanizması eklendi.

### Changed
- **Güvenli Ryujinx Kaynağı**: get-download.opik.net üzerindeki sahte ve potansiyel olarak zararlı (adware/malware içeren) yükleyici yerine, emülasyon güvenliğini sağlamak amacıyla doğrudan topluluk tarafından doğrulanmış resmi Ryubing dağıtım kanalı indirme kaynağı olarak belirlendi.

### Fixed
- **Boşluklu Yollarla Oyun Başlatma**: Emülatör parametrelerindeki mükerrer tırnaklama nedeniyle boşluklu klasör yollarındaki oyunların emülatör tarafından bölünerek yüklenememesi hatası giderildi (oyunların sorunsuz başlatılabilmesi için).

## [0.22] - 2026-07-08

### Added
- **Emülatör indirme altyapısı generic hale getirildi**: Cemu, melonDS, Vita3K, RPCS3, Xemu ve
  PCSX2'de ayrı ayrı yazılmış GitHub Releases asset-seçim kodu tek, paylaşılan bir algoritmaya
  (`StandaloneEmulatorInstallerService.ResolveGitHubReleaseWindowsAssetAsync`) indirildi — "windows"
  içeren, sembol/hata ayıklama/ARM/libretro asset'lerini hariç tutan adaylar arasından uzantı
  önceliğine (.7z > .zip > .exe) göre seçim yapılıyor.
- Ayarlar > Komutlar sekmesine **Emülatör İndirme Kaynakları** eklendi: her standalone emülatör
  için Kaynak Türü (GitHub Releases API / Direct URL / RPCS3'e özel Yerleşik API) ve Kaynak (tam
  URL) düzenlenebiliyor; değişiklik diske kaydedilip her indirmede TAZE okunuyor — kaynağı
  değiştirmek programı yeniden derlemeyi gerektirmiyor.
- Cemu (Wii U), melonDS (Nintendo DS), Vita3K (PS Vita) ve Xenia Canary (Xbox 360) için otomatik
  GitHub Releases indirme desteği eklendi.
- Nintendo 3DS: "Lime3DS" yerine artık **Azahar** gösteriliyor (proje GitHub'da bu isimle devam
  ediyor) — eski kayıtlı seçimler geriye dönük uyumlu şekilde göçürülüyor.
- Dolphin (GameCube/Wii) ve Ryujinx (Switch) için resmi bir GitHub Releases kaynağı olmadığından
  Direct URL ile gerçek indirme linkleri tanımlandı; boş bırakılırsa "İndir & Kur" artık "İndirme
  kaynağı tanımlı değil" durumunu net şekilde gösteriyor (sessizce başarısız olmuyor).
- Core Adı dropdown'ında seçim artık **gerçekten** kullanılan core/exe'yi belirliyor (önceden
  BAŞLAT her zaman ilk indirilen core'u kullanıyordu); "+" ile özel core/exe eklenebiliyor, eklenen
  RetroArch core'ları otomatik olarak o platformun kendi klasörüne taşınıyor. Silinmiş/eksik
  core'lar listeden kaybolmuyor, silik gri gösterilip yeniden indirilebiliyor. "Core Adı listesi:
  Sadece Önerilen/Alternatif veya Hepsini Göster" seçeneği ve "Önerilenlere Sıfırla" butonu eklendi.
- İlerleme çubuğu artık arşiv açma aşamasında da (gerçek bir yüzde olmadığından) belirsiz
  (indeterminate) modda animasyonlu çalışıyor.
- **Media Provider penceresi gerçek işlevsellik kazandı**: önceki tamamen mock (sabit sahte liste +
  simülasyon indirme) hâli kaldırıldı — artık kütüphaneden gerçekten hesaplanan eksik Box/Clear
  Logo/Ekran Görüntüsü listesiyle çalışıyor, "Otomatik İndir" gerçekten LaunchBox kaynağından
  indiriyor, "Ara" yeni **MediaSearchWindow** ile uygulama içi (embedded WebView2) arama açıyor.
- Oyun detay panelinde tek bir "eksik görsel" rozeti/butonu eklendi — üç görselden biri bile
  eksikse görünüyor, tıklanınca hangi türlerin getirileceği seçilebiliyor.

### Changed
- Emülatör kurulumları platform bazlı değil emülatör kimliği bazlı paylaşılıyor
  (`ThirdParty/Emulation/{EmulatorId}/`) — aynı emülatör birden fazla platform satırından
  atanabiliyorsa tek kurulum yeterli.
- Top 100 / Top 25 / Top 250 rozetleri: tek bir platform seçiliyken artık o platformun kendi
  sıralamasına göre hesaplanıyor — önceden "Tüm Platformlar" havuzundan hesaplandığı için bir oyun
  Top 100 listesinde görünürken rozetinde yanlışlıkla "Top 25" yazabiliyordu.
- DAT ayrıştırma: oyun adı (game name) temiz göründüğü hâlde ROM dosya adında "(Unl)"/"(Aftermarket)"
  gibi dışlanan bir etiket varsa kayıt artık yine tamamen eleniyor (önceden sadece oyun adına
  bakılıyordu) — Build raporunda bu durum ayrı sayılıyor.

### Fixed
- "Tüm Platformlar" seçimi artık doğru şekilde kalıcı hale getiriliyor (silinmiş/geçersiz bir
  platform adı saklanmıyordu).

## [0.21] - 2026-07-06

### Added
- **Embedded YouTube oynatıcı**: gameplay screenshot alanında bir Play overlay'i, tıklanınca dış
  tarayıcı AÇMADAN aynı alanda (WebView2 + Plyr.io skin'i) videoyu embed olarak oynatıyor; kapat
  butonuna basılınca veya başka oyun seçilince otomatik duruyor. YouTube'un embed doğrulamasının
  reddettiği "Hata 153" sorunu, `player.html`'i `SetVirtualHostNameToFolderMapping` ile gerçek bir
  https origin'i altında sunarak; sesli autoplay ise ayrı bir `CoreWebView2Environment`'a
  `--autoplay-policy=no-user-gesture-required` argümanı vererek çözüldü. Video 1. dakikadan
  başlıyor, 4:3 içerik yandaki siyah bantları azaltmak için hafifçe yatayda geriliyor. Başlık
  satırındaki YouTube butonu artık linki olmayan oyunlarda da görünür (sadece pasif/soluk).
- **Gerçek kırpma/yeniden boyutlandırma**: Box art ve Clear Logo'ya sol tıklayınca (sürüklenebilir
  kırpma dikdörtgeni, köşe tutamaçları, isteğe bağlı en-boy kilidi, piksel küçültme seçenekleriyle)
  gerçek bir kırpma editörü açılıyor; sağ tık ile "Klasöre Git" ile o görselin dosya konumu
  açılabiliyor.
- **Fanart tamamen kaldırıldı**: kod, veritabanı şeması ve daha önce indirilmiş dosyalar dahil —
  artwork türleri artık sadece Box/Screenshot/Clear Logo.
- LaunchBox'ın `ReleaseType` alanı okunuyor; Homebrew/ROM Hack/Unlicensed/Unreleased/DLC/Early
  Access olarak işaretli eşleşmeler otomatik olarak Junk (varsayılan gizli) işaretleniyor.
- Region/ülke bayrakları (LaunchBox CDN kaynaklı, 32 bayrak) indirilip tabloda Region sütununda,
  Sürümler kartlarında ve Alternate Names satırlarında gösteriliyor. Region sütununun görünümü
  (Bayrak+Text/Text+Bayrak/Sadece Bayrak/Sadece Text) Ayarlar > Arayüz'den seçilebiliyor.
- Ayarlar > Arayüz'e yeni bir "Sürümler" görünüm tercihi eklendi: tek kart + açılır listeden seçim
  veya tüm sürümlerin tam listesi.
- Ayarlar penceresi artık **canlı** yansıyor: bir tercih değiştirildiğinde Kaydet'e basmadan ana
  pencerede anında görünüyor; Kaydet hâlâ sadece diske kalıcı yazmak için gerekli.
- "Ayarlar" düğmesi Tools açılır menüsünden ayrılıp toolbar'da kendi başına, tek tıkla erişilen bir
  düğme oldu.

### Changed
- `VersionResolver.NormalizeForCompare`, LaunchBox'ın kendi `CompareName` normalizasyon algoritması
  (183 binin üzerinde gerçek isim çifti üzerinden tersine mühendislikle) yeniden yazıldı — stopword
  temizliği, Roma rakamı dönüşümü (II-VIII) ve noktalama kuralları eklendi. Bölge varyantı
  birleştirme (ör. "1943 - The Battle of Midway"/"...Valhalla") artık doğru çalışıyor; eşleşen oyun
  sayısı 43.495'ten 45.540'a çıktı.
- Detay panelindeki Alternate Names bölümü artık her zaman görünür (isim yoksa "-" gösteriyor),
  minimum 2 satır yer kaplıyor ve fazlası bir açılır menüde listeleniyor — Box art'ın oyunlar
  arasında geçişte zıplaması önlendi.
- Box art artık sabit yükseklikte; Details sütunundaki değişken satırlara (Publisher/Steam AppID
  vb.) bağlı olarak boyu oynamıyor.
- Gameplay screenshot alanı biraz daha yüksek (230 → 260px).

### Fixed
- Ayarlar penceresindeki RadioButton grupları (Sağ Tık Menüsü/Sürümler/Platform Listesi/Bölge
  Sütunu) `GroupName` belirtilmediği için WPF tarafından tek bir grup sayılıyor, birbirini
  işaretsiz bırakıyordu — her gruba ayrı `GroupName` eklendi.
- Ayarlar penceresi "Kaydet" sonrası kapatılınca ana pencere bazen simge durumuna küçülüp
  arkada kalıyordu (WindowChrome + Owner zinciri etkileşimi) — kapanışta ana pencere artık
  açıkça eski haline getirilip öne alınıyor.
- Ayarlar penceresi kapanınca çalışan gereksiz tam dosya/artwork yeniden tarama işlemi (artık veri
  klasörü konumu Ayarlar'dan değiştirilemediği için anlamsızdı) kaldırıldı — algılanabilir bir
  donma/"kapanıp açılma" hissi veriyordu.
- Ayarlar penceresinin Genel/Arayüz sekmelerinde içerik pencere boyunu aşınca kaydırma çubuğu
  yoktu, alttaki alanlara ulaşılamıyordu — ScrollViewer eklendi.
- Clear Logo/gameplay screenshot fallback mantığı: Clear Logo var ama gameplay yoksa artık her
  zaman gerçek logo gösteriliyor (önceden bazı durumlarda yanlışlıkla yer tutucu görseli
  gösteriliyordu).

## [0.20] - 2026-07-05

### Added
- **Taşınabilir veri düzeni**: Games/Images/Metadata/Emulation klasörleri artık her zaman
  uygulamanın kendi .exe'sinin yanında — kullanıcı tarafından seçilebilen `RetroAuditDataPath`
  ayarı kaldırıldı. `RetroAuditUserData.db` eski `%LocalAppData%\RetroAudit\` konumundan otomatik
  (tek seferlik) taşınıyor.
- **Görsel Getir**: tekli (Actions sütunu) ve toplu (sağ tık kapsülü) komutlarla Box/Background/
  Screenshot/Clear Logo görselleri LaunchBox kaynağından indirilip yerel `Images\` klasörüne
  yazılıyor; indirme sırasında alt durum çubuğunda % gösteren ilerleme çubuğu. İndirilen görseller
  netlik kaybı olmadan Ayarlar > Genel'de seçilen boyuta (600px/800px/Original) küçültülüp
  (şeffaflık gerekmeyen türler için) JPEG'e çevriliyor — küçük/basit görsellerde yeniden kodlama
  orijinalden büyük çıkarsa orijinal korunuyor.
- Sol panelde kullanılan ~40 platform için gerçek **platform logoları** (`Images/Platforms/`) —
  detay panelinde Box'ın üstünde ayrı bir rozet olarak gösteriliyor, tıklanınca o platformun ROM
  klasörünü açıyor.
- **Top 250 / Top 100 / Top 25**: LaunchBox'ın `CommunityRatingCount` (oy sayısı) alanı artık
  içe aktarılıyor ve IMDb'nin Top 250'sinde kullanılanla aynı ağırlıklı (Bayesian) ortalamayla
  her platform için ayrı ayrı hesaplanıyor — az oyla yüksek puan almış oyunlar platform
  ortalamasına çekiliyor. Playlist chip şeridine üç yeni filtre eklendi; bir oyun bu sıralamalardan
  birine giriyorsa detay panelinde ilgili rozet (`Images/Badges/`) gösteriliyor.
- Detay paneli yeniden tasarlandı: başlık artık ayrı bir üst satırda (YouTube/Wikipedia ikon
  butonlarıyla aynı hizada, sağ üstte), Clear Logo kendi kartında (gameplay alanının üstünde),
  Topluluk Puanı fanart'ın sağ alt köşesinde, görseli olmayan Box/Background/Screenshot için
  `Images/NoImage/` altında sabit yer tutucular.
- Ayarlar penceresine **Kaydet** düğmesi — ayarlar artık her değişiklikte sessizce değil, bu
  düğmeye (veya Export'a) basıldığında diske yazılıyor. Bu arada Emülatörler/Bölge Önceliği/
  Komutlar sekmelerinin hiçbir zaman kalıcı olmadığı (sadece JSON export/import ile taşınabildiği)
  fark edildi ve düzeltildi.

### Changed
- Sağ detay paneli artık sabit genişlikte — kenarından tutup sürüklenerek yeniden
  boyutlandırılamıyor, sadece toolbar'daki düğmeyle açılıp kapanıyor. Platform listesinde
  gezinirken (henüz bir oyun seçilmemişken) boş görünmek yerine tamamen gizleniyor; bir oyuna
  tıklanınca otomatik açılıyor.
- Sürüm kartları (Sürümler listesi) daha kompakt: kaynak etiketi ("no-intro" vb.) artık kendi
  satırı yerine üst satırın sağında, CRC32 kodları kaldırıldı.
- DataGrid'deki Logo sütunu genişletildi ve yüksek kaliteli ölçekleme (`BitmapScalingMode`)
  eklendi — geniş/yatay Clear Logo'lar artık küçük kare bir alana sıkışıp bulanıklaşmıyor.

### Fixed
- "Görsel Getir" ile indirilen bir görsel, uygulama yeniden başlatılana kadar grid/detay
  panelinde görünmüyordu (bellek içi medya indeksi diskten sadece açılışta okunuyordu) —
  indirme tamamlanır tamamlanmaz indeks güncelleniyor.
- Sağ paneldeki Sürümler listesinin üzerine gelince fare tekerleği dıştaki panel yerine hiçbir
  yeri kaydırmıyordu (iç kaydırması bilinçli kapalı ama olayı yutuyordu) — olay artık üst
  ScrollViewer'a yönlendiriliyor.
- YouTube/Wikipedia ikon butonları, yanlarındaki BAŞLAT butonundan daha kısa oldukları için
  StackPanel'in dikey gerdirmesiyle üstten/alttan taşıyordu — artık sabit boyutlu, çerçevesiz
  "widget" görünümünde.

## [0.19] - 2026-07-05

### Added
- LaunchBox'tan 6 yeni zenginleştirme alanı okunuyor: Release Date, Topluluk Yıldız
  Derecelendirmesi, VideoUrl (tıklanabilir YouTube butonu), WikipediaUrl (tıklanabilir Wikipedia
  butonu), Steam App ID ve Cooperative bilgisi; `GameAlternateTitles` de artık okunup
  `AlternateNames` tablosuna yazılıyor (eşleştirmede ileride kullanılabilir).
- **Uygulama içi ROM arama** (`RomSearchWindow`, WebView2 tabanlı): "Ara" butonu artık dış
  tarayıcı yerine RetroAudit içinde açılan bir pencerede arama yapıyor; kullanıcının kendi
  başlattığı indirmeler otomatik olarak oyunun `{Platform}\{Dosya}` klasörüne yönlendiriliyor
  ("Farklı Kaydet" diyaloğu hiç çıkmıyor). Platforma göre arşiv standardı etiketi
  (No-Intro/Redump/TOSEC/MAME, bkz. `RomSearchTagMap`) arama sorgusuna otomatik ekleniyor.
- **Toplu ROM İçe Aktarma** (`RomImportWindow`): kullanıcının kendi düzenindeki bir klasörü
  tarayıp dosya adına göre katalogla eşleştiriyor (tekil oyun başına .zip arşivleri dahil),
  Taşı / Kopyala / Referans (dosyayı olduğu yerde bırak, yeni `FilePathOverrides` tablosuyla)
  modlarından biri toplu uygulanabiliyor; opsiyonel CRC32 hash doğrulaması yanlış/bozuk
  dosyaları içe aktarımdan önce yakalıyor.
- RetroAudit Data dizini artık ilk açılışta otomatik bir varsayılan konum alıyor
  (`%LocalAppData%\RetroAudit\Data`) ve Ayarlar penceresinde gerçek, kalıcı bir alan — önceden
  "ileriki aşama için hazırlık" notuyla pasif duruyordu.
- DataGrid'e birleşik **Actions** sütunu: Favori ve Ara/Başlat butonları tek hücrede, filtreleri
  (Favori + Durum) tek bir popup'tan yönetiliyor. **Gizle** ayrı, her zaman en solda sabit bir
  sütun (sağ-tık pin/sürükleme dahil hiçbir kullanıcı eylemiyle yer değiştirmiyor).
- Sütun başlığına sağ tıklayıp **sola/sağa sabitleme (pin)** — sabitlenen alanın sınırını
  gösteren ince bir dikey çizgi. Sütunları sürükleyip yeniden sıralamak artık kalıcı (yeni
  `AppSettings.ColumnOrder`).
- Playlist chip şeridine **"Ready to Play"** (ROM'u olan) ve **"Needs Search"** (ROM'u eksik)
  filtreleri eklendi.
- Tüm ikincil pencerelere (Ayarlar, Metadata Düzenle, Medya Sağlayıcı, Kırpma, ROM Ara, ROM İçe
  Aktar) Windows'un immersive koyu başlık çubuğu uygulandı (`DarkTitleBarHelper`).
- Ayarlar > Arayüz'e satır yüksekliği kaydırıcısı ve platform listesi görünüm/kategori
  tercihleri taşındı — önceden ana penceredeki geçici, kalıcı olmayan kontrollerdi.

### Changed
- `ReleaseYear` boş/0 olan ama LaunchBox `ReleaseDate`'i dolu olan oyunlar artık tabloda ve
  Metadata'yı Yeniden Eşleştir sonrasında doğru yılı gösteriyor (önceden "0" görünüyordu).
- Sürüm (Released/Junk) ve Yıl/Tarih sütunları tablodan kaldırıldı — ilki zaten üst araç
  çubuğundaki Released/Junk butonlarıyla, ikincisi detay panelindeki tam tarihle karşılanıyordu.
- Box/BG/SS ve (Maks. Oyuncu'dan yeniden adlandırılan) Oyuncu sütunlarının başlığı ve içeriği
  ortalandı.
- ROM'u eksik/eşleşmeyen satırların soluklaştırılması artık SATIR değil HÜCRE bazlı uygulanıyor.

### Fixed
- Sütun sabitleme (pin) durumu birkaç ayrı kök nedenden dolayı uygulama kapatılıp açıldığında
  bozuluyordu: (1) sabitlemeyi kaldırmak sütunu eski konumuna döndürmüyor, komşu sütunların
  sırasını kalıcı olarak bozuyordu — artık her değişiklikte tüm sütunların konumu baştan
  hesaplanıyor; (2) sürükleyerek yapılan sıralama hiç kaydedilmiyordu; (3) pinli bir sütunu
  sürüklemek, WPF'in kendi "ilk N sütun donuk" konumsal modeliyle (Excel/Sheets'teki "bölmeleri
  dondur" gibi) artık doğru şekilde ele alınıyor — ne stale pin kayıtları sütunu yanlış yere
  zorluyor ne de pinli alan içinde ince ayar yapmak sabitlemeyi bozuyor.
- Soluklaştırmanın SATIR seviyesinde uygulanması, satırın kendi şablonundaki yatay ayırıcı
  çizgiyi de soldurup satırdan satıra farklı tonda çizgilere yol açıyordu — hücre seviyesine
  taşınarak çizgi artık tüm satırlarda aynı.
- Actions sütunu başlığı gerçekten ortalandı; favori yıldızının (★/☆ karakterinin font
  metriklerinden kaynaklanan) "aşağıda kalmış" görünümü düzeltildi; Actions/Gizle/Favori
  butonları arasındaki boşluk artırılıp üzerine gelindiğinde oluşan "yanıp sönme" (global buton
  stilinin tam dikdörtgen hover efekti, bitişik küçük butonlarda ardışık görünüp kayboluyordu)
  küçük, dairesel bir hover stiliyle giderildi.

## [0.18] - 2026-07-05

### Fixed
- README'deki "Mevcut durum" başlığı hâlâ v0.13'ü gösteriyordu ve özellik listesi Stage B
  sonrasında eklenen sütun filtre/sıralama/görünürlük yeniden tasarımını yansıtmıyordu — v0.17'ye
  ve güncel etkileşim modeline (sol tık: filtre+sırala, sağ tık: sütun görünürlüğü) güncellendi.

## [0.17] - 2026-07-05

### Added
- `AGENTS.md` eklendi — bu depoda çalışan AI ajanları için mimari özeti, iki-veritabanı
  invaryantı, ham/görünen platform adı ayrımı ve bu oturumda tekrar keşfedilmiş WPF tuzakları.
- README'nin en üstüne kısa bir gezinme satırı eklendi (Changelog / AI Guide / License).

### Fixed
- README'deki "Proje yapısı" kod bloğunda `Converters/` satırının hizası düzeltildi; kabuk
  komutu bloklarına `bash`/`text` dil etiketleri eklendi.

## [0.16] - 2026-07-05

### Added
- Sol paneldeki platform listesi artık sade isimler gösteriyor (ör. "Nintendo - Nintendo 64" yerine
  "Nintendo 64"); DataGrid'in Platform sütunu ve filtresi de aynı sade isimlere geçti. Ham DAT adı
  hâlâ filtreleme/eşleştirme/dosya yolu için kullanılıyor, sadece görünen metin değişti (bkz.
  `PlatformDisplayNameMap`).
- "PC Engine CD - TurboGrafx-CD" platformu tamamen kaldırıldı (temel "PC Engine - TurboGrafx-16"
  yeterli kabul edildi, ikisi kafa karıştırıyordu) — hem Builder'ın taşıma listesinden hem de
  önceden üretilmiş RetroAudit.db'den okuma sırasında filtreleniyor.
- Sütun görünürlüğü artık tüm 19 sütun için ayarlanabiliyor (önceden sadece 7 ek metadata sütunu
  açılıp kapanabiliyordu) ve tercih diske kaydedilip bir sonraki açılışta hatırlanıyor. Ayrı bir
  "Sütunlar" düğmesi kaldırıldı — herhangi bir sütun başlığına **sağ tık** bu seçiciyi açıyor.
- Sütun filtreleri yeniden tasarlandı: huni ikonu kaldırıldı, başlığın herhangi bir yerine **sol
  tık** filtre/arama açılır menüsünü açıyor. Menüye "↑ Sırala A-Z" / "↓ Sırala Z-A" eklendi
  (DataGrid'in kendi tık-ile-sırala davranışının yerini aldı). Tüm sütunların filtre menüsü artık
  aynı sabit genişlikte (önceden içerik uzunluğuna göre değişiyordu); uzun değerler "..." ile
  kırpılıp tam metin tooltip'te gösteriliyor.
- Başlık ve File sütunları için filtre menüsü artık sadece bir arama kutusu (checkbox listesi
  yok) — bu iki sütunda ~67 bin neredeyse hiç tekrarlamayan değer olduğundan tam liste kurmak
  popup'ı açarken donmaya yol açıyordu. Arama artık her tuş vuruşunda değil **Enter**'a basılınca
  uygulanıyor (66 bin satırı her harfte yeniden filtrelemek gecikme yaratıyordu); arama kutusunun
  içindeki "✕" önceki aramayı Enter'a gerek kalmadan anında temizliyor. Menü her açıldığında arama
  kutusu otomatik odaklanıp mevcut metni seçili getiriyor.

## [0.15] - 2026-07-05

### Fixed
- **DataGrid'de Ctrl/Ctrl+Shift ile toplu satır seçimi çalışmıyordu.** Kök neden: `ObsidianDark.xaml`
  içindeki global `DataGrid` stili `SelectionMode="Single"` olarak ayarlanmıştı — bu, WPF'in
  Ctrl+tık (tekil ekle/çıkar) ve Shift+tık (aralık seç) davranışını tamamen devre dışı bırakıyordu.
  `SelectionMode="Extended"` olarak düzeltildi. Bununla birlikte sağ tık menüsünün toplu (bulk) modu
  (Sil/Gizle/Favorile/Playlist) da artık gerçekten birden fazla satır seçiliyken tetiklenebiliyor.
- `DataGridRow` stilinde `IsSelected` ve `IsMouseOver` tetikleyicileri aynı `Background`'ı
  ayarlıyordu; `IsMouseOver` sonradan tanımlı olduğundan, seçili bir satırın üzerinde fare dururken
  hover rengi seçili (mavi) rengi eziyordu — çoklu seçimde, imlecin üzerinde durduğu satır seçili
  değilmiş gibi görünüyordu. Tetikleyici sırası düzeltildi.
- Sağ detay panelini aç/kapa düğmesi, kendi dar sütununda scrollbar'la sürekli görsel dikiş/hizalanma
  sorunu çıkarıyordu (bkz. önceki sürümlerdeki elle sürükleme/GridSplitter denemeleri). Düğme o ayrı
  sütundan tamamen kaldırıldı; DataGrid'in kendi köşesindeki (başlık satırının bittiği, scrollbar'ın
  başladığı) zaten boş duran küçük alana ikon olarak yerleştirildi — ayrı bir sütuna gerek kalmadı.

## [0.14] - 2026-07-05

### Fixed
- **Açılış süresi ~17 saniyeden ~1 saniyeye indi.** Her sütun filtresi (Platform/Tür/Geliştirici/...)
  dropdown'ı `Options` listesine değerleri tek tek `Add()` ediyordu; `Options` zaten bir `Filter`
  atanmış `CollectionView`'a bağlı olduğundan, WPF'te bu her ekleme başına maliyeti mevcut liste
  boyutuyla orantılı hale getiriyor (O(n) per add → toplamda O(n²)). Başlık/Dosya gibi neredeyse
  hiç tekrarlamayan (~55-65 bin farklı değer) sütunlarda bu tek başına ~15 saniye tutuyordu.
  `ColumnFilterViewModel` artık tüm listeyi `ObservableCollection` constructor'ına toplu veriyor,
  `CollectionView`/`Filter` ancak liste tamamen doluyken bağlanıyor. Ayrıca "bu oyunun ROM'u diskte
  var mı" kontrolü 67 bin oyun için ayrı ayrı `File.Exists` çağırmak yerine platform klasörünü bir
  kez listeleyip bellekte aranıyor.
- **Sağ tık kapsül menüsündeki ve DataGrid'deki scrollbar'lar** artık tutarlı, koyu temalı ve doğru
  çalışıyor. Kök nedenler: `Track` kendi `Orientation`'ını `ScrollBar`'ınkine hiç bağlamıyordu (her
  zaman dikey gibi davranıyordu), `RepeatButton`'ların görünmez (Transparent) alanları thumb dışında
  hiçbir iz bırakmadığından scrollbar "kayıp" gibi görünüyordu, ve — asıl gizli sorun — WPF'in
  `Track` bileşeni thumb için minimum boyutu hesaplarken `Thumb.MinHeight/MinWidth`'e ek olarak
  `SystemParameters.VerticalScrollBarButtonHeight` (ve yatay karşılığı) değerinin yarısını da bir
  taban olarak kullanıyor; bu sistem parametresi override edilmeden `MinHeight="60"` vermek thumb'ı
  yerleşim (Arrange) sırasında kırpıyordu. Çözüm: `Style.Resources` içinde
  `VerticalScrollBarButtonHeightKey`/`HorizontalScrollBarButtonWidthKey` istenen minimumun iki katına
  (120) ayarlandı, `Track`'e `Orientation="{TemplateBinding Orientation}"` eklendi. Ayrıca DataGrid'in
  kendi scrollbar'ı ile sağ paneldeki sürgülü tutamaç arasına görünür bir boşluk (`Margin`) eklendi —
  ikisi bitişik durduğunda thumb ortadayken tek bir blok gibi görünüyordu.
- `README.md` eski (v0.04, mock-veri) durumu anlatıyordu; gerçek Builder/Catalog/WPF mimarisini,
  RetroAudit.db + RetroAuditUserData.db ayrımını ve güncel özellik listesini anlatacak şekilde
  yeniden yazıldı.

### Added
- Kök dizine sürüm takibi için bir `VERSION` dosyası eklendi (önceden sadece CHANGELOG başlıklarında
  tutuluyordu).

## [0.13] - 2026-07-04

### Added
- **Stage B: WPF gerçek RetroAudit.db okuyor.** `MockDataService` tamamen kaldırıldı; yeni
  `CatalogDatabaseService` platformları/oyunları/sürümleri doğrudan `RetroAudit.db`'den okuyor.
  Sol platform paneli, oyun DataGrid'i ve sağ "Sürümler (Region)" paneli artık gerçek veriyle
  çalışıyor (43 platform, 67.335 oyun).
- **Ayrı kullanıcı-verisi veritabanı** (`RetroAuditUserData.db`) — `RetroAudit.db` Builder'ın her
  koşuda tamamen sildiği/yeniden ürettiği disposable bir dosya olduğu için (`GameId` kalıcı değil),
  favori/gizli/çöp kutusu/playlist/düzenlenmiş metadata gibi kullanıcı verisi buraya, sabit bir
  `GameKey = "{Platform}|{CompareTitle}"` anahtarıyla yazılıyor — Builder'ı tekrar çalıştırmak bu
  veriyi silmiyor.
- **Playlist sistemi**: ana tablonun üstünde hashtag/chip şeridi — sabit Favorites (★) + Hidden (👁)
  + Recycle Bin (🗑) chip'leri (sağ tık menüsündekiyle aynı ikonlar) ve kullanıcının oluşturduğu
  sınırsız sayıda playlist (renk seçilebilir, yeniden adlandırılabilir, silinebilir). Chip'e
  tıklamak sadece o listedeki oyunları gösterir.
- **Kapsül sağ tık menüsü**: satıra sağ tıklayınca yatay, dairesel ikonlu, animasyonlu bir menü
  açılıyor (Icon Only / Icon + Text modu Ayarlar > Arayüz'den seçilebilir). İçeriği: Edit Metadata,
  Versions, Hide/Unhide, Open File Location, Delete (çöp kutusuna taşır) / Restore / Kalıcı Sil
  (onaylı), Re-match Metadata, Playlist'e Ekle (+ yeni playlist oluşturma). Birden fazla satır
  seçiliyken menü "toplu moda" geçip sadece Favorilere Ekle/Playlist'e Ekle/Hide/Delete gösteriyor
  — tekil eylemler çakışmasın diye.
- DataGrid'de yeni **Favori (★)** ve **eksik ROM'u web'de ara (🔍)** sütunları; her sütuna joystick
  yerine huni (filter funnel) ikonlu, arama kutulu, "Hepsini Seç/Temizle" tek düğmeli filtre
  dropdown'ı eklendi (LaunchBox tarzı). Sütun seçici ile ek metadata sütunları (Geliştirici,
  Yayıncı, Yıl, Bölge, Kaynak, Eşleşme Yöntemi) açılıp kapatılabiliyor.
- **Edit Metadata** penceresi (Başlık/Tür/Açıklama/Notlar/Publisher/Developer) ve Versions
  panelinde "Preferred yap" — ikisi de RetroAuditUserData.db'ye yazıyor, Builder'ın varsayılan
  seçiminin üzerine kalıcı olarak geçiyor.
- **Re-match Metadata**: tek bir oyun için LaunchBox eşleştirmesini yeniden çalıştırır
  (`RetroAudit.Catalog` projesine referans eklendi; LaunchBox.Metadata.db yolu Ayarlar > Genel'de).
- Sağ detay paneli artık sürgülü (aç/kapa tutamacıyla), DataGrid gerçek yatay kaydırma çubuğuna
  sahip (sütun genişlikleri sabit piksele çevrildi).
- Ayarlar penceresine **Arayüz** sekmesi eklendi; ayarlar artık otomatik diske kaydedilip
  yükleniyor (önceden sadece Export/Import vardı).

### Fixed
- Varsayılan WPF `ToolTip`'i (beyaz arka plan) ve birkaç düğmenin `Style`'ı `BasedOn` vermediği
  için uygulamanın koyu temasını ezip varsayılan (beyaz) hover'a düşmesi düzeltildi.
- Yatay scrollbar'ın neredeyse görünmez olması (sadece `Width` ayarlıydı, `Height` hiç yoktu) ve
  yatay sürüklemenin ters çalışması (`IsDirectionReversed` sadece dikey için doğruydu, aynı şablon
  yataya da sızıyordu) düzeltildi.
- Sütun filtre huni ikonunun dar sütunlarda kenar çizgisiyle çakışması — mutlak konumlandırma
  yerine `Grid.ColumnDefinitions` tabanlı akış düzenine geçildi.

## [0.12] - 2026-07-03

### Added
- **WPF platform paneli sadeleştirildi**: eski favori/görünürlük seçici kaldırıldı, yerine sabit
  taksonomi geldi (CONSOLES/HANDHELDS/ARCADE/COMPUTERS/CLASSIC/OTHERS), OTHERS ilk açılışta kapalı.
  `PlatformListItem` + `MainViewModel.RebuildPlatformListItems()` bu hiyerarşiyi oluşturuyor;
  Builder/DB tarafı hiçbir platformu silmiyor, bu tamamen UI kategorileme.
- Platform rozetlerindeki oyun sayısı artık gerçek oyun listesinden dinamik hesaplanıyor
  (`SyncPlatformGameCounts`), önceki statik mock değerlerin yerine.
- Mock oyun listesi boşaltıldı (`MockDataService.GetGames()`), gerçek veritabanı entegrasyonuna
  (Stage B) hazırlık.
- **Builder finalizasyonu**: `RetroAudit.Catalog/Dat/PlatformCategoryMap.cs` eklendi — DAT platform
  adlarını WPF taksonomisine eşliyor; `Platforms.Category` sütunu şemaya eklendi.
- `BuildInfo` tablosu: `SchemaVersion/CatalogVersion/BuildDate/BuilderVersion/SourceSummary` artık
  RetroAudit.db'nin içine yazılıyor — WPF tarafı (Stage B) açılışta uyumsuz şemayı erkenden reddedebilecek.
- Build raporu genişletildi: kaynak/platform bazlı oyun sayısı kırılımı, filtrelenen kayıt sayısı,
  bilinmeyen bölge sayısı, build süresi, kapsam dışı bırakılan platform listesi.
- **ZX Spectrum platformu tamamen kaldırıldı** (hem TOSEC ana seti hem No-Intro "+3" varyantı,
  hem Builder hem WPF mock listesi hem Ayarlar panelindeki emülatör eşleşmesi) — TOSEC seti
  17.381 oyuna şişiyordu (en yakın platformun 2,5 katı) ve versiyonlarının yarısından fazlası
  net bölge etiketi taşımıyordu; kullanıcı kararıyla programdan tamamen çıkarıldı.
- **Curated platform kapsamı**: `PlatformAllowList` eklendi — Builder artık sadece WPF sol
  panelindeki 39 curated platformla eşleşen DAT dosyalarını tarıyor (65 niş platform bilinçli
  olarak kapsam dışı, raporda şeffaf listeleniyor). Kullanıcı kararı: gösterilmeyecek platformları
  ileride tekrar tekrar eklemeyeceğimiz için taramayı en baştan sadece ihtiyaç olana sınırladık.
- **Dağıtım varyantı birleştirme**: `PlatformMergeMap` eklendi — 3DS (Digital)/New 3DS/New 3DS
  (Digital), DS (Download Play), Wii (Digital), PS3/PSP/Vita (PSN) gibi aynı fiziksel platformun
  dağıtım biçimleri artık taban platform altında tek bir Platforms satırında toplanıyor (kaynak
  dosyaları hâlâ ayrı ayrı taranıyor, veri kaybı yok). PSP UMD Music/UMD Video (oyun değil, medya
  diski) tamamen hariç tutuldu.
- **1G1R (one-game-one-row) ana katalog**: `VersionResolver` gruplama anahtarı ham başlıktan
  normalize edilmiş başlığa (`CompareTitle`) geçirildi — bölgeler arası yazım farkı yüzünden aynı
  oyunun yanlışlıkla iki ayrı `Games` satırına bölünmesi önlendi. Tercih edilen sürüm önceliği
  USA > Europe > World > Japan olarak düzeltildi (önceki sıralamada World, Europe'un önündeydi).
  `Games.Title` artık gruptaki ilk görülen (rastgele) kaydın değil, tercih edilen sürümün başlığı.
- **Headered/headerless birleştirme**: No-Intro'nun aynı sürüm için ürettiği iki ayrı ROM dökümü
  (`.nes`/`.unh` gibi, aynı isim farklı CRC) artık iki ayrı `GameVersion` değil, tek sürümün
  altında birden fazla `GameHashes` satırı olarak tutuluyor (1 Game / 1 GameVersion / N Hash).
- **Tür bazlı varsayılan gizleme**: `Games.HiddenByDefault` sütunu eklendi — Casino/Gambling/
  Mahjong/Pachinko/Pachislot/Quiz/Board Game/Tabletop/Card Game/Educational türündeki oyunlar
  silinmiyor, sadece WPF'in varsayılan ana listesinde gizlenmek üzere işaretleniyor.

### Sonuç (final tam koşu: no-intro + redump + tosec, curated 39 platform + ZX Spectrum hariç)
43 platform (mame/fbneo kaynakları henüz taranmadığı için 3'ü boş), 67.335 oyun, 108.159 sürüm,
124.974 hash, 10:01 build süresi. %63 LaunchBox eşleşmesi (44.722 kesin + 5.277 fuzzy, 3.893
Needs Review), 4.151 oyun tür bazlı gizlendi (HiddenByDefault), duplicate hash çakışması 4,
belirsiz/varsayılana düşen platform 0, her oyun tam 1 preferred sürüme sahip (0 istisna).

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
