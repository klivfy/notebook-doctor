using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

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
try { $h=(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher(); $c=$h.GetTotalHistoryCount(); if($c -gt 0){ $o.wu=@($h.QueryHistory(0,[Math]::Min($c,80)) | Where-Object { $_.Operation -eq 1 } | ForEach-Object { [ordered]@{t="$($_.Title)";d=$_.Date.ToLocalTime().ToString('yyyy-MM-ddTHH:mm:ss');r=[int]$_.ResultCode} }) } } catch {}
$o.wuoff=("$((Get-Service wuauserv).StartType)" -eq 'Disabled')
$o.wupause="$((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings').PauseUpdatesExpiryTime)"
$o.gpu=@(Get-CimInstance Win32_VideoController | ForEach-Object { [ordered]@{n="$($_.Name)";v="$($_.DriverVersion)";d=$(if($_.DriverDate){$_.DriverDate.ToString('yyyy-MM-dd')}else{''})} })
$t=Get-ScheduledTask -TaskPath '\Microsoft\Windows\Defrag\' -TaskName 'ScheduledDefrag'; if($t){ $o.defrag=("$($t.State)" -ne 'Disabled') }
$o.maintoff=((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance').MaintenanceDisabled -eq 1)
$o | ConvertTo-Json -Compress -Depth 4
""";

    private const string SecurityScript = """
$ErrorActionPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$o=[ordered]@{}
$mp=Get-MpComputerStatus
if($mp){ $o.rt=$mp.RealTimeProtectionEnabled; $o.sig=$mp.AntivirusSignatureAge }
$avs=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntivirusProduct)
$o.av=@($avs | ForEach-Object { "$($_.displayName)" })
$o.avon=@($avs | Where-Object { (([int]$_.productState -shr 12) -band 0xF) -eq 1 } | ForEach-Object { "$($_.displayName)" })
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
            static bool NotDefender(string n) => !n.Contains("Defender", StringComparison.OrdinalIgnoreCase);
            var others = Strings(r, "av").Where(NotDefender).ToList();
            // productState: o 4º dígito hexadecimal (da direita) igual a 1 significa proteção em tempo real ligada
            var othersOn = Strings(r, "avon").Where(NotDefender).ToList();
            return new SecurityInfo(rt, sig, others, Strings(r, "fwoff"), othersOn);
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
        var tVhd = Task.Run(FindVirtualDisks);
        var tOneDrive = Task.Run(OneDriveScan.Get);
        await Task.WhenAll(tPs, tBat, tPlan, tJunk, tStart, tUnsigned, tSec, tVhd, tOneDrive);

        var (name, type, health, wear, temp, boots, drivers) = tPs.Result;
        var (batPct, cycles) = tBat.Result;
        var plans = tPlan.Result;
        var high = plans.FirstOrDefault(p => p.Name.Contains("desempenho", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("performance", StringComparison.OrdinalIgnoreCase));
        int? bootLast = boots.Count > 0 ? boots[0] : null;
        int? bootAvg = boots.Count > 1 ? (int)boots.Average() : null;

        return new HealthInfo(name, type, health, wear, temp, batPct, cycles, bootLast, bootAvg,
            RebootPending(), System.Windows.SystemParameters.PowerLineStatus == System.Windows.PowerLineStatus.Online,
            plans.FirstOrDefault(p => p.Active)?.Name ?? "", high?.Guid, tStart.Result, tJunk.Result, drivers, tUnsigned.Result, tSec.Result, tVhd.Result, tOneDrive.Result,
            _updates, _gpus, _maintenance);
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

    /// <summary>
    /// M40: discos virtuais do WSL (distribuições da Microsoft Store e as instaladas com "wsl --install") e do Docker Desktop.
    /// Só lê o tamanho dos arquivos.
    /// </summary>
    private static List<VirtualDisk> FindVirtualDisks()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var found = new List<VirtualDisk>();
        void Add(string name, string path)
        {
            try { if (File.Exists(path)) found.Add(new VirtualDisk(name, path, new FileInfo(path).Length)); } catch { }
        }
        try
        {
            // Distribuições da Store: Packages\CanonicalGroupLimited.Ubuntu_...\LocalState\ext4.vhdx
            foreach (var pkg in Directory.EnumerateDirectories(Path.Combine(local, "Packages")))
            {
                string name = Path.GetFileName(pkg).Split('_')[0];
                // "CanonicalGroupLimited.Ubuntu22.04LTS" vira "Ubuntu22.04LTS": o nome vem depois do editor
                Add(name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name, Path.Combine(pkg, "LocalState", "ext4.vhdx"));
            }
        }
        catch { }
        try
        {
            // WSL moderno: %LocalAppData%\wsl\{guid}\ext4.vhdx
            string wsl = Path.Combine(local, "wsl");
            if (Directory.Exists(wsl))
                foreach (var dir in Directory.EnumerateDirectories(wsl)) Add("WSL", Path.Combine(dir, "ext4.vhdx"));
        }
        catch { }
        Add("Docker Desktop", Path.Combine(local, @"Docker\wsl\data\ext4.vhdx"));
        Add("Docker Desktop", Path.Combine(local, @"Docker\wsl\disk\docker_data.vhdx"));
        return found;
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

    // A22, A23 e A39: lidos pelo mesmo script do disco, numa só chamada ao PowerShell
    private static UpdateStatus? _updates;
    private static List<GpuDriver> _gpus = new();
    private static WindowsMaintenance? _maintenance;

    private static void ReadUpkeep(JsonElement root)
    {
        _updates = new UpdateStatus(
            root.TryGetProperty("wu", out var wu) ? UpdateRules.ParseHistory(wu) : new List<UpdateEntry>(),
            root.TryGetProperty("wuoff", out var off) && off.ValueKind == JsonValueKind.True,
            root.TryGetProperty("wupause", out var pause) && pause.ValueKind == JsonValueKind.String ? UpdateRules.ParsePause(pause.GetString()) : null);
        _gpus = root.TryGetProperty("gpu", out var gpu) ? DriverRules.Parse(gpu) : new();
        bool? defrag = root.TryGetProperty("defrag", out var df) && df.ValueKind is JsonValueKind.True or JsonValueKind.False ? df.GetBoolean() : null;
        _maintenance = new WindowsMaintenance(defrag, root.TryGetProperty("maintoff", out var mo) && mo.ValueKind == JsonValueKind.True);
    }

    private static (string name, string type, string health, int? wear, int? temp, List<int> boots, List<string> drivers) RunPowerShell()
    {
        var boots = new List<int>();
        var drivers = new List<string>();
        _updates = null; _gpus = new(); _maintenance = null;
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
            ReadUpkeep(root);

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
