using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Espaço: maiores pastas e arquivos e duplicados. Os aplicativos instalados ficam na aba Programas.</summary>
public partial class MainWindow
{
    // ---------- Espaço em disco (A27) ----------
    private CancellationTokenSource? _spaceCts;
    private readonly List<FolderUsage> _space = new();
    private List<DuplicateGroup> _dupeGroups = [];
    private string _dupeRoot = "";

    private async void SpaceScan_Click(object sender, RoutedEventArgs e)
    {
        string root = ((SpaceRoot.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "drive" => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            _ => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        _spaceCts?.Cancel();
        _spaceCts = new CancellationTokenSource();
        var ct = _spaceCts.Token;
        _space.Clear();
        SpaceGrid.ItemsSource = null;
        SpaceScanBtn.IsEnabled = false; SpaceCancelBtn.IsEnabled = true;
        string mode = (SpaceMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "folders";
        void Progress(string m) => Dispatcher.BeginInvoke(() => SpaceMsg.Text = m);
        void Show(List<FolderUsage> items)
        {
            long max = items.Count > 0 ? items.Max(x => x.Bytes) : 0;
            foreach (var x in items) x.Percent = max > 0 ? x.Bytes * 100.0 / max : 0;
            SpaceGrid.ItemsSource = items;
            ApplySpaceFilter();
        }
        try
        {
            if (mode == "large")
            {
                var items = await DiskUsage.LargestFilesAsync(root, 50L << 20, 100, Progress, ct);
                Show(items);
                SpaceMsg.Text = Loc.F("{0} maiores arquivos (50 MB ou mais) em {1}. Só leitura.", items.Count, root);
                return;
            }
            if (mode == "types")
            {
                var types = await DiskUsage.ByTypeAsync(root, Progress, ct);
                var items = types.Select(t => new FolderUsage { Name = t.Description, Path = "", Bytes = t.Bytes }).ToList();
                Show(items);
                SpaceMsg.Text = Loc.F("{0} em {1}, por tipo de arquivo. Só leitura.", Cleaner.FormatBytes(types.Sum(t => t.Bytes)), root);
                return;
            }
            if (mode == "dupes")
            {
                long min = long.TryParse((SpaceMin.SelectedItem as ComboBoxItem)?.Tag as string, out long m) ? m : 10L << 20;
                var places = WindowsPlaces.Current(AppPaths.Dir);
                var groups = await DiskUsage.DuplicatesAsync(root, min, 60, places, Progress, ct);
                _dupeGroups = groups;
                _dupeRoot = root;
                Show(groups.SelectMany(g => g.Files.Select((f, i) => new FolderUsage
                {
                    Name = $"{System.IO.Path.GetFileName(f.Path)}  ({Loc.F("cópia {0} de {1}", i + 1, g.Files.Count)})", Path = f.Path, Bytes = g.Size, IsFile = true,
                })).ToList());
                SpaceDupesBtn.IsEnabled = groups.Count > 0;
                long extra = groups.Sum(g => g.Wasted);
                SpaceMsg.Text = groups.Count == 0
                    ? Loc.F("Nenhum arquivo duplicado de {0} ou mais.", Cleaner.FormatBytes(min))
                    : Loc.F("{0} de arquivos idênticos; as cópias extras ocupam {1}. Clique em “Revisar duplicados…” para escolher o que apagar.", Pt.N(groups.Count, Loc.T("grupo"), Loc.T("grupos")), Cleaner.FormatBytes(extra));
                return;
            }
            await DiskUsage.ScanAsync(root,
                item => Dispatcher.BeginInvoke(() =>
                {
                    _space.Add(item);
                    long max = _space.Max(x => x.Bytes);
                    foreach (var x in _space) x.Percent = max > 0 ? x.Bytes * 100.0 / max : 0;
                    SpaceGrid.ItemsSource = _space.OrderByDescending(x => x.Bytes).ToList();
                    ApplySpaceFilter();
                }),
                msg => Dispatcher.BeginInvoke(() => SpaceMsg.Text = msg), ct);
            SpaceMsg.Text = Loc.F("Pronto: {0} itens em {1} · total {2}. Só leitura: nada foi apagado.", _space.Count, root, Cleaner.FormatBytes(_space.Sum(x => x.Bytes)));
        }
        catch (OperationCanceledException) { SpaceMsg.Text = Loc.T("Análise cancelada."); }
        finally { SpaceScanBtn.IsEnabled = true; SpaceCancelBtn.IsEnabled = false; }
    }

    private void SpaceCancel_Click(object sender, RoutedEventArgs e) => _spaceCts?.Cancel();

    /// <summary>Leva 6.3: janela de revisão dos grupos de duplicados (miniaturas, qual manter, Lixeira).</summary>
    private void SpaceDupes_Click(object sender, RoutedEventArgs e)
    {
        if (_dupeGroups.Count == 0) return;
        var window = new DuplicatesWindow(this, _dupeGroups);
        window.ShowDialog();
        if (window.RemovedAny) SpaceScan_Click(sender, e); // as cópias que saíram não devem continuar na lista
    }

    private void SpaceMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != SpaceMode) return;
        SpaceGrid.ItemsSource = null;
        SpaceMsg.Text = Loc.T("Clique em Analisar.");
        string mode = (SpaceMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "folders";
        SpaceMin.Visibility = SpaceDupesBtn.Visibility = mode == "dupes" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SpaceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage f) { SpaceMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (f.IsFile && System.IO.File.Exists(f.Path))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\""); } catch { }
        }
        else if (System.IO.Directory.Exists(f.Path)) AppInfo.OpenShell(f.Path);
        else SpaceMsg.Text = Loc.T("Este item não informa uma pasta.");
    }

    // Fase 10, leva 10.1: o item selecionado direto no destravador
    private void SpaceWhoUses_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage f || f.Path.Length == 0) { SpaceMsg.Text = Loc.T("Selecione um item na lista."); return; }
        OpenFileUnlock(f.Path);
    }

    private void FileUnlock_Click(object sender, RoutedEventArgs e) => OpenFileUnlock(null);

    internal void OpenFileUnlock(string? path) => new FileUnlockWindow(this, path).ShowDialog();

    // A29 passou para a aba Programas (Fase 5)
    private void SpacePrograms_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = TabPrograms;

    private void ApplySpaceFilter() => ApplyFilter(SpaceGrid, SpaceSearch.Text, o => o is FolderUsage f ? f.Name + " " + f.Path : "");
    private void SpaceSearch_Changed(object sender, TextChangedEventArgs e) => ApplySpaceFilter();
}
