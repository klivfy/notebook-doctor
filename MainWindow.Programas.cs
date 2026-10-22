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
    private bool _progLoaded, _progBusy, _progDone;

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
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        string message = Loc.F("Abrir o desinstalador de:\n{0}\n\nO desinstalador do próprio programa vai abrir e pedir confirmação. Quando ele terminar, o app procura o que sobrou (pastas, registro, atalhos) e mostra antes de apagar.", p.Name);
        // As sobras de um programa de todos os usuários ficam em Arquivos de Programas e HKLM: sem administrador, não dá para apagar
        if (p.MachineWide == true && !AppInfo.IsAdmin)
            message += "\n\n" + Loc.T("Dica: abra o app como administrador antes (Ajustes › Reabrir como administrador) para apagar também as sobras em Arquivos de Programas e no registro do sistema.");
        if (!Dialogs.Confirm(this, Loc.T("Desinstalar programa?"), message, Loc.T("Abrir desinstalador"), danger: true)) return;

        _progBusy = true;
        try
        {
            // O retrato vem antes: depois, a chave do registro some e não diz mais onde o programa estava
            var fingerprint = ProgramFingerprint.Of(p, Directory.Exists, File.Exists);
            ProgMsg.Text = Loc.T("Preparando…");
            string restore = await EnsureRestorePointAsync("Desinstalar " + p.Name);
            UninstallRun run;
            _progDone = false;
            ProgDoneBtn.Visibility = Visibility.Visible;
            ProgMsg.Text = Loc.F("Desinstalador de {0} aberto. Siga as etapas na janela dele; o app espera ele terminar.", p.Name);
            try { run = await UninstallRunner.RunAsync(cmd, () => _progDone, CancellationToken.None); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { ProgMsg.Text = Loc.T("Cancelado no aviso de permissão do Windows."); return; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                ProgMsg.Text = Loc.F("Não foi possível abrir o desinstalador: {0}", ex.Message);
                return;
            }
            finally { ProgDoneBtn.Visibility = Visibility.Collapsed; }
            ActionLog.Add("Desinstalador executado", $"{p.Name} · ponto de restauração: {restore}");

            if (run.State == WaitState.TimedOut)
            {
                ProgMsg.Text = Loc.F("O desinstalador de {0} continua aberto depois de 2 horas. Quando ele terminar, clique em Atualizar.", p.Name);
                return;
            }
            _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
            ShowPrograms();
            if (_progKeys.Any(k => k.RegistryPath == p.Id))
            {
                // Ainda instalado (cancelado ou incompleto): nada é apagado, a pasta ainda é do programa
                string why = p.Kind == ProgramKind.Msi && run.ExitCode is int code ? MsiExit.Result(code) switch
                {
                    UninstallResult.Canceled => Loc.T("A desinstalação foi cancelada."),
                    UninstallResult.Busy => Loc.T("Outra instalação está em andamento; tente de novo quando ela terminar."),
                    UninstallResult.RebootNeeded => Loc.T("O Windows pede para reiniciar para concluir."),
                    _ => "",
                } : "";
                ProgMsg.Text = $"{Loc.F("{0} ainda aparece instalado, então nada foi apagado.", p.Name)} {why}".Trim();
                return;
            }
            ActionLog.Add("Programa desinstalado", p.Name);
            await ScanLeftoversAsync(fingerprint);
        }
        finally { _progBusy = false; }
    }

    private void ProgDone_Click(object sender, RoutedEventArgs e) => _progDone = true;

    /// <summary>Procura as sobras do programa que acabou de sair e mostra a revisão. Store e atualizações não deixam sobras para o app tratar.</summary>
    private async Task ScanLeftoversAsync(ProgramFingerprint fp)
    {
        if (fp.Kind is not (ProgramKind.Program or ProgramKind.Msi)) return;
        ProgMsg.Text = Loc.F("{0} foi desinstalado. Procurando sobras no disco e no registro…", fp.Name);
        var places = WindowsPlaces.Current(AppPaths.Dir);
        var others = AllPrograms(includeSystem: true);
        List<Leftover> found;
        try
        {
            found = await Task.Run(() =>
            {
                var ctx = new LeftoverContext(places, others, ScheduledTasks.List(), ShortcutScan.List(places));
                return LeftoverScanner.Scan(fp, ctx, new WindowsLeftoverSystem());
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ProgMsg.Text = Loc.F("{0} foi desinstalado, mas a busca por sobras falhou: {1}", fp.Name, ex.Message);
            return;
        }
        if (found.Count == 0) { ProgMsg.Text = Loc.F("{0} foi desinstalado e não deixou sobras.", fp.Name); return; }
        var chosen = Dialogs.ReviewLeftovers(this, fp.Name, found, AppInfo.IsAdmin);
        if (chosen is not { Count: > 0 }) { ProgMsg.Text = Loc.F("{0} foi desinstalado. As sobras ficaram onde estavam.", fp.Name); return; }
        await RemoveLeftoversAsync(fp.Name, chosen);
    }

    /// <summary>Apaga com backup e registra em Ações, com desfazer.</summary>
    private async Task RemoveLeftoversAsync(string program, List<Leftover> items)
    {
        if (Simulate(Loc.F("Apagar sobras de {0}", program), Loc.F("Apagaria {0}, guardando tudo num backup.", Pt.N(items.Count, Loc.T("item"), Loc.T("itens"))))) return;
        ProgMsg.Text = Loc.T("Guardando o backup e apagando…");
        var store = new BackupStore(AppPaths.Backups);
        var (id, results) = await Task.Run(() =>
        {
            using var session = store.Begin(program);
            return (session.Id, LeftoverRemover.Remove(items, session, AppInfo.IsAdmin));
        });
        int ok = results.Count(r => r.Ok), pending = results.Count(r => r.PendingReboot);
        var failed = results.Where(r => !r.Ok).ToList();
        ActionLog.Add("Sobras removidas", $"{program}: {ok} de {results.Count}", "leftovers", id);
        string msg = Loc.F("{0} de {1} sobras apagadas, com backup.", ok, results.Count)
            + (pending > 0 ? " " + Loc.F("{0} saem no próximo reinício.", pending) : "")
            + (failed.Count > 0 ? " " + Loc.F("{0} não saíram: {1}", failed.Count, failed[0].Error) : "");
        ProgMsg.Text = msg;
        ShowSnack(Loc.T("Sobras removidas"), msg, Loc.T("Ver em Ações"), () => Tabs.SelectedIndex = TabActions);
    }

    private void ProgBackups_Click(object sender, RoutedEventArgs e) => Dialogs.ShowBackups(this, new BackupStore(AppPaths.Backups));

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
