using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class UpdateRow : CheckRow
{
    public UpdateCandidate Item { get; }
    public UpdateState State { get; private set; }

    public UpdateRow(UpdateCandidate item, UpdateState state) { Item = item; State = state; }

    public string Name => Item.Name;
    public string Id => Item.Id;
    public string Installed => Item.Installed;
    public string Available => Item.Available;
    public bool CanSelect => State == UpdateState.Available;
    public string StateText => State switch
    {
        UpdateState.Skipped => Loc.T("Versão pulada"),
        UpdateState.Paused => Loc.T("Pausado"),
        _ => Loc.T("Atualização disponível"),
    };
}

/// <summary>
/// Leva 7.1 (C14): atualiza programas pelo WinGet. A pessoa vê a lista, escolhe, e aceita os termos na tela; o app nunca
/// passa o aceite sozinho. Atualizar não tem desfazer, e cada resultado vai para a aba Ações.
/// </summary>
public partial class UpdatesWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<UpdateRow> _rows = [];
    private List<UpdateCandidate> _found = [];
    private int _hidden;
    private bool _busy, _stop, _scanned;

    public bool UpdatedAny { get; private set; }

    public UpdatesWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Grid.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Grid.SelectionChanged += (_, _) => UpdateInfo();
        Loaded += (_, _) => UpdateInfo();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere a atualização atual terminar."); return; }
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true; // um instalador no meio do caminho não pode ficar sem quem acompanhe
        base.OnClosing(e);
    }

    private UpdateRow? Current => Grid.SelectedItem as UpdateRow;

    // ---------- lista ----------

    private void Render()
    {
        var s = Host.Settings;
        var ordered = ProgramUpdateRules.Order(_found, s.UpdateSkipped, s.UpdatePaused);
        _hidden = ordered.Count(t => t.State != UpdateState.Available);
        _rows.Clear();
        foreach (var (item, state) in ordered)
        {
            if (state != UpdateState.Available && ShowHidden.IsChecked != true) continue;
            var row = new UpdateRow(item, state);
            row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(CheckRow.Selected)) UpdateInfo(); };
            _rows.Add(row);
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (!_scanned) { Info.Text = Loc.T("Clique em “Procurar atualizações”."); return; }
        int available = _rows.Count(r => r.CanSelect);
        int marked = _rows.Count(r => r.Selected);
        string text = available == 0
            ? Loc.T("Tudo atualizado.")
            : Loc.F("{0} com versão nova · {1} marcados.", available, marked);
        if (_hidden > 0 && ShowHidden.IsChecked != true) text += " " + Loc.F("{0} escondidos (pulados ou pausados).", _hidden);
        Info.Text = text;
        PauseBtn.Content = Current is { State: UpdateState.Paused } ? Loc.T("Retomar este programa") : Loc.T("Pausar este programa");
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e) => Render();
    private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows.Where(r => r.CanSelect)) r.Selected = true; }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = false; }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || Current is not { CanSelect: true } row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    // ---------- WinGet ----------

    /// <summary>A fonte do WinGet exige que a pessoa aceite os termos dela. O app só mostra a pergunta; nunca aceita sozinho.</summary>
    private bool EnsureSourceTerms()
    {
        var s = Host.Settings;
        if (s.WingetSourceTerms) return true;
        bool ok = Dialogs.Confirm(this, Loc.T("Termos do catálogo do WinGet"),
            Loc.T("Para consultar as versões, o WinGet usa o catálogo público dele e exige que você aceite os termos de uso da fonte (https://aka.ms/winget-agreements). O Notebook Doctor não aceita nada em seu nome. Se não aceitar, a busca não é feita."),
            Loc.T("Li e aceito"));
        if (!ok) return false;
        s.WingetSourceTerms = true;
        s.Save();
        return true;
    }

    private async Task<bool> EnsureWingetAsync()
    {
        if (await WingetRunner.AvailableAsync()) return true;
        if (Dialogs.Confirm(this, Loc.T("WinGet não encontrado"),
                Loc.T("O WinGet vem no “Instalador de Aplicativo”, da Microsoft. Ele não está instalado ou não responde neste computador. Abrir a página dele na Microsoft Store?"),
                Loc.T("Abrir a Store")))
        {
            try { Process.Start(new ProcessStartInfo(WingetRunner.AppInstallerStore) { UseShellExecute = true }); }
            catch (Win32Exception) { Msg.Text = Loc.T("Não consegui abrir a Microsoft Store."); }
        }
        return false;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ScanBtn.IsEnabled = RunBtn.IsEnabled = false;
        Msg.Text = "";
        try
        {
            if (AppPaths.Demo) { _found = DemoUpdates(); _scanned = true; Render(); return; }
            if (!await EnsureWingetAsync() || !EnsureSourceTerms()) return;
            Msg.Text = Loc.T("Consultando o WinGet…");
            UpdateScan scan;
            try { scan = await UpdateEngines.Shared.ListUpgradesAsync(Host.Settings.WingetSourceTerms, CancellationToken.None); }
            catch (UpdateEngineUnavailableException) { Msg.Text = Loc.T("O WinGet não respondeu."); return; }
            catch (Win32Exception) { Msg.Text = Loc.T("O WinGet não respondeu."); return; }
            if (scan.TimedOut) { Msg.Text = Loc.T("O WinGet demorou demais. Tente de novo."); return; }
            var list = scan.List;
            if (list.Items.Count == 0 && scan.ExitCode != 0 && !scan.HasTable)
            {
                // Sem tabela: não foi "nada a atualizar", foi erro
                Msg.Text = Loc.F("O WinGet terminou com o código 0x{0:X8}.", scan.ExitCode);
                return;
            }
            _found = list.Items.ToList();
            _scanned = true;
            Render();
            Msg.Text = list.Truncated > 0
                ? Loc.F("{0} linhas vieram cortadas pelo WinGet e ficaram de fora.", list.Truncated)
                : "";
        }
        finally { _busy = false; ScanBtn.IsEnabled = RunBtn.IsEnabled = true; }
    }

    private static List<UpdateCandidate> DemoUpdates() =>
    [
        new("Mozilla Firefox", "Mozilla.Firefox", "139.0", "140.0.2", "winget"),
        new("7-Zip", "7zip.7zip", "24.07", "24.09", "winget"),
        new("VLC media player", "VideoLAN.VLC", "3.0.20", "3.0.21", "winget"),
        new("Notepad++", "Notepad++.Notepad++", "8.6.9", "8.7.1", "winget"),
    ];

    // ---------- por programa ----------

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { CanSelect: true } row) { Msg.Text = Loc.T("Selecione um programa com atualização disponível."); return; }
        var s = Host.Settings;
        s.UpdateSkipped = ProgramUpdateRules.WithSkip(s.UpdateSkipped, row.Id, row.Available);
        s.Save();
        Msg.Text = Loc.F("A versão {0} de {1} não será mais oferecida. Uma versão mais nova voltará a aparecer.", row.Available, row.Name);
        Render();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um programa na lista."); return; }
        var s = Host.Settings;
        bool pause = row.State != UpdateState.Paused;
        s.UpdatePaused = ProgramUpdateRules.WithPause(s.UpdatePaused, row.Id, pause);
        s.Save();
        Msg.Text = pause
            ? Loc.F("{0}: atualizações pausadas.", row.Name)
            : Loc.F("{0}: atualizações retomadas.", row.Name);
        Render();
    }

    private async void Terms_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (_busy) return;
        if (AppPaths.Demo) { Dialogs.ShowText(this, row.Name, Loc.T("No modo demonstração o app não consulta o WinGet.")); return; }
        if (!EnsureSourceTerms()) return;
        _busy = true;
        Msg.Text = Loc.T("Consultando o WinGet…");
        try
        {
            var details = await UpdateEngines.Shared.ShowAsync(row.Id, Host.Settings.WingetSourceTerms, CancellationToken.None);
            Dialogs.ShowText(this, row.Name, details?.Text is { Length: > 0 } text ? text : Loc.T("O WinGet não devolveu informações deste pacote."));
            Msg.Text = "";
        }
        catch (Exception ex) when (ex is UpdateEngineUnavailableException or Win32Exception) { Msg.Text = Loc.T("O WinGet não respondeu."); }
        finally { _busy = false; }
    }

    // ---------- atualizar ----------

    private void Stop_Click(object sender, RoutedEventArgs e) { _stop = true; StopBtn.IsEnabled = false; Msg.Text = Loc.T("Vai parar depois do programa atual."); }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_busy) return;
        var queue = _rows.Where(r => r.Selected && r.CanSelect).ToList();
        if (queue.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        string list = string.Join("\n", queue.Select(r => $"• {r.Name}  {r.Installed} → {r.Available}"));
        if (_main.Simulate(Loc.T("Atualizar programas"), Loc.F("Atualizaria {0} pelo WinGet.", Pt.N(queue.Count, Loc.T("programa"), Loc.T("programas"))))) return;
        if (AppPaths.Demo) return;
        if (!EnsureSourceTerms()) return;

        // Os termos de cada pacote são da pessoa: só depois deste clique o argumento de aceite vai para o WinGet
        string text = Loc.F("Atualizar {0}?", Pt.N(queue.Count, Loc.T("programa"), Loc.T("programas"))) + "\n\n" + list + "\n\n"
            + Loc.T("Cada programa tem os próprios termos de licença: use “Ver termos…” para ler antes. Ao continuar, você aceita os termos dos programas listados. Alguns instaladores pedem permissão de administrador, e o Windows mostra a janela. Atualizar não pode ser desfeito.");
        if (!Dialogs.Confirm(this, Loc.T("Aceitar os termos e atualizar"), text, Loc.T("Aceito os termos e atualizo"))) return;

        _busy = true; _stop = false;
        ScanBtn.IsEnabled = RunBtn.IsEnabled = false;
        StopBtn.IsEnabled = true; StopBtn.Visibility = Visibility.Visible;
        Log.Visibility = Visibility.Visible; Log.Clear();
        int ok = 0, failed = 0;
        try
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (_stop) break;
                var row = queue[i];
                Msg.Text = Loc.F("Atualizando {0} de {1}: {2}…", i + 1, queue.Count, row.Name);
                AppendLog($"== {row.Name} ({row.Id}) {row.Installed} → {row.Available}");
                WingetRun run;
                try
                {
                    run = await WingetRunner.UpgradeAsync(row.Id, Host.Settings.WingetSourceTerms, packageTermsAccepted: true,
                        line => Dispatcher.BeginInvoke(() => AppendLog(line)));
                }
                catch (Win32Exception) { AppendLog(Loc.T("O WinGet não respondeu.")); failed++; continue; }

                bool success = !run.TimedOut && WingetCommands.Succeeded(run.ExitCode);
                string result = run.TimedOut
                    ? Loc.T("O WinGet passou de 45 minutos. O app parou de esperar, mas o instalador pode ainda estar rodando.")
                    : ExitText(run.ExitCode);
                AppendLog(result);
                ActionLog.Add(
                    success ? Loc.F("Programa atualizado: {0}", row.Name) : Loc.F("Falha ao atualizar: {0}", row.Name),
                    Loc.F("{0} → {1} (WinGet, {2}). {3}", row.Installed, row.Available, row.Id, result));
                if (success) { ok++; UpdatedAny = true; } else failed++;
            }
        }
        finally
        {
            _busy = false;
            ScanBtn.IsEnabled = RunBtn.IsEnabled = true;
            StopBtn.Visibility = Visibility.Collapsed;
        }
        Msg.Text = Loc.F("{0} atualizados · {1} com falha.", ok, failed);
        System.Media.SystemSounds.Asterisk.Play();
        if (ok > 0) Scan_Click(this, new RoutedEventArgs()); // a lista reflete o que sobrou
    }

    private static string ExitText(int exit) => WingetCommands.Classify(exit) switch
    {
        ExitKind.Done => Loc.T("Atualizado."),
        ExitKind.NotApplicable => Loc.T("Nada a atualizar: já está na versão mais recente ou o instalador não se aplica."),
        ExitKind.Cancelled => Loc.T("Cancelado na janela de permissão."),
        _ => Loc.F("O WinGet terminou com o código 0x{0:X8}.", exit),
    };

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log.AppendText(line.TrimEnd() + Environment.NewLine);
        Log.ScrollToEnd();
    }
}
