using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class MainWindow
{
    // ---------- Suporte, relatório e otimização ----------
    private void Support_Click(object sender, RoutedEventArgs e) => AppInfo.OpenSupport();
    private void OpenData_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell(AppPaths.Dir);

    private async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Settings.SimulationMode)
        {
            long would = await Task.Run(() => Cleaner.SafeJunkBytes(Host.Settings.TempDays));
            Simulate("Otimizar agora", $"Apagaria cerca de {Cleaner.FormatBytes(would)} de temporários com mais de {Host.Settings.TempDays} dias" + (AppInfo.IsAdmin ? " e otimizaria o SSD (TRIM)." : "."));
            return;
        }
        OptimizeBtn.IsEnabled = false;
        var before = Host.Latest;
        int scoreBefore = Host.Score.Score;
        long freed = await Host.QuickCleanAsync(notify: false);
        await Task.Delay(4000); // espera uma amostra nova para comparar (A48: antes × depois)
        var after = Host.Latest;
        OptimizeBtn.IsEnabled = true;

        string compare = before is null || after is null ? "" :
            $"Antes → depois: espaço livre {before.DiskFreeGB:0.0} → {after.DiskFreeGB:0.0} GB · memória {before.RamPct:0}% → {after.RamPct:0}% · nota {scoreBefore} → {Host.Score.Score}. ";
        string admin = AppInfo.IsAdmin ? "O SSD também foi otimizado (TRIM)." : "Pastas do Windows e otimização do SSD exigem o modo administrador.";
        // Resultado no próprio app, sem diálogo modal; o detalhe fica no registro de Ações
        ShowSnack($"Otimização concluída: {Cleaner.FormatBytes(freed)} liberados", compare + admin,
                  "Mais opções", () => Tabs.SelectedIndex = TabClean, seconds: 15);
    }

    // ---------- Limpeza ----------
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        CleanMsg.Text = "Analisando…";
        int days = Host.Settings.TempDays;
        var items = new System.Collections.ObjectModel.ObservableCollection<CleanItem>();
        CleanGrid.ItemsSource = items;
        void Header() => CleanHeader.Text = $"{Cleaner.FormatBytes(items.Where(i => i.Selected).Sum(i => i.Bytes))} marcados para limpeza · {Cleaner.FormatBytes(items.Sum(i => i.Bytes))} no total (temporários com mais de {days} dias; caches inteiros).";
        // Cada local aparece assim que é medido; caches grandes (npm, NuGet) chegam por último
        // O total "marcados para limpeza" acompanha cada caixa marcada ou desmarcada
        await Cleaner.ScanProgressiveAsync(days, item => Dispatcher.BeginInvoke(() => { item.PropertyChanged += (_, _) => Header(); items.Add(item); Header(); }));
        await Dispatcher.InvokeAsync(() => { }); // garante que os últimos itens já entraram na lista
        Header();
        CleanMsg.Text = "";
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        CleanGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (CleanGrid.ItemsSource is not IEnumerable<CleanItem> source || !source.Any(i => i.Selected)) { CleanMsg.Text = "Nada selecionado."; return; }
        var items = source.ToList();
        var chosen = items.Where(i => i.Selected).ToList();
        string list = string.Join("\n", chosen.Select(i => $"• {i.Label} ({i.SizeText})"));
        string extra = chosen.Any(i => i.IsRecycleBin) ? "\n\nA Lixeira será esvaziada DEFINITIVAMENTE." : "";
        if (Simulate("Limpar os itens marcados", $"Apagaria: {string.Join("; ", chosen.Select(i => $"{i.Label} ({i.SizeText})"))}.")) return;
        if (!Dialogs.Confirm(this, "Confirmar limpeza", $"Apagar os itens abaixo?\n\n{list}{extra}", "Apagar", danger: chosen.Any(i => i.IsRecycleBin))) return;

        CleanMsg.Text = "Limpando…";
        int days = Host.Settings.TempDays;
        long freed = await Task.Run(() => Cleaner.Clean(items, days));
        CleanMsg.Text = $"{Cleaner.FormatBytes(freed)} liberados. Arquivos em uso foram mantidos.";
        ActionLog.Add("Limpeza (seção Limpeza)", $"{Cleaner.FormatBytes(freed)} liberados: {string.Join(", ", chosen.Select(i => i.Label.Split('(')[0].Trim()))}");
        _ = Host.RefreshHealthAsync();
        Scan_Click(sender, e);
    }

    // ---------- Inicialização ----------
    private void RefreshStartup()
    {
        _startup = StartupManager.List();
        StartupGrid.ItemsSource = _startup;
        ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
        StartupMsg.Text = $"{_startup.Count(x => x.Enabled)} ativos de {_startup.Count}";
    }

    private void RefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = "Selecione um item."; return; }
        try
        {
            bool before = entry.Enabled;
            if (Simulate($"{(before ? "Desativar" : "Ativar")} {entry.Name} na inicialização", $"Mudaria o registro StartupApproved de {entry.Name}. O programa não seria apagado.")) return;
            string restore = await RestorePoint.EnsureAsync(Host.Settings, "Programas de inicializacao");
            StartupManager.SetEnabled(entry, !before);
            StartupGrid.Items.Refresh();
            StartupMsg.Text = $"{entry.Name}: {entry.State.ToLower()}. Vale a partir do próximo login (reversível aqui).";
            ActionLog.Add($"Inicialização: {entry.Name} {(before ? "desativado" : "ativado")}", $"Ponto de restauração: {restore}", "startup", entry.Name, $"{StartupManager.SubKeyOf(entry)}|{before}");
            _ = Host.RefreshHealthAsync();
        }
        catch (Exception ex) { StartupMsg.Text = "Falhou: " + ex.Message; }
    }

    // ---------- Ações (registro + desfazer) ----------
    private void LoadActions() => ActionsGrid.ItemsSource = ActionLog.Load().OrderByDescending(a => a.Time).ToList();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionEntry entry) { ActionsMsg.Text = "Selecione uma ação na lista."; return; }
        if (!entry.CanUndo) { ActionsMsg.Text = entry.Undone ? "Essa ação já foi desfeita." : "Essa ação é definitiva e não pode ser desfeita."; return; }
        if (Simulate($"Desfazer: {entry.Title}", entry.Detail)) return;
        ActionsMsg.Text = ActionLog.Undo(entry);
        ActionLog.Add($"Desfeito: {entry.Title}", entry.Detail);
        RefreshStartup();
        LoadActions();
        _ = Host.RefreshHealthAsync();
    }
}
