using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using NotebookDoctor.Services;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 7.2 (C13): drivers só pelo Windows Update (Windows Update Agent, <c>Type='Driver'</c>), nunca de sites de terceiros.
/// Antes de instalar, quem chama cria o ponto de restauração; aqui fica a cópia do driver atual (<c>pnputil /export-driver</c>),
/// a instalação e a volta. Tudo exige administrador, que quem chama já conferiu.
/// </summary>
public static class DriverUpdater
{
    private const string ScanScript = """
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$ErrorActionPreference='Stop'
$o=[ordered]@{}
try {
  $session=New-Object -ComObject Microsoft.Update.Session
  $searcher=$session.CreateUpdateSearcher()
  $res=$searcher.Search("IsInstalled=0 and Type='Driver'")
  $o.offered=@($res.Updates | ForEach-Object { [ordered]@{ id="$($_.Identity.UpdateID)"; t="$($_.Title)"; mf="$($_.DriverManufacturer)"; model="$($_.DriverModel)"; cls="$($_.DriverClass)"; hw="$($_.DriverHardwareID)"; date=$(if($_.DriverVerDate){$_.DriverVerDate.ToString('yyyy-MM-dd')}else{''}); size=[int64]$_.MaxDownloadSize; opt=[bool]$_.BrowseOnly; eula=[bool]$_.EulaAccepted; eulaText=$(if(-not $_.EulaAccepted){"$($_.EulaText)"}else{''}) } })
} catch { $o.error="$($_.Exception.Message)"; $o.offered=@() }
$ErrorActionPreference='SilentlyContinue'
$o.installed=@(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceID -and $_.HardWareID } | ForEach-Object { [ordered]@{ name="$($_.DeviceName)"; dev="$($_.DeviceID)"; hw=@($_.HardWareID); v="$($_.DriverVersion)"; d=$(if($_.DriverDate){$_.DriverDate.ToString('yyyy-MM-dd')}else{''}); inf="$($_.InfName)"; prov="$($_.DriverProviderName)" } })
$o | ConvertTo-Json -Compress -Depth 5
""";

    // O id da atualização é um GUID validado antes de entrar no texto do script: nada digitado chega ao PowerShell.
    private static string InstallScript(Guid updateId, bool acceptEula) => $$"""
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$ErrorActionPreference='Stop'
function Out-Result($ok,$stage,$code,$hr,$reboot){ [ordered]@{ ok=$ok; stage=$stage; code=[int]$code; hr=[int]$hr; reboot=[bool]$reboot } | ConvertTo-Json -Compress }
try {
  $session=New-Object -ComObject Microsoft.Update.Session
  $found=$session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Driver'")
  $u=$null
  foreach($x in $found.Updates){ if($x.Identity.UpdateID -eq '{{updateId}}'){ $u=$x } }
  if(-not $u){ Out-Result $false 'find' 0 0 $false; exit 0 }
  {{(acceptEula ? "if(-not $u.EulaAccepted){ $u.AcceptEula() }" : "if(-not $u.EulaAccepted){ Out-Result $false 'eula' 0 0 $false; exit 0 }")}}
  $coll=New-Object -ComObject Microsoft.Update.UpdateColl
  [void]$coll.Add($u)
  $dl=$session.CreateUpdateDownloader(); $dl.Updates=$coll
  $dr=$dl.Download()
  if($dr.ResultCode -ne 2){ Out-Result $false 'download' $dr.ResultCode $dr.HResult $false; exit 0 }
  $inst=$session.CreateUpdateInstaller(); $inst.Updates=$coll
  $ir=$inst.Install()
  Out-Result ($ir.ResultCode -eq 2) 'install' $ir.ResultCode $ir.HResult $ir.RebootRequired
} catch { Out-Result $false 'error' 0 ($_.Exception.HResult) $false }
""";

    public static async Task<DriverScan> ScanAsync(CancellationToken ct = default)
    {
        var (output, _) = await PowerShellAsync(ScanScript, TimeSpan.FromMinutes(5), ct);
        return DriverUpdateRules.ParseScan(output.Trim());
    }

    /// <summary>Instala uma atualização de driver. <paramref name="eulaAccepted"/> só é verdadeiro depois de a pessoa ler e aceitar o contrato na tela.</summary>
    public static async Task<DriverInstallResult> InstallAsync(OfferedDriver offered, bool eulaAccepted)
    {
        if (!Guid.TryParse(offered.UpdateId, out var id)) throw new ArgumentException("Identificador de atualização inválido.", nameof(offered));
        // Sem limite de tempo curto: baixar e instalar driver de vídeo pode levar muitos minutos, e matar no meio estragaria o driver
        var (output, timedOut) = await PowerShellAsync(InstallScript(id, eulaAccepted), TimeSpan.FromHours(2), CancellationToken.None, killOnTimeout: false);
        if (timedOut) return new DriverInstallResult(false, "timeout", 0, 0, false);
        try { return DriverUpdateRules.ParseInstall(output.Trim()); }
        catch (System.Text.Json.JsonException) { return new DriverInstallResult(false, "parse", 0, 0, false); }
    }

    // ---------- cópia e volta ----------

    public static string BackupsRoot(string appDir) => Path.Combine(appDir, "driver-backups");

    /// <summary>Guarda uma cópia do driver instalado. Devolve null se o pnputil não conseguiu: nesse caso nada deve ser instalado.</summary>
    public static async Task<DriverBackup?> BackupAsync(InstalledDriver current, string title, string appDir, DateTime now)
    {
        if (!DriverUpdateRules.IsExportable(current.InfName)) return null;
        string id = DriverUpdateRules.BackupId(now, current.DeviceName);
        string folder = Path.Combine(BackupsRoot(appDir), id);
        Directory.CreateDirectory(folder);
        var run = await PnpUtilAsync(DriverUpdateRules.ExportArgs(current.InfName, folder));
        bool hasFiles = Directory.EnumerateFiles(folder, "*.inf", SearchOption.TopDirectoryOnly).Any();
        if (run != 0 || !hasFiles)
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        var backup = new DriverBackup
        {
            Id = id, Created = now, DeviceName = current.DeviceName, DeviceId = current.DeviceId, Version = current.Version,
            Date = current.Date?.ToString("yyyy-MM-dd") ?? "", InfName = current.InfName, Folder = folder, Replaced = title,
        };
        await File.WriteAllTextAsync(Path.Combine(folder, DriverBackupIndex.FileName), DriverBackupIndex.Serialize(backup));
        return backup;
    }

    public static List<DriverBackup> ListBackups(string appDir)
    {
        var list = new List<DriverBackup>();
        string root = BackupsRoot(appDir);
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                string file = Path.Combine(dir, DriverBackupIndex.FileName);
                if (!File.Exists(file)) continue;
                var b = DriverBackupIndex.Read(File.ReadAllText(file));
                // O manifesto é só um arquivo: a pasta que vale é a que o lê, nunca uma que ele aponte para fora
                if (b is null) continue;
                b.Folder = dir;
                list.Add(b);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }

    public static DriverBackup? Find(string appDir, string id) => ListBackups(appDir).FirstOrDefault(b => b.Id == id);

    public static void DeleteBackup(string appDir, DriverBackup b)
    {
        string root = Path.GetFullPath(BackupsRoot(appDir)) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(b.Folder);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return; // nunca apaga fora da pasta das cópias
        try { Directory.Delete(full, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Volta ao driver da cópia: instala a cópia e confere o que ficou ativo. Se o driver novo continuar valendo (o Windows
    /// prefere o mais recente), tira o pacote novo do dispositivo sem forçar e confere de novo.
    /// </summary>
    public static async Task<RestoreOutcome> RestoreAsync(DriverBackup b, string appDir)
    {
        if (!Directory.Exists(b.Folder) || !Directory.EnumerateFiles(b.Folder, "*.inf").Any()) return RestoreOutcome.CopyMissing;
        string root = Path.GetFullPath(BackupsRoot(appDir)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(b.Folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return RestoreOutcome.CopyMissing;

        int add = await PnpUtilAsync(DriverUpdateRules.AddArgs(b.Folder));
        if (add != 0 && add != 3010) return RestoreOutcome.AddFailed; // 3010 = precisa reiniciar, mas instalou

        var now = Current(await ScanInstalledAsync(), b.DeviceId);
        if (now is not null && now.Version == b.Version) return RestoreOutcome.Restored;
        if (now is not null && DriverUpdateRules.IsExportable(now.InfName))
        {
            await PnpUtilAsync(DriverUpdateRules.DeleteArgs(now.InfName));
            now = Current(await ScanInstalledAsync(), b.DeviceId);
            if (now is not null && now.Version == b.Version) return RestoreOutcome.Restored;
        }
        return RestoreOutcome.StillNewer;
    }

    private static InstalledDriver? Current(IEnumerable<InstalledDriver> all, string deviceId) =>
        all.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    public static async Task<IReadOnlyList<InstalledDriver>> ScanInstalledAsync() => (await ScanAsync()).Installed;

    // ---------- processos ----------

    private static async Task<int> PnpUtilAsync(string[] args)
    {
        var psi = new ProcessStartInfo("pnputil.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new Win32Exception("pnputil não iniciou.");
        var drain = Task.WhenAll(p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            return -1;
        }
        await drain;
        return p.ExitCode;
    }

    private static async Task<(string Output, bool TimedOut)> PowerShellAsync(string script, TimeSpan limit, CancellationToken ct, bool killOnTimeout = true)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi) ?? throw new Win32Exception("powershell não iniciou.");
        var output = p.StandardOutput.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (killOnTimeout || ct.IsCancellationRequested)
                try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            if (ct.IsCancellationRequested) throw;
            return ("", true);
        }
        return (await output, false);
    }
}
