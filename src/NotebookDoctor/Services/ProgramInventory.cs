using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 5: lê os programas instalados (chaves de desinstalação), os apps da Microsoft Store e as atualizações do
/// Windows. Só lê; remover um app da Store é a única escrita, e quem chama confirma antes.
/// </summary>
public static class ProgramInventory
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallPath32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<UninstallKey> ReadUninstallKeys()
    {
        var list = new List<UninstallKey>();
        Read(Registry.LocalMachine, "HKEY_LOCAL_MACHINE", UninstallPath, machine: true, wow64: false);
        Read(Registry.LocalMachine, "HKEY_LOCAL_MACHINE", UninstallPath32, machine: true, wow64: true);
        Read(Registry.CurrentUser, "HKEY_CURRENT_USER", UninstallPath, machine: false, wow64: false);
        return list;

        void Read(RegistryKey hive, string hiveName, string path, bool machine, bool wow64)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k is null) continue;
                    string? S(string value) => k.GetValue(value)?.ToString();
                    int I(string value) => k.GetValue(value) is int i ? i : 0;
                    list.Add(new UninstallKey
                    {
                        KeyName = name, RegistryPath = $@"{hiveName}\{path}\{name}", MachineWide = machine, Wow64 = wow64,
                        DisplayName = S("DisplayName"), DisplayVersion = S("DisplayVersion"), Publisher = S("Publisher"),
                        InstallLocation = S("InstallLocation"), InstallDate = S("InstallDate"), UninstallString = S("UninstallString"),
                        QuietUninstallString = S("QuietUninstallString"), DisplayIcon = S("DisplayIcon"), ParentKeyName = S("ParentKeyName"),
                        ReleaseType = S("ReleaseType"), EstimatedSizeKb = k.GetValue("EstimatedSize") as int?,
                        SystemComponent = I("SystemComponent") == 1, WindowsInstaller = I("WindowsInstaller") == 1, NoRemove = I("NoRemove") == 1,
                    });
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
    }

    // Os nomes dos apps da Store vêm do Menu Iniciar (Get-StartApps): o nome do pacote é técnico ("Microsoft.WindowsCalculator")
    private const string InventoryScript = """
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$ErrorActionPreference='SilentlyContinue'
$names=@{}
foreach($a in Get-StartApps){ $f=($a.AppID -split '!')[0]; if($f -and -not $names.ContainsKey($f)){ $names[$f]=$a.Name } }
$store=@(Get-AppxPackage -PackageTypeFilter Main | ForEach-Object { [ordered]@{ n=$_.Name; f=$_.PackageFullName; p=$_.Publisher; v="$($_.Version)"; l=$_.InstallLocation; s="$($_.SignatureKind)"; fw=[bool]$_.IsFramework; nr=[bool]$_.NonRemovable; a="$($_.Architecture)"; d=$(if($_.PackageFamilyName){$names[$_.PackageFamilyName]}) } })
$kb=@(Get-HotFix | ForEach-Object { [ordered]@{ id=$_.HotFixID; d=$_.Description; t=$(if($_.InstalledOn){$_.InstalledOn.ToString('yyyy-MM-dd')}else{''}) } })
[ordered]@{ store=$store; kb=$kb } | ConvertTo-Json -Compress -Depth 3
""";

    /// <summary>Apps da Store e atualizações do Windows, numa chamada só ao PowerShell (leva alguns segundos).</summary>
    public static (List<StorePackage> Store, List<Hotfix> Updates) ReadStoreAndUpdates() =>
        ProgramRules.ParseInventory(PowerShell(InventoryScript, 90_000));

    /// <summary>Remove o app da Store para o usuário atual. Devolve a mensagem de erro do Windows, ou null quando deu certo.</summary>
    public static string? RemoveStoreApp(string packageFullName)
    {
        string name = packageFullName.Replace("'", "''");
        return Ok(PowerShell($$"""
[Console]::OutputEncoding=[Text.Encoding]::UTF8
try { Remove-AppxPackage -Package '{{name}}' -ErrorAction Stop; 'ok' } catch { $_.Exception.Message }
""", 180_000));
    }

    /// <summary>
    /// Leva 5.3: remove o app de todas as contas e do pacote que o Windows instala em contas novas (provisionado).
    /// Exige administrador. Devolve o erro do Windows, ou null quando deu certo.
    /// </summary>
    public static string? RemoveStoreAppAllUsers(string packageFullName)
    {
        string full = packageFullName.Replace("'", "''");
        string name = full.Split('_')[0];
        return Ok(PowerShell($$"""
[Console]::OutputEncoding=[Text.Encoding]::UTF8
try {
  Remove-AppxPackage -Package '{{full}}' -AllUsers -ErrorAction Stop
  Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq '{{name}}' } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null
  'ok'
} catch { $_.Exception.Message }
""", 300_000));
    }

    /// <summary>Leva 5.3: volta o app ao estado de recém-instalado (configurações e dados locais apagados).</summary>
    public static string? ResetStoreApp(string packageFullName)
    {
        string full = packageFullName.Replace("'", "''");
        return Ok(PowerShell($$"""
[Console]::OutputEncoding=[Text.Encoding]::UTF8
try { Get-AppxPackage | Where-Object { $_.PackageFullName -eq '{{full}}' } | Reset-AppxPackage -ErrorAction Stop; 'ok' } catch { $_.Exception.Message }
""", 180_000));
    }

    private static string? Ok(string output)
    {
        string o = output.Trim();
        return o == "ok" ? null : o.Length > 0 ? o : Loc.T("O PowerShell não respondeu.");
    }

    /// <summary>Pastas que não pertencem a um programa só: medir uma delas somaria o que não é dele.</summary>
    private static IEnumerable<string> SharedRoots()
    {
        foreach (var f in new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86, Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
        })
            yield return Environment.GetFolderPath(f);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
    }

    private static string SizeCachePath => Path.Combine(AppPaths.Dir, "tamanhos-programas.json");

    /// <summary>
    /// Mede a pasta dos itens sem tamanho informado pelo instalador, um de cada vez e fora da thread de interface.
    /// Avisa a cada medição para a tabela ir se completando. Leva 9.3: as pastas que não mudaram desde a última medição
    /// (<see cref="FolderSizeCache"/>) aparecem logo, sem medir; só as outras são percorridas.
    /// </summary>
    public static Task MeasureAsync(IReadOnlyList<InstalledProgram> items, Action<InstalledProgram> measured, Action<int, int> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var roots = SharedRoots().ToList();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var todo = items.Where(p => p.SizeSource == SizeSource.None && p.Kind != ProgramKind.Update)
                .Select(p => (p, dir: ProgramRules.FolderOf(p, Directory.Exists, File.Exists)))
                .Where(x => x.dir is not null && ProgramRules.CanMeasure(x.dir, roots, windows))
                .Select(x => (x.p, dir: x.dir!, changed: LastWriteUtc(x.dir!)))
                .ToList();
            FolderSizeCache cache;
            try { cache = FolderSizeCache.Parse(File.Exists(SizeCachePath) ? File.ReadAllText(SizeCachePath) : null); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { cache = new FolderSizeCache(); }

            var now = DateTime.UtcNow;
            var pending = new List<(InstalledProgram p, string dir, DateTime? changed)>();
            foreach (var t in todo)
            {
                if (t.changed is { } c && cache.TryGet(t.dir, c, now, out long cached)) Apply(t.p, cached);
                else pending.Add(t);
            }
            try
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress(i + 1, pending.Count);
                    long bytes = DiskUsage.FolderSize(pending[i].dir, ct);
                    if (pending[i].changed is { } c) cache.Set(pending[i].dir, c, bytes, DateTime.UtcNow);
                    if (bytes > 0) Apply(pending[i].p, bytes);
                }
            }
            finally
            {
                // Também quando a pessoa sai da aba no meio: o que já foi medido vale para a próxima vez
                try { AtomicFile.WriteAllText(SizeCachePath, cache.Serialize(DateTime.UtcNow)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            void Apply(InstalledProgram p, long bytes)
            {
                p.Bytes = bytes;
                p.SizeSource = SizeSource.Folder;
                measured(p);
            }
        }, ct);

    private static DateTime? LastWriteUtc(string dir)
    {
        try { return Directory.GetLastWriteTimeUtc(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string PowerShell(string script, int timeoutMs)
    {
        try
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
            return output.Result;
        }
        catch { return ""; }
    }
}
