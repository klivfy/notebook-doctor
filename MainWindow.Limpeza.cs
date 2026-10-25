using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Limpeza: análise e remoção de temporários.</summary>
public partial class MainWindow
{
    // ---------- Limpeza ----------
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        CleanMsg.Text = Loc.T("Analisando…");
        int days = Host.Settings.TempDays;
        var items = new System.Collections.ObjectModel.ObservableCollection<CleanItem>();
        CleanGrid.ItemsSource = items;
        void Header() => CleanHeader.Text = Loc.F("{0} marcados para limpeza · {1} no total (temporários com mais de {2} dias; caches inteiros).", Cleaner.FormatBytes(items.Where(i => i.Selected).Sum(i => i.Bytes)), Cleaner.FormatBytes(items.Sum(i => i.Bytes)), days);
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
        if (CleanGrid.ItemsSource is not IEnumerable<CleanItem> source || !source.Any(i => i.Selected)) { CleanMsg.Text = Loc.T("Nada selecionado."); return; }
        var items = source.ToList();
        var chosen = items.Where(i => i.Selected).ToList();
        string list = string.Join("\n", chosen.Select(i => $"• {i.Label} ({i.SizeText})"));
        string extra = chosen.Any(i => i.IsRecycleBin) ? "\n\n" + Loc.T("A Lixeira será esvaziada DEFINITIVAMENTE.") : "";
        if (Simulate("Limpar os itens marcados", Loc.F("Apagaria: {0}.", string.Join("; ", chosen.Select(i => $"{i.Label} ({i.SizeText})"))))) return;
        if (!Dialogs.Confirm(this, "Confirmar limpeza", Loc.T("Apagar os itens abaixo?") + $"\n\n{list}{extra}", "Apagar", danger: chosen.Any(i => i.IsRecycleBin))) return;

        CleanMsg.Text = Loc.T("Limpando…");
        int days = Host.Settings.TempDays;
        // B12: mostra em que etapa está, para limpezas longas não parecerem travadas
        var progress = new Progress<(int Step, int Steps, string Label)>(p => CleanMsg.Text = Loc.F("Limpando {0} de {1}: {2}…", p.Step, p.Steps, p.Label));
        long freed = await Task.Run(() => Cleaner.Clean(items, days, progress));
        var (title, text, celebrate) = CleanOutcome.Describe(freed);
        CleanMsg.Text = $"{Loc.T(title)} {Loc.T(text)}";
        if (celebrate) ShowSnack(title, text, seconds: 12); // B14
        PlayDoneSound();
        ActionLog.Add("Limpeza (seção Limpeza)", $"{Cleaner.FormatBytes(freed)} liberados: {string.Join(", ", chosen.Select(i => i.Label.Split('(')[0].Trim()))}");
        _ = Host.RefreshHealthAsync();
        Scan_Click(sender, e);
    }

    // ---------- B35: prévia do que seria apagado ----------
    private async void CleanPreview_Click(object sender, RoutedEventArgs e)
    {
        if (CleanGrid.SelectedItem is not CleanItem item) { CleanMsg.Text = Loc.T("Selecione um local para ver os arquivos."); return; }
        if (item.IsRecycleBin)
        {
            // A Lixeira não é uma pasta comum: a própria tela do Windows mostra o conteúdo
            AppInfo.OpenShell("shell:RecycleBinFolder");
            return;
        }
        CleanMsg.Text = Loc.T("Listando…");
        int days = Host.Settings.TempDays;
        var files = await Task.Run(() => Cleaner.Preview(item, days));
        CleanMsg.Text = "";
        Dialogs.ShowText(this, Loc.F("O que seria apagado: {0}", item.Label), CleanPreview.Format(item.Label, item.Paths, files));
    }

    // ---------- B34: marcar e desmarcar todos ----------
    private void CleanSelectAll_Click(object sender, RoutedEventArgs e)
    {
        // A Lixeira fica de fora: esvaziar é definitivo e precisa de uma escolha explícita
        foreach (var item in CleanItems()) item.Selected = item.CanClean && !item.IsRecycleBin;
    }

    private void CleanSelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in CleanItems()) item.Selected = false;
    }

    /// <summary>M12: barra de espaço marca ou desmarca a linha com foco, sem precisar chegar na caixa com o Tab.</summary>
    private void CleanGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || CleanGrid.SelectedItem is not CleanItem { CanClean: true } item) return;
        item.Selected = !item.Selected;
        e.Handled = true;
    }

    // ---------- Fase 6: limpeza avançada ----------
    private void AdvancedClean_Click(object sender, RoutedEventArgs e) => new AdvancedCleanWindow(this).ShowDialog();

    // Leva 8.4 (D01–D03)
    private void Defrag_Click(object sender, RoutedEventArgs e) => new DefragWindow(this).ShowDialog();

    // Leva 8.3 (V01)
    private void Recovery_Click(object sender, RoutedEventArgs e) => new RecoveryWindow(this).ShowDialog();

    // Leva 8.2 (R20, R21, C11)
    private void SecureErase_Click(object sender, RoutedEventArgs e) => new SecureEraseWindow(this).ShowDialog();

    // Leva 7.4 (C04)
    private void RegistryCheck_Click(object sender, RoutedEventArgs e) => new RegistryCheckWindow(this).ShowDialog();

    private IEnumerable<CleanItem> CleanItems() => (CleanGrid.ItemsSource as IEnumerable<CleanItem>) ?? Enumerable.Empty<CleanItem>();
}
