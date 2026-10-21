using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class MainWindow
{
    // ---------- Busca nas tabelas (M24) ----------
    private static void ApplyFilter(DataGrid grid, string text, Func<object, string> key)
    {
        if (grid.ItemsSource is null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        string q = text.Trim();
        view.Filter = q.Length == 0 ? null : o => key(o).Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplySpaceFilter() => ApplyFilter(SpaceGrid, SpaceSearch.Text, o => o is FolderUsage f ? f.Name + " " + f.Path : "");
    private void ProcSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
    private void StartupSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
    private void SpaceSearch_Changed(object sender, TextChangedEventArgs e) => ApplySpaceFilter();

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
}
