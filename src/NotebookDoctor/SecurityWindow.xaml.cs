using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// Verificar malware: o Microsoft Defender (verificação rápida, completa ou de uma pasta, e o histórico de ameaças) e o MRT, a Ferramenta de
/// Remoção de Software Mal-Intencionado, que vêm no Windows. O app só chama as ferramentas da Microsoft e mostra o resultado; não tem motor
/// nem assinaturas próprios.
/// </summary>
public partial class SecurityWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cts;
    private DateTime _since;
    private string _what = "";
    private bool _busy;

    public SecurityWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        _clock.Tick += (_, _) => Msg.Text = Loc.F("{0}… {1}", _what, Elapsed(DateTime.Now - _since));
        // A primeira leitura do estado do Defender pode terminar com a janela já aberta
        Host.HealthUpdated += ShowStatus;
        Closed += (_, _) => Host.HealthUpdated -= ShowStatus;
        Loaded += async (_, _) =>
        {
            ShowStatus();
            if (!MalwareScanner.MrtAvailable) MrtQuickBtn.IsEnabled = MrtFullBtn.IsEnabled = MrtCleanBtn.IsEnabled = false;
            await LoadHijackAsync();
            if (!AppPaths.Demo) await LoadHistoryAsync(quiet: true);
        };
    }

    // ---------- Leva 12.3: hosts e script de proxy ----------

    private async Task LoadHijackAsync()
    {
        var (findings, pac) = AppPaths.Demo
            ? (NetworkPrivacy.AuditHosts(["0.0.0.0 ads.example.com", "203.0.113.7 www.itau.com.br"]), (string?)null)
            : await Task.Run(() => (NetworkPrivacy.AuditHosts(PrivacyProbe.ReadHosts()), PrivacyProbe.Pac()));
        string? pacProblem = NetworkPrivacy.SuspiciousPac(pac);
        var lines = new List<string>();
        lines.AddRange(findings.Select(f => "• " + Loc.F("Arquivo hosts: {0}", f.Reason)));
        if (pacProblem is not null) lines.Add("• " + Loc.F("Proxy: {0} ({1})", pacProblem, pac!));
        HijackInfo.Text = lines.Count == 0 ? Loc.T("Nada desviando a navegação: o hosts e o proxy estão limpos.") : string.Join("\n", lines);
        HostsBtn.Visibility = findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PacBtn.Visibility = pacProblem is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Hosts_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.IsAdmin) { HijackInfo.Text += "\n\n" + Loc.T("Para mexer no hosts, abra o app como administrador."); return; }
        if (!Dialogs.Confirm(this, Loc.T("Restaurar o hosts padrão?"),
                Loc.T("O arquivo hosts volta a ser o de um Windows recém-instalado: todas as linhas atuais saem, inclusive as de bloqueadores de anúncio. O atual fica no backup, e o Desfazer em Ações devolve."),
                Loc.T("Restaurar"), danger: true)) return;
        if (_main.Simulate("Restaurar o hosts padrão", Loc.T("Trocaria o arquivo hosts pelo padrão do Windows, com backup."))) return;
        var (id, ok) = await Task.Run(() =>
        {
            using var session = new BackupStore(AppPaths.Backups).Begin(Loc.T("Arquivo hosts"));
            return (session.Id, PrivacyProbe.RestoreHosts(session));
        });
        if (!ok) { HijackInfo.Text += "\n\n" + Loc.T("O Windows não deixou alterar o hosts (o antivírus pode estar protegendo o arquivo)."); return; }
        ActionLog.Add("Arquivo hosts restaurado", PrivacyProbe.HostsPath, "leftovers", id);
        _main.ShowSnack(Loc.T("Arquivo hosts restaurado"), Loc.T("Desfaça em Ações se algo deixar de funcionar."));
        await LoadHijackAsync();
    }

    private async void Pac_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, Loc.T("Tirar o script de proxy?"),
                Loc.T("O Windows deixa de usar o script de proxy configurado para a sua conta. Se o notebook é da empresa e a internet parar, desfaça em Ações."),
                Loc.T("Tirar"))) return;
        if (_main.Simulate("Tirar o script de proxy", Loc.T("Removeria o script de proxy da sua conta, com backup."))) return;
        var (id, ok) = await Task.Run(() =>
        {
            using var session = new BackupStore(AppPaths.Backups).Begin(Loc.T("Script de proxy"));
            return (session.Id, PrivacyProbe.RemovePac(session));
        });
        if (!ok) { HijackInfo.Text += "\n\n" + Loc.T("Não foi possível tirar o script de proxy."); return; }
        ActionLog.Add("Script de proxy removido", Loc.T("Configuração da sua conta"), "leftovers", id);
        _main.ShowSnack(Loc.T("Script de proxy removido"), Loc.T("Desfaça em Ações se a internet parar."));
        await LoadHijackAsync();
    }

    private static string Elapsed(TimeSpan t) => t.TotalHours >= 1 ? Loc.F("{0} h {1:00} min", (int)t.TotalHours, t.Minutes)
        : t.TotalMinutes >= 1 ? Loc.F("{0} min {1:00} s", (int)t.TotalMinutes, t.Seconds) : Loc.F("{0} s", t.Seconds);

    private void ShowStatus() => Dispatcher.InvokeAsync(() =>
    {
        var sec = Host.Health.Sec;
        bool ok = DefenderRules.StatusOk(sec);
        StatusLine.Text = DefenderRules.StatusLine(sec);
        StatusGlyph.Text = ok ? "\uE73E" : "\uE7BA";
        StatusGlyph.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Success" : "Caution");
        // Tudo certo fica alinhado ao texto de cima; o que pede ação ganha o fundo de atenção
        if (ok) { StatusBox.ClearValue(Border.BackgroundProperty); StatusBox.Padding = new Thickness(0); }
        else { StatusBox.SetResourceReference(Border.BackgroundProperty, "CautionContainer"); StatusBox.Padding = new Thickness(8, 4, 8, 4); }
    });

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

    private void OpenSecurity_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell("windowsdefender://threat/");

    private void SetBusy(bool busy, string what = "")
    {
        _busy = busy;
        DefQuickBtn.IsEnabled = DefFullBtn.IsEnabled = DefFolderBtn.IsEnabled = !busy;
        MrtQuickBtn.IsEnabled = MrtFullBtn.IsEnabled = MrtCleanBtn.IsEnabled = !busy && MalwareScanner.MrtAvailable;
        CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        Bar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Bar.IsIndeterminate = busy;
        _what = what;
        _since = DateTime.Now;
        if (busy) { Msg.Text = Loc.F("{0}… {1}", what, Elapsed(TimeSpan.Zero)); _clock.Start(); } else _clock.Stop();
    }

    /// <summary>MRT e Defender verificam o computador inteiro: os dois pedem administrador.</summary>
    private bool NeedsAdmin()
    {
        if (AppInfo.IsAdmin) return false;
        if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("A verificação de malware exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir")))
            Host.RestartAsAdmin();
        return true;
    }

    // ---------- Microsoft Defender ----------
    private async void DefenderQuick_Click(object sender, RoutedEventArgs e) => await RunDefenderAsync(DefenderScan.Quick, null);
    private async void DefenderFull_Click(object sender, RoutedEventArgs e) => await RunDefenderAsync(DefenderScan.Full, null);

    private async void DefenderFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Escolha a pasta a verificar") };
        if (dlg.ShowDialog(this) != true) return;
        await RunDefenderAsync(DefenderScan.Folder, dlg.FolderName);
    }

    private async Task RunDefenderAsync(DefenderScan scan, string? folder)
    {
        if (_busy) return;
        string what = scan switch
        {
            DefenderScan.Quick => Loc.T("Verificação rápida do Defender"),
            DefenderScan.Full => Loc.T("Verificação completa do Defender"),
            _ => Loc.F("Verificação do Defender em {0}", folder),
        };
        // Pode pôr arquivos em quarentena: com a simulação ligada, só mostra o que faria
        if (_main.Simulate(what, Loc.T("O Microsoft Defender verificaria e poria em quarentena o que achasse."))) return;
        if (NeedsAdmin()) return;

        _cts = new CancellationTokenSource();
        SetBusy(true, what);
        (DefenderResult? Result, RecoveryRun Run) outcome;
        bool userCancelled;
        try { outcome = await Task.Run(() => MalwareScanner.RunDefenderAsync(scan, folder, _cts.Token)); }
        catch (ArgumentException) { SetBusy(false); Msg.Text = Loc.T("Essa pasta não pode ser verificada pelo Defender. Escolha uma pasta de uma unidade do computador."); return; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { SetBusy(false); Msg.Text = Loc.F("O PowerShell não abriu: {0}", ex.Message); return; }
        finally { userCancelled = _cts?.IsCancellationRequested == true; _cts?.Dispose(); _cts = null; }
        SetBusy(false);

        if (outcome.Run.Cancelled) { Msg.Text = Stopped(userCancelled, MalwareScanner.DefenderLimit); return; }
        if (outcome.Result is not { } r) { Msg.Text = Loc.T("O Defender não respondeu. Abra a Segurança do Windows para ver se ele está ligado."); return; }
        ShowHistory(r);
        if (r.Error is { } err) { Msg.Text = Loc.F("O Defender não fez a verificação: {0}", err); return; }

        var found = r.FoundNow;
        string summary = found.Count == 0
            ? Loc.T("Nenhuma ameaça encontrada.")
            : Loc.F("{0}: {1}.", Pt.N(found.Count, "ameaça encontrada", "ameaças encontradas"), string.Join(", ", found.Select(f => Loc.F("{0} ({1})", f.Name, f.StatusText))));
        Msg.Text = summary;
        ActionLog.Add(what, found.Count == 0 ? summary : summary + " " + Loc.T("Para restaurar um arquivo, use o Histórico de proteção da Segurança do Windows."));
        if (found.Any(f => f.NeedsAttention)) Msg.Text += " " + Loc.T("Algumas pedem uma ação sua: abra a Segurança do Windows.");
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async void History_Click(object sender, RoutedEventArgs e) => await LoadHistoryAsync(quiet: false);

    private async Task LoadHistoryAsync(bool quiet)
    {
        if (_busy) return;
        _cts = new CancellationTokenSource();
        SetBusy(true, Loc.T("Lendo o histórico do Defender"));
        DefenderResult? r = null;
        bool cancelled = false;
        try
        {
            var outcome = await Task.Run(() => MalwareScanner.RunDefenderAsync(DefenderScan.HistoryOnly, null, _cts.Token));
            (r, cancelled) = (outcome.Result, outcome.Run.Cancelled);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        finally { _cts?.Dispose(); _cts = null; }
        SetBusy(false);
        if (cancelled) { Msg.Text = Loc.T("Leitura do histórico cancelada."); return; }
        if (r is null) { Msg.Text = quiet ? "" : Loc.T("O Defender não respondeu. Abra a Segurança do Windows para ver se ele está ligado."); return; }
        ShowHistory(r);
        // Sem administrador o Windows pode esconder o histórico: avisa uma vez, sem tratar como erro
        Msg.Text = r.Error is not null && !AppInfo.IsAdmin ? Loc.T("Para ver o histórico completo, abra o app como administrador.")
                 : quiet ? "" : Pt.N(r.Detections.Count, "ameaça no histórico", "ameaças no histórico");
    }

    private void ShowHistory(DefenderResult r) => ThreatGrid.ItemsSource = r.Detections;

    /// <summary>O executor encerra pelo Cancelar e também pelo limite de tempo: a mensagem diz qual dos dois foi.</summary>
    private static string Stopped(bool userCancelled, TimeSpan limit) => userCancelled
        ? Loc.T("Verificação cancelada.")
        : Loc.F("A verificação passou de {0} horas e foi interrompida. Tente de novo com o computador ligado na tomada, ou use a Segurança do Windows.", (int)limit.TotalHours);

    // ---------- MRT ----------
    private async void MrtQuick_Click(object sender, RoutedEventArgs e) => await RunMrtAsync(MrtMode.QuickDetect);
    private async void MrtFull_Click(object sender, RoutedEventArgs e) => await RunMrtAsync(MrtMode.FullDetect);

    private async void MrtClean_Click(object sender, RoutedEventArgs e)
    {
        if (_main.Simulate(Loc.T("Verificar e remover com o MRT"), Loc.T("O MRT faria uma verificação completa e removeria o que achasse."))) return;
        if (!Dialogs.Confirm(this, Loc.T("Verificar e remover?"),
                Loc.T("O MRT faz uma verificação completa (pode levar horas) e remove sozinho o que achar. A remoção é da Microsoft e não passa pelo backup do app.\n\nPara só ver o que ele acharia, use \"Completa (só apontar)\"."),
                Loc.T("Verificar e remover"), danger: true)) return;
        await RunMrtAsync(MrtMode.FullClean);
    }

    private async Task RunMrtAsync(MrtMode mode)
    {
        if (_busy) return;
        if (NeedsAdmin()) return;
        string what = mode switch
        {
            MrtMode.QuickDetect => Loc.T("Verificação rápida do MRT"),
            MrtMode.FullDetect => Loc.T("Verificação completa do MRT"),
            _ => Loc.T("Verificação e remoção do MRT"),
        };
        // O resumo da execução anterior sai antes de começar: o que ficar na caixa é sempre desta verificação
        MrtOutput.Text = "";
        MrtOutput.Visibility = Visibility.Collapsed;
        _cts = new CancellationTokenSource();
        SetBusy(true, what);
        (MrtResult? Result, RecoveryRun Run) outcome;
        bool userCancelled;
        try { outcome = await Task.Run(() => MalwareScanner.RunMrtAsync(mode, _cts.Token)); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { SetBusy(false); Msg.Text = Loc.F("O MRT não abriu: {0}", ex.Message); return; }
        finally { userCancelled = _cts?.IsCancellationRequested == true; _cts?.Dispose(); _cts = null; }
        SetBusy(false);

        if (outcome.Run.Cancelled) { Msg.Text = Stopped(userCancelled, MalwareScanner.MrtLimit); return; }
        if (outcome.Result is not { } r) { Msg.Text = Loc.F("O MRT terminou com o código {0}, mas não registrou o resultado no mrt.log.", outcome.Run.ExitCode); return; }

        // O resumo é o texto do próprio MRT, em inglês: aparece como veio
        MrtOutput.Text = string.Join("\n", r.Summary);
        MrtOutput.Visibility = r.Summary.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        string summary = r.Found
            ? Loc.T(MrtRules.Removes(mode) ? "O MRT encontrou ameaças e tentou removê-las. Veja o resumo abaixo." : "O MRT encontrou ameaças. Veja o resumo abaixo e use \"Verificar e remover…\" ou o Defender.")
            : r.ReturnCode is null or 0 ? Loc.T("O MRT não encontrou ameaças.") : Loc.F("O MRT terminou com o código {0}. Veja o resumo abaixo.", r.ReturnCode);
        Msg.Text = summary;
        ActionLog.Add(what, summary + (r.Summary.Count > 0 ? " " + string.Join(" ", r.Summary) : ""));
        System.Media.SystemSounds.Asterisk.Play();
    }
}
