using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class NtfsRow : CheckRow
{
    private RecoveryChance _chance;

    public DeletedFile File { get; }
    public string Folder { get; }
    public RecoveryAssessment Assessment { get; }

    public NtfsRow(DeletedFile file, string folder, RecoveryAssessment assessment)
    {
        File = file;
        Folder = folder;
        Assessment = assessment;
        _chance = assessment.Chance;
    }

    public string Name => File.Name;
    public string FolderText => Folder.Length > 0 ? Folder.Replace(NtfsFolders.Lost, Loc.T(NtfsFolders.Lost)) : Loc.T("(raiz da unidade)");
    public string SizeText => Units.Bytes(File.Size);
    public string ModifiedText => File.Modified is { } m ? m.ToLocalTime().ToString("g") : "";
    public bool CanSelect => _chance != RecoveryChance.Unsupported;
    public RecoveryChance Chance { get => _chance; set { _chance = value; Raise(nameof(Chance)); Raise(nameof(ChanceText)); Raise(nameof(CanSelect)); if (value == RecoveryChance.Unsupported) Selected = false; } }

    public string ChanceText => _chance switch
    {
        RecoveryChance.Excellent => Loc.T("Excelente"),
        RecoveryChance.Partial => Loc.T("Parcial"),
        RecoveryChance.Unknown => Loc.T("Não sei"),
        RecoveryChance.Poor => Loc.T("Ruim"),
        _ => Loc.T("Não dá"),
    };

    public string Explain() => _chance switch
    {
        RecoveryChance.Excellent => File.Resident ? Loc.T("O arquivo é pequeno e está guardado inteiro na tabela de arquivos: deve voltar inteiro.") : Loc.T("Nenhum cluster do arquivo foi usado por outro arquivo desde que ele foi apagado: deve voltar inteiro."),
        RecoveryChance.Partial => Assessment.TotalClusters > 0 && Assessment.UsedClusters > 0
            ? Loc.F("{0} de {1} blocos do arquivo já foram usados por outros arquivos. Ele volta, mas provavelmente danificado.", Assessment.UsedClusters, Assessment.TotalClusters)
            : Loc.T("O começo do arquivo não combina com o tipo dele: parte dos dados pode ter sido trocada. Ele volta, mas provavelmente danificado."),
        RecoveryChance.Unknown => Loc.T("O app não conseguiu ver quais blocos da unidade estão em uso. Dá para tentar."),
        RecoveryChance.Poor => Loc.T("Os blocos do arquivo já foram usados por outros arquivos, ou o SSD os apagou. O que voltar provavelmente não abre."),
        _ => File.Limit.HasFlag(NtfsLimit.Compressed) ? Loc.T("Arquivo comprimido pelo NTFS: o app ainda não recupera esse tipo.")
            : File.Limit.HasFlag(NtfsLimit.Encrypted) ? Loc.T("Arquivo criptografado pelo NTFS: sem a chave, o que voltasse seria ilegível.")
            : File.Limit.HasFlag(NtfsLimit.SpreadOut) ? Loc.T("Os dados do arquivo estão espalhados em vários registros: o app ainda não recupera esse tipo.")
            : Loc.T("Os dados do registro estão incompletos ou fora da unidade."),
    };
}

/// <summary>
/// Leva 8.5 (V01/V02, opção B): motor próprio de recuperação de arquivos em NTFS. Lê a MFT pelo volume cru (só leitura), lista os apagados com a chance de
/// cada um, mostra prévia de imagens e textos e copia os marcados para uma pasta de OUTRA unidade.
/// </summary>
public partial class NtfsRecoveryWindow : Window
{
    private static App Host => App.Current;
    private const int MaxRows = 20_000;
    private const long MaxImagePreview = 20L << 20;

    private readonly MainWindow _main;
    private readonly ObservableCollection<NtfsRow> _rows = [];
    private List<NtfsRow> _all = [];
    private List<DriveFacts> _drives = [];
    private DriveFacts? _scanDrive;
    private NtfsScan? _scan;
    private RawVolume? _raw;
    private CancellationTokenSource? _cts, _previewCts;
    private bool _busy;

    public NtfsRecoveryWindow(MainWindow main)
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
        else { _previewCts?.Cancel(); _raw?.Dispose(); _raw = null; }
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar ou clique em Cancelar."); return; }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); CancelBtn.IsEnabled = false; Msg.Text = Loc.T("Cancelando…"); }

    private static IEnumerable<string> AppFolders => [AppPaths.Dir, AppContext.BaseDirectory];

    private async Task LoadDrivesAsync()
    {
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));
        SourceBox.Items.Clear();
        foreach (var d in _drives.Where(d => d.Ready && d.Kind is DriveKind.Fixed or DriveKind.Removable && d.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)))
        {
            string name = d.Label.Length > 0 ? $" {d.Label}" : "";
            string kind = d.IsSystem ? Loc.T("Windows") : d.Kind == DriveKind.Removable ? Loc.T("removível") : Loc.T("fixa");
            SourceBox.Items.Add(new ComboBoxItem { Tag = d.Letter, Content = $"{d.Letter}:{name} · {kind} · NTFS · {Units.Bytes(d.TotalBytes)}" });
        }
        SourceBox.SelectedItem = SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => _drives.Any(d => d.Letter == (string?)i.Tag && !d.IsSystem))
                                 ?? SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        if (SourceBox.Items.Count == 0) Msg.Text = Loc.T("Não achei nenhuma unidade NTFS. Para FAT e exFAT, use o modo profundo do Windows File Recovery.");
    }

    private DriveFacts? Source => (SourceBox.SelectedItem as ComboBoxItem)?.Tag is string l ? _drives.FirstOrDefault(d => d.Letter == l) : null;

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Source is not { } d) { Warn.Text = ""; return; }
        Warn.Text = string.Join("\n", NtfsRecoveryRules.Warnings(d).Select(w => "• " + Loc.T(w)));
        bool destOk = DestBox.Text.Length > 2 && char.ToUpperInvariant(DestBox.Text[0]) != char.ToUpperInvariant(d.Letter[0]);
        if (!destOk)
        {
            var other = _drives.FirstOrDefault(x => x.Ready && x.Kind == DriveKind.Fixed && x.Letter != d.Letter && x.FreeBytes > 1L << 30);
            DestBox.Text = other is null ? "" : $@"{other.Letter}:\Recuperados";
        }
    }

    private void PickDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Pasta onde guardar o que for recuperado") };
        if (dlg.ShowDialog(this) == true) DestBox.Text = dlg.FolderName;
    }

    private void OfferAdmin()
    {
        if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Isso exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir"))) Host.RestartAsAdmin();
    }

    private void SetBusy(bool busy, bool progress = true)
    {
        _busy = busy;
        ScanBtn.IsEnabled = !busy;
        RecoverBtn.IsEnabled = !busy && _all.Count > 0;
        SourceBox.IsEnabled = !busy;
        CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        Bar.Visibility = busy && progress ? Visibility.Visible : Visibility.Collapsed;
        Bar.IsIndeterminate = busy;
    }

    // ---------- procurar ----------

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Source is not { } src) { Msg.Text = Loc.T("Escolha uma unidade."); return; }
        if (!AppInfo.IsAdmin) { OfferAdmin(); return; }
        char letter = src.Letter[0];

        _previewCts?.Cancel();
        _raw?.Dispose();
        _raw = null;
        _rows.Clear();
        _all = [];
        _scan = null;
        ClearPreview();
        Summary.Text = "";
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Msg.Text = Loc.T("Lendo a lista de arquivos da unidade… isso pode levar alguns minutos.");
        var progress = new Progress<ScanProgress>(p =>
        {
            Bar.IsIndeterminate = false;
            Bar.Value = p.RecordsTotal > 0 ? p.RecordsDone * 100.0 / p.RecordsTotal : 0;
            Msg.Text = Loc.F("Lendo a lista de arquivos… {0} apagados até agora.", p.Found);
        });
        var token = _cts.Token;
        try
        {
            var (scan, bitmap, raw) = await Task.Run(() =>
            {
                var volume = new RawVolume(letter);
                try
                {
                    var found = NtfsScanner.Scan(volume, progress, token);
                    VolumeBitmap? bits = null;
                    try { using var v = VolumeApi.OpenVolume(letter); bits = VolumeApi.ReadBitmap(v); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException) { /* sem o bitmap, a chance fica "não sei" */ }
                    return (found, bits, volume);
                }
                catch { volume.Dispose(); throw; }
            }, token);

            _raw = raw;
            _scan = scan;
            _scanDrive = src;
            var rows = new List<NtfsRow>(scan.Files.Count);
            foreach (var f in scan.Files)
                rows.Add(new NtfsRow(f, scan.Folders.PathOf(f.Parent, f.ParentSequence), NtfsChance.Assess(f, bitmap)));
            _all = rows.OrderBy(r => NtfsChance.Order(r.Chance)).ThenByDescending(r => r.File.Modified ?? DateTime.MinValue).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            ApplyFilter();
            int good = _all.Count(r => r.Chance == RecoveryChance.Excellent);
            Msg.Text = _all.Count == 0
                ? Loc.T("Não achei arquivos apagados nesta unidade. Se foram apagados há muito tempo, a lista pode ter sido reaproveitada.")
                : Loc.F("{0} apagados encontrados · {1} com chance excelente.{2}", Pt.N(_all.Count, Loc.T("arquivo"), Loc.T("arquivos")), good,
                    scan.Truncated ? " " + Loc.F("Mostrando só os primeiros {0}.", NtfsScanner.MaxFiles) : "");
            ActionLog.Add(Loc.F("Procura de apagados em {0}:", src.Letter), Msg.Text);
        }
        catch (OperationCanceledException) { Msg.Text = Loc.T("Cancelado."); }
        catch (UnauthorizedAccessException) { OfferAdmin(); Msg.Text = ""; }
        catch (NtfsException ex) { Msg.Text = Loc.T(ex.Message); }
        catch (Win32Exception ex) { Msg.Text = Loc.F("Não consegui ler a unidade: {0}", ex.Message); }
        finally { _cts?.Dispose(); _cts = null; SetBusy(false); }
    }

    // ---------- lista e filtros ----------

    private void ApplyFilter()
    {
        string text = FilterBox?.Text.Trim() ?? "";
        bool good = OnlyGood?.IsChecked == true;
        var view = _all.Where(r => (!good || r.Chance is RecoveryChance.Excellent or RecoveryChance.Partial or RecoveryChance.Unknown)
                                   && (text.Length == 0 || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || r.Folder.Contains(text, StringComparison.CurrentCultureIgnoreCase)));
        _rows.Clear();
        int shown = 0;
        foreach (var r in view) { if (shown++ >= MaxRows) break; _rows.Add(r); }
        RecoverBtn.IsEnabled = !_busy && _all.Count > 0;
        UpdateSummary();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) ApplyFilter(); }

    private void MarkBest_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows.Where(r => r.Chance == RecoveryChance.Excellent)) r.Selected = true;
        UpdateSummary();
    }

    private void MarkNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.Selected = false;
        UpdateSummary();
    }

    private List<NtfsRow> Chosen => _all.Where(r => r.Selected && r.CanSelect).ToList();

    private void UpdateSummary()
    {
        var chosen = Chosen;
        Summary.Text = _all.Count == 0 ? "" : _rows.Count < _all.Count && (FilterBox.Text.Length > 0 || OnlyGood.IsChecked == true)
            ? Loc.F("Mostrando {0} de {1}. Marcados: {2} ({3}).", _rows.Count, _all.Count, chosen.Count, Units.Bytes(chosen.Sum(r => r.File.Size)))
            : Loc.F("Marcados: {0} ({1}).", chosen.Count, Units.Bytes(chosen.Sum(r => r.File.Size)));
    }

    // ---------- prévia ----------

    private void ClearPreview()
    {
        PreviewTitle.Text = Loc.T("Prévia");
        PreviewImage.Source = null;
        PreviewImage.Visibility = PreviewText.Visibility = Visibility.Collapsed;
        PreviewText.Text = PreviewInfo.Text = "";
    }

    private async void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSummary();
        _previewCts?.Cancel();
        if (FilesGrid.SelectedItem is not NtfsRow row || _scan is null || _raw is null) { ClearPreview(); return; }
        var cts = _previewCts = new CancellationTokenSource();
        var token = cts.Token;
        ClearPreview();
        PreviewTitle.Text = row.Name;
        if (row.Chance == RecoveryChance.Unsupported) { PreviewInfo.Text = row.Explain(); return; }

        var kind = FileSignatures.PreviewOf(row.File.Extension);
        long want = kind switch { PreviewKind.Image => row.File.Size <= MaxImagePreview ? row.File.Size : 4096, PreviewKind.Text => 8192, _ => 4096 };
        var raw = _raw;
        var geo = _scan.Geometry;
        byte[] head;
        try
        {
            head = await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                NtfsRecoverer.CopyTo(raw, geo, row.File, ms, token, want);
                return ms.ToArray();
            }, token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is NtfsException or Win32Exception or IOException)
        {
            if (token.IsCancellationRequested) return;
            PreviewInfo.Text = Loc.F("Não consegui ler o arquivo: {0}", ex is NtfsException ? Loc.T(ex.Message) : ex.Message);
            return;
        }
        if (token.IsCancellationRequested) return;

        var signature = FileSignatures.Check(row.File.Extension, head);
        bool zeros = FileSignatures.AllZero(head.AsSpan(0, Math.Min(head.Length, 4096)));
        row.Chance = NtfsChance.Refine(row.Assessment.Chance, signature, zeros);
        var notes = new List<string> { row.Explain() };
        if (zeros) notes.Add(Loc.T("O começo do arquivo só tem zeros: se a unidade é SSD, ele provavelmente foi apagado pelo TRIM."));
        else if (signature == SignatureCheck.Mismatch) notes.Add(Loc.T("O começo do arquivo não combina com o tipo dele: outro arquivo pode ter escrito por cima."));
        else if (signature == SignatureCheck.Match) notes.Add(Loc.T("O começo do arquivo combina com o tipo dele."));
        PreviewInfo.Text = string.Join("\n\n", notes);

        if (zeros) return;
        if (kind == PreviewKind.Image && head.Length == row.File.Size)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(head);
                image.EndInit();
                image.Freeze();
                PreviewImage.Source = image;
                PreviewImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or IOException or FileFormatException)
            {
                PreviewInfo.Text += "\n\n" + Loc.T("Não foi possível mostrar a imagem: ela está danificada ou incompleta.");
            }
        }
        else if (kind == PreviewKind.Text)
        {
            PreviewText.Text = Encoding.UTF8.GetString(head);
            PreviewText.Visibility = Visibility.Visible;
        }
        else if (kind == PreviewKind.Image)
        {
            PreviewInfo.Text += "\n\n" + Loc.T("A imagem é grande demais para a prévia.");
        }
    }

    // ---------- recuperar ----------

    private async void Recover_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_scan is not { } scan || _scanDrive is not { } src || _raw is not { } raw) { Msg.Text = Loc.T("Procure os arquivos apagados primeiro."); return; }
        var chosen = Chosen;
        if (chosen.Count == 0) { Msg.Text = Loc.T("Marque pelo menos um arquivo."); return; }
        string dest = DestBox.Text.Trim();
        if (NtfsRecoveryRules.CheckDestination(src.Letter, dest, ResolveExisting(dest)) is { } bad) { Msg.Text = Loc.T(bad); return; }
        long total = chosen.Sum(r => r.File.Size);
        try
        {
            string root = Path.GetPathRoot(dest)!;
            if (new DriveInfo(root).AvailableFreeSpace < NtfsRecoveryRules.SpaceNeeded(total))
            { Msg.Text = Loc.F("Falta espaço no destino: são necessários cerca de {0}.", Units.Bytes(NtfsRecoveryRules.SpaceNeeded(total))); return; }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Msg.Text = Loc.T("Não consegui ver o espaço livre do destino."); return; }

        if (_main.Simulate(Loc.T("Recuperar arquivos"), Loc.F("Recuperaria {0} ({1}) da unidade {2}: para {3}.", Pt.N(chosen.Count, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(total), src.Letter, dest))) return;

        int weak = chosen.Count(r => r.Chance is RecoveryChance.Partial or RecoveryChance.Poor);
        string text = Loc.F("Copiar {0} ({1}) da unidade {2}: para {3}?", Pt.N(chosen.Count, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(total), src.Letter, dest);
        if (weak > 0) text += "\n\n" + Loc.F("{0} deles têm chance parcial ou ruim e podem voltar danificados.", weak);
        text += "\n\n" + Loc.T("O app só lê a unidade de origem; ele grava apenas na pasta de destino, numa subpasta nova com data e hora. Nada que já existe lá é sobrescrito.");
        if (!Dialogs.Confirm(this, Loc.T("Recuperar arquivos"), text, Loc.T("Começar"))) return;

        string outRoot = Path.Combine(dest, "Recuperados_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        try
        {
            Directory.CreateDirectory(outRoot);
            // De novo, agora com a pasta criada: se ela é uma junção para a origem, o caminho verdadeiro entrega
            if (NtfsRecoveryRules.CheckDestination(src.Letter, outRoot, RealPath.Of(outRoot)) is { } bad2)
            { try { Directory.Delete(outRoot); } catch (IOException) { } Msg.Text = Loc.T(bad2); return; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Msg.Text = Loc.F("Não consegui criar a pasta de destino: {0}", ex.Message); return; }

        var files = chosen.Select(r => r.File).ToList();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        var progress = new Progress<(int Done, int Total, string Name)>(p =>
        {
            Bar.IsIndeterminate = false;
            Bar.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            Msg.Text = p.Name.Length > 0 ? Loc.F("Recuperando {0} de {1}: {2}", p.Done + 1, p.Total, p.Name) : Loc.T("Terminando…");
        });
        List<RecoveredFile> results;
        int mismatched = 0;
        try
        {
            results = await Task.Run(() =>
            {
                var r = NtfsRecoveryJob.Run(raw, scan, files, outRoot, progress, token);
                // Confere o começo do que foi gravado com o tipo do arquivo
                foreach (var done in r.Where(x => x.Complete && x.Bytes > 0))
                {
                    try
                    {
                        using var fs = new FileStream(done.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var head = new byte[4096];
                        int n = fs.Read(head, 0, head.Length);
                        if (FileSignatures.Check(Path.GetExtension(done.Path), head.AsSpan(0, n)) == SignatureCheck.Mismatch) mismatched++;
                    }
                    catch (IOException) { }
                }
                return r;
            });
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
            _cts?.Dispose(); _cts = null;
            Msg.Text = Loc.T("Cancelado. O que já foi gravado fica na pasta de destino.");
            ActionLog.Add(Loc.F("Recuperação de arquivos em {0}:", src.Letter), Msg.Text);
            AppInfo.OpenShell(outRoot);
            return;
        }
        finally { _cts?.Dispose(); _cts = null; }
        SetBusy(false);

        int ok = results.Count(r => r.Complete), partial = results.Count(r => !r.Complete && r.Bytes > 0), failed = results.Count(r => r.Bytes == 0 && !r.Complete);
        string summary = Loc.F("Pronto: {0} recuperados ({1}).", ok, Units.Bytes(results.Where(r => r.Complete).Sum(r => r.Bytes)));
        if (partial > 0) summary += " " + Loc.F("{0} só em parte.", partial);
        if (failed > 0) summary += " " + Loc.F("{0} não deram.", failed);
        if (mismatched > 0) summary += " " + Loc.F("{0} não parecem do tipo certo e podem estar danificados.", mismatched);
        Msg.Text = summary;
        ActionLog.Add(Loc.F("Recuperação de arquivos em {0}:", src.Letter), summary);
        var problems = results.Where(r => r.Note is not null).Take(30).Select(r => $"{r.Name}: {Loc.T(r.Note!)}").ToList();
        if (problems.Count > 0) Dialogs.ShowText(this, Loc.T("Arquivos com problema"), string.Join("\n", problems));
        AppInfo.OpenShell(outRoot);
        System.Media.SystemSounds.Asterisk.Play();
    }

    // O destino ainda pode não existir: confere o caminho verdadeiro da pasta existente mais próxima (links e junções)
    private static string? ResolveExisting(string dest)
    {
        try
        {
            string? cur = dest;
            while (!string.IsNullOrEmpty(cur) && !Directory.Exists(cur)) cur = Path.GetDirectoryName(cur);
            return string.IsNullOrEmpty(cur) ? null : RealPath.Of(cur);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }
}
