namespace RetroAudit.Services;

// "Core Adı" sütunundaki dropdown'da gösterilen İSİM ("Mesen", "PCSX2" ...) ile GERÇEK indirme
// hedefi arasındaki köprü — bkz. SettingsViewModel.DownloadCoreForEmulator/InstallForEmulatorRow.
// RetroArchCore satırlarında isim -> tahmini libretro çekirdek dosya adı (RetroArch'ın kendi
// buildbot'undaki YAYGIN adlandırma kuralı: küçük harf + alt çizgi + "_libretro.dll"); StandaloneEXE
// satırlarında isim -> StandaloneEmulatorInstallerService.SupportedEmulatorIds'teki id (SADECE
// otomatik kurulumu olan PCSX2/RPCS3/Xemu için — diğerleri bilinçli olarak eşleşmiyor, "daha sonra
// hepsinin tek tek bağlarız" kullanıcı kararı, o satırlar için indirme butonu Gözat'a düşer).
public static class EmulatorNameResolver
{
    private static readonly Dictionary<string, string> CoreFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MAME"] = "mame_libretro.dll",
        ["MAME 2000"] = "mame2000_libretro.dll",
        ["MAME 2003"] = "mame2003_libretro.dll",
        ["MAME 2003-Plus"] = "mame2003_plus_libretro.dll",
        ["MAME 2010"] = "mame2010_libretro.dll",
        ["FBNeo"] = "fbneo_libretro.dll",
        ["FB Alpha 2012"] = "fbalpha2012_libretro.dll",
        ["FB Alpha 2012 CPS-1"] = "fbalpha2012_cps1_libretro.dll",
        ["FB Alpha 2012 CPS-2"] = "fbalpha2012_cps2_libretro.dll",
        ["FB Alpha 2012 CPS-3"] = "fbalpha2012_cps3_libretro.dll",
        ["FB Alpha 2012 Neo Geo"] = "fbalpha2012_neogeo_libretro.dll",
        ["Mesen"] = "mesen_libretro.dll",
        ["Mesen-S"] = "mesen-s_libretro.dll",
        ["Nestopia"] = "nestopia_libretro.dll",
        ["FCEUmm"] = "fceumm_libretro.dll",
        ["QuickNES"] = "quicknes_libretro.dll",
        ["FixNES"] = "fixnes_libretro.dll",
        ["bsnes"] = "bsnes_libretro.dll",
        ["bsnes HD Beta"] = "bsnes_hd_beta_libretro.dll",
        ["bsnes-mercury Accuracy"] = "bsnes_mercury_accuracy_libretro.dll",
        ["bsnes-mercury Balanced"] = "bsnes_mercury_balanced_libretro.dll",
        ["bsnes-mercury Performance"] = "bsnes_mercury_performance_libretro.dll",
        ["bsnes2014 Accuracy"] = "bsnes2014_accuracy_libretro.dll",
        ["bsnes2014 Balanced"] = "bsnes2014_balanced_libretro.dll",
        ["bsnes2014 Performance"] = "bsnes2014_performance_libretro.dll",
        ["bsnes-jg"] = "bsnes-jg_libretro.dll",
        ["Snes9x"] = "snes9x_libretro.dll",
        ["Snes9x 2002"] = "snes9x2002_libretro.dll",
        ["Snes9x 2005"] = "snes9x2005_libretro.dll",
        ["Snes9x 2005 Plus"] = "snes9x2005_plus_libretro.dll",
        ["Snes9x 2010"] = "snes9x2010_libretro.dll",
        ["SwanStation"] = "swanstation_libretro.dll",
        ["Beetle PSX HW"] = "mednafen_psx_hw_libretro.dll",
        ["PCSX ReARMed"] = "pcsx_rearmed_libretro.dll",
        ["Genesis Plus GX"] = "genesis_plus_gx_libretro.dll",
        ["PicoDrive"] = "picodrive_libretro.dll",
        ["SMS Plus"] = "smsplus_libretro.dll",
        ["Gearsystem"] = "gearsystem_libretro.dll",
        ["BlastEm"] = "blastem_libretro.dll",
        ["PPSSPP"] = "ppsspp_libretro.dll",
        ["Flycast"] = "flycast_libretro.dll",
        ["VICE x64sc"] = "vice_x64sc_libretro.dll",
        ["PUAE"] = "puae_libretro.dll",
        ["PUAE 2021"] = "puae2021_libretro.dll",
        ["Stella"] = "stella_libretro.dll",
        ["Stella 2014"] = "stella2014_libretro.dll",
        ["Stella 2023"] = "stella2023_libretro.dll",
        ["Mupen64Plus-Next"] = "mupen64plus_next_libretro.dll",
        ["Parallel N64"] = "parallel_n64_libretro.dll",
        ["Gambatte"] = "gambatte_libretro.dll",
        ["SameBoy"] = "sameboy_libretro.dll",
        ["Gearboy"] = "gearboy_libretro.dll",
        ["TGB Dual"] = "tgbdual_libretro.dll",
        ["FixGB"] = "fixgb_libretro.dll",
        ["mGBA"] = "mgba_libretro.dll",
        ["VBA-M"] = "vbam_libretro.dll",
        ["VBA Next"] = "vba_next_libretro.dll",
        ["gpSP"] = "gpsp_libretro.dll",
        ["Meteor"] = "meteor_libretro.dll",
        ["Beetle Saturn"] = "mednafen_saturn_libretro.dll",
        ["Kronos"] = "kronos_libretro.dll",
        ["Yabause"] = "yabause_libretro.dll",
        ["YabaSanshiro"] = "yabasanshiro_libretro.dll",
        ["Atari800"] = "atari800_libretro.dll",
        ["ProSystem"] = "prosystem_libretro.dll",
        ["Virtual Jaguar"] = "virtualjaguar_libretro.dll",
        ["Beetle Lynx"] = "mednafen_lynx_libretro.dll",
        ["Handy"] = "handy_libretro.dll",
        ["Beetle PCE"] = "mednafen_pce_libretro.dll",
        ["Beetle SuperGrafx"] = "mednafen_supergrafx_libretro.dll",
        ["Beetle PCE Fast"] = "mednafen_pce_fast_libretro.dll",
        ["Mednafen"] = "mednafen_pce_fast_libretro.dll",
        ["Beetle NeoPop"] = "mednafen_ngp_libretro.dll",
        ["Race"] = "race_libretro.dll",
        ["NeoCD"] = "neocd_libretro.dll",
        ["UAE4ARM"] = "uae4arm_libretro.dll",
    };

    // Kullanıcı isteği: "Şunlar retroarch ın içindeki çekirdekler ve açıklamaları bunları ilgili
    // emulatörlere bağlayalım açılır listelerine ekleyelim ... Bizde olmayan platformları ekleme" —
    // Tercih Edilen/Alternatif DIŞINDA, o platform için bilinen EK çekirdekler. SADECE
    // CanonicalEmulatorDefinitions'ta GERÇEKTEN sahip olduğumuz RetroArchCore platformlar için;
    // DOSBox/MSX/ZX Spectrum/ScummVM/Quake portları/3DO/CD-i gibi platformumuz olmayan çekirdekler
    // kasıtlı olarak burada YOK. Nintendo DS/3DS/GameCube/Wii/PS2/Neo Geo CD gibi StandaloneEXE
    // platformların libretro çekirdekleri de kasıtlı YOK — o satırlar RetroArch modunda değil.
    private static readonly Dictionary<string, string[]> AdditionalCoresByPlatform = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nintendo"] = new[] { "Mesen-S", "FCEUmm", "QuickNES", "FixNES" },
        ["Super Nintendo"] = new[]
        {
            "Snes9x 2002", "Snes9x 2005", "Snes9x 2005 Plus", "Snes9x 2010", "bsnes HD Beta",
            "bsnes-mercury Accuracy", "bsnes-mercury Balanced", "bsnes-mercury Performance",
            "bsnes2014 Accuracy", "bsnes2014 Balanced", "bsnes2014 Performance", "bsnes-jg",
        },
        ["Game Boy"] = new[] { "Gearboy", "TGB Dual", "FixGB" },
        ["Game Boy Color"] = new[] { "Gearboy", "TGB Dual", "FixGB" },
        ["Game Boy Advance"] = new[] { "VBA Next", "gpSP", "Meteor" },
        ["PlayStation"] = new[] { "PCSX ReARMed" },
        ["Genesis"] = new[] { "BlastEm" },
        ["Master System"] = new[] { "Gearsystem" },
        ["Game Gear"] = new[] { "Gearsystem" },
        ["Saturn"] = new[] { "Yabause", "YabaSanshiro" },
        ["MAME"] = new[] { "MAME 2000", "MAME 2003", "MAME 2003-Plus", "MAME 2010" },
        ["NeoGeo"] = new[] { "FB Alpha 2012", "FB Alpha 2012 CPS-1", "FB Alpha 2012 CPS-2", "FB Alpha 2012 CPS-3", "FB Alpha 2012 Neo Geo" },
        ["Atari 2600"] = new[] { "Stella 2014", "Stella 2023" },
        ["Amiga"] = new[] { "PUAE 2021" },
        ["PC Engine - TurboGrafx-16"] = new[] { "Beetle SuperGrafx" },
    };

    // "açıklamalarıda mouse u üstüne getirince gözüksün" (kullanıcı isteği) — hem Tercih Edilen/
    // Alternatif hem de yukarıdaki ek çekirdekler için, ComboBox öğesinin ToolTip'i (bkz.
    // SettingsWindow.xaml CoreChoiceItemStyle).
    private static readonly Dictionary<string, string> CoreDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mesen"] = "En gelişmiş, en isabetli NES ve SNES emülatörü.",
        ["Mesen-S"] = "Mesen'in SNES'e özel sürümü.",
        ["Nestopia"] = "Çok popüler, hafif ve aşırı kararlı NES çekirdeği.",
        ["FCEUmm"] = "Hile ve mod desteği yüksek, hafif NES emülatörü.",
        ["QuickNES"] = "Çok eski işlemciler için süper hızlı NES emülatörü.",
        ["FixNES"] = "Sadece belirli sorunlu oyunları çalıştırmak için optimize edilmiş NES çekirdeği.",

        ["Snes9x"] = "Her PC'de uçan, en dengeli, en popüler SNES çekirdeği.",
        ["Snes9x 2002"] = "Tost makinesinde bile çalışsın diye optimize edilmiş çok eski Snes9x sürümü.",
        ["Snes9x 2005"] = "Tost makinesinde bile çalışsın diye optimize edilmiş çok eski Snes9x sürümü.",
        ["Snes9x 2005 Plus"] = "Tost makinesinde bile çalışsın diye optimize edilmiş çok eski Snes9x sürümü.",
        ["Snes9x 2010"] = "Tost makinesinde bile çalışsın diye optimize edilmiş çok eski Snes9x sürümü.",
        ["bsnes"] = "Dünyanın en hatasız SNES emülatörü.",
        ["bsnes HD Beta"] = "bsnes'in modlu 3D grafik desteği sunan HD sürümü.",
        ["bsnes-mercury Accuracy"] = "bsnes'in işlemci yükünü azaltmak için özelleştirilmiş, doğruluk odaklı yan dalı.",
        ["bsnes-mercury Balanced"] = "bsnes'in işlemci yükünü azaltmak için özelleştirilmiş, dengeli yan dalı.",
        ["bsnes-mercury Performance"] = "bsnes'in işlemci yükünü azaltmak için özelleştirilmiş, performans odaklı yan dalı.",
        ["bsnes2014 Accuracy"] = "bsnes'in 2014 yılındaki eski sürüm varyasyonu (doğruluk odaklı).",
        ["bsnes2014 Balanced"] = "bsnes'in 2014 yılındaki eski sürüm varyasyonu (dengeli).",
        ["bsnes2014 Performance"] = "bsnes'in 2014 yılındaki eski sürüm varyasyonu (performans odaklı).",
        ["bsnes-jg"] = "bsnes'in başka bir basitleştirilmiş klonu.",

        ["Mupen64Plus-Next"] = "Şu an RetroArch'taki en iyi, en güncel ve yüksek grafik destekli N64 çekirdeği.",
        ["Parallel N64"] = "Orijinal çözünürlük sadakati ve eski bilgisayarlar için alternatif N64 çekirdeği.",

        ["Gambatte"] = "GB/GBC için en kararlı ve renk paletleri en doğru çekirdek.",
        ["SameBoy"] = "Çok yüksek doğruluğa sahip, gelişmiş GB/GBC emülatörü.",
        ["Gearboy"] = "Açık kaynaklı, temiz ve hafif bir GB/GBC alternatifi.",
        ["TGB Dual"] = "Aynı ekranda iki Game Boy oyununu bağlayıp (Link Cable) oynamanı sağlayan özel çekirdek.",
        ["FixGB"] = "Sadece sorunlu GB oyunları için hata ayıklama odaklı çekirdek.",

        ["mGBA"] = "GBA emülasyonunun şu an dünyadaki açık ara en iyi ve en hızlı kralı.",
        ["VBA-M"] = "Eski efsane VisualBoyAdvance'in RetroArch portu (mGBA varken gereksiz).",
        ["VBA Next"] = "Eski efsane VisualBoyAdvance'in RetroArch portu (mGBA varken gereksiz).",
        ["gpSP"] = "Özellikle çok zayıf mobil cihazlar için optimize edilmiş eski bir GBA çekirdeği.",
        ["Meteor"] = "Deneysel bir GBA emülatör projesi.",

        ["SwanStation"] = "4K çözünürlük, PGXP (poligon düzeltme) destekli en hızlı ve modern PS1 çekirdeği.",
        ["Beetle PSX HW"] = "Dünyanın en hatasız, en isabetli çalışan ağır PS1 emülatörü (ekran kartı gücünü kullanır).",
        ["PCSX ReARMed"] = "Özellikle ARM işlemciler ve çok eski PC'ler için hafifletilmiş PS1 emülatörü.",

        ["PPSSPP"] = "Muazzam çalışan, HD çözünürlük destekli resmi PSP çekirdeği.",

        ["Genesis Plus GX"] = "Sega'nın 8-bit ve 16-bit tüm konsollarını kusursuz oynatan en iyi çekirdek.",
        ["PicoDrive"] = "Sega CD ve Sega 32X oyunları için en kararlı çalışan alternatif.",
        ["SMS Plus"] = "Sadece Master System ve Game Gear için hafif bir alternatif.",
        ["Gearsystem"] = "Çok isabetli çalışan bir başka Master System / Game Gear emülatörü.",
        ["BlastEm"] = "Sadece Mega Drive (Genesis) odaklı, döngü bazlı çok yüksek doğruluğa sahip çekirdek.",

        ["Beetle Saturn"] = "Şu an piyasadaki en kusursuz ve en kararlı çalışan SEGA Saturn emülatörü.",
        ["Kronos"] = "Yabause tabanlı alternatif Saturn emülatörü (Mednafen kadar stabil değil).",
        ["Yabause"] = "Alternatif Saturn emülatörü (Mednafen kadar stabil değil).",
        ["YabaSanshiro"] = "Yabause tabanlı, güncellenmiş alternatif Saturn emülatörü.",
        ["Flycast"] = "NAOMI ve Atomiswave arcade kartları dahil tüm Dreamcast oyunlarını harika oynatan çekirdek.",

        ["MAME"] = "Güncel, devasa MAME arcade veritabanını çalıştıran ana çekirdek.",
        ["MAME 2000"] = "Eski romsetleri uyumluluk sorunu yaşamadan çalıştırmak için dondurulmuş eski MAME sürümü.",
        ["MAME 2003"] = "Eski romsetleri uyumluluk sorunu yaşamadan çalıştırmak için dondurulmuş eski MAME sürümü.",
        ["MAME 2003-Plus"] = "Eski romsetleri uyumluluk sorunu yaşamadan çalıştırmak için dondurulmuş eski MAME sürümü.",
        ["MAME 2010"] = "Eski romsetleri uyumluluk sorunu yaşamadan çalıştırmak için dondurulmuş eski MAME sürümü.",
        ["FBNeo"] = "NeoGeo, Capcom (CPS1/2/3) oyunlarını MAME'den çok daha hızlı ve performanslı açan efsane arcade çekirdeği.",
        ["FB Alpha 2012"] = "FinalBurn Alpha'nın eski, cihazlara göre parçalanmış eski versiyonu.",
        ["FB Alpha 2012 CPS-1"] = "FinalBurn Alpha'nın CPS-1 odaklı eski versiyonu.",
        ["FB Alpha 2012 CPS-2"] = "FinalBurn Alpha'nın CPS-2 odaklı eski versiyonu.",
        ["FB Alpha 2012 CPS-3"] = "FinalBurn Alpha'nın CPS-3 odaklı eski versiyonu.",
        ["FB Alpha 2012 Neo Geo"] = "FinalBurn Alpha'nın Neo Geo odaklı eski versiyonu.",

        ["Beetle NeoPop"] = "NeoGeo Pocket / Color el konsolu emülatörü.",
        ["Race"] = "Çok hızlı çalışan alternatif NeoGeo Pocket emülatörü.",

        ["Stella"] = "Efsanevi Atari 2600 emülatörü.",
        ["Stella 2014"] = "Efsanevi Atari 2600 emülatörünün eski sürümü.",
        ["Stella 2023"] = "Efsanevi Atari 2600 emülatörünün güncel sürümü.",
        ["Atari800"] = "Atari 5200 ve eski Atari 8-bit bilgisayar sistemleri için.",
        ["ProSystem"] = "Atari 7800 emülatörü.",
        ["Handy"] = "Atari Lynx el konsolu emülatörü.",
        ["Beetle Lynx"] = "Atari Lynx el konsolu emülatörü.",
        ["Virtual Jaguar"] = "Atari'nin başarısız 64-bit konsolu Atari Jaguar emülatörü.",

        ["VICE x64sc"] = "C64 oyunlarını en yüksek doğrulukla açan çekirdek.",
        ["PUAE"] = "Amiga oyunlarını disket derdi olmadan açan canavar Amiga çekirdeği.",
        ["PUAE 2021"] = "PUAE'nin güncellenmiş sürümü.",
        ["UAE4ARM"] = "ARM tabanlı/düşük güçlü cihazlar için optimize edilmiş alternatif Amiga çekirdeği.",

        ["Beetle PCE"] = "PC Engine (TurboGrafx-16) emülatörü.",
        ["Beetle SuperGrafx"] = "PC Engine'in SuperGrafx varyantı için emülatör.",
        ["Beetle PCE Fast"] = "PC Engine CD emülatörü (hız odaklı).",
        ["Mednafen"] = "Çoklu sistem destekli emülasyon paketi.",

        ["NeoCD"] = "NeoGeo CD konsolu emülatörü.",
    };

    // Sadece StandaloneEmulatorInstallerService'te bir "İndir & Kur" satırı olanlar (bkz. o dosyanın
    // Sources sözlüğü) — Dolphin/Ryujinx de artık BURADA (sadece manuel URL ile çalışıyorlar, otomatik
    // GitHub/API çözümlemesi YOK, bkz. o dosyanın yorumları), TryGetStandaloneId artık ikisi için de
    // dolu döner ki satır InstallStandaloneEmulator akışına girip "Manuel indirme linki gerekli"
    // durumunu doğru gösterebilsin. "Lime3DS" eski adı GERİYE DÖNÜK UYUMLULUK için ayrıca eşleniyor
    // (kullanıcı kararı: Nintendo 3DS artık "Azahar" — eski settings.json'da SelectedCoreOrEmulatorName
    // hâlâ "Lime3DS" olarak kaydedilmiş olabilir, bkz. SettingsViewModel.MigrateLegacyPlatformNames).
    private static readonly Dictionary<string, string> StandaloneIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PCSX2"] = "PCSX2",
        ["RPCS3"] = "RPCS3",
        ["Xemu"] = "Xemu",
        ["Cemu"] = "Cemu",
        ["melonDS"] = "melonDS",
        ["Vita3K"] = "Vita3K",
        ["Xenia"] = "Xenia",
        ["Dolphin"] = "Dolphin",
        ["Ryujinx"] = "Ryujinx",
        ["Azahar"] = "Azahar",
        ["Lime3DS"] = "Azahar",
    };

    // "parametrelerde seçili olan core ve standalone exe ye göre otomatik gelecek" (kullanıcı isteği)
    // — StandaloneEXE isimleri gerçekten FARKLI CLI kurallarına sahip (PCSX2 doğrudan ROM yolu ister,
    // Dolphin "-b -e", Xemu "-dvd_path" gibi) — bkz. SettingsViewModel.CanonicalEmulatorDefinitions'taki
    // ORİJİNAL platform-başına parametreler, burada İSME göre yeniden düzenlendi ki EmulatorConfig
    // (bkz. OnSelectedCoreOrEmulatorNameChanged) kullanıcı Alternatif'e geçtiğinde doğru şablonu
    // otomatik yazabilsin. RetroArchCore tarafında TEK bir evrensel şablon var (bkz. EmulatorConfig),
    // hangi çekirdek seçilirse seçilsin `-L "%CORE%" "%ROM%"` aynı kalıyor — çekirdek DOSYASI zaten
    // %CORE% üzerinden değişiyor, komut satırı yapısı değişmiyor.
    private static readonly Dictionary<string, string> StandaloneParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PCSX2"] = "\"%ROM%\"",
        ["RPCS3"] = "\"%ROM%\"",
        ["Xemu"] = "\"-dvd_path\" \"%ROM%\"",
        ["Xenia"] = "\"%ROM%\"",
        ["Dolphin"] = "\"-b\" \"-e\" \"%ROM%\"",
        ["Cemu"] = "\"-g\" \"%ROM%\"",
        ["Ryujinx"] = "\"%ROM%\"",
        ["melonDS"] = "\"%ROM%\"",
        ["Azahar"] = "\"%ROM%\"",
        ["Lime3DS"] = "\"%ROM%\"", // Geriye dönük uyumluluk — güncel görünen ad "Azahar" (bkz. StandaloneIds).
        ["Vita3K"] = "\"%ROM%\"",
    };

    public static string? TryGetCoreFileName(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && CoreFileNames.TryGetValue(displayName.Trim(), out var fileName)
            ? fileName
            : null;

    public static string? TryGetStandaloneId(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && StandaloneIds.TryGetValue(displayName.Trim(), out var id)
            ? id
            : null;

    public static string? TryGetStandaloneParameters(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && StandaloneParameters.TryGetValue(displayName.Trim(), out var parameters)
            ? parameters
            : null;

    public static string? TryGetDescription(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && CoreDescriptions.TryGetValue(displayName.Trim(), out var description)
            ? description
            : null;

    public static IReadOnlyList<string> GetAdditionalCoreNames(string platformName) =>
        AdditionalCoresByPlatform.TryGetValue(platformName, out var names) ? names : Array.Empty<string>();
}
