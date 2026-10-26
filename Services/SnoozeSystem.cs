using System.Diagnostics;
using System.IO;

namespace NotebookDoctor.Services;

/// <summary>As ações reais do modo soneca: as mesmas das abas Inicialização, Ajustes e Processos.</summary>
public sealed class WindowsSnoozeSystem : ISnoozeSystem
{
    public void SetStartup(string approvalSubKey, string name, bool enabled) => StartupManager.SetEnabled(approvalSubKey, name, enabled);
    public void SetServiceStart(string name, int start) => ServiceManager.SetStart(name, start);
    public void SetTask(string path, bool enabled) => ScheduledTasks.SetEnabled(path, enabled);
    public int Kill(string processName) => ProcessActions.Kill(processName);
}

/// <summary>Lê o que há no PC e monta os aplicativos candidatos ao soneca (<see cref="SnoozeRules.Build"/>).</summary>
public static class SnoozeScanner
{
    public static List<SnoozeApp> Scan(IEnumerable<string> userNever)
    {
        var startup = StartupManager.List().Select(e =>
            new SnoozeStartup(e.Name, StartupManager.SubKeyOf(e), e.Command, e.FilePath ?? CommandLine.ExecutablePath(e.Command) ?? "", e.Enabled)).ToList();
        return SnoozeRules.Build(startup, ServiceManager.List(), ScheduledTasks.List(), Processes(), ServiceManager.SystemRoot, userNever);
    }

    /// <summary>Processos somados por nome. Sem permissão para ler o caminho ou a janela, o processo conta como sem janela e sem caminho.</summary>
    public static List<SnoozeProcess> Processes()
    {
        var byName = new Dictionary<string, (string Path, long Mb, double Cpu, bool Window, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    double cpu = 0;
                    try { cpu = p.TotalProcessorTime.TotalSeconds; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    bool window = false;
                    try { window = p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { }
                    var cur = byName.GetValueOrDefault(p.ProcessName);
                    byName[p.ProcessName] = (cur.Path.Length > 0 ? cur.Path : path, cur.Mb + p.WorkingSet64 / 1048576, cur.Cpu + cpu, cur.Window || window, cur.Count + 1);
                }
                catch (InvalidOperationException) { /* terminou enquanto lia */ }
            }
        }
        return byName.Select(kv => new SnoozeProcess(kv.Key, kv.Value.Path, kv.Value.Mb, kv.Value.Cpu, kv.Value.Window, kv.Value.Count)).ToList();
    }

    /// <summary>Nomes (minúsculos, sem .exe) dos processos abertos, para o vigia.</summary>
    public static HashSet<string> RunningNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p) names.Add(p.ProcessName);
        }
        return names;
    }
}
