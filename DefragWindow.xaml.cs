using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class FragRow : CheckRow
{
    private string _status = "";
    private int _fragments;
    public string Path { get; }
    public long Size { get; }
    public FragRow(string path, long size, int fragments, string status) { Path = path; Size = size; _fragments = fragments; _status = status; }
    public string SizeText => Units.Bytes(Size);
    public int Fragments { get => _fragments; set { _fragments = value; Raise(nameof(Fragments)); } }
    public string Status { get => _status; set { _status = value; Raise(nameof(Status)); } }
}

/// <summary>
/// Leva 8.4 (D01–D03): desfragmentar. Na unidade, usa o <c>Optimize-Volume</c> do Windows (HD: desfragmenta; SSD: só refaz o TRIM) e mostra o
/// mapa dos clusters; em arquivo ou pasta, mostra os pedaços de cada arquivo e junta os clusters pela API de desfragmentação do NTFS.
/// </summary>
public partial class DefragWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<FragRow> _rows = [];
    private List<DriveFacts> _drives = [];
    private CancellationTokenSource? _cts;
    private bool _busy;

    public DefragWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        FilesGrid.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await LoadDrivesAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar ou clique em Cancelar."); return; }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); CancelBtn.IsEnabled = false; Msg.Text = Loc.T("Cancelando…"); }

    private void ShowMode(bool files)
    {
        DrivePanel.Visibility = files ? Visibility.Collapsed : Visibility.Visible;
        FilePanel.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        DriveTab.SetResourceReference(StyleProperty, files ? "TextButton" : "TonalButton");
        FileTab.SetResourceReference(StyleProperty, files ? "TonalButton" : "TextButton");
        Msg.Text = "";
    }

    private void DriveTab_Click(object sender, RoutedEventArgs e) => ShowMode(false);
    private void FileTab_Click(object sender, RoutedEventArgs e) => ShowMode(true);

    private void SetBusy(bool busy, bool cancellable = true)
    {
        _busy = busy;
        AnalyzeBtn.IsEnabled = OptimizeBtn.IsEnabled = MapBtn.IsEnabled = FileAnalyzeBtn.IsEnabled = FileDefragBtn.IsEnabled = !busy;
        CancelBtn.Visibility = busy && cancellable ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        Bar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Bar.IsIndeterminate = busy;
        if (!busy) Drive_Changed(this, null!);
    }

    private static IEnumerable<string> AppFolders => [AppPaths.Dir, AppContext.BaseDirectory];

    // ---------- unidade ----------

    private async Task LoadDrivesAsync()
    {
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));
        string sel = (DriveBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        DriveBox.Items.Clear();
        foreach (var d in _drives.Where(d => d.Ready && d.Kind is DriveKind.Fixed or DriveKind.Removable))
        {
            string media = d.SolidState switch { true => "SSD", false => "HD", _ => Loc.T("tipo desconhecido") };
            string name = d.Label.Length > 0 ? $" {d.Label}" : "";
            DriveBox.Items.Add(new ComboBoxItem { Tag = d.Letter, Content = $"{d.Letter}:{name} · {d.FileSystem} · {media} · {Units.Bytes(d.TotalBytes)}" });
        }
        var pick = DriveBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == sel) ?? DriveBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        DriveBox.SelectedItem = pick;
    }

    private DriveFacts? Selected => (DriveBox.SelectedItem as ComboBoxItem)?.Tag is string l ? _drives.FirstOrDefault(d => d.Letter == l) : null;

    private void Drive_Changed(object sender, SelectionChangedEventArgs? e)
    {
        if (Selected is not { } d) { DriveNote.Text = ""; AnalyzeBtn.IsEnabled = OptimizeBtn.IsEnabled = MapBtn.IsEnabled = false; return; }
        var o = DefragRules.Check(d);
        AnalyzeBtn.IsEnabled = o.Analyze && !_busy;
        MapBtn.IsEnabled = d.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !_busy;
        OptimizeBtn.IsEnabled = (o.Defrag || o.ReTrim) && !_busy;
        OptimizeBtn.Content = o.ReTrim ? Loc.T("Refazer o TRIM") : Loc.T("Desfragmentar");
        DriveNote.Text = o.Reason.Length > 0 ? Loc.T(o.Reason) : Loc.T("É um HD: pode ser desfragmentado.");
    }

    private IProgress<int> BarProgress => new Progress<int>(p => { Bar.IsIndeterminate = false; Bar.Value = p; });

    private async Task RunOptimizeAsync(DriveFacts d, DefragAction action)
    {
        Output.Clear();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Msg.Text = action switch { DefragAction.Analyze => Loc.T("Analisando…"), DefragAction.Defrag => Loc.T("Desfragmentando… isso pode levar horas."), _ => Loc.T("Refazendo o TRIM…") };
        var lines = new Progress<string>(l => { Output.AppendText(l + Environment.NewLine); Output.ScrollToEnd(); });
        RecoveryRun run;
        try { run = await DefragRunner.RunAsync(d.Letter, action, BarProgress, lines, _cts.Token); }
        catch (System.ComponentModel.Win32Exception ex) { Msg.Text = ex.Message; SetBusy(false); return; }
        finally { _cts?.Dispose(); _cts = null; }
        SetBusy(false);
        string what = action switch { DefragAction.Analyze => Loc.T("Análise"), DefragAction.Defrag => Loc.T("Desfragmentação"), _ => Loc.T("TRIM") };
        string summary = run.Cancelled ? Loc.F("{0} cancelada.", what) : run.ExitCode == 0 ? Loc.F("{0} de {1}: concluída.", what, d.Letter + ":") : Loc.F("{0} terminou com o código {1}. Confira a resposta acima (precisa de administrador).", what, run.ExitCode);
        Msg.Text = summary;
        if (action != DefragAction.Analyze) ActionLog.Add(Loc.F("{0} de {1}:", what, d.Letter), summary);
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Selected is not { } d) return;
        if (!AppInfo.IsAdmin) { OfferAdmin(); return; }
        await RunOptimizeAsync(d, DefragAction.Analyze);
    }

    private async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));   // lê de novo: a regra vale para o disco como está agora
        if (Selected is not { } d) return;
        var o = DefragRules.Check(d);
        var action = o.Defrag ? DefragAction.Defrag : o.ReTrim ? DefragAction.ReTrim : (DefragAction?)null;
        if (action is null) { Msg.Text = Loc.T(o.Reason); return; }
        if (_main.Simulate(Loc.T("Otimizar a unidade"), Loc.F("Rodaria {0} em {1}:.", action == DefragAction.Defrag ? Loc.T("a desfragmentação") : Loc.T("o TRIM"), d.Letter))) return;
        if (!AppInfo.IsAdmin) { OfferAdmin(); return; }
        string text = action == DefragAction.Defrag
            ? Loc.F("Desfragmentar {0}:? O Windows reorganiza os arquivos do HD para ficarem juntos. Pode levar horas, e o PC fica mais lento enquanto isso. Não desligue o computador.", d.Letter)
            : Loc.F("Refazer o TRIM de {0}:? O Windows avisa o SSD de quais blocos estão livres. É rápido e seguro.", d.Letter);
        if (!Dialogs.Confirm(this, Loc.T("Otimizar a unidade"), text, Loc.T("Começar"))) return;
        await RunOptimizeAsync(d, action.Value);
    }

    private void OfferAdmin()
    {
        if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Isso exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir"))) Host.RestartAsAdmin();
    }

    private void Map_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) return;
        try
        {
            using var volume = VolumeApi.OpenVolume(d.Letter[0], forMoving: false);
            var data = VolumeApi.GetVolumeData(volume);
            var bitmap = VolumeApi.ReadBitmap(volume);
            if (bitmap is null) { Msg.Text = Loc.T("A unidade é grande demais para o mapa."); return; }
            const int cols = 160, rows = 30;
            var cells = bitmap.Map(cols * rows);
            MapImage.Source = Render(cells, cols, rows);
            MapImage.Visibility = Visibility.Visible;
            long used = bitmap.UsedClusters;
            long cellBytes = (long)data.TotalClusters * data.BytesPerCluster / (cols * rows);
            MapInfo.Text = Loc.F("{0} de {1} usados ({2}%). Cada quadrado é cerca de {3}. Escuro: ocupado; claro: livre.",
                Units.Bytes(used * data.BytesPerCluster), Units.Bytes(data.TotalClusters * data.BytesPerCluster), (int)(used * 100 / Math.Max(1, data.TotalClusters)), Units.Bytes(cellBytes));
            Msg.Text = "";
        }
        catch (UnauthorizedAccessException) { Msg.Text = Loc.T("Ler o mapa exige administrador."); OfferAdmin(); }
        catch (Win32Exception ex) { Msg.Text = Loc.F("Não consegui ler o mapa: {0}", ex.Message); }
    }

    private BitmapSource Render(double[] cells, int cols, int rows)
    {
        Color free = (TryFindResource("SurfaceVariant") as SolidColorBrush)?.Color ?? Colors.LightGray;
        Color used = (TryFindResource("Primary") as SolidColorBrush)?.Color ?? Colors.Blue;
        var pixels = new byte[cols * rows * 4];
        for (int i = 0; i < cells.Length; i++)
        {
            double f = Math.Clamp(cells[i], 0, 1);
            pixels[i * 4] = (byte)(free.B + (used.B - free.B) * f);
            pixels[i * 4 + 1] = (byte)(free.G + (used.G - free.G) * f);
            pixels[i * 4 + 2] = (byte)(free.R + (used.R - free.R) * f);
            pixels[i * 4 + 3] = 255;
        }
        var bmp = BitmapSource.Create(cols, rows, 96, 96, PixelFormats.Bgra32, null, pixels, cols * 4);
        bmp.Freeze();
        return bmp;
    }

    // ---------- arquivo ou pasta ----------

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Escolha o arquivo") };
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FileName;
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Escolha a pasta") };
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FolderName;
    }

    private async void FileAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string path = PathBox.Text.Trim().Trim('"');
        if (!File.Exists(path) && !Directory.Exists(path)) { Msg.Text = Loc.T("Escolha um arquivo ou uma pasta que existe."); return; }
        _rows.Clear();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        var ct = _cts.Token;
        Msg.Text = Loc.T("Olhando onde cada arquivo está no disco…");
        int scanned = 0;
        bool capped = false;
        var found = new List<FragRow>();
        try
        {
            await Task.Run(() =>
            {
                long clusterBytes = 4096;
                try { using var v = VolumeApi.OpenVolume(path[0], false); clusterBytes = VolumeApi.GetVolumeData(v).BytesPerCluster; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or Win32Exception) { /* sem administrador: assume o cluster comum de 4 KB */ }
                var files = File.Exists(path) ? [path] : EnumerateFiles(path);
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (++scanned > 20_000) { capped = true; break; }
                    try
                    {
                        var info = new FileInfo(f);
                        if (!DefragRules.CanMove(f, info.Attributes, info.Length, clusterBytes).Ok) continue;
                        int frags = DefragRules.Fragments(VolumeApi.GetExtents(f));
                        if (frags > 1) found.Add(new FragRow(f, info.Length, frags, Loc.T("fragmentado")));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { /* arquivo em uso ou sem permissão */ }
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { _cts?.Dispose(); _cts = null; SetBusy(false); }
        foreach (var r in found.OrderByDescending(r => r.Fragments).Take(2000)) _rows.Add(r);
        FileInfo.Text = _rows.Count == 0
            ? Loc.F("{0} arquivos olhados: nenhum fragmentado.", scanned)
            : Loc.F("{0} arquivos olhados · {1} fragmentados.", scanned, found.Count) + (capped ? " " + Loc.T("Parei no limite de 20.000 arquivos.") : "");
        Msg.Text = "";
    }

    private static IEnumerable<string> EnumerateFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });

    private async void FileDefrag_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_rows.Count == 0) { Msg.Text = Loc.T("Analise primeiro: a lista está vazia."); return; }
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));
        string letter = _rows[0].Path[..1].ToUpperInvariant();
        var drive = _drives.FirstOrDefault(d => d.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));
        if (drive is null || !DefragRules.Check(drive).Defrag) { Msg.Text = drive is null ? Loc.T("Unidade não encontrada.") : Loc.T(DefragRules.Check(drive).Reason.Length > 0 ? DefragRules.Check(drive).Reason : "Só em HD NTFS"); return; }
        if (_rows.Any(r => !r.Path.StartsWith(letter, StringComparison.OrdinalIgnoreCase))) { Msg.Text = Loc.T("Os arquivos da lista são de unidades diferentes."); return; }
        if (_main.Simulate(Loc.T("Desfragmentar arquivos"), Loc.F("Desfragmentaria {0}.", Pt.N(_rows.Count, Loc.T("arquivo"), Loc.T("arquivos"))))) return;
        if (!AppInfo.IsAdmin) { OfferAdmin(); return; }
        if (!Dialogs.Confirm(this, Loc.T("Desfragmentar arquivos"),
                Loc.F("Juntar no disco os pedaços de {0}? O Windows move os dados com segurança, mas isso mexe no disco e pode demorar. Feche os programas que usam esses arquivos.", Pt.N(_rows.Count, Loc.T("arquivo"), Loc.T("arquivos"))), Loc.T("Começar"))) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        var ct = _cts.Token;
        var list = _rows.ToList();
        int ok = 0, same = 0, failed = 0, done = 0;
        try
        {
            await Task.Run(() =>
            {
                using var volume = VolumeApi.OpenVolume(letter[0], forMoving: true);
                int clusterBytes = VolumeApi.GetVolumeData(volume).BytesPerCluster;
                foreach (var row in list)
                {
                    ct.ThrowIfCancellationRequested();
                    var bitmap = VolumeApi.ReadBitmap(volume);
                    if (bitmap is null) throw new InvalidOperationException(Loc.T("A unidade é grande demais para o app."));
                    var r = FileDefragmenter.Defrag(volume, bitmap, clusterBytes, row.Path, ct);
                    Dispatcher.Invoke(() =>
                    {
                        row.Fragments = r.FragmentsAfter > 0 ? r.FragmentsAfter : row.Fragments;
                        row.Status = r.Status switch
                        {
                            FileDefragStatus.Defragmented => Loc.T("juntado"), FileDefragStatus.AlreadyTogether => Loc.T("já estava junto"),
                            FileDefragStatus.NoRoom => Loc.T("sem espaço livre contínuo"), _ => Loc.F("pulado: {0}", r.Note.Length > 0 ? Loc.T(r.Note) : ""),
                        };
                        Bar.IsIndeterminate = false;
                        Bar.Value = ++done * 100.0 / list.Count;
                    });
                    switch (r.Status) { case FileDefragStatus.Defragmented: ok++; break; case FileDefragStatus.AlreadyTogether: same++; break; default: failed++; break; }
                }
            }, ct);
            Msg.Text = Loc.F("{0} juntados · {1} já estavam juntos · {2} não deu.", ok, same, failed);
        }
        catch (OperationCanceledException) { Msg.Text = Loc.F("Cancelado. {0} juntados até aqui.", ok); }
        catch (UnauthorizedAccessException) { Msg.Text = Loc.T("Abrir o volume exige administrador."); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Msg.Text = ex.Message; }
        finally { _cts?.Dispose(); _cts = null; SetBusy(false); }
        if (ok + same + failed > 0) ActionLog.Add(Loc.T("Arquivos desfragmentados"), Msg.Text);
    }
}
