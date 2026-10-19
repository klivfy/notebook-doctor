using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

public sealed record UnsignedApp(string Name, string Path);

/// <param name="DefenderRealtime">Proteção em tempo real do Windows Defender (null = não deu para ler)</param>
/// <param name="OtherAv">Antivírus de terceiros registrados no Windows (quando há, o Defender pode estar desligado de propósito)</param>
public sealed record SecurityInfo(bool? DefenderRealtime, int? SignatureAgeDays, IReadOnlyList<string> OtherAv, IReadOnlyList<string> FirewallOff)
{
    public static readonly SecurityInfo Empty = new(null, null, Array.Empty<string>(), Array.Empty<string>());
}

public sealed record HealthInfo(
    string DiskName, string DiskType, string DiskHealth, int? DiskWearPct, int? DiskTempC,
    int? BatteryHealthPct, int? BatteryCycles,
    int? BootSeconds, int? BootAvgSeconds,
    bool RebootPending, bool OnAc, string ActivePlan, string? HighPlanGuid,
    int StartupEnabled, long TempBytes,
    IReadOnlyList<string> DriverErrors, IReadOnlyList<UnsignedApp> Unsigned, SecurityInfo? Security = null)
{
    public SecurityInfo Sec => Security ?? SecurityInfo.Empty;
    public static readonly HealthInfo Empty = new("", "", "", null, null, null, null, null, null, false, false, "", null, 0, 0,
        Array.Empty<string>(), Array.Empty<UnsignedApp>());
    public bool HasDisk => DiskName.Length > 0;
    public bool DiskHealthy => DiskHealth is "" or "Healthy";
    public bool IsBalancedPlan => ActivePlan.Contains("quilibrado", StringComparison.OrdinalIgnoreCase) || ActivePlan.Contains("balanced", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Coleta dados "lentos" (SMART, bateria, boot, plano de energia, lixo) fora da thread de interface.
/// Sem administrador, alguns campos (desgaste/temperatura do SSD, log de boot) podem vir vazios: o app mostra "indisponível".
/// </summary>
public static class HealthProbe
{
    private const string Script = """
$ErrorActionPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$o=[ordered]@{}
$d=$null
try { $n=(Get-Partition -DriveLetter C | Get-Disk).Number; $d=Get-PhysicalDisk | Where-Object { $_.DeviceId -eq "$n" } } catch {}
if(-not $d){ $d=Get-PhysicalDisk | Sort-Object DeviceId | Select-Object -First 1 }
if($d){ $r=$d | Get-StorageReliabilityCounter; $o.disk=[ordered]@{name="$($d.FriendlyName)";type="$($d.MediaType)";health="$($d.HealthStatus)";wear=$r.Wear;temp=$r.Temperature} }
$b=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational';Id=100} -MaxEvents 5 | ForEach-Object { $x=[xml]$_.ToXml(); ($x.Event.EventData.Data | Where-Object Name -eq 'BootTime').'#text' })
$o.boot=$b
$o.drivers=@(Get-PnpDevice -PresentOnly | Where-Object { $_.Status -eq 'Error' } | Select-Object -First 8 | ForEach-Object { "$($_.FriendlyName)" })
$o | ConvertTo-Json -Compress -Depth 4
""";

    private const string SecurityScript = """
$ErrorActionPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$o=[ordered]@{}
$mp=Get-MpComputerStatus
if($mp){ $o.rt=$mp.RealTimeProtectionEnabled; $o.sig=$mp.AntivirusSignatureAge }
$o.av=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntivirusProduct | ForEach-Object { "$($_.displayName)" })
$o.fwoff=@(Get-NetFirewallProfile | Where-Object { -not $_.Enabled } | ForEach-Object { "$($_.Name)" })
$o | ConvertTo-Json -Compress -Depth 3
""";

    private static SecurityInfo ReadSecurity()
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(SecurityScript));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return SecurityInfo.Empty;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(45000)) { try { p.Kill(); } catch { } return SecurityInfo.Empty; }
            using var doc = JsonDocument.Parse(output.Result.Trim());
            var r = doc.RootElement;

            static List<string> Strings(JsonElement root, string name)
            {
                var list = new List<string>();
                if (!root.TryGetProperty(name, out var v)) return list;
                IEnumerable<JsonElement> items = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : new[] { v };
                foreach (var e in items) if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) list.Add(e.GetString()!);
                return list;
            }
            bool? rt = r.TryGetProperty("rt", out var rtv) && rtv.ValueKind is JsonValueKind.True or JsonValueKind.False ? rtv.GetBoolean() : null;
            int? sig = r.TryGetProperty("sig", out var sv) && sv.ValueKind == JsonValueKind.Number && sv.TryGetInt32(out int days) ? days : null;
            var others = Strings(r, "av").Where(n => !n.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToList();
            return new SecurityInfo(rt, sig, others, Strings(r, "fwoff"));
        }
        catch { return SecurityInfo.Empty; }
    }

    public static async Task<HealthInfo> RunAsync()
    {
        var tSec = Task.Run(ReadSecurity);
        var tPs = Task.Run(RunPowerShell);
        var tBat = Task.Run(ReadBattery);
        var tPlan = Task.Run(PowerPlans.List);
        var tJunk = Task.Run(() => Cleaner.SafeJunkBytes(7));
        var tStart = Task.Run(() => StartupManager.List().Count(e => e.Enabled));
        var tUnsigned = Task.Run(FindUnsigned);
        await Task.WhenAll(tPs, tBat, tPlan, tJunk, tStart, tUnsigned, tSec);

        var (name, type, health, wear, temp, boots, drivers) = tPs.Result;
        var (batPct, cycles) = tBat.Result;
        var plans = tPlan.Result;
        var high = plans.FirstOrDefault(p => p.Name.Contains("desempenho", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("performance", StringComparison.OrdinalIgnoreCase));
        int? bootLast = boots.Count > 0 ? boots[0] : null;
        int? bootAvg = boots.Count > 1 ? (int)boots.Average() : null;

        return new HealthInfo(name, type, health, wear, temp, batPct, cycles, bootLast, bootAvg,
            RebootPending(), System.Windows.SystemParameters.PowerLineStatus == System.Windows.PowerLineStatus.Online,
            plans.FirstOrDefault(p => p.Active)?.Name ?? "", high?.Guid, tStart.Result, tJunk.Result, drivers, tUnsigned.Result, tSec.Result);
    }

    // Pastas de dados do usuário, onde malware costuma rodar. Pastas de desenvolvimento ficam de fora para evitar alarme falso.
    private static readonly Dictionary<string, bool> SignedCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DevMarkers = { @"\node_modules\", @"\bin\debug\", @"\bin\release\", @"\obj\", @"\.dotnet\", @"\.nuget\", @"\.vscode\", @"\.cargo\", @"\.rustup\", @"\npm\", @"\pnpm\", @"\python" };

    private static List<UnsignedApp> FindUnsigned()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.GetTempPath().TrimEnd('\\'),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        var found = new Dictionary<string, UnsignedApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string? path;
                try { path = p.MainModule?.FileName; } catch { continue; } // processo protegido/de outro usuário
                if (path is null || found.ContainsKey(path)) continue;
                if (p.Id == Environment.ProcessId || !roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase))) continue;
                if (DevMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
                if (!IsSigned(path)) found[path] = new UnsignedApp(p.ProcessName, path);
                if (found.Count >= 5) break;
            }
        }
        return found.Values.ToList();
    }

    private static bool IsSigned(string path)
    {
        lock (SignedCache)
        {
            if (SignedCache.TryGetValue(path, out bool s)) return s;
            try { _ = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path); s = true; }
            catch { s = false; } // sem assinatura Authenticode (ou ilegível)
            return SignedCache[path] = s;
        }
    }

    private static bool RebootPending()
    {
        try
        {
            using var a = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var b = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            return a is not null || b is not null;
        }
        catch { return false; }
    }

    private static (int? pct, int? cycles) ReadBattery()
    {
        string file = Path.Combine(AppPaths.Dir, "bateria.xml");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", $"/batteryreport /xml /output \"{file}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            p?.WaitForExit(15000);
            if (!File.Exists(file)) return (null, null);
            var bat = XDocument.Load(file).Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");
            string? Val(string n) => bat?.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value;
            if (!double.TryParse(Val("DesignCapacity"), out double design) || !double.TryParse(Val("FullChargeCapacity"), out double full) || design <= 0) return (null, null);
            int? cyc = int.TryParse(Val("CycleCount"), out int c) ? c : null;
            return ((int)Math.Round(full / design * 100), cyc);
        }
        catch { return (null, null); }
        finally { try { File.Delete(file); } catch { } }
    }

    private static (string name, string type, string health, int? wear, int? temp, List<int> boots, List<string> drivers) RunPowerShell()
    {
        var boots = new List<int>();
        var drivers = new List<string>();
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return ("", "", "", null, null, boots, drivers);
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return ("", "", "", null, null, boots, drivers); }
            using var doc = JsonDocument.Parse(output.Result.Trim());
            var root = doc.RootElement;

            if (root.TryGetProperty("boot", out var b))
            {
                IEnumerable<JsonElement> items = b.ValueKind == JsonValueKind.Array ? b.EnumerateArray() : new[] { b };
                foreach (var e in items)
                    if (int.TryParse(e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString(), out int ms) && ms > 0) boots.Add(ms / 1000);
            }
            if (root.TryGetProperty("drivers", out var dv))
            {
                IEnumerable<JsonElement> items = dv.ValueKind == JsonValueKind.Array ? dv.EnumerateArray() : new[] { dv };
                foreach (var e in items)
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) drivers.Add(e.GetString()!);
            }
            if (!root.TryGetProperty("disk", out var d)) return ("", "", "", null, null, boots, drivers);
            string S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int? I(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;
            return (S("name"), S("type"), S("health"), I("wear"), I("temp"), boots, drivers);
        }
        catch { return ("", "", "", null, null, boots, drivers); }
    }
}
