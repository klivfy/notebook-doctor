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

    // M12: Enter abre o detalhe e Delete encerra (sempre com a confirmação de sempre)
    private void ProcGrid_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { ProcDetails_Click(sender, e); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Delete) { KillSelected_Click(sender, e); e.Handled = true; }
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
        // M25: guarda todos os selecionados (Ctrl/Shift), não só o primeiro, para a atualização a cada 3 s não desfazer a seleção
        var selected = ProcGrid.SelectedItems.OfType<ProcRow>().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sort = ProcGrid.Items.SortDescriptions.ToList();
        ProcGrid.ItemsSource = s.Groups.OrderByDescending(g => g.CpuPct).ThenByDescending(g => g.MemMB).Select(g =>
        {
            var about = ProcessKnowledge.Describe(g.Name);
            return new ProcRow(g.Name, g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, about.Description, about.SafetyText, ProcessIcons.Get(g.Name),
                               RowSeverity.Process(g.CpuPct, g.MemMB, s.RamTotalGB));
        }).ToList();
        ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
        if (ProcGrid.Items.SortDescriptions.Count == 0) // B29: a ordenação salva já pode ter voltado sozinha
            foreach (var d in sort) ProcGrid.Items.SortDescriptions.Add(d);
        foreach (var row in ProcGrid.Items.OfType<ProcRow>().Where(r => selected.Contains(r.Name))) ProcGrid.SelectedItems.Add(row);
    }

    private void KillSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = ProcGrid.SelectedItems.OfType<ProcRow>().ToList();
        if (rows.Count == 0) { ProcMsg.Text = "Selecione um programa na lista (Ctrl ou Shift para vários)."; return; }
        if (rows.Count == 1) { KillProgram(rows[0].Name, rows[0].Count, rows[0].MemMB); return; }
        KillPrograms(rows);
    }

    /// <summary>M25: encerra vários programas com uma só confirmação. Os protegidos ficam de fora e são listados.</summary>
    private void KillPrograms(List<ProcRow> rows)
    {
        var allowed = rows.Where(r => !ProcessTools.IsProtected(r.Name)).ToList();
        var skipped = rows.Except(allowed).Select(r => r.Name).ToList();
        string skippedText = skipped.Count > 0 ? $"\n\nFicam de fora (protegidos): {string.Join(", ", skipped)}." : "";
        if (allowed.Count == 0) { ProcMsg.Text = "Todos os selecionados são protegidos: nada a encerrar."; return; }
        long mem = allowed.Sum(r => r.MemMB);
        string list = string.Join("\n", allowed.Select(r => $"• {r.Name} ({Pt.N(r.Count, "processo", "processos")}, {r.MemMB:N0} MB) — {r.SafetyText}"));
        if (Simulate($"Encerrar {Pt.N(allowed.Count, "programa", "programas")}", $"Fecharia {string.Join(", ", allowed.Select(r => r.Name))} ({mem:N0} MB).")) return;
        if (!Dialogs.Confirm(this, $"Encerrar {Pt.N(allowed.Count, "programa", "programas")}?",
                $"{list}{skippedText}\n\nTrabalho não salvo nesses programas será perdido.", "Encerrar todos", danger: true)) return;

        int done = 0; long freed = 0;
        foreach (var r in allowed)
        {
            int n = ProcessActions.Kill(r.Name);
            if (n == 0) continue;
            done++; freed += r.MemMB;
            ActionLog.Add("Programa encerrado", $"{r.Name} ({Pt.N(n, "processo", "processos")}, {r.MemMB:N0} MB)");
        }
        ProcMsg.Text = done == allowed.Count ? $"{Pt.N(done, "programa encerrado", "programas encerrados")}."
                     : $"{done} de {allowed.Count} encerrados; os outros já tinham fechado ou falta permissão.";
        if (done > 0) ShowSnack(ProcMsg.Text, $"{freed:N0} MB de memória liberados.");
        RefreshHeavyBanner();
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

    // ---------- M26: menu de clique direito ----------
    private void ProcOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessIcons.ExePath(r.Name) is not { } path) { ProcMsg.Text = $"O Windows não informa onde está {r.Name} (processo do sistema ou de outro usuário)."; return; }
        // Argumento entre aspas: o Explorador só seleciona o arquivo, não o executa
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    private void ProcSearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        AppInfo.OpenShell("https://www.bing.com/search?q=" + Uri.EscapeDataString($"{r.Name}.exe o que é processo"));
    }
}
