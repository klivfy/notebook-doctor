using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Uma linha da aba Programas. O ícone e o tamanho medido na pasta chegam depois, sem travar a tela.</summary>
public sealed class ProgramRow(InstalledProgram program) : INotifyPropertyChanged
{
    public InstalledProgram Program { get; } = program;
    public string Name => Program.Name;
    public string Subtitle => Program.Publisher.Length > 0 ? Program.Publisher : Program.KindText;
    public string Version => Program.Version;
    public string SizeText => Program.SizeText;
    public long Bytes => Program.Bytes;
    public DateTime? InstallDate => Program.InstallDate;
    public string InstallDateText => Program.InstallDateText;
    public string KindText => Program.KindText;
    public string ScopeText => Program.ScopeText;
    public string BitsText => Program.BitsText;

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; Raise(nameof(Icon)); Raise(nameof(HasIcon)); }
    }
    public bool HasIcon => _icon is not null;

    public void SizeUpdated() { Raise(nameof(SizeText)); Raise(nameof(Bytes)); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Aba Programas (Fase 5): programas instalados, apps da Microsoft Store e atualizações do Windows.</summary>
public partial class MainWindow
{
    private List<UninstallKey> _progKeys = new();
    private (List<StorePackage> Store, List<Hotfix> Updates)? _progExtras; // vem do PowerShell, depois da lista do registro
    private readonly Dictionary<string, long> _progMeasured = new(); // tamanhos medidos na pasta nesta sessão, por Id
    private List<ProgramRow> _progRows = new();
    private CancellationTokenSource? _progCts;
    private bool _progLoaded;

    private InstalledProgram? SelectedProgram => (ProgGrid.SelectedItem as ProgramRow)?.Program;

    /// <summary>Registro primeiro (rápido), depois Store e atualizações (PowerShell) e, por fim, as pastas sem tamanho informado.</summary>
    private async Task LoadProgramsAsync()
    {
        _progCts?.Cancel();
        var cts = _progCts = new CancellationTokenSource();
        _progLoaded = true;
        ProgMsg.Text = Loc.T("Lendo os programas instalados…");
        try
        {
            _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys, cts.Token);
            ShowPrograms();
            var extras = await Task.Run(ProgramInventory.ReadStoreAndUpdates, cts.Token);
            if (cts.IsCancellationRequested) return;
            _progExtras = extras;
            ShowPrograms();
            await MeasureProgramsAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private List<InstalledProgram> AllPrograms(bool includeSystem)
    {
        var items = _progKeys.Select(k => ProgramRules.FromKey(k, includeSystem))
            .Concat(_progExtras?.Store.Select(s => ProgramRules.FromStore(s, includeSystem)) ?? [])
            .Concat(_progExtras?.Updates.Select(ProgramRules.FromHotfix) ?? [])
            .OfType<InstalledProgram>();
        var list = ProgramRules.Dedupe(items);
        foreach (var p in list)
            if (p.SizeSource == SizeSource.None && _progMeasured.TryGetValue(p.Id, out long bytes)) { p.Bytes = bytes; p.SizeSource = SizeSource.Folder; }
        return list;
    }

    private void ShowPrograms()
    {
        var filter = ProgramRules.ParseFilter((ProgFilter.SelectedItem as ComboBoxItem)?.Tag as string);
        var list = AllPrograms(ProgSystemBox.IsChecked == true).Where(p => ProgramRules.Matches(p, filter))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _progRows = list.Select(p => new ProgramRow(p)).ToList();
        foreach (var row in _progRows) LoadProgramIcon(row);
        ProgGrid.ItemsSource = _progRows;
        ApplyProgFilter();
        bool waiting = _progExtras is null && (filter is ProgramFilter.Store or ProgramFilter.Updates or ProgramFilter.All);
        ProgMsg.Text = waiting ? Loc.T("Lendo os apps da Microsoft Store e as atualizações do Windows…")
            : list.Count == 0 ? Loc.T("Nada para mostrar com este filtro.")
            : Loc.T("Selecione um item para desinstalar, abrir a pasta ou ver as propriedades.");
    }

    private void LoadProgramIcon(ProgramRow row)
    {
        if (ProgramRules.IconSource(row.Program.DisplayIcon) is not { } icon) return;
        row.Icon = ProcessIcons.ForFile(icon.Path, icon.Index, () => Dispatcher.BeginInvoke(() => row.Icon = ProcessIcons.ForFile(icon.Path, icon.Index)));
    }

    /// <summary>Mede a pasta dos programas cujo instalador não informa o tamanho. Fica guardado até fechar o app.</summary>
    private async Task MeasureProgramsAsync(CancellationToken ct)
    {
        try
        {
            await ProgramInventory.MeasureAsync(AllPrograms(includeSystem: false),
                p => Dispatcher.BeginInvoke(() =>
                {
                    _progMeasured[p.Id] = p.Bytes;
                    if (_progRows.FirstOrDefault(r => r.Program.Id == p.Id) is not { } row || row.Program.SizeSource != SizeSource.None) return;
                    row.Program.Bytes = p.Bytes;
                    row.Program.SizeSource = SizeSource.Folder;
                    row.SizeUpdated();
                    UpdateTotals(ProgGrid);
                }),
                (i, n) => Dispatcher.BeginInvoke(() => ProgMsg.Text = Loc.F("Medindo as pastas dos programas sem tamanho informado: {0} de {1}…", i, n)),
                ct);
            ProgMsg.Text = Loc.T("Pronto. \"—\" no tamanho: o instalador não informa e a pasta do programa não foi encontrada.");
        }
        catch (OperationCanceledException) { }
    }

    private void ProgFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && e.Source == ProgFilter && _progLoaded) ShowPrograms();
    }

    private void ProgSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_progLoaded) ShowPrograms();
    }

    private void ApplyProgFilter() => ApplyFilter(ProgGrid, ProgSearch.Text, o => o is ProgramRow r ? $"{r.Name} {r.Program.Publisher} {r.Version}" : "");
    private void ProgSearch_Changed(object sender, TextChangedEventArgs e) => ApplyProgFilter();
    private async void ProgRefresh_Click(object sender, RoutedEventArgs e) => await LoadProgramsAsync();

    private void ProgGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedProgram is not null) ProgProperties_Click(sender, e);
    }

    /// <summary>M12: Enter abre as propriedades e Delete desinstala, como em Processos (sempre com confirmação).</summary>
    private void ProgGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (SelectedProgram is null) return;
        if (e.Key == Key.Enter) { ProgProperties_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Delete) { ProgUninstall_Click(sender, e); e.Handled = true; }
    }

    // ---------- Desinstalar ----------
    private async void ProgUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (p.Kind == ProgramKind.Update)
        {
            AppInfo.OpenShell("ms-settings:windowsupdate-history");
            ProgMsg.Text = Loc.T("Atualizações do Windows são removidas pelo próprio Windows: Histórico de atualizações › Desinstalar atualizações.");
            return;
        }
        if (!p.CanUninstall) { ProgMsg.Text = Loc.F("{0}: o Windows marca este item como não removível.", p.Name); return; }
        if (p.Kind == ProgramKind.Store) { await RemoveStoreAppAsync(p); return; }

        var cmd = ProgramRules.CommandFor(p, File.Exists);
        if (cmd is null) { ProgMsg.Text = Loc.F("{0} não informa como desinstalar.", p.Name); return; }
        if (Simulate(Loc.F("Desinstalar {0}", p.Name), Loc.F("Abriria o desinstalador do programa: {0} {1}", cmd.File, cmd.Arguments))) return;
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (!Dialogs.Confirm(this, Loc.T("Desinstalar programa?"),
                Loc.F("Abrir o desinstalador de:\n{0}\n\nO desinstalador do próprio programa vai abrir e pedir confirmação. Quando ele terminar, a lista se atualiza.", p.Name),
                Loc.T("Abrir desinstalador"), danger: true)) return;

        ProgMsg.Text = Loc.T("Preparando…");
        string restore = await EnsureRestorePointAsync("Desinstalar " + p.Name);
        System.Diagnostics.Process? proc;
        try
        {
            // ShellExecute: o desinstalador que exige administrador mostra o aviso do Windows (UAC)
            proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd.File, cmd.Arguments) { UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { ProgMsg.Text = Loc.T("Cancelado no aviso de permissão do Windows."); return; }
        catch (Exception ex) { ProgMsg.Text = Loc.F("Não foi possível abrir o desinstalador: {0}", ex.Message); return; }
        ActionLog.Add("Desinstalador aberto", $"{p.Name} · ponto de restauração: {restore}");
        ProgMsg.Text = Loc.F("Desinstalador de {0} aberto. Siga as etapas na janela dele.", p.Name);
        if (proc is null) return;
        using (proc)
        {
            try { await proc.WaitForExitAsync(); } catch (Exception) { return; }
        }

        _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
        ShowPrograms();
        bool gone = !_progKeys.Any(k => k.RegistryPath == p.Id);
        if (gone) ActionLog.Add("Programa desinstalado", p.Name);
        ProgMsg.Text = gone ? Loc.F("{0} foi desinstalado.", p.Name)
            : Loc.F("{0} ainda aparece na lista. O desinstalador pode continuar em outra janela; clique em Atualizar quando ele terminar.", p.Name);
    }

    private async Task RemoveStoreAppAsync(InstalledProgram p)
    {
        if (Simulate(Loc.F("Remover {0}", p.Name), Loc.F("Removeria o app {0} da Microsoft Store para este usuário (pacote {1}).", p.Name, p.PackageFullName))) return;
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (!Dialogs.Confirm(this, Loc.T("Remover app?"),
                Loc.F("Remover {0} deste usuário?\n\nOs dados do app neste computador são apagados junto. Dá para instalar de novo pela Microsoft Store.", p.Name),
                Loc.T("Remover"), danger: true)) return;
        ProgMsg.Text = Loc.F("Removendo {0}…", p.Name);
        string? error = await Task.Run(() => ProgramInventory.RemoveStoreApp(p.PackageFullName));
        if (error is not null) { ProgMsg.Text = Loc.F("O Windows não removeu {0}: {1}", p.Name, error); return; }
        ActionLog.Add("App da Store removido", p.Name);
        _progExtras = await Task.Run(ProgramInventory.ReadStoreAndUpdates);
        ShowPrograms();
        ProgMsg.Text = Loc.F("{0} foi removido.", p.Name);
    }

    // ---------- Pasta, registro, internet e propriedades ----------
    private void ProgOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (ProgramRules.FolderOf(p, Directory.Exists, File.Exists) is { } dir) AppInfo.OpenShell(dir);
        else ProgMsg.Text = Loc.F("{0} não informa a pasta de instalação.", p.Name);
    }

    private void ProgRegistry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (p.RegistryPath.Length == 0) { ProgMsg.Text = Loc.T("Apps da Store e atualizações do Windows não têm chave de desinstalação no registro."); return; }
        if (AppPaths.Demo) { ProgMsg.Text = Loc.T("Indisponível no modo demonstração."); return; } // B26: a demonstração não grava no registro
        try
        {
            // O Editor do Registro abre na última chave vista; "-m" abre outra janela mesmo com ele já aberto
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                k.SetValue("LastKey", ProgramRules.RegeditLastKey(k.GetValue("LastKey") as string, p.RegistryPath));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("regedit.exe", "-m") { UseShellExecute = true });
            ProgMsg.Text = Loc.T("O Editor do Registro abre na chave do programa. Só consulte: mudar valores ali pode quebrar a desinstalação.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { ProgMsg.Text = Loc.T("Cancelado no aviso de permissão do Windows."); }
        catch (Exception ex) { ProgMsg.Text = Loc.F("Não foi possível abrir o Editor do Registro: {0}", ex.Message); }
    }

    private void ProgSearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        AppInfo.OpenShell("https://www.bing.com/search?q=" + Uri.EscapeDataString($"{p.Name} {p.Publisher}".Trim()));
    }

    private void ProgProperties_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        Dialogs.ShowText(this, p.Name, ProgramRules.Describe(p));
    }

    // ---------- Exportar ----------
    private void ProgExport_Click(object sender, RoutedEventArgs e)
    {
        var visible = ProgGrid.Items.OfType<ProgramRow>().Select(r => r.Program).ToList();
        if (visible.Count == 0) { ProgMsg.Text = Loc.T("A lista está vazia: nada para exportar."); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Loc.F("Programas instalados {0}", DateTime.Now.ToString("yyyy-MM-dd")),
            Filter = $"{Loc.T("Planilha (CSV)")}|*.csv|{Loc.T("Página (HTML)")}|*.html",
        };
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName;
        try
        {
            bool html = Path.GetExtension(path).StartsWith(".htm", StringComparison.OrdinalIgnoreCase);
            string text = html ? ProgramExport.Html(visible, Environment.MachineName, DateTime.Now)
                               : ProgramExport.Csv(visible, System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator);
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // com BOM, o Excel lê os acentos
            ShowSnack(Loc.F("Lista salva: {0}", Path.GetFileName(path)), Pt.N(visible.Count, Loc.T("item"), Loc.T("itens")), Loc.T("Abrir"), () => AppInfo.OpenShell(path));
        }
        catch (Exception ex) { ProgMsg.Text = Loc.F("Não foi possível salvar: {0}", ex.Message); }
    }
}
