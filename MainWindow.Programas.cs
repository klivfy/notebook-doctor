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
    private string? _progReveal; // programa a selecionar quando a lista for montada (Caçador, Processos, Inicialização)

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
        string? keep = _progReveal ?? SelectedProgram?.Id; // a lista é refeita: a seleção acompanha
        _progReveal = null;
        _progRows = list.Select(p => new ProgramRow(p)).ToList();
        foreach (var row in _progRows) LoadProgramIcon(row);
        ProgGrid.ItemsSource = _progRows;
        ApplyProgFilter();
        if (keep is not null && _progRows.FirstOrDefault(r => r.Program.Id == keep) is { } selected)
        {
            ProgGrid.SelectedItem = selected;
            ProgGrid.ScrollIntoView(selected);
        }
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
        var selected = ProgGrid.SelectedItems.OfType<ProgramRow>().Select(r => r.Program).ToList();
        if (selected.Count > 1) { await UninstallQueueAsync(selected); return; }
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
            ActionLog.Add("Desinstalação iniciada", Loc.F("{0} · ponto de restauração: {1}", p.Name, Loc.T(restore)));
            ProgMsg.Text = Loc.F("Desinstalador de {0} aberto. Siga as etapas na janela dele; o app espera ele terminar.", p.Name);
            var (gone, result) = await UninstallOneAsync(p, cmd);
            ProgMsg.Text = result;
            if (gone) await FindReviewRemoveAsync(fingerprint, afterUninstall: true);
        }
        finally { _progBusy = false; }
    }

    /// <summary>Roda o desinstalador, espera e confere se o programa saiu da lista. Ainda instalado: nada é apagado, a pasta ainda é dele.</summary>
    private async Task<(bool Gone, string Message)> UninstallOneAsync(InstalledProgram p, UninstallCommand cmd)
    {
        UninstallRun run;
        _progDone = false;
        ProgDoneBtn.Visibility = Visibility.Visible;
        try { run = await UninstallRunner.RunAsync(cmd, () => _progDone, CancellationToken.None); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return (false, Loc.T("Cancelado no aviso de permissão do Windows.")); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (false, Loc.F("Não foi possível abrir o desinstalador: {0}", ex.Message));
        }
        finally { ProgDoneBtn.Visibility = Visibility.Collapsed; }

        if (run.State == WaitState.TimedOut)
            return (false, Loc.F("O desinstalador de {0} continua aberto depois de 2 horas. Quando ele terminar, clique em Atualizar.", p.Name));
        _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
        ShowPrograms();
        if (_progKeys.Any(k => k.RegistryPath == p.Id))
        {
            string why = p.Kind == ProgramKind.Msi && run.ExitCode is int code ? MsiExit.Result(code) switch
            {
                UninstallResult.Canceled => Loc.T("A desinstalação foi cancelada."),
                UninstallResult.Busy => Loc.T("Outra instalação está em andamento; tente de novo quando ela terminar."),
                UninstallResult.RebootNeeded => Loc.T("O Windows pede para reiniciar para concluir."),
                _ => "",
            } : "";
            return (false, $"{Loc.F("{0} ainda aparece instalado, então nada foi apagado.", p.Name)} {why}".Trim());
        }
        ActionLog.Add("Programa desinstalado", p.Name);
        return (true, Loc.F("{0} foi desinstalado.", p.Name));
    }

    /// <summary>
    /// Leva 5.3: fila. Um ponto de restauração para todos; um de cada vez; quem falha não para os outros. As sobras
    /// saem sem perguntar (nível Moderado, com backup) ou ficam para a revisão no fim.
    /// </summary>
    private async Task UninstallQueueAsync(List<InstalledProgram> selected)
    {
        var queue = selected.Where(p => p.Kind != ProgramKind.Update && p.CanUninstall).ToList();
        var skipped = selected.Except(queue).Select(p => p.Name).ToList();
        if (queue.Count == 0) { ProgMsg.Text = Loc.T("Nenhum dos itens selecionados pode ser desinstalado por aqui."); return; }
        if (Simulate(Loc.F("Desinstalar {0}", Pt.N(queue.Count, Loc.T("programa"), Loc.T("programas"))), string.Join(", ", queue.Select(p => p.Name)))) return;
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        if (Dialogs.ConfirmQueue(this, queue.Select(p => p.Name).ToList(), skipped) is not { } choice) return;

        _progBusy = true;
        var problems = new List<string>();
        var review = new List<(ProgramFingerprint Fp, List<Leftover> Found)>();
        int gone = 0;
        try
        {
            string restore = await EnsureRestorePointAsync("Desinstalar varios programas");
            ActionLog.Add("Fila de desinstalação iniciada", Loc.F("{0} · ponto de restauração: {1}", Pt.N(queue.Count, "programa", "programas"), Loc.T(restore)));
            for (int i = 0; i < queue.Count; i++)
            {
                var p = queue[i];
                ProgMsg.Text = Loc.F("Desinstalando {0} de {1}: {2}…", i + 1, queue.Count, p.Name);
                if (p.Kind == ProgramKind.Store)
                {
                    if (await Task.Run(() => ProgramInventory.RemoveStoreApp(p.PackageFullName)) is { } error) problems.Add($"{p.Name}: {error}");
                    else { gone++; ActionLog.Add("App da Store removido", p.Name); }
                    continue;
                }
                var fp = ProgramFingerprint.Of(p, Directory.Exists, File.Exists);
                (UninstallCommand Command, bool Silent)? plan = choice.Silent
                    ? UninstallQueue.Plan(p, File.Exists, UninstallerVersionText(p))
                    : ProgramRules.CommandFor(p, File.Exists) is { } normal ? (normal, false) : null;
                if (plan is not { } pl) { problems.Add(Loc.F("{0} não informa como desinstalar.", p.Name)); continue; }
                var (ok, message) = await UninstallOneAsync(p, pl.Command);
                if (!ok) { problems.Add(message); continue; }
                gone++;
                var (found, findError) = await FindLeftoversAsync(fp);
                if (findError is not null) problems.Add(findError);
                else if (found is { Count: > 0 } && choice.AutoLeftovers)
                    await RemoveLeftoversAsync(fp.Name, found.Where(l => l.Selected && l.Level != LeftoverLevel.Advanced && (AppInfo.IsAdmin || !l.NeedsAdmin)).ToList());
                else if (found is { Count: > 0 }) review.Add((fp, found));
            }
            if (queue.Any(p => p.Kind == ProgramKind.Store))
            {
                _progExtras = await Task.Run(ProgramInventory.ReadStoreAndUpdates);
                ShowPrograms();
            }
            foreach (var (fp, found) in review) await ReviewAndRemoveAsync(fp.Name, found);
            ProgMsg.Text = $"{Loc.F("{0} de {1} desinstalados.", gone, queue.Count)} {string.Join(" ", problems)}".Trim();
        }
        finally { _progBusy = false; }
    }

    /// <summary>Texto da versão do desinstalador, para reconhecer o tipo (Inno Setup, NSIS…) e usar o modo silencioso certo.</summary>
    private static string UninstallerVersionText(InstalledProgram p)
    {
        if (ProgramRules.CommandFor(p, File.Exists) is not { } cmd || !File.Exists(cmd.File)) return "";
        try
        {
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(cmd.File);
            string text = string.Join(" ", v.Comments, v.FileDescription, v.ProductName, v.CompanyName);
            // Desinstaladores NSIS quase nunca dizem isso na versão, mas trazem o manifesto "Nullsoft.NSIS" logo no começo
            using var f = File.OpenRead(cmd.File);
            var head = new byte[Math.Min(f.Length, 512 * 1024)];
            int read = f.Read(head, 0, head.Length);
            return text + (System.Text.Encoding.ASCII.GetString(head, 0, read).Contains("Nullsoft.NSIS", StringComparison.Ordinal) ? " Nullsoft" : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private void ProgDone_Click(object sender, RoutedEventArgs e) => _progDone = true;

    /// <summary>Procura as sobras (Store e atualizações não deixam sobras para o app tratar). Devolve a lista ou o erro.</summary>
    private async Task<(List<Leftover>? Found, string? Error)> FindLeftoversAsync(ProgramFingerprint fp, InstallTrace? trace = null)
    {
        if (fp.Kind is not (ProgramKind.Program or ProgramKind.Msi)) return ([], null);
        ProgMsg.Text = Loc.F("Procurando sobras de {0} no disco e no registro…", fp.Name);
        var places = WindowsPlaces.Current(AppPaths.Dir);
        var others = AllPrograms(includeSystem: true).Where(o => o.Id != fp.Id).ToList();
        trace ??= TraceStore.ForKey(fp.RegistryPath); // leva 5.4b: o que a instalação monitorada criou entra junto
        try
        {
            return (await Task.Run(() =>
            {
                var sys = new WindowsLeftoverSystem();
                var ctx = new LeftoverContext(places, others, ScheduledTasks.List(), ShortcutScan.List(places));
                var scan = LeftoverScanner.Scan(fp, ctx, sys);
                return trace is null ? scan : TraceRules.Merge(TraceRules.ToLeftovers(trace, places, others, sys), scan);
            }), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return (null, Loc.F("A busca por sobras de {0} falhou: {1}", fp.Name, ex.Message));
        }
    }

    /// <summary>Procura, mostra a revisão e apaga o que a pessoa marcou.</summary>
    private async Task FindReviewRemoveAsync(ProgramFingerprint fp, bool afterUninstall, InstallTrace? trace = null)
    {
        var (found, error) = await FindLeftoversAsync(fp, trace);
        if (error is not null) { ProgMsg.Text = error; return; }
        if (found is not { Count: > 0 })
        {
            ProgMsg.Text = afterUninstall ? Loc.F("{0} foi desinstalado e não deixou sobras.", fp.Name) : Loc.F("Nada encontrado de {0}.", fp.Name);
            return;
        }
        await ReviewAndRemoveAsync(fp.Name, found);
    }

    private async Task ReviewAndRemoveAsync(string program, List<Leftover> found)
    {
        var chosen = Dialogs.ReviewLeftovers(this, program, found, AppInfo.IsAdmin);
        if (chosen is not { Count: > 0 }) { ProgMsg.Text = Loc.F("As sobras de {0} ficaram onde estavam.", program); return; }
        await RemoveLeftoversAsync(program, chosen);
    }

    /// <summary>Apaga com backup e registra em Ações, com desfazer.</summary>
    private async Task RemoveLeftoversAsync(string program, List<Leftover> items)
    {
        if (items.Count == 0) return;
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
        ActionLog.Add("Sobras removidas", Loc.F("{0}: {1} de {2}", program, ok, results.Count), "leftovers", id);
        string msg = Loc.F("{0} de {1} sobras apagadas, com backup.", ok, results.Count)
            + (pending > 0 ? " " + Loc.F("{0} saem no próximo reinício.", pending) : "")
            + (failed.Count > 0 ? " " + Loc.F("{0} não saíram: {1}", failed.Count, failed[0].Error) : "");
        ProgMsg.Text = msg;
        ShowSnack(Loc.T("Sobras removidas"), msg, Loc.T("Ver em Ações"), () => Tabs.SelectedIndex = TabActions);
    }

    private void ProgBackups_Click(object sender, RoutedEventArgs e) => Dialogs.ShowBackups(this, new BackupStore(AppPaths.Backups));

    // ---------- Leva 5.4: instalação monitorada ----------
    private async void ProgMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.IsAdmin)
        {
            ProgMsg.Text = Loc.T("O monitor de instalação lê o diário do disco e o registro do sistema: abra o app como administrador (Ajustes › Reabrir como administrador).");
            return;
        }
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        var pick = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Escolha o instalador"), Filter = $"{Loc.T("Instaladores")}|*.exe;*.msi" };
        if (pick.ShowDialog(this) != true) return;
        string installer = pick.FileName, file = Path.GetFileName(installer);
        if (Simulate(Loc.T("Instalar monitorando"), Loc.F("Fotografaria o registro, abriria {0} e anotaria o que ele mudou.", file))) return;
        if (!Dialogs.Confirm(this, Loc.T("Instalar monitorando?"),
                Loc.F("O app fotografa o registro, abre {0} e anota tudo o que a instalação criar. Depois, dá para desinstalar removendo exatamente isso.\n\nFeche os outros programas antes: o que eles mudarem durante a instalação também entra na anotação (você revisa antes de apagar qualquer coisa).\n\nA fotografia leva de alguns segundos a um minuto, antes e depois.", file),
                Loc.T("Começar"))) return;

        _progBusy = true;
        try
        {
            ProgMsg.Text = Loc.T("Fotografando o registro…");
            void Progress(int n) => Dispatcher.BeginInvoke(() => ProgMsg.Text = Loc.F("Fotografando o registro… {0:N0} chaves", n));
            var (before, usn) = await Task.Run(() => (RegistrySnapshotter.Take(TraceRules.Roots, Progress), UsnWatcher.Start()));
            var started = DateTime.Now;
            var cmd = installer.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? new UninstallCommand("msiexec.exe", $"/i \"{installer}\"")
                : new UninstallCommand(installer, "");
            _progDone = false;
            ProgDoneBtn.Visibility = Visibility.Visible;
            ProgMsg.Text = Loc.F("Instalador {0} aberto. Siga as etapas; o app espera ele terminar.", file);
            try { await UninstallRunner.RunAsync(cmd, () => _progDone, CancellationToken.None); } // a espera serve para instaladores também
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { ProgMsg.Text = Loc.T("Cancelado no aviso de permissão do Windows."); return; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                ProgMsg.Text = Loc.F("Não foi possível abrir o instalador: {0}", ex.Message);
                return;
            }
            finally { ProgDoneBtn.Visibility = Visibility.Collapsed; }

            ProgMsg.Text = Loc.T("Fotografando de novo e comparando…");
            var places = WindowsPlaces.Current(AppPaths.Dir);
            var trace = await Task.Run(() =>
            {
                var after = RegistrySnapshotter.Take(TraceRules.Roots, Progress);
                var changes = TraceRules.Diff(before, after, RegistrySnapshotter.ValueNames);
                return TraceRules.Build(installer, started, DateTime.Now, changes, usn.Changes(), places, AppPaths.Dir, RegistrySnapshotter.DisplayName);
            });
            TraceStore.Save(trace);
            ActionLog.Add("Instalação monitorada", Loc.F("{0}: {1} itens", trace.Name, trace.Count));
            _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
            if (trace.UninstallKey.Length > 0) _progReveal = trace.UninstallKey;
            ShowPrograms();
            ProgMsg.Text = Loc.F("{0}: anotados {1} pastas, {2} arquivos soltos, {3} chaves e {4} valores do registro. Veja em \"Instalações monitoradas…\".",
                                 trace.Name, trace.CreatedFolders.Count, trace.CreatedFiles.Count, trace.CreatedKeys.Count, trace.CreatedValues.Count)
                           + (usn.Volumes == 0 ? " " + Loc.T("O diário do disco não estava disponível: só o registro foi anotado.") : "");
        }
        finally { _progBusy = false; }
    }

    private async void ProgTraces_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.ShowTraces(this) is { } trace) await UninstallByTraceAsync(trace);
    }

    /// <summary>
    /// Leva 5.4b: com o programa ainda instalado, roda o desinstalador dele primeiro; depois junta o que a instalação
    /// criou (e ainda existe) com a busca de sobras comum, na mesma revisão.
    /// </summary>
    private async Task UninstallByTraceAsync(InstallTrace trace)
    {
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        if (_progKeys.Count == 0) _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
        var program = trace.UninstallKey.Length == 0 ? null : AllPrograms(includeSystem: true).FirstOrDefault(p => Paths.Same(p.Id, trace.UninstallKey));
        var cmd = program is null ? null : ProgramRules.CommandFor(program, File.Exists);
        if (Simulate(Loc.F("Desinstalar {0}", trace.Name), Loc.F("Rodaria o desinstalador (se ainda existir) e mostraria o que a instalação criou: {0}.", Pt.N(trace.Count, Loc.T("item"), Loc.T("itens"))))) return;
        string message = cmd is not null
            ? Loc.F("Desinstalar {0}?\n\nO desinstalador dele abre primeiro. Depois, o app mostra o que a instalação monitorada criou e ainda está no computador, junto com a busca de sobras, para você revisar.", trace.Name)
            : Loc.F("{0} não está mais na lista de programas. Procurar e apagar o que a instalação monitorada criou e ainda está no computador?\n\nVocê revisa tudo antes, e tudo vai para um backup.", trace.Name);
        if (!Dialogs.Confirm(this, Loc.T("Desinstalar pela instalação monitorada?"), message, Loc.T("Continuar"), danger: true)) return;

        _progBusy = true;
        try
        {
            ProgramFingerprint fp;
            if (program is not null && cmd is not null)
            {
                fp = ProgramFingerprint.Of(program, Directory.Exists, File.Exists);
                string restore = await EnsureRestorePointAsync("Desinstalar " + program.Name);
                ActionLog.Add("Desinstalação iniciada", Loc.F("{0} · ponto de restauração: {1}", program.Name, Loc.T(restore)));
                var (gone, result) = await UninstallOneAsync(program, cmd);
                ProgMsg.Text = result;
                if (!gone) return;
            }
            else fp = ProgramFingerprint.Manual(trace.Name, trace.CreatedFolders.FirstOrDefault()) with { RegistryPath = trace.UninstallKey };
            await FindReviewRemoveAsync(fp, afterUninstall: cmd is not null, trace);
            _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
            ShowPrograms();
        }
        finally { _progBusy = false; }
    }

    // ---------- Leva 5.3: remoção forçada e sobras de programa já removido ----------
    private async void ProgForced_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { } p) { ProgMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (p.Kind is ProgramKind.Store or ProgramKind.Update)
        {
            ProgMsg.Text = Loc.T("A remoção forçada vale para programas; apps da Store e atualizações saem pelo Windows.");
            return;
        }
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        if (!Dialogs.Confirm(this, Loc.T("Remoção forçada?"),
                Loc.F("Procurar e apagar o que é de {0} sem rodar o desinstalador dele?\n\nUse quando o desinstalador sumiu, está quebrado ou o programa foi removido só em parte. A pasta do programa e a entrada dele na lista também entram. Você revisa tudo antes, e tudo vai para um backup.", p.Name),
                Loc.T("Procurar sobras"), danger: true)) return;
        _progBusy = true;
        try
        {
            await FindReviewRemoveAsync(ProgramFingerprint.Of(p, Directory.Exists, File.Exists), afterUninstall: false);
            _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
            ShowPrograms();
        }
        finally { _progBusy = false; }
    }

    // Leva 7.1 (C14): atualizador de programas pelo WinGet
    private void ProgUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        var dlg = new UpdatesWindow(this);
        dlg.ShowDialog();
        if (dlg.UpdatedAny) ProgRefresh_Click(this, new RoutedEventArgs()); // versões novas na lista de instalados
    }

    private async void ProgFindLeftovers_Click(object sender, RoutedEventArgs e)
    {
        if (_progBusy) { ProgMsg.Text = Loc.T("Espere a desinstalação atual terminar."); return; }
        if (Dialogs.AskLeftoverTarget(this) is not { } target) return;
        var fp = ProgramFingerprint.Manual(target.Name, target.Folder);
        if (target.Folder is { } folder && LeftoverRules.IsProtectedPath(folder, WindowsPlaces.Current(AppPaths.Dir)))
        {
            ProgMsg.Text = Loc.T("Essa pasta é do Windows ou sua e nunca é apagada inteira. Escolha a pasta do programa.");
            return;
        }
        if (!ProgramNames.Usable(fp.Product) && fp.Dirs.Count == 0)
        {
            ProgMsg.Text = Loc.T("Esse nome é genérico demais para procurar sem a pasta do programa.");
            return;
        }
        if (AllPrograms(includeSystem: false).FirstOrDefault(p => ProgramNames.Same(ProgramNames.Product(p.Name), fp.Product)) is { } installed)
        {
            ProgMsg.Text = Loc.F("{0} ainda está instalado: use Desinstalar.", installed.Name);
            return;
        }
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        _progBusy = true;
        try { await FindReviewRemoveAsync(fp, afterUninstall: false); }
        finally { _progBusy = false; }
    }

    // ---------- Leva 5.3: apps da Store para todos os usuários e redefinir ----------
    private async void ProgStoreAllUsers_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { Kind: ProgramKind.Store } p) { ProgMsg.Text = Loc.T("Selecione um app da Microsoft Store."); return; }
        if (!p.CanUninstall) { ProgMsg.Text = Loc.F("{0}: o Windows marca este item como não removível.", p.Name); return; }
        if (!AppInfo.IsAdmin) { ProgMsg.Text = Loc.T("Remover de todas as contas exige administrador: Ajustes › Reabrir como administrador."); return; }
        if (Simulate(Loc.F("Remover {0}", p.Name), Loc.F("Removeria {0} de todas as contas deste computador e das contas criadas depois.", p.Name))) return;
        if (!Host.EnsureUnlocked(Loc.T("desinstalar programas"))) return; // B38
        if (!Dialogs.Confirm(this, Loc.T("Remover de todas as contas?"),
                Loc.F("Remover {0} de todas as contas deste computador, inclusive das que forem criadas depois?\n\nOs dados do app em cada conta são apagados junto. Dá para instalar de novo pela Microsoft Store.", p.Name),
                Loc.T("Remover"), danger: true)) return;
        ProgMsg.Text = Loc.F("Removendo {0}…", p.Name);
        if (await Task.Run(() => ProgramInventory.RemoveStoreAppAllUsers(p.PackageFullName)) is { } error)
        {
            ProgMsg.Text = Loc.F("O Windows não removeu {0}: {1}", p.Name, error);
            return;
        }
        ActionLog.Add("App da Store removido de todas as contas", p.Name);
        _progExtras = await Task.Run(ProgramInventory.ReadStoreAndUpdates);
        ShowPrograms();
        ProgMsg.Text = Loc.F("{0} foi removido.", p.Name);
    }

    private async void ProgStoreReset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram is not { Kind: ProgramKind.Store } p) { ProgMsg.Text = Loc.T("Selecione um app da Microsoft Store."); return; }
        if (Simulate(Loc.F("Redefinir {0}", p.Name), Loc.F("Apagaria as configurações e os dados locais de {0}, como se ele tivesse acabado de ser instalado.", p.Name))) return;
        if (!Dialogs.Confirm(this, Loc.T("Redefinir app?"),
                Loc.F("Redefinir {0}?\n\nAs configurações e os dados do app neste computador voltam ao padrão, como se ele tivesse acabado de ser instalado. Ajuda quando o app não abre ou trava.", p.Name),
                Loc.T("Redefinir"), danger: true)) return;
        ProgMsg.Text = Loc.F("Redefinindo {0}…", p.Name);
        if (await Task.Run(() => ProgramInventory.ResetStoreApp(p.PackageFullName)) is { } error)
        {
            if (error.Contains("Reset-AppxPackage", StringComparison.OrdinalIgnoreCase))
            {
                // Versões mais antigas do Windows 10 não têm o comando: a tela de Configurações faz o mesmo
                AppInfo.OpenShell("ms-settings:appsfeatures");
                ProgMsg.Text = Loc.F("Este Windows não redefine apps pelo PowerShell. Em Configurações, abra {0} › Opções avançadas › Redefinir.", p.Name);
            }
            else ProgMsg.Text = Loc.F("O Windows não redefiniu {0}: {1}", p.Name, error);
            return;
        }
        ActionLog.Add("App da Store redefinido", p.Name);
        ProgMsg.Text = Loc.F("{0} foi redefinido.", p.Name);
    }

    // ---------- Leva 5.3: de quem é este programa? (Caçador, atalho solto na lista, Processos, Inicialização) ----------
    private void ProgHunt_Click(object sender, RoutedEventArgs e) =>
        ProgMsg.Text = Loc.T("Segure o botão do mouse em \"Mirar\", arraste até a janela do programa e solte.");

    private void ProgHunt_Down(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        Mouse.OverrideCursor = Cursors.Cross;
        ProgMsg.Text = Loc.T("Solte o botão do mouse sobre a janela do programa.");
        e.Handled = true;
    }

    private async void ProgHunt_Up(object sender, MouseButtonEventArgs e)
    {
        var target = (UIElement)sender;
        if (!target.IsMouseCaptured) return;
        target.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;
        int pid = Native.ProcessUnderCursor();
        if (pid == 0 || pid == Environment.ProcessId) { ProgMsg.Text = Loc.T("Solte sobre a janela de outro programa."); return; }
        if (ProcessIcons.PathOfPid(pid) is not { } exe)
        {
            ProgMsg.Text = Loc.T("O Windows não deixou ver qual programa é esse. Tente com o app aberto como administrador.");
            return;
        }
        await RevealProgramForAsync(exe);
    }

    private async void ProgGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        string file = files[0];
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            try { file = StartMenu.ReadTarget(file); }
            catch (System.Runtime.InteropServices.COMException) { }
        await RevealProgramForAsync(file);
    }

    /// <summary>Mostra na aba Programas o programa dono do arquivo, já selecionado, para desinstalar ou ver as propriedades.</summary>
    private async Task RevealProgramForAsync(string file)
    {
        Tabs.SelectedIndex = TabPrograms;
        if (_progKeys.Count == 0) _progKeys = await Task.Run(ProgramInventory.ReadUninstallKeys);
        var owner = UninstallQueue.OwnerOf(file, AllPrograms(includeSystem: true), WindowsPlaces.Current(AppPaths.Dir), Directory.Exists, File.Exists);
        if (owner is null)
        {
            ProgMsg.Text = Loc.F("{0} não pertence a um programa instalado (pode ser do Windows ou um programa portátil).", Path.GetFileName(file));
            return;
        }
        if (owner.IsSystem) ProgSystemBox.IsChecked = true;
        if (!ProgramRules.Matches(owner, ProgramRules.ParseFilter((ProgFilter.SelectedItem as ComboBoxItem)?.Tag as string))) ProgFilter.SelectedIndex = 3; // Tudo
        ProgSearch.Text = "";
        _progReveal = owner.Id;
        ShowPrograms();
        ProgGrid.Focus();
        ProgMsg.Text = Loc.F("{0} é do programa {1}. Use Desinstalar ou o menu do botão direito.", Path.GetFileName(file), owner.Name);
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
