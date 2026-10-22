using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// Leva 8.2 (R20, R21, C11): apagar de vez. Três caminhos: arquivos e pastas (sobrescrever, renomear e apagar), espaço livre (encher e apagar)
/// e unidade externa inteira (formatação completa com uma passada de zeros). Nada vai para a Lixeira e não há desfazer; por isso cada um
/// mostra o plano, o que o app não consegue garantir (SSD) e pede confirmação.
/// </summary>
public partial class SecureEraseWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<string> _paths = [];
    private readonly KnownPlaces _places = WindowsPlaces.Current(AppPaths.Dir);
    private readonly Dictionary<char, bool?> _solid = [];
    private List<DriveFacts> _drives = [];
    private ErasePlan? _plan;
    private bool _special;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public SecureEraseWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        FilesList.ItemsSource = _paths;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => { await RefreshDrivesAsync(); await RefreshPlanAsync(); };
    }

    private static IEnumerable<string> AppFolders => [AppPaths.Dir, AppContext.BaseDirectory];
    private static IEnumerable<string> CloudFolders => OneDriveScan.Folders();

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

    // ---------- abas ----------

    private void ShowMode(int mode)
    {
        FilesPanel.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        WipePanel.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        DrivePanel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        FilesTab.SetResourceReference(StyleProperty, mode == 0 ? "TonalButton" : "TextButton");
        WipeTab.SetResourceReference(StyleProperty, mode == 1 ? "TonalButton" : "TextButton");
        DriveTab.SetResourceReference(StyleProperty, mode == 2 ? "TonalButton" : "TextButton");
        Msg.Text = "";
    }

    private void FilesTab_Click(object sender, RoutedEventArgs e) => ShowMode(0);
    private void WipeTab_Click(object sender, RoutedEventArgs e) => ShowMode(1);
    private void DriveTab_Click(object sender, RoutedEventArgs e) => ShowMode(2);

    private void SetBusy(bool busy, bool cancellable)
    {
        _busy = busy;
        EraseBtn.IsEnabled = WipeBtn.IsEnabled = FormatBtn.IsEnabled = !busy;
        CancelBtn.Visibility = busy && cancellable ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        Bar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Bar.IsIndeterminate = false;
        Bar.Value = 0;
    }

    private bool? IsSolid(string path)
    {
        char letter = char.ToUpperInvariant(path.Length > 0 ? path[0] : 'C');
        if (!_solid.TryGetValue(letter, out var s)) _solid[letter] = s = DriveProbe.IsSolidState(letter);
        return s;
    }

    // ---------- arquivos e pastas ----------

    private async Task RefreshPlanAsync()
    {
        if (_paths.Count == 0) { _plan = null; FilesInfo.Text = Loc.T("Adicione arquivos ou pastas, ou solte-os aqui."); return; }
        FilesInfo.Text = Loc.T("Calculando…");
        var chosen = _paths.ToList();
        var cloud = CloudFolders.ToList();
        var (plan, special) = await Task.Run(() =>
        {
            var p = EraseRules.Plan(chosen, new WindowsLeftoverSystem(), _places, cloud, AppFolders, RealPath.Of);
            bool sp = p.Files.Take(20_000).Any(f =>
            {
                try { return File.GetAttributes(f) is var a && (a.HasFlag(FileAttributes.Compressed) || a.HasFlag(FileAttributes.SparseFile) || a.HasFlag(FileAttributes.Encrypted)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            });
            return (p, sp);
        });
        _plan = plan;
        _special = special;
        var lines = new List<string> { Loc.F("{0} em {1}. Pastas ficam só se esvaziarem.", Pt.N(plan.Files.Count, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(plan.Bytes)) };
        if (plan.Truncated) lines.Add(Loc.F("Lista cortada em {0} arquivos: escolha uma pasta menor.", EraseRules.MaxFiles));
        foreach (var b in plan.Blocked.Take(5)) lines.Add(Loc.F("Fora da lista: {0} ({1})", b.Path, Loc.T(b.Reason)));
        if (plan.Blocked.Count > 5) lines.Add(Loc.F("…e mais {0} fora da lista.", plan.Blocked.Count - 5));
        FilesInfo.Text = string.Join("\n", lines);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = Loc.T("Escolha os arquivos a apagar de vez") };
        if (dlg.ShowDialog(this) != true) return;
        AddPaths(dlg.FileNames);
        await RefreshPlanAsync();
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Escolha a pasta a apagar de vez"), Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        AddPaths(dlg.FolderNames);
        await RefreshPlanAsync();
    }

    private async void Files_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped) { AddPaths(dropped); await RefreshPlanAsync(); }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (!_paths.Contains(p, StringComparer.OrdinalIgnoreCase)) _paths.Add(p);
    }

    private async void RemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in FilesList.SelectedItems.Cast<string>().ToList()) _paths.Remove(p);
        await RefreshPlanAsync();
    }

    private async void ClearList_Click(object sender, RoutedEventArgs e) { _paths.Clear(); await RefreshPlanAsync(); }

    private async void Erase_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshPlanAsync();
        if (_plan is not { } plan || plan.Files.Count == 0) { Msg.Text = Loc.T("Não há arquivos para apagar."); return; }

        bool? solid = IsSolid(plan.Files[0]);
        var preview = _paths.Take(6).Select(p => "• " + p).ToList();
        if (_paths.Count > 6) preview.Add(Loc.F("…e mais {0}", _paths.Count - 6));
        string text = Loc.F("Apagar de vez {0} ({1})?", Pt.N(plan.Files.Count, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(plan.Bytes)) + "\n\n" + string.Join("\n", preview)
            + "\n\n" + Loc.T("Não vai para a Lixeira e não tem como voltar.") + "\n\n"
            + string.Join("\n", EraseRules.Warnings(solid, _special).Select(w => "• " + Loc.T(w)));
        if (_main.Simulate(Loc.T("Apagar de vez"), Loc.F("Apagaria de vez {0}.", Pt.N(plan.Files.Count, Loc.T("arquivo"), Loc.T("arquivos"))))) return;
        if (!Dialogs.Confirm(this, Loc.T("Apagar de vez"), text, Loc.T("Apagar de vez"), danger: true)) return;

        _cts = new CancellationTokenSource();
        SetBusy(true, cancellable: true);
        var progress = new Progress<EraseProgress>(p =>
        {
            Bar.Value = p.BytesTotal <= 0 ? 100 : Math.Min(100, p.BytesDone * 100.0 / p.BytesTotal);
            Msg.Text = Loc.F("Apagando {0} de {1}: {2}", Math.Min(p.FilesDone + 1, p.FilesTotal), p.FilesTotal, Path.GetFileName(p.Current));
        });
        EraseResult result;
        try { result = await Task.Run(() => SecureEraser.Erase(plan, progress, _cts.Token)); }
        finally { SetBusy(false, false); _cts.Dispose(); _cts = null; }

        var summary = new List<string> { Loc.F("{0} apagados de vez ({1}).", Pt.N(result.Erased, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(result.Bytes)) };
        if (result.Skipped > 0) summary.Add(Loc.F("{0} pulados.", result.Skipped));
        if (result.Failed > 0) summary.Add(Loc.F("{0} falharam (em uso ou sem permissão).", result.Failed));
        if (result.Cancelled) summary.Add(Loc.T("Cancelado."));
        if (result.NeedsFreeSpaceWipe) summary.Add(Loc.T("Havia arquivos comprimidos ou criptografados: limpe também o espaço livre."));
        Msg.Text = string.Join(" ", summary);
        ActionLog.Add(Loc.F("Apagado de vez: {0}", Pt.N(result.Erased, Loc.T("arquivo"), Loc.T("arquivos"))), string.Join(" ", summary.Skip(1)));
        if (result.Notes.Count > 0) Dialogs.ShowText(this, Loc.T("O que ficou"), string.Join("\n", result.Notes.Take(200)));

        foreach (var p in _paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList()) _paths.Remove(p);
        await RefreshPlanAsync();
        System.Media.SystemSounds.Asterisk.Play();
    }

    // ---------- unidades ----------

    private async Task RefreshDrivesAsync()
    {
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));

        string wipeSel = (WipeDrive.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        WipeDrive.Items.Clear();
        foreach (var d in _drives.Where(d => d.Ready && d.Kind is DriveKind.Fixed or DriveKind.Removable))
            WipeDrive.Items.Add(new ComboBoxItem { Tag = d.Letter, Content = DriveText(d) });
        SelectByTag(WipeDrive, wipeSel);

        string fmtSel = (FormatDrive.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FormatDrive.Items.Clear();
        foreach (var d in _drives.Where(d => DriveWipeRules.Check(d).Allowed))
            FormatDrive.Items.Add(new ComboBoxItem { Tag = d.Letter, Content = DriveText(d) });
        SelectByTag(FormatDrive, fmtSel);
        FormatBtn.IsEnabled = FormatDrive.Items.Count > 0;
        if (FormatDrive.Items.Count == 0) FormatInfo.Text = Loc.T("Nenhuma unidade externa disponível. Ligue o pen drive, cartão ou HD externo e abra esta tela de novo.");
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == tag) ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();
        box.SelectedItem = item;
    }

    private static string DriveText(DriveFacts d)
    {
        string kind = d.IsSystem ? Loc.T("Windows") : d.Kind == DriveKind.Removable ? Loc.T("removível") : Loc.T("fixa");
        string name = d.Label.Length > 0 ? $" {d.Label}" : "";
        return $"{d.Letter}:{name} · {kind} · {d.FileSystem} · {Loc.F("{0} livres de {1}", Units.Bytes(d.FreeBytes), Units.Bytes(d.TotalBytes))}";
    }

    private DriveFacts? Selected(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag is string letter ? _drives.FirstOrDefault(d => d.Letter == letter) : null;

    private void WipeDrive_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected(WipeDrive) is not { } d) { WipeInfo.Text = ""; return; }
        WipeInfo.Text = string.Join("\n", WipeRules.Warnings(d.IsSystem, d.SolidState).Select(w => "• " + Loc.T(w)));
    }

    private void FormatDrive_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected(FormatDrive) is not { } d) return;
        FormatInfo.Text = string.Join("\n", DriveWipeRules.Warnings(d).Select(w => "• " + Loc.T(w)));
    }

    // ---------- espaço livre ----------

    private async void Wipe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Selected(WipeDrive) is not { } d) { Msg.Text = Loc.T("Escolha uma unidade."); return; }
        long reserve = WipeRules.Reserve(d.IsSystem, d.TotalBytes);
        long target = WipeRules.Target(d.FreeBytes, reserve);
        if (target <= 0) { Msg.Text = Loc.T("Não há espaço livre para limpar."); return; }
        string text = Loc.F("Limpar o espaço livre de {0}:? Vão ser escritos cerca de {1} de dados aleatórios, que depois são apagados.", d.Letter, Units.Bytes(target)) + "\n\n"
            + string.Join("\n", WipeRules.Warnings(d.IsSystem, d.SolidState).Select(w => "• " + Loc.T(w)));
        if (_main.Simulate(Loc.T("Limpar o espaço livre"), Loc.F("Limparia o espaço livre de {0}:.", d.Letter))) return;
        if (!Dialogs.Confirm(this, Loc.T("Limpar o espaço livre"), text, Loc.T("Começar"), danger: false)) return;

        string tempDir = WipeRules.TempDir(d.Root, Path.GetTempPath(), Guid.NewGuid());
        _cts = new CancellationTokenSource();
        SetBusy(true, cancellable: true);
        var progress = new Progress<WipeProgress>(p =>
        {
            Bar.Value = WipeRules.Percent(p.Written, p.Target);
            Msg.Text = Loc.F("Escrevendo {0} de {1} ({2}%)", Units.Bytes(p.Written), Units.Bytes(p.Target), WipeRules.Percent(p.Written, p.Target));
        });
        WipeResult result;
        try { result = await Task.Run(() => FreeSpaceWiper.Wipe(d.Root, tempDir, reserve, null, progress, _cts.Token)); }
        finally { SetBusy(false, false); _cts.Dispose(); _cts = null; }

        string summary = result.Error.Length > 0 ? Loc.F("Parou com erro: {0}", result.Error)
            : result.Cancelled ? Loc.F("Cancelado. {0} escritos e já apagados.", Units.Bytes(result.Written))
            : Loc.F("Pronto: {0} de espaço livre sobrescritos e apagados.", Units.Bytes(result.Written));
        Msg.Text = summary;
        ActionLog.Add(Loc.F("Espaço livre limpo: {0}:", d.Letter), summary);
        await RefreshDrivesAsync();
        System.Media.SystemSounds.Asterisk.Play();
    }

    // ---------- unidade externa inteira ----------

    private async void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Selected(FormatDrive) is not { } chosen) { Msg.Text = Loc.T("Escolha uma unidade."); return; }
        // Lê de novo na hora: a unidade pode ter mudado de letra, saído ou virado outra desde que a tela abriu
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));
        if (_drives.FirstOrDefault(d => d.Letter == chosen.Letter) is not { } d || !DriveWipeRules.Check(d).Allowed || d.Label != chosen.Label || d.TotalBytes != chosen.TotalBytes)
        {
            Msg.Text = Loc.T("A unidade mudou desde que você a escolheu. Escolha de novo.");
            await RefreshDrivesAsync();
            return;
        }
        if (_main.Simulate(Loc.T("Apagar a unidade inteira"), Loc.F("Formataria {0}: por completo, com uma passada de zeros.", d.Letter))) return;
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Formatar uma unidade exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir"))) Host.RestartAsAdmin();
            return;
        }
        string message = Loc.F("Vai apagar TUDO de {0}: ({1}, {2}).", d.Letter, d.Label.Length > 0 ? d.Label : Loc.T("sem nome"), Units.Bytes(d.TotalBytes)) + "\n\n"
            + string.Join("\n", DriveWipeRules.Warnings(d).Select(w => "• " + Loc.T(w)));
        if (!Dialogs.ConfirmDriveWipe(this, d, message)) return;

        SetBusy(true, cancellable: false);
        Bar.IsIndeterminate = true;
        Msg.Text = Loc.T("Formatando… não desconecte a unidade nem feche o app.");
        var progress = new Progress<int>(p => { Bar.IsIndeterminate = false; Bar.Value = p; Msg.Text = Loc.F("Formatando… {0}%", p); });
        int exit;
        try { exit = await DriveFormatter.FormatAsync(d, progress); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException) { Msg.Text = ex.Message; SetBusy(false, false); return; }
        SetBusy(false, false);

        string summary = exit == 0 ? Loc.F("{0}: formatada por completo.", d.Letter) : Loc.F("O format.com terminou com o código {0}. A unidade pode ter ficado pela metade.", exit);
        Msg.Text = summary;
        ActionLog.Add(exit == 0 ? Loc.F("Unidade apagada: {0}:", d.Letter) : Loc.F("Falha ao apagar a unidade {0}:", d.Letter), summary);
        await RefreshDrivesAsync();
        System.Media.SystemSounds.Asterisk.Play();
    }
}
