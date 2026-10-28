using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>Aplicativo de drivers do fabricante achado neste computador.</summary>
public sealed record DetectedTool(AssistantTool Tool, string Path);

/// <summary>
/// Drivers do fabricante: o inventário pelo CIM (sem MAC), os aplicativos oficiais já instalados e o estado da Proteção
/// do Sistema. Só leitura, e nada sai do computador. Fica aqui, e não no app, para a CI do Windows rodar o script de verdade.
/// </summary>
public static class OemProbe
{
    // Win32_ComputerSystem: fabricante, modelo e SKU; Win32_ComputerSystemProduct: número de série e, na Lenovo, o nome
    // comercial (Version) e o tipo de máquina (Name). Tudo sai em JSON: o C# limpa e valida cada valor (OemRules.Clean).
    public const string Script = """
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$ErrorActionPreference='SilentlyContinue'
function D($v){ if($v){ try{ ([datetime]$v).ToString('yyyy-MM-dd') }catch{ '' } } else { '' } }
$o=[ordered]@{}
$c=Get-CimInstance -ClassName Win32_ComputerSystem
$p=Get-CimInstance -ClassName Win32_ComputerSystemProduct
$e=Get-CimInstance -ClassName Win32_SystemEnclosure | Select-Object -First 1
$o.cs=[ordered]@{ mf="$($c.Manufacturer)"; model="$($c.Model)"; sku="$($c.SystemSKUNumber)"; serial="$($p.IdentifyingNumber)"; product="$($p.Name)"; version="$($p.Version)"; chassis=@($e.ChassisTypes | ForEach-Object { [int]$_ }) }
$b=Get-CimInstance -ClassName Win32_BIOS
$o.bios=[ordered]@{ vendor="$($b.Manufacturer)"; ver="$($b.SMBIOSBIOSVersion)"; date=(D $b.ReleaseDate); serial="$($b.SerialNumber)" }
$o.gpu=@(Get-CimInstance -ClassName Win32_VideoController | ForEach-Object { [ordered]@{ name="$($_.Name)"; mf="$($_.AdapterCompatibility)"; drv="$($_.DriverVersion)"; pnp="$($_.PNPDeviceID)" } })
$drv=@{}; Get-CimInstance -ClassName Win32_PnPSignedDriver | Where-Object { $_.DeviceID } | ForEach-Object { $drv["$($_.DeviceID)"]="$($_.DriverVersion)" }
$o.net=@(Get-CimInstance -ClassName Win32_NetworkAdapter -Filter "PhysicalAdapter=True" | Where-Object { $_.PNPDeviceID -match '^(PCI|USB)\\' } | ForEach-Object { [ordered]@{ name="$($_.Name)"; mf="$($_.Manufacturer)"; drv=$drv["$($_.PNPDeviceID)"]; pnp="$($_.PNPDeviceID)" } })
$o.audio=@(Get-CimInstance -ClassName Win32_SoundDevice | ForEach-Object { "$($_.Name)" })
$o | ConvertTo-Json -Compress -Depth 5
""";

    /// <summary>
    /// Leva 9.3: lê o WMI direto e só roda o script do PowerShell quando a leitura direta falha ou não traz nem o fabricante.
    /// Falha das duas devolve null: a tela diz que não deu para ler.
    /// </summary>
    public static async Task<HardwareInventory?> InventoryAsync(CancellationToken ct = default)
    {
        var native = await NativeInventoryAsync(ct);
        if (native is not null && native.Chassis.Manufacturer.Length > 0) return native;
        return await ScriptInventoryAsync(ct);
    }

    /// <summary>O inventário pelo WMI, sem PowerShell (até 2 minutos). Null quando a leitura falha.</summary>
    public static async Task<HardwareInventory?> NativeInventoryAsync(CancellationToken ct = default)
    {
        try { return OemRules.ParseInventory(await Task.Run(WmiReader.OemInventoryJson, ct).WaitAsync(TimeSpan.FromMinutes(2), ct)); }
        catch (Exception ex) when (ex is TimeoutException or PlatformNotSupportedException or DllNotFoundException or TypeInitializationException
                                   or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>Roda o script (até 2 minutos). Falha devolve null.</summary>
    public static async Task<HardwareInventory?> ScriptInventoryAsync(CancellationToken ct = default)
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                if (ct.IsCancellationRequested) throw;
                return null;
            }
            return OemRules.ParseInventory((await output).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    // ---------- aplicativos dos fabricantes ----------

    /// <summary>Onde cada aplicativo fica quando instalado. Pastas de programas vêm de fora para o teste não depender do Windows.</summary>
    public static IEnumerable<(string Key, string Path)> Candidates(string programFiles, string programFilesX86)
    {
        foreach (var root in new[] { programFiles, programFilesX86 }.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return ("dcu", Path.Combine(root, "Dell", "CommandUpdate", "dcu-cli.exe"));
            yield return ("lsu", Path.Combine(root, "Lenovo", "System Update", "tvsu.exe"));
            yield return ("hpia", Path.Combine(root, "HP", "HPIA", "HPImageAssistant.exe"));
            yield return ("hpsa", Path.Combine(root, "HP", "HP Support Framework", "HPSF.exe"));
            yield return ("dsa", Path.Combine(root, "Intel", "Driver and Support Assistant", "DSATray.exe"));
            yield return ("adrenalin", Path.Combine(root, "AMD", "CNext", "CNext", "RadeonSoftware.exe"));
            yield return ("nvapp", Path.Combine(root, "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe"));
        }
    }

    // O Vantage é da Microsoft Store: o Windows registra o pacote (consumo e corporativo) e o serviço dele
    private static readonly string[] VantagePackages = ["E046963F.LenovoCompanion", "E046963F.LenovoSettingsforEnterprise"];
    private static readonly string[] VantageServices = ["LenovoVantageService", "ImControllerService"];

    /// <summary>Os aplicativos de drivers dos fabricantes que estão instalados.</summary>
    public static List<DetectedTool> DetectTools()
    {
        var found = new List<DetectedTool>();
        var byKey = OemLinks.AllTools.ToDictionary(t => t.Key);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var (key, path) in Candidates(pf, pf86))
            if (!found.Any(f => f.Tool.Key == key) && File.Exists(path)) found.Add(new DetectedTool(byKey[key], path));
        if (!OperatingSystem.IsWindows()) return found;
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            bool vantage = packages?.GetSubKeyNames().Any(n => VantagePackages.Any(v => n.StartsWith(v + "_", StringComparison.OrdinalIgnoreCase))) == true;
            if (!vantage)
                foreach (var s in VantageServices)
                {
                    using var svc = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{s}");
                    if (svc is not null) { vantage = true; break; }
                }
            if (vantage) found.Add(new DetectedTool(byKey["vantage"], ""));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return found;
    }

    // ---------- proteção do sistema ----------

    /// <summary>
    /// A Proteção do Sistema (pontos de restauração) está ligada? Lê o registro do serviço de restauração do Windows:
    /// DisableSR=1 ou RPSessionInterval=0 é desligada. Null quando não deu para ler.
    /// </summary>
    public static bool? SystemRestoreEnabled()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            if (key is null) return null;
            if (key.GetValue("DisableSR") is int disabled && disabled == 1) return false;
            return key.GetValue("RPSessionInterval") switch { int i => i != 0, _ => null };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
