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
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name, r.Names);
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
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name, r.Names);
        else ProcMsg.Text = "Selecione um programa na lista.";
    }

    // ---------- Processos ----------
    private void RefreshProcesses(Snapshot s)
    {
        // M25: guarda todos os selecionados (Ctrl/Shift), não só o primeiro, para a atualização a cada 3 s não desfazer a seleção
        var selected = ProcGrid.SelectedItems.OfType<ProcRow>().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sort = ProcGrid.Items.SortDescriptions.ToList();
        // M22: processos do mesmo aplicativo numa linha só (opção no topo da aba)
        var groups = Host.Settings.GroupByApp ? AppGroups.Merge(s.Groups) : AppGroups.Single(s.Groups);
        ProcGrid.ItemsSource = groups.OrderByDescending(g => g.CpuPct).ThenByDescending(g => g.MemMB).Select(g =>
        {
            var about = ProcessKnowledge.Describe(g.Main);
            string desc = g.Merged ? $"{about.Description} · {string.Join(", ", g.Members)}" : about.Description;
            return new ProcRow(g.Name, g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, desc, about.SafetyText, ProcessIcons.Get(g.Main),
                               RowSeverity.Process(g.CpuPct, g.MemMB, s.RamTotalGB), g.Merged ? g.Members : null);
        }).ToList();
        ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
        if (ProcGrid.Items.SortDescriptions.Count == 0) // B29: a ordenação salva já pode ter voltado sozinha
            foreach (var d in sort) ProcGrid.Items.SortDescriptions.Add(d);
        foreach (var row in ProcGrid.Items.OfType<ProcRow>().Where(r => selected.Contains(r.Name))) ProcGrid.SelectedItems.Add(row);
    }

    // M22
    private void GroupByApp_Click(object sender, RoutedEventArgs e)
    {
        Host.Settings.GroupByApp = GroupByAppBox.IsChecked == true;
        Host.Settings.Save();
        ProcGrid.SelectedItems.Clear(); // a mesma linha pode ter mudado de nome
        if (Host.Latest is { } s) RefreshProcesses(s);
    }

    private void KillSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = ProcGrid.SelectedItems.OfType<ProcRow>().ToList();
        if (rows.Count == 0) { ProcMsg.Text = "Selecione um programa na lista (Ctrl ou Shift para vários)."; return; }
        if (rows.Count == 1 && !rows[0].Merged) { KillProgram(rows[0].Name, rows[0].Count, rows[0].MemMB); return; }
        KillPrograms(rows);
    }

    /// <summary>M25: encerra vários programas com uma só confirmação. Os protegidos ficam de fora e são listados.</summary>
    private void KillPrograms(List<ProcRow> rows)
    {
        var allowed = rows.Where(r => !r.IsProtected).ToList();
        var skipped = rows.Except(allowed).Select(r => r.Name).ToList();
        string skippedText = skipped.Count > 0 ? $"\n\nFicam de fora (protegidos): {string.Join(", ", skipped)}." : "";
        if (allowed.Count == 0) { ProcMsg.Text = "Todos os selecionados são protegidos: nada a encerrar."; return; }
        long mem = allowed.Sum(r => r.MemMB);
        string list = string.Join("\n", allowed.Select(r => $"• {r.Name} ({Pt.N(r.Count, "processo", "processos")}, {r.MemMB:N0} MB){(r.Merged ? $": {string.Join(", ", r.Names)}" : "")} — {r.SafetyText}"));
        if (Simulate($"Encerrar {Pt.N(allowed.Count, "programa", "programas")}", $"Fecharia {string.Join(", ", allowed.Select(r => r.Name))} ({mem:N0} MB).")) return;
        if (!Dialogs.Confirm(this, $"Encerrar {Pt.N(allowed.Count, "programa", "programas")}?",
                $"{list}{skippedText}\n\nTrabalho não salvo nesses programas será perdido.", "Encerrar todos", danger: true)) return;

        int done = 0; long freed = 0;
        foreach (var r in allowed)
        {
            int n = r.Names.Sum(ProcessActions.Kill);
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
        if (g.IsProtected) { ProcMsg.Text = $"{g.Name} é do sistema: a prioridade não é alterada."; return; }
        if (Simulate(lower ? $"Baixar a prioridade de {g.Name}" : $"Restaurar a prioridade de {g.Name}",
                     $"Mudaria {Pt.N(g.Count, "processo", "processos")} para prioridade {(lower ? "abaixo do normal" : "normal")}.")) return;
        var priority = lower ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
        var done = g.Names.Select(name => (Name: name, N: ProcessActions.SetPriority(name, priority))).ToList();
        int n = done.Sum(d => d.N);
        ProcMsg.Text = n > 0 ? $"{g.Name}: prioridade {(lower ? "reduzida" : "normal")} em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível alterar: falta permissão.";
        // O desfazer volta a prioridade de cada processo do grupo
        if (lower) foreach (var d in done.Where(d => d.N > 0)) ActionLog.Add($"Prioridade reduzida: {d.Name}", Pt.N(d.N, "processo", "processos"), "priority", d.Name);
    }

    // ---------- Exceções de avisos (M20) a partir da tabela de processos ----------
    private void AlertException_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        var s = Host.Settings;
        // Os avisos são por processo: num aplicativo agrupado, todos os processos dele entram
        var names = g.Names.Where(n => !s.IsAlertException(n)).ToList();
        if (names.Count == 0) { ProcMsg.Text = $"{g.Name} já está nas exceções."; return; }
        s.AlertExceptions = AppSettings.ParseNames(string.Join("\n", s.AlertExceptions.Concat(names)));
        s.Save();
        LoadSettingsIntoForm();
        if (Host.HeavyAlert is { } heavy && names.Contains(heavy.Name, StringComparer.OrdinalIgnoreCase)) Host.IgnoreHeavy(heavy.Name);
        ActionLog.Add($"Sem avisos: {g.Name}", $"{string.Join(", ", names)}: adicionado às exceções de avisos (Configurações › Exceções).");
        ShowSnack($"{g.Name} não gera mais avisos", "Ele continua sendo medido e aparece normalmente nas tabelas.",
                  "Desfazer", () =>
                  {
                      s.AlertExceptions.RemoveAll(n => names.Contains(n, StringComparer.OrdinalIgnoreCase));
                      s.Save();
                      LoadSettingsIntoForm();
                  });
    }

    // ---------- A19: modo eficiência ----------
    private void Efficiency_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (g.IsProtected) { ProcMsg.Text = $"{g.Name} é protegido: o modo eficiência não é aplicado."; return; }
        if (Simulate($"Modo eficiência em {g.Name}", $"Ligaria o modo econômico do Windows (EcoQoS) e a prioridade baixa em {Pt.N(g.Count, "processo", "processos")}. O programa continuaria aberto.")) return;
        var done = g.Names.Select(name => (Name: name, N: ProcessActions.SetEfficiency(name, on: true))).ToList();
        int n = done.Sum(d => d.N);
        ProcMsg.Text = n > 0 ? $"{g.Name}: modo eficiência em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível aplicar: falta permissão.";
        foreach (var d in done.Where(d => d.N > 0)) ActionLog.Add($"Modo eficiência: {d.Name}", Pt.N(d.N, "processo", "processos"), "efficiency", d.Name);
    }

    // ---------- M26: menu de clique direito ----------
    private void ProcOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessIcons.ExePath(r.Names[0]) is not { } path) { ProcMsg.Text = $"O Windows não informa onde está {r.Name} (processo do sistema ou de outro usuário)."; return; }
        // Argumento entre aspas: o Explorador só seleciona o arquivo, não o executa
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    private void ProcSearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        AppInfo.OpenShell("https://www.bing.com/search?q=" + Uri.EscapeDataString($"{r.Name}.exe o que é processo"));
    }
}
