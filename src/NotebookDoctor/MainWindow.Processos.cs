using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Processos: lista, detalhe, encerrar, prioridade, modo eficiência e exceções de aviso.</summary>
public partial class MainWindow
{
    private void ProcSearch_Changed(object sender, TextChangedEventArgs e) => ApplyProcFilter();

    // Leva 10.2: busca e filtro rápido juntos
    private void ApplyProcFilter()
    {
        if (ProcGrid.ItemsSource is null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ProcGrid.ItemsSource);
        string q = ProcSearch.Text.Trim();
        var filter = Host.Settings.ProcessFilter;
        view.Filter = q.Length == 0 && filter == ProcessFilter.All ? null : o => o is ProcRow r
            && (q.Length == 0 || (r.Name + " " + r.About).Contains(q, StringComparison.CurrentCultureIgnoreCase))
            && ProcessView.Matches(filter, r.CpuPct, r.Hung, r.HasWindow, r.Safety, r.IsProtected);
    }

    private void ProcFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<ProcessFilter>(tag, out var f) || !IsLoaded) return;
        Host.Settings.ProcessFilter = f;
        Host.Settings.Save();
        ApplyProcFilter();
        UpdateTotals(ProcGrid);
    }

    /// <summary>Marca a pílula salva ao abrir a janela.</summary>
    private void LoadProcFilter()
    {
        foreach (var rb in ProcFilters.Children.OfType<RadioButton>())
            if (rb.Tag is string tag && tag == Host.Settings.ProcessFilter.ToString()) rb.IsChecked = true;
    }

    private void ProcMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool pinned = ProcGrid.SelectedItem is ProcRow { Pinned: true };
        ProcPinItem.Header = Loc.T(pinned ? "Desa_fixar do topo" : "Fi_xar no topo");
    }

    private void ProcPin_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        var list = ProcessView.TogglePin(Host.Settings.PinnedProcesses, r.Name);
        if (list is null) { ProcMsg.Text = Loc.F("Já há {0} programas fixados: desafixe um antes.", ProcessView.MaxPinned); return; }
        Host.Settings.PinnedProcesses = list;
        Host.Settings.Save();
        ProcMsg.Text = r.Pinned ? Loc.F("{0} não fica mais no topo.", r.Name) : Loc.F("{0} fica no topo da lista.", r.Name);
        if (Host.Latest is { } s) RefreshProcesses(s, force: true);
    }

    // Leva 10.2: socorro rápido
    private void ProcRestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Simulate("Reiniciar o Explorador de Arquivos", "Fecharia e abriria de novo o Explorador (barra de tarefas e janelas de pasta).")) return;
        if (!Dialogs.Confirm(this, Loc.T("Reiniciar o Explorador de Arquivos?"),
                Loc.T("A barra de tarefas e as janelas de pasta somem por alguns segundos e voltam sozinhas. Cópias de arquivos em andamento no Explorador são interrompidas."),
                "Reiniciar")) return;
        _ = Task.Run(FileUnlocker.RestartExplorer).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            ProcMsg.Text = t.Result ? Loc.T("Explorador reiniciado.") : Loc.T("O Explorador não voltou sozinho: aperte Ctrl+Shift+Esc e use Arquivo › Executar nova tarefa › explorer.");
            if (t.Result) ActionLog.Add("Explorador de Arquivos reiniciado");
        }), TaskScheduler.Default);
    }

    private void ProcRestartVideo_Click(object sender, RoutedEventArgs e)
    {
        if (Simulate("Reiniciar o driver de vídeo", "Mandaria Windows+Ctrl+Shift+B: a tela piscaria e voltaria.")) return;
        if (!Dialogs.Confirm(this, Loc.T("Reiniciar o driver de vídeo?"),
                Loc.T("A tela fica preta por um ou dois segundos e o Windows toca um aviso. Nenhum programa é fechado. Use quando a imagem congelar ou ficar com defeito."),
                "Reiniciar")) return;
        GraphicsDriver.Restart();
        ProcMsg.Text = Loc.T("Pedido enviado ao Windows: a tela deve piscar e voltar.");
        ActionLog.Add("Driver de vídeo reiniciado", "Windows+Ctrl+Shift+B");
    }

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
        else ProcMsg.Text = Loc.T("Selecione um programa na lista.");
    }

    // ---------- Processos ----------
    private void RefreshProcesses(Snapshot s, bool force = false)
    {
        // Leva 10.2: com o menu de clique direito aberto ou Espaço segurado na tabela, a ordem não muda embaixo do ponteiro
        if (!force && ProcessView.HoldRefresh(ProcGrid.ContextMenu?.IsOpen == true,
                ProcGrid.IsKeyboardFocusWithin && System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Space))) return;
        // M25: guarda todos os selecionados (Ctrl/Shift), não só o primeiro, para a atualização a cada 3 s não desfazer a seleção
        var selected = ProcGrid.SelectedItems.OfType<ProcRow>().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sort = ProcGrid.Items.SortDescriptions.ToList();
        // M22: processos do mesmo aplicativo numa linha só (opção no topo da aba)
        var groups = Host.Settings.GroupByApp ? AppGroups.Merge(s.Groups) : AppGroups.Single(s.Groups);
        var pinned = Host.Settings.PinnedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProcGrid.ItemsSource = groups
            .OrderBy(g => ProcessView.Rank(pinned.Contains(g.Name), g.Hung)).ThenByDescending(g => g.CpuPct).ThenByDescending(g => g.MemMB).Select(g =>
        {
            var about = ProcessKnowledge.Describe(g.Main);
            string desc = g.Merged ? $"{about.Description} · {string.Join(", ", g.Members)}" : about.Description;
            // Leva 10.2: numa linha juntada com uso, quem dentro dela pesa mais
            if (g.Heaviest.Length > 0 && g.CpuPct >= 1) desc = Loc.F("Mais pesado: {0}", g.Heaviest) + " · " + desc;
            return new ProcRow(g.Name, g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, desc, about.SafetyText, ProcessIcons.Get(g.Main),
                               RowSeverity.Process(g.CpuPct, g.MemMB, s.RamTotalGB), g.Merged ? g.Members : null,
                               g.Hung, g.HasWindow, pinned.Contains(g.Name), about.Safety);
        }).ToList();
        ApplyProcFilter();
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
        if (rows.Count == 0) { ProcMsg.Text = Loc.T("Selecione um programa na lista (Ctrl ou Shift para vários)."); return; }
        if (rows.Count == 1 && !rows[0].Merged) { KillProgram(rows[0].Name, rows[0].Count, rows[0].MemMB); return; }
        KillPrograms(rows);
    }

    /// <summary>M25: encerra vários programas com uma só confirmação. Os protegidos ficam de fora e são listados.</summary>
    private void KillPrograms(List<ProcRow> rows)
    {
        var allowed = rows.Where(r => !r.IsProtected).ToList();
        var skipped = rows.Except(allowed).Select(r => r.Name).ToList();
        string skippedText = skipped.Count > 0 ? "\n\n" + Loc.F("Ficam de fora (protegidos): {0}.", string.Join(", ", skipped)) : "";
        if (allowed.Count == 0) { ProcMsg.Text = Loc.T("Todos os selecionados são protegidos: nada a encerrar."); return; }
        long mem = allowed.Sum(r => r.MemMB);
        string list = string.Join("\n", allowed.Select(r => $"• {r.Name} ({Pt.N(r.Count, "processo", "processos")}, {r.MemMB:N0} MB){(r.Merged ? $": {string.Join(", ", r.Names)}" : "")} — {r.SafetyText}"));
        if (Simulate(Loc.F("Encerrar {0}", Pt.N(allowed.Count, "programa", "programas")), Loc.F("Fecharia {0} ({1:N0} MB).", string.Join(", ", allowed.Select(r => r.Name)), mem))) return;
        if (!Dialogs.Confirm(this, Loc.F("Encerrar {0}?", Pt.N(allowed.Count, "programa", "programas")),
                $"{list}{skippedText}\n\n" + Loc.T("Trabalho não salvo nesses programas será perdido."), "Encerrar todos", danger: true)) return;

        int done = 0; long freed = 0;
        foreach (var r in allowed)
        {
            int n = r.Names.Sum(ProcessActions.Kill);
            if (n == 0) continue;
            done++; freed += r.MemMB;
            ActionLog.Add("Programa encerrado", $"{r.Name} ({Pt.N(n, "processo", "processos")}, {r.MemMB:N0} MB)");
        }
        ProcMsg.Text = done == allowed.Count ? $"{Pt.N(done, "programa encerrado", "programas encerrados")}."
                     : Loc.F("{0} de {1} encerrados; os outros já tinham fechado ou falta permissão.", done, allowed.Count);
        if (done > 0) ShowSnack(ProcMsg.Text, Loc.F("{0:N0} MB de memória liberados.", freed));
        RefreshHeavyBanner();
    }

    // ---------- Prioridade manual (A18) ----------
    private void LowerPriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: true);
    private void RestorePriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: false);

    private void SetPriority(bool lower)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (g.IsProtected) { ProcMsg.Text = Loc.F("{0} é do sistema: a prioridade não é alterada.", g.Name); return; }
        if (Simulate(lower ? Loc.F("Baixar a prioridade de {0}", g.Name) : Loc.F("Restaurar a prioridade de {0}", g.Name),
                     Loc.F("Mudaria {0} para prioridade {1}.", Pt.N(g.Count, "processo", "processos"), Loc.T(lower ? "abaixo do normal" : "normal")))) return;
        var priority = lower ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
        var done = g.Names.Select(name => (Name: name, N: ProcessActions.SetPriority(name, priority))).ToList();
        int n = done.Sum(d => d.N);
        ProcMsg.Text = n > 0 ? Loc.F("{0}: prioridade {1} em {2}. Vale até o programa ser reaberto.", g.Name, Loc.T(lower ? "reduzida" : "normal"), Pt.N(n, "processo", "processos")) : Loc.T("Não foi possível alterar: falta permissão.");
        // O desfazer volta a prioridade de cada processo do grupo
        if (lower) foreach (var d in done.Where(d => d.N > 0)) ActionLog.Add(Loc.F("Prioridade reduzida: {0}", d.Name), Pt.N(d.N, "processo", "processos"), "priority", d.Name);
    }

    // ---------- Exceções de avisos (M20) a partir da tabela de processos ----------
    private void AlertException_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        var s = Host.Settings;
        // Os avisos são por processo: num aplicativo agrupado, todos os processos dele entram
        var names = g.Names.Where(n => !s.IsAlertException(n)).ToList();
        if (names.Count == 0) { ProcMsg.Text = Loc.F("{0} já está nas exceções.", g.Name); return; }
        s.AlertExceptions = AppSettings.ParseNames(string.Join("\n", s.AlertExceptions.Concat(names)));
        s.Save();
        LoadSettingsIntoForm();
        if (Host.HeavyAlert is { } heavy && names.Contains(heavy.Name, StringComparer.OrdinalIgnoreCase)) Host.IgnoreHeavy(heavy.Name);
        ActionLog.Add(Loc.F("Sem avisos: {0}", g.Name), Loc.F("{0}: adicionado às exceções de avisos (Configurações › Exceções).", string.Join(", ", names)));
        ShowSnack(Loc.F("{0} não gera mais avisos", g.Name), "Ele continua sendo medido e aparece normalmente nas tabelas.",
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
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (g.IsProtected) { ProcMsg.Text = Loc.F("{0} é protegido: o modo eficiência não é aplicado.", g.Name); return; }
        if (Simulate(Loc.F("Modo eficiência em {0}", g.Name), Loc.F("Ligaria o modo econômico do Windows (EcoQoS) e a prioridade baixa em {0}. O programa continuaria aberto.", Pt.N(g.Count, "processo", "processos")))) return;
        var done = g.Names.Select(name => (Name: name, N: ProcessActions.SetEfficiency(name, on: true))).ToList();
        int n = done.Sum(d => d.N);
        ProcMsg.Text = n > 0 ? Loc.F("{0}: modo eficiência em {1}. Vale até o programa ser reaberto.", g.Name, Pt.N(n, "processo", "processos")) : Loc.T("Não foi possível aplicar: falta permissão.");
        foreach (var d in done.Where(d => d.N > 0)) ActionLog.Add(Loc.F("Modo eficiência: {0}", d.Name), Pt.N(d.N, "processo", "processos"), "efficiency", d.Name);
    }

    // ---------- M26: menu de clique direito ----------
    private void ProcOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (ProcessIcons.ExePath(r.Names[0]) is not { } path) { ProcMsg.Text = Loc.F("O Windows não informa onde está {0} (processo do sistema ou de outro usuário).", r.Name); return; }
        // Argumento entre aspas: o Explorador só seleciona o arquivo, não o executa
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    /// <summary>Leva 5.3: abre a aba Programas no programa instalado dono do processo.</summary>
    private async void ProcShowProgram_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (ProcessIcons.ExePath(r.Names[0]) is not { } exe) { ProcMsg.Text = Loc.T("O Windows não deixou ver o arquivo desse processo."); return; }
        await RevealProgramForAsync(exe);
    }

    private void ProcSearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow r) { ProcMsg.Text = Loc.T("Selecione um programa na lista."); return; }
        AppInfo.OpenShell("https://www.bing.com/search?q=" + Uri.EscapeDataString($"{r.Name}.exe o que é processo"));
    }
}
