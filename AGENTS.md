# AGENTS.md

Bu dosya, bu depoda çalışan AI ajanları/asistanları için kısa bir rehberdir. Amaç: kod tabanının
mimarisini ve kırılgan noktalarını tekrar keşfetmeden, doğru varsayımlarla işe başlamak.

## Proje nedir

RetroAudit, retro oyun kütüphanesi düzenleme/denetleme aracı. WPF (.NET 9) + MVVM
(CommunityToolkit.Mvvm) ile geliştiriliyor. Ayrıntılı özellik listesi ve kullanım için
[README.md](README.md)'ye, sürüm geçmişi için [CHANGELOG.md](CHANGELOG.md)'ye bakın.

## Mimari — üç proje, iki veritabanı

```
RetroAudit.Catalog/    DAT ayrıştırma, 1G1R gruplama, LaunchBox eşleştirme, SQLite şeması
RetroAudit.Builder/    RetroAudit.db'yi üreten komut satırı aracı
RetroAudit/            WPF uygulaması
```

**RetroAudit.db** (Builder'ın çıktısı) — her Builder koşusunda **tamamen silinip yeniden
üretilir**. `Games.GameId` bu yüzden kalıcı değildir. Bu dosyaya asla kullanıcı verisi
yazılmaz ve WPF tarafı bu dosyayı sadece **okur**.

**RetroAuditUserData.db** (WPF'nin sahibi olduğu ayrı dosya) — favori, gizli, çöp kutusu,
playlist, elle düzenlenmiş metadata gibi tüm kullanıcı verisini tutar. Builder bu dosyanın
varlığından haberdar değildir ve asla dokunmaz.

Bu ikisi arasındaki köprü **stabil bir anahtar**dır: `GameKey = "{Platform}|{CompareTitle}"`
(bkz. `GameKeyHelper.Compute`). Bu anahtar `VersionResolver.Group`'un kullandığı gruplama
mantığıyla birebir aynıdır, bu yüzden Builder'ı tekrar çalıştırmak kullanıcı verisini bozmaz.

**Bu invaryantı bozacak bir değişiklik yapmayın**: `Games.GameId`'yi kalıcı bir anahtar gibi
kullanmayın, kullanıcı verisini `RetroAudit.db`'ye yazmayın.

## Platform adları: ham ad vs. görünen ad

`Games`/`Platforms` tablolarındaki adlar DAT kaynaklı, üretici önekli ham adlardır (ör.
`"Nintendo - Nintendo 64"`, `"Atari - 2600"`). Bunlar **filtreleme, eşleştirme, GameKey
hesaplama ve dosya yolu birleştirme** için kullanılır — değiştirilemez.

`RetroAudit/Models/PlatformDisplayNameMap.cs`, bu ham adları arayüzde gösterilen sade
isimlere çevirir (ör. `"Nintendo 64"`). Bu harita **sadece sunum amaçlıdır**; `Game.Platform`
alanını değil, `Game.PlatformDisplayName` alanını besler. Yeni bir platform eklerken bu
haritaya bir satır eklemeyi unutmayın, aksi halde o platform ham adıyla görünür.

İki platform kasıtlı olarak dışlanmıştır ve bunu "eksik" sanıp eklememelisiniz:
- **ZX Spectrum** — 17.381 oyunluk şişme, %53 bilinmeyen bölge (kullanıcı kararı).
- **PC Engine CD - TurboGrafx-CD** — temel "PC Engine - TurboGrafx-16" ile karıştırıyordu
  (kullanıcı kararı). Bkz. `CatalogDatabaseService.RemovedPlatformName` ve
  `PlatformAllowList.cs`.

Curated platform listesi (Builder'ın hangi DAT'ları tarayacağı) `RetroAudit.Catalog/Dat/
PlatformAllowList.cs`'te; UI kategorisi (CONSOLES/HANDHELDS/...) `PlatformCategoryMap.cs`'te.

## Build / çalıştırma

```
dotnet build                                    # tüm çözüm, 0 hata/uyarı beklenir
dotnet run --project RetroAudit.Builder -- ...  # katalog üret (bkz. README "Çalıştırma")
dotnet run --project RetroAudit                 # WPF uygulamasını çalıştır
```

Bir değişiklikten sonra en azından `dotnet build`'in temiz geçtiğini doğrulayın. WPF
uygulamasını PowerShell ile başlatıp ekran görüntüsü alarak self-test döngüsüne **girmeyin**
— bu ortamda güvenilmez (pencere odağı/süreç ömrü tutarsız) ve token israfına yol açar. Build
başarılıysa ve değişiklik mekanik/anlaşılırsa kullanıcıya doğrulaması için bırakın.

## WPF'e özgü, bu depoda tekrar keşfedilmiş tuzaklar

- **`DataGridColumn.Header` görsel ağacın parçası değil** — normal `{Binding}` çalışmaz.
  `Views/BindingProxy.cs` (bir `Freezable`) üzerinden `Source={StaticResource ViewModelProxy}`
  ile ViewModel'e erişilir.
- **`Popup StaysOpen="False"` + aynı tıklamada senkron açma = anında kapanma.** Açan olayı
  `PreviewMouseDown` yerine `PreviewMouseUp`'ta işleyin.
- **Örtük (implicit) `Button` Style'ını `<Button.Style>` ile ezmek `BasedOn` vermezseniz
  temanın tamamını siler**, varsayılan (beyaz) WPF chrome'una düşer. Özel bir Button Style
  yazarken her zaman `BasedOn="{StaticResource {x:Type Button}}"` verin ya da (daha basitse)
  sadece `Background`/`BorderThickness` gibi tekil özellikleri instance üzerinde override edin.
- **Native `ScrollBar`'ı elle yeniden şablonlamaktan kaçının.** `Track`'in kendi
  Minimum/Maximum/Value/ViewportSize'a göre iç boyutlandırma matematiği (özellikle 60 bin+
  satırlık bir DataGrid için) defalarca yeni görsel bozukluğa yol açtı. `ObsidianDark.xaml`
  şu an sadece `Background`/`Foreground` rengini override ediyor, `Template`'e dokunmuyor —
  bunu böyle bırakın.
- **`ObservableCollection`'a, `Filter` atanmış bir `ICollectionView`'a bağlıyken tek tek
  `Add()` etmek O(n²)'dir.** 60 bin+ farklı değeri olan bir listeyi (ör. sütun filtresi
  seçenekleri) doldururken `ObservableCollection(IEnumerable)` ctor'unu kullanın, `Filter`
  atanmadan önce toplu doldurun (bkz. `ColumnFilterViewModel` ctor'u).
- **~67 bin neredeyse hiç tekrarlamayan değeri olan bir sütun için (Title, File) checkbox
  listesi kurmayın** — render etmek popup açılışında donmaya yol açar. Bunun yerine
  `ColumnFilterViewModel.IsSearchOnly` + Enter-ile-ara deseni kullanılıyor, yeni büyük-
  kardinaliteli bir sütun eklerseniz aynı deseni izleyin.

## Sürümleme

Küçük, sık sürümler (0.01, 0.02, ...) — henüz bir SemVer/1.0 taahhüdü yok. Anlamlı bir
değişiklik grubunu bitirdiğinizde: `VERSION` dosyasını bir sonraki sürüme yükseltin,
`CHANGELOG.md`'ye o sürümün "Neden" odaklı bir özetini ekleyin (ne değişti değil, neden
değişti). **`git push` sadece kullanıcı açıkça istediğinde yapılır** — her commit otomatik
push edilmez.
