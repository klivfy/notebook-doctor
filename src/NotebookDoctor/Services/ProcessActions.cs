using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NotebookDoctor.Services;

/// <summary>Encerra ou muda prioridade de programas. Nunca age sobre os protegidos de <see cref="ProcessTools"/>.</summary>
public static class ProcessActions
{
    // ---------- A19: modo eficiência (o mesmo do Gerenciador de Tarefas: EcoQoS + prioridade baixa) ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    /// <summary>Liga ou desliga o modo eficiência em todos os processos do programa. Retorna quantos mudaram.</summary>
    public static int SetEfficiency(string name, bool on)
    {
        if (ProcessTools.IsProtected(name)) return 0;
        int n = 0;
        foreach (var p in Process.GetProcessesByName(name))
            using (p)
            {
                // PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION
                IntPtr h = Native.OpenProcess(0x0200 | 0x1000, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var st = new PowerThrottlingState { Version = 1, ControlMask = 1, StateMask = on ? 1u : 0u }; // 1 = EXECUTION_SPEED
                    if (!SetProcessInformation(h, 4 /* ProcessPowerThrottling */, ref st, Marshal.SizeOf<PowerThrottlingState>())) continue;
                    try { p.PriorityClass = on ? ProcessPriorityClass.Idle : ProcessPriorityClass.Normal; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    n++;
                }
                finally { Native.CloseHandle(h); }
            }
        return n;
    }

    /// <summary>Muda a prioridade de todos os processos com esse nome. Retorna quantos foram alterados.</summary>
    public static int SetPriority(string name, ProcessPriorityClass priority)
    {
        if (ProcessTools.IsProtected(name)) return 0;
        int changed = 0;
        foreach (var p in Process.GetProcessesByName(name))
            using (p)
            {
                try { p.PriorityClass = priority; changed++; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* sem permissão ou já terminou */ }
            }
        return changed;
    }

    /// <summary>Encerra todos os processos com esse nome. Retorna quantos foram encerrados.</summary>
    public static int Kill(string name)
    {
        if (ProcessTools.IsProtected(name)) return 0;
        int killed = 0;
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try { p.Kill(); killed++; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* sem permissão ou já terminou */ }
            }
        }
        return killed;
    }
}
