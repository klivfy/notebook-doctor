using System.IO;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Origin { get; init; } = "";
    public bool Enabled { get; set; }
    public string State => Enabled ? "Ativado" : "Desativado";
    /// <summary>Estimativa pelo que o programa já consumiu desde que abriu (CPU acumulada e RAM). Não é a medição do Windows.</summary>
    public string Impact { get; set; } = "";
    internal string ApprovalSubKey { get; init; } = "Run";
}

/// <summary>
/// Lista e liga/desliga programas de inicialização do mesmo modo que o Gerenciador de Tarefas:
/// grava em HKCU\...\Explorer\StartupApproved (não apaga o item, então é reversível).
/// </summary>
public static class StartupManager
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static List<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        AddRun(result, Registry.CurrentUser, RunPath, "Usuário (registro)");
        AddRun(result, Registry.LocalMachine, RunPath, "Sistema (registro)");
        AddRun(result, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Sistema 32-bit (registro)");
        AddFolder(result, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Pasta Inicializar (usuário)");
        AddFolder(result, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Pasta Inicializar (todos)");
        FillImpact(result);
        return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void FillImpact(List<StartupEntry> entries)
    {
        var usage = new Dictionary<string, (double cpuSec, long mb)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    double cpu = 0; try { cpu = p.TotalProcessorTime.TotalSeconds; } catch { }
                    var u = usage.GetValueOrDefault(p.ProcessName);
                    usage[p.ProcessName] = (u.cpuSec + cpu, u.mb + p.WorkingSet64 / 1048576);
                }
                catch { }
            }
        }
        foreach (var e in entries)
        {
            string exe = ExeName(e.Command);
            if (exe.Length == 0 || !usage.TryGetValue(exe, out var u)) { e.Impact = "Não está em execução"; continue; }
            string level = u.cpuSec >= 20 || u.mb >= 300 ? "Alto" : u.cpuSec >= 5 || u.mb >= 100 ? "Médio" : "Baixo";
            e.Impact = $"{level} ({u.cpuSec:0} s de CPU, {u.mb} MB)";
        }
    }

    private static string ExeName(string command)
    {
        command = command.Trim();
        string path;
        if (command.StartsWith('"')) { int end = command.IndexOf('"', 1); path = end > 1 ? command[1..end] : command[1..]; }
        else { int i = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); path = i > 0 ? command[..(i + 4)] : command; }
        try { return Path.GetFileNameWithoutExtension(path); } catch { return ""; }
    }

    private static void AddRun(List<StartupEntry> list, RegistryKey hive, string path, string origin)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetValueNames())
            list.Add(new StartupEntry { Name = name, Command = key.GetValue(name)?.ToString() ?? "", Origin = origin, Enabled = IsEnabled("Run", name), ApprovalSubKey = "Run" });
    }

    private static void AddFolder(List<StartupEntry> list, string dir, string origin)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            list.Add(new StartupEntry { Name = Path.GetFileName(f), Command = f, Origin = origin, Enabled = IsEnabled("StartupFolder", Path.GetFileName(f)), ApprovalSubKey = "StartupFolder" });
    }

    private static bool IsEnabled(string sub, string name)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(Approved + sub);
            if (key?.GetValue(name) is byte[] { Length: > 0 } data)
                return (data[0] & 1) == 0; // 02/06 = ativado, 03 = desativado
        }
        return true;
    }

    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        SetEnabled(entry.ApprovalSubKey, entry.Name, enabled);
        entry.Enabled = enabled;
    }

    public static void SetEnabled(string approvalSubKey, string name, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Approved + approvalSubKey);
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
        key.SetValue(name, data, RegistryValueKind.Binary);
    }

    internal static string SubKeyOf(StartupEntry e) => e.ApprovalSubKey;
}
