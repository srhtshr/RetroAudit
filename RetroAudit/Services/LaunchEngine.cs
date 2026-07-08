using System.Diagnostics;
using RetroAudit.Models;

namespace RetroAudit.Services;

// Bir EmulatorConfig + ROM yolundan gerçek Process.Start çağrısını üreten tek yer. İki mod
// arasındaki fark (bkz. AppSettings.LauncherType) SADECE hangi yer tutucuların (%CORE%/%ROM%)
// doldurulacağı ve hangi alanların zorunlu olduğu — Process.Start'ın kendisi ikisinde de aynı.
// Statik/saf: UI'dan bağımsız, MainViewModel.LaunchWithEmulator bunu çağırıp sonucu
// RequestShowMessage ile kullanıcıya bildiriyor.
public static class LaunchEngine
{
    public readonly record struct LaunchResult(bool Success, string? ErrorMessage);

    public static LaunchResult Launch(EmulatorConfig emulator, string romPath)
    {
        if (string.IsNullOrWhiteSpace(emulator.ExecutablePath))
        {
            var label = emulator.LauncherType == LauncherType.RetroArchCore ? "RetroArch.exe" : "emülatör .exe";
            return new LaunchResult(false, $"Bu platform için {label} yolu tanımlanmamış.");
        }

        if (emulator.LauncherType == LauncherType.RetroArchCore && string.IsNullOrWhiteSpace(emulator.CorePath))
            return new LaunchResult(false, "Bu platform için bir libretro çekirdeği (Core) tanımlanmamış.");

        // Her iki modda da yer tutucular tırnak içine alınıyor — hem ROM hem çekirdek yolları
        // boşluk içerebilir. Parametre şablonunda zaten tırnak varsa ("%ROM%" gibi) mükerrer tırnak
        // oluşmasını engellemek için önce bunları temizliyoruz, ardından tek tırnak/çift tırnak
        // ile güvenli şekilde sarmalıyoruz.
        var arguments = emulator.Parameters
            .Replace("\"%CORE%\"", "%CORE%")
            .Replace("'%CORE%'", "%CORE%")
            .Replace("\"%ROM%\"", "%ROM%")
            .Replace("'%ROM%'", "%ROM%")
            .Replace("%CORE%", $"\"{emulator.CorePath}\"")
            .Replace("%ROM%", $"\"{romPath}\"");

        try
        {
            Process.Start(new ProcessStartInfo(emulator.ExecutablePath, arguments) { UseShellExecute = true });
            return new LaunchResult(true, null);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, $"Emülatör başlatılamadı: {ex.Message}");
        }
    }
}
