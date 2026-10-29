using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NotebookDoctor.Services;

public sealed record UninstallRun(WaitState State, int? ExitCode);

/// <summary>
/// Fase 5: abre o desinstalador pelo Shell (o UAC aparece quando ele exige administrador) e espera ele terminar de
/// verdade. Inno Setup e NSIS se copiam para a pasta temporária e fecham o original na hora; por isso a espera
/// acompanha a árvore de processos (<see cref="ProcessTree"/>), não só o processo aberto.
/// </summary>
public static class UninstallRunner
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Limit = TimeSpan.FromHours(2);

    /// <param name="userSaysDone">A pessoa clicou em "O desinstalador terminou": para de esperar.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">1223 quando a pessoa cancela o UAC.</exception>
    public static async Task<UninstallRun> RunAsync(UninstallCommand cmd, Func<bool> userSaysDone, CancellationToken ct)
    {
        string exe = Path.IsPathRooted(cmd.File) ? cmd.File : SystemExe.System32(cmd.File);
        using var root = Process.Start(new ProcessStartInfo(exe, cmd.Arguments) { UseShellExecute = true, WorkingDirectory = Environment.SystemDirectory });
        if (root is null) return new UninstallRun(WaitState.Done, null); // o Shell entregou a outro processo já aberto
        var tracked = new HashSet<int> { root.Id };
        var watch = new UninstallWatch(DateTime.Now, Settle, Limit);
        while (true)
        {
            await Task.Delay(1000, ct);
            if (userSaysDone()) return new UninstallRun(WaitState.Done, ExitCode(root));
            int alive = ProcessTree.Step(tracked, Snapshot());
            var state = watch.Observe(DateTime.Now, alive);
            if (state is WaitState.Done or WaitState.TimedOut) return new UninstallRun(state, ExitCode(root));
        }
    }

    private static int? ExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode : null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Todos os processos com o pai de cada um (Toolhelp32), sem precisar de permissão para abrir os processos.</summary>
    private static List<ProcInfo> Snapshot()
    {
        var list = new List<ProcInfo>();
        IntPtr snap = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snap == InvalidHandle) return list;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (bool ok = Process32First(snap, ref entry); ok; ok = Process32Next(snap, ref entry))
                list.Add(new ProcInfo((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    private const uint SnapProcess = 0x2;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Fase 5: atalhos (.lnk) da Área de Trabalho e do Menu Iniciar com o destino, para achar os que apontam para um programa desinstalado.</summary>
public static class ShortcutScan
{
    public static List<ShortcutInfo> List(KnownPlaces places)
    {
        var list = new List<ShortcutInfo>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var root in new[] { places.Desktop, places.PublicDesktop, places.StartMenu, places.CommonStartMenu }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                try
                {
                    string target = StartMenu.ReadTarget(lnk);
                    if (target.Length > 0) list.Add(new ShortcutInfo(lnk, target));
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException) { } // atalho corrompido ou sem permissão
            }
        }
        return list;
    }
}

