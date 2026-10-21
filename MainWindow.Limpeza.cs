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
        // B12: mostra em que etapa está, para limpezas longas não parecerem travadas
        var progress = new Progress<(int Step, int Steps, string Label)>(p => CleanMsg.Text = $"Limpando {p.Step} de {p.Steps}: {p.Label}…");
        long freed = await Task.Run(() => Cleaner.Clean(items, days, progress));
        var (title, text, celebrate) = CleanOutcome.Describe(freed);
        CleanMsg.Text = $"{title} {text}";
        if (celebrate) ShowSnack(title, text, seconds: 12); // B14
        PlayDoneSound();
        ActionLog.Add("Limpeza (seção Limpeza)", $"{Cleaner.FormatBytes(freed)} liberados: {string.Join(", ", chosen.Select(i => i.Label.Split('(')[0].Trim()))}");
        _ = Host.RefreshHealthAsync();
        Scan_Click(sender, e);
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

    private IEnumerable<CleanItem> CleanItems() => (CleanGrid.ItemsSource as IEnumerable<CleanItem>) ?? Enumerable.Empty<CleanItem>();
}
