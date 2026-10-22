using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// B29/B31: lembra a ordenação e as larguras de coluna de cada tabela. A ordenação volta sempre que a tabela
/// recebe dados novos (Processos atualiza a cada 3 s); as larguras são salvas ao fechar a janela.
/// B30: clique direito no título de uma coluna abre a lista de colunas para mostrar ou esconder.
/// </summary>
public static class GridMemory
{
    // Largura de cada coluna no XAML: só guarda as que a pessoa mudou, para uma versão nova poder mudar o padrão
    private static readonly Dictionary<DataGridColumn, double> Defaults = new();
    // Quantas colunas do começo de cada tabela nunca se escondem (identificam a linha)
    private static readonly Dictionary<DataGrid, int> Fixed = new();

    public static void Attach(DataGrid grid, AppSettings settings, int fixedColumns = 1)
    {
        string key = grid.Name;
        var layout = settings.Grids.TryGetValue(key, out var saved) ? saved : null;
        Fixed[grid] = fixedColumns;
        ApplyHidden(grid, settings);
        // Clique direito no título: a lista de colunas. Nas linhas continua o menu da própria tabela (Processos).
        grid.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (FindHeader(e.OriginalSource as DependencyObject) is null) return;
            e.Handled = true;
            ShowColumnMenu(grid, settings);
        };
        grid.ContextMenuOpening += (_, e) =>
        {
            if (FindHeader(e.OriginalSource as DependencyObject) is not null) e.Handled = true;
        };

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
                .Where(c => c.Header is string && c.Visibility == Visibility.Visible && c.Width.IsAbsolute && GridLayout.IsSaneWidth(c.ActualWidth)
                            && !(Defaults.TryGetValue(c, out double d) && Math.Abs(d - c.ActualWidth) < 1))
                .ToDictionary(c => (string)c.Header, c => Math.Round(c.ActualWidth));
            // B30: coluna escondida tem largura 0 na tela; fica a largura que já estava guardada
            if (settings.Grids.TryGetValue(grid.Name, out var old))
                foreach (var col in grid.Columns)
                    if (col.Visibility != Visibility.Visible && col.Header is string h && old.Widths.TryGetValue(h, out double w))
                        widths[h] = w;
            if (widths.Count > 0 || settings.Grids.ContainsKey(grid.Name)) Get(settings, grid.Name).Widths = widths;
        }
    }

    // ---------- B30: colunas visíveis ----------
    private static List<string> Headers(DataGrid grid) => grid.Columns.Select(c => c.Header).OfType<string>().ToList();

    private static void ApplyHidden(DataGrid grid, AppSettings settings)
    {
        var hidden = settings.Grids.TryGetValue(grid.Name, out var l)
            ? ColumnChoice.Sanitize(Headers(grid), l.Hidden, Fixed.GetValueOrDefault(grid, 1)) : new List<string>();
        foreach (var col in grid.Columns)
            col.Visibility = col.Header is string h && hidden.Contains(h) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void ShowColumnMenu(DataGrid grid, AppSettings settings)
    {
        var headers = Headers(grid);
        int fixedCount = Fixed.GetValueOrDefault(grid, 1);
        var hidden = ColumnChoice.Sanitize(headers, Get(settings, grid.Name).Hidden, fixedCount);
        var menu = new ContextMenu { PlacementTarget = grid, Placement = PlacementMode.MousePoint };
        menu.Items.Add(new MenuItem { Header = "Colunas visíveis", IsEnabled = false });
        foreach (string h in headers)
        {
            bool visible = !hidden.Contains(h);
            var item = new MenuItem
            {
                Header = h.Replace("_", "__"), // "_" no título não vira tecla de acesso
                IsCheckable = true,
                IsChecked = visible,
                // Coluna fixa ou a última visível: aparece marcada, mas não dá para desmarcar
                IsEnabled = !visible || ColumnChoice.CanHide(headers, hidden, h, fixedCount),
            };
            item.Click += (_, _) =>
            {
                Get(settings, grid.Name).Hidden = ColumnChoice.Toggle(headers, Get(settings, grid.Name).Hidden, h, fixedCount);
                settings.Save();
                ApplyHidden(grid, settings);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "Mostrar _todas", IsEnabled = hidden.Count > 0 };
        all.Click += (_, _) => { Get(settings, grid.Name).Hidden = new(); settings.Save(); ApplyHidden(grid, settings); };
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    /// <summary>B30: "Mostrar todas as colunas das tabelas", em Configurações.</summary>
    public static void ShowAll(IEnumerable<DataGrid> grids, AppSettings settings)
    {
        foreach (var grid in grids)
        {
            if (settings.Grids.TryGetValue(grid.Name, out var l)) l.Hidden = new();
            ApplyHidden(grid, settings);
        }
        settings.Save();
    }

    private static DataGridColumnHeader? FindHeader(DependencyObject? d)
    {
        while (d is not null and not DataGrid)
        {
            if (d is DataGridColumnHeader h) return h;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private static GridLayout Get(AppSettings s, string key)
    {
        if (!s.Grids.TryGetValue(key, out var l)) s.Grids[key] = l = new GridLayout();
        return l;
    }
}
