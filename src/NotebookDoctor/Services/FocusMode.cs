using System.Diagnostics;

namespace NotebookDoctor.Services;

/// <summary>
/// Modo Foco: dá prioridade ao programa em uso sem fechar nada.
/// 1) ativa o plano Alto desempenho, se existir; 2) baixa a prioridade dos programas pesados em segundo plano.
/// Tudo é revertido ao desativar (ou ao fechar o app). Nenhum programa é encerrado nem suspenso.
/// </summary>
public sealed class FocusMode
{
    private readonly Dictionary<int, ProcessPriorityClass> _original = new();
    private string? _previousPlan;

    public bool Active { get; private set; }
    public int LoweredCount => _original.Count;

    /// <summary>Retorna uma frase com o que foi feito.</summary>
    public async Task<string> StartAsync(Snapshot latest, HealthInfo health)
    {
        if (Active) return "O Modo Foco já está ativo.";
        Active = true;
        string planMsg = "";

        if (health.HighPlanGuid is { } high)
        {
            var plans = await Task.Run(PowerPlans.List);
            _previousPlan = plans.FirstOrDefault(p => p.Active)?.Guid;
            if (_previousPlan is not null && !_previousPlan.Equals(high, StringComparison.OrdinalIgnoreCase)
                && await Task.Run(() => PowerPlans.SetActive(high)))
                planMsg = "Plano Alto desempenho ativado; ";
            else _previousPlan = null;
        }

        int fgPid = Native.ForegroundProcessId();
        string? fgName = null;
        try { fgName = Process.GetProcessById(fgPid).ProcessName; } catch { }

        // Programas pesados (não protegidos, que não são o da janela em uso) passam para prioridade "abaixo do normal"
        var targets = latest.Groups
            .Where(g => (g.CpuPct >= 3 || g.MemMB >= 300) && !ProcessTools.IsProtected(g.Name) && !g.Name.Equals(fgName, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            foreach (var name in targets)
                foreach (var p in Process.GetProcessesByName(name))
                    using (p)
                    {
                        try
                        {
                            if (p.PriorityClass is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal)
                            {
                                _original[p.Id] = p.PriorityClass;
                                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            }
                        }
                        catch { /* sem permissão ou já fechou */ }
                    }
        });
        return $"{planMsg}{Pt.N(_original.Count, "processo", "processos")} em segundo plano com prioridade reduzida" + (fgName is null ? "." : $" (mantido: {fgName}).");
    }

    public async Task<string> StopAsync()
    {
        if (!Active) return "";
        Active = false;
        int restored = 0;
        await Task.Run(() =>
        {
            foreach (var (pid, cls) in _original)
            {
                try { using var p = Process.GetProcessById(pid); p.PriorityClass = cls; restored++; } catch { /* já fechou */ }
            }
            _original.Clear();
        });
        string plan = "";
        if (_previousPlan is not null && await Task.Run(() => PowerPlans.SetActive(_previousPlan))) plan = " e plano de energia anterior";
        _previousPlan = null;
        string what = restored == 1 ? "1 prioridade" : $"{restored} prioridades";
        return plan.Length > 0 ? $"{what}{plan} restaurados." : $"{what} {(restored == 1 ? "restaurada" : "restauradas")}.";
    }
}
