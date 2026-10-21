using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class MainWindow
{
    // ---------- Espaço em disco (A27) ----------
    private CancellationTokenSource? _spaceCts;
    private readonly List<FolderUsage> _space = new();

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
                SpaceMsg.Text = $"{items.Count} maiores arquivos (50 MB ou mais) em {root}. Só leitura.";
                return;
            }
            if (mode == "dupes")
            {
                var items = await DiskUsage.DuplicatesAsync(root, 10L << 20, 60, Progress, ct);
                Show(items);
                int groups = items.Count(i => i.Name.Contains("(cópia 1 de"));
                long extra = items.Where(i => !i.Name.Contains("(cópia 1 de")).Sum(i => i.Bytes);
                SpaceMsg.Text = groups == 0 ? "Nenhum arquivo duplicado de 10 MB ou mais." : $"{Pt.N(groups, "grupo", "grupos")} de arquivos idênticos; as cópias extras ocupam {Cleaner.FormatBytes(extra)}. Confira antes de apagar qualquer uma.";
                return;
            }
            if (mode == "apps")
            {
                var apps = await Task.Run(DiskUsage.InstalledApps, ct);
                Show(apps);
                SpaceMsg.Text = $"{apps.Count} aplicativos. Tamanho informado pelo próprio instalador (\"n/d\" quando ele não informa).";
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
            SpaceMsg.Text = $"Pronto: {_space.Count} itens em {root} · total {Cleaner.FormatBytes(_space.Sum(x => x.Bytes))}. Só leitura: nada foi apagado.";
        }
        catch (OperationCanceledException) { SpaceMsg.Text = "Análise cancelada."; }
        finally { SpaceScanBtn.IsEnabled = true; SpaceCancelBtn.IsEnabled = false; }
    }

    private void SpaceCancel_Click(object sender, RoutedEventArgs e) => _spaceCts?.Cancel();

    private void SpaceMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != SpaceMode) return;
        bool apps = (SpaceMode.SelectedItem as ComboBoxItem)?.Tag as string == "apps";
        SpaceRoot.IsEnabled = !apps;
        SpaceUninstallBtn.Visibility = apps ? Visibility.Visible : Visibility.Collapsed;
        SpaceGrid.ItemsSource = null;
        SpaceMsg.Text = "Clique em Analisar.";
    }

    private void SpaceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage f) { SpaceMsg.Text = "Selecione um item na lista."; return; }
        if (f.IsFile && System.IO.File.Exists(f.Path))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\""); } catch { }
        }
        else if (System.IO.Directory.Exists(f.Path)) AppInfo.OpenShell(f.Path);
        else SpaceMsg.Text = "Este item não informa uma pasta.";
    }

    // ---------- Desinstalador assistido (A29) ----------
    private void SpaceUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage app || app.Tag.Length == 0) { SpaceMsg.Text = "Selecione um aplicativo."; return; }
        var cmd = UninstallCommand.Parse(app.Tag);
        if (cmd is null) { SpaceMsg.Text = $"O desinstalador de {app.Name} não foi encontrado no disco. Use \"Aplicativos instalados\" do Windows."; return; }
        if (Simulate("Abrir o desinstalador", $"Executaria o desinstalador registrado de {app.Name}: {cmd.Display}")) return;

        // Desinstaladores sem assinatura são comuns (inclusive legítimos); o aviso só dá a informação para decidir.
        string signer = Authenticode.Verify(cmd.Executable, checkRevocation: false) is { } s
            ? $"Assinado por: {s.CommonName ?? s.Organization ?? "(sem nome)"}"
            : "Atenção: este programa não tem assinatura digital válida.";
        if (!Dialogs.Confirm(this, "Desinstalar aplicativo?",
                $"Abrir o desinstalador de:\n{app.Name}\n\nComando exato:\n{cmd.Display}\n\n{signer}\n\nO próprio desinstalador vai abrir e pedir confirmação. O Notebook Doctor não remove nada sozinho.",
                "Abrir desinstalador", danger: true)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd.Executable, cmd.Arguments) { UseShellExecute = true });
            ActionLog.Add("Desinstalador aberto", app.Name);
            SpaceMsg.Text = "Desinstalador aberto. Clique em Analisar depois para atualizar a lista.";
        }
        catch (Exception ex) { SpaceMsg.Text = "Não foi possível abrir o desinstalador: " + ex.Message; }
    }
}
