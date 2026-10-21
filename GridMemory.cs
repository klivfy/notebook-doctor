using System.ComponentModel;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// B29/B31: lembra a ordenação e as larguras de coluna de cada tabela. A ordenação volta sempre que a tabela
/// recebe dados novos (Processos atualiza a cada 3 s); as larguras são salvas ao fechar a janela.
/// </summary>
public static class GridMemory
{
    // Largura de cada coluna no XAML: só guarda as que a pessoa mudou, para uma versão nova poder mudar o padrão
    private static readonly Dictionary<DataGridColumn, double> Defaults = new();

    public static void Attach(DataGrid grid, AppSettings settings)
    {
        string key = grid.Name;
        var layout = settings.Grids.TryGetValue(key, out var saved) ? saved : null;

        foreach (var col in grid.Columns)
        {
            if (col.Width.IsAbsolute) Defaults[col] = col.Width.Value;
            if (layout is not null && col.Header is string h && layout.Widths.TryGetValue(h, out double w) && GridLayout.IsSaneWidth(w))
                col.Width = new DataGridLength(w);
        }

        // O DataGrid só aplica a ordenação depois do evento: lê o resultado na próxima volta do Dispatcher
        grid.Sorting += (_, _) => grid.Dispatcher.BeginInvoke(() =>
        {
            var l = Get(settings, key);
            var d = grid.Items.SortDescriptions.FirstOrDefault();
            l.Sort = string.IsNullOrEmpty(d.PropertyName) ? null : d.PropertyName;
            l.Desc = d.Direction == ListSortDirection.Descending;
            settings.Save();
        });

        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
            .AddValueChanged(grid, (_, _) => RestoreSort(grid, settings));
    }

    private static void RestoreSort(DataGrid grid, AppSettings settings)
    {
        if (grid.ItemsSource is null || grid.Items.SortDescriptions.Count > 0) return;
        if (!settings.Grids.TryGetValue(grid.Name, out var l) || l.Sort is null) return;
        var col = grid.Columns.FirstOrDefault(c => c.SortMemberPath == l.Sort);
        if (col is null) return; // coluna removida numa versão nova: fica a ordem padrão
        var dir = l.Desc ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var c in grid.Columns) c.SortDirection = null;
        col.SortDirection = dir;
        grid.Items.SortDescriptions.Add(new SortDescription(l.Sort, dir));
    }

    /// <summary>Guarda as colunas com largura em pixels (as que a pessoa arrastou); as proporcionais (*) ficam como estão.</summary>
    public static void SaveWidths(IEnumerable<DataGrid> grids, AppSettings settings)
    {
        foreach (var grid in grids)
        {
            var widths = grid.Columns
                .Where(c => c.Header is string && c.Width.IsAbsolute && GridLayout.IsSaneWidth(c.ActualWidth)
                            && !(Defaults.TryGetValue(c, out double d) && Math.Abs(d - c.ActualWidth) < 1))
                .ToDictionary(c => (string)c.Header, c => Math.Round(c.ActualWidth));
            if (widths.Count > 0 || settings.Grids.ContainsKey(grid.Name)) Get(settings, grid.Name).Widths = widths;
        }
    }

    private static GridLayout Get(AppSettings s, string key)
    {
        if (!s.Grids.TryGetValue(key, out var l)) s.Grids[key] = l = new GridLayout();
        return l;
    }
}
