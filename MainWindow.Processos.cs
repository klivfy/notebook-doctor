using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Processos: lista, detalhe, encerrar, prioridade, modo eficiência e exceções de aviso.</summary>
public partial class MainWindow
{
    private void ProcSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");

    // ---------- Detalhe do processo (A47) ----------
    private void ProcGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name);
    }

    // Mesmo detalhe do clique duplo, alcançável pelo teclado
    private void ProcDetails_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name);
        else ProcMsg.Text = "Selecione um programa na lista.";
    }

    // ---------- Processos ----------
    private void RefreshProcesses(Snapshot s)
    {
        var selected = (ProcGrid.SelectedItem as ProcRow)?.Name;
        var sort = ProcGrid.Items.SortDescriptions.ToList();
        ProcGrid.ItemsSource = s.Groups.OrderByDescending(g => g.CpuPct).ThenByDescending(g => g.MemMB).Select(g =>
        {
            var about = ProcessKnowledge.Describe(g.Name);
            return new ProcRow(g.Name, g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, about.Description, about.SafetyText, ProcessIcons.Get(g.Name));
        }).ToList();
        ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
        foreach (var d in sort) ProcGrid.Items.SortDescriptions.Add(d);
        if (selected is not null) ProcGrid.SelectedItem = ProcGrid.Items.OfType<ProcRow>().FirstOrDefault(g => g.Name == selected);
    }

    private void KillSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        KillProgram(g.Name, g.Count, g.MemMB);
    }

    // ---------- Prioridade manual (A18) ----------
    private void LowerPriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: true);
    private void RestorePriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: false);

    private void SetPriority(bool lower)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessTools.IsProtected(g.Name)) { ProcMsg.Text = $"{g.Name} é do sistema: a prioridade não é alterada."; return; }
        if (Simulate(lower ? $"Baixar a prioridade de {g.Name}" : $"Restaurar a prioridade de {g.Name}",
                     $"Mudaria {Pt.N(g.Count, "processo", "processos")} para prioridade {(lower ? "abaixo do normal" : "normal")}.")) return;
        int n = ProcessActions.SetPriority(g.Name, lower ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal);
        ProcMsg.Text = n > 0 ? $"{g.Name}: prioridade {(lower ? "reduzida" : "normal")} em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível alterar: falta permissão.";
        if (n > 0 && lower) ActionLog.Add($"Prioridade reduzida: {g.Name}", Pt.N(n, "processo", "processos"), "priority", g.Name);
    }

    // ---------- Exceções de avisos (M20) a partir da tabela de processos ----------
    private void AlertException_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        var s = Host.Settings;
        if (s.IsAlertException(g.Name)) { ProcMsg.Text = $"{g.Name} já está nas exceções."; return; }
        s.AlertExceptions = AppSettings.ParseNames(string.Join("\n", s.AlertExceptions.Append(g.Name)));
        s.Save();
        LoadSettingsIntoForm();
        if (Host.HeavyAlert?.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase) == true) Host.IgnoreHeavy(g.Name);
        ActionLog.Add($"Sem avisos: {g.Name}", "Adicionado às exceções de avisos (Configurações › Exceções).");
        ShowSnack($"{g.Name} não gera mais avisos", "Ele continua sendo medido e aparece normalmente nas tabelas.",
                  "Desfazer", () =>
                  {
                      s.AlertExceptions.RemoveAll(n => n.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                      s.Save();
                      LoadSettingsIntoForm();
                  });
    }

    // ---------- A19: modo eficiência ----------
    private void Efficiency_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessTools.IsProtected(g.Name)) { ProcMsg.Text = $"{g.Name} é protegido: o modo eficiência não é aplicado."; return; }
        if (Simulate($"Modo eficiência em {g.Name}", $"Ligaria o modo econômico do Windows (EcoQoS) e a prioridade baixa em {Pt.N(g.Count, "processo", "processos")}. O programa continuaria aberto.")) return;
        int n = ProcessActions.SetEfficiency(g.Name, on: true);
        ProcMsg.Text = n > 0 ? $"{g.Name}: modo eficiência em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível aplicar: falta permissão.";
        if (n > 0) ActionLog.Add($"Modo eficiência: {g.Name}", Pt.N(n, "processo", "processos"), "efficiency", g.Name);
    }
}
