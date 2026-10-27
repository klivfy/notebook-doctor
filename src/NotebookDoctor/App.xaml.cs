using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record HeavyAlertInfo(string Name, double CpuPct, long MemMB, DateTime Since);

public partial class App : Application
{
    private Mutex? _single;
    private CancellationTokenSource _cts = new();
    private SystemMonitor _monitor = null!;
    private TrayIcon _tray = null!;
    private readonly List<Snapshot> _recent = new();           // ~últimos 3 minutos ao vivo
    private readonly Sustained _cpuHigh = new(), _ramHigh = new();
    private DateTime _lastAlert = DateTime.MinValue, _lastDiskAlert = DateTime.MinValue, _lastHeatAlert = DateTime.MinValue;

    public AppSettings Settings { get; private set; } = new();
    public HistoryStore History { get; private set; } = null!;
    public Snapshot? Latest { get; private set; }
    public IReadOnlyList<Snapshot> Recent => _recent;
    // O mini-widget também precisa de números a cada 3 s
    public bool WindowVisible => MainWindow is { IsVisible: true } || _mini is { IsVisible: true };
    private MiniWidget? _mini;
    public UpdateInfo? PendingUpdate { get; private set; }
    public event Action<Snapshot>? SnapshotReady;

    // Saúde (dados lentos), nota e alerta de programa pesado
    public HealthInfo Health { get; private set; } = HealthInfo.Empty;
    public HealthScoreResult Score { get; private set; } = new(0, "Analisando");
    public HeavyAlertInfo? HeavyAlert { get; private set; }
    public MemoryTrend Memory { get; } = new();
    public FocusMode Focus { get; } = new();
    public event Action? HealthUpdated, HeavyAlertChanged;
    private DateTime _lastProbe = DateTime.MinValue;
    private readonly BrowserWatch _browserWatch = new();
    private bool _browserCleaning, _scheduleRan;
    private DateTime _lastJunkCheck = DateTime.MinValue;
    private bool _probing;
    private readonly Dictionary<string, DateTime> _heavySince = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _heavyIgnored = new(StringComparer.OrdinalIgnoreCase);

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Reinício como administrador: espera a instância anterior fechar antes de assumir a instância única
        var waitArg = e.Args.FirstOrDefault(a => a.StartsWith("--wait-parent="));
        if (waitArg is not null && int.TryParse(waitArg[14..], out int parentPid))
        {
            try { System.Diagnostics.Process.GetProcessById(parentPid).WaitForExit(15000); } catch { }
        }
        string? uri = e.Args.FirstOrDefault(a => a.StartsWith(Toasts.Scheme + ":", StringComparison.OrdinalIgnoreCase));
        // A demonstração é uma instância à parte: abre mesmo com o app de verdade rodando
        _single = new Mutex(true, @"Local\NotebookDoctor.SingleInstance" + (AppPaths.Demo ? ".Demo" : ""), out bool first);
        if (!first)
        {
            // Já está rodando: repassa o pedido (abrir o painel ou o link clicado na notificação) para a instância aberta
            // --tab=N (jump list, atalhos) vira o mesmo pedido que a notificação usa para abrir uma aba
            var tabRequest = e.Args.FirstOrDefault(a => a.StartsWith("--tab="));
            SendToRunningInstance(uri ?? (tabRequest is not null ? $"{Toasts.Scheme}:tab/{tabRequest[6..]}" : $"{Toasts.Scheme}:open"));
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, a) => { LogError(a.Exception); CrashLog.Save(a.Exception, "tela (o app continuou aberto)"); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is not Exception ex) return;
            LogError(ex);
            CrashLog.Save(ex, "segundo plano (o app fechou)");
        };
        TaskScheduler.UnobservedTaskException += (_, a) => { LogError(a.Exception); CrashLog.Save(a.Exception, "tarefa em segundo plano"); a.SetObserved(); };

        Settings = AppPaths.Demo ? StartDemo() : AppSettings.Load();
        ApplyLanguage();
        CrashLog.Enabled = Settings.CrashReports;
        Settings.ApplyUnits();
        ThemeManager.UseAccent = Settings.UseAccentColor;
        ApplyTheme();
        TextScale.Apply();
        ApplyDensity();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            // Tema e cor de destaque do Windows podem mudar com o app aberto
            if (Settings.ThemeMode == "system" || Settings.UseAccentColor) ApplyTheme();
            TextScale.Apply(); // o Windows avisa por aqui quando o "Tamanho do texto" muda
        });
        // A02: ao ligar ou tirar da tomada, sugere o plano de energia adequado
        _onAc = SystemParameters.PowerLineStatus == PowerLineStatus.Online;
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, a) =>
        {
            if (a.Mode == Microsoft.Win32.PowerModes.StatusChange) Dispatcher.BeginInvoke(() => _ = OnPowerSourceChangedAsync());
        };
        History = new HistoryStore();
        if (AppPaths.Demo) History.Seed(DemoData.Week(DateTime.Now));
        _monitor = new SystemMonitor();

        _tray = new TrayIcon(Settings.StartWithWindows);
        ThemeManager.Changed += _tray.ApplyTheme;
        _tray.OpenRequested += ShowWindow;
        _tray.BalloonClicked += ShowWindow;
        _tray.ExitRequested += Quit;
        _tray.SupportRequested += AppInfo.OpenSupport;
        _tray.CleanRequested += () => _ = QuickCleanAsync(notify: true);
        _tray.StartWithWindowsToggled += v =>
        {
            if (v == Settings.StartWithWindows) return; // eco da própria desmarcação abaixo
            if (!EnsureUnlocked(Loc.T("mudar \"Iniciar com o Windows\""))) { _tray.SyncStartWithWindows(Settings.StartWithWindows); return; }
            Settings.StartWithWindows = v; Settings.ApplyStartWithWindows(); Settings.Save();
        };
        _tray.MiniToggled += SetMiniWidget;
        _tray.UnlockRequested += () => { ShowWindow(); (MainWindow as NotebookDoctor.MainWindow)?.OpenFileUnlock(null); };
        _tray.EjectRequested += letter => _ = EjectAsync(letter);
        _tray.PowerPlanRequested += SetPowerPlanFromTray;
        _tray.ClearClipboardRequested += ClearClipboard;
        _tray.MiddleClicked += () => RunMiddleAction(Settings.TrayMiddle);

        var win = new MainWindow();
        MainWindow = win;
        if (e.Args.Contains("--background")) { }
        // B09: aberto pelo atalho, sem pedido de aba ou de notificação, começa minimizado na barra de tarefas
        else if (Settings.StartMinimized && Settings.FirstRunDone && uri is null && !e.Args.Any(a => a.StartsWith("--tab=")))
        {
            win.WindowState = WindowState.Minimized;
            win.Show();
        }
        else ShowWindow();
        // Atalho: --tab=programas (ou o número, 0 = Painel) abre direto na aba. Útil para testes e atalhos do Windows.
        var tabArg = e.Args.FirstOrDefault(a => a.StartsWith("--tab="));
        if (tabArg is not null && AppTabs.Parse(tabArg[6..]) is int tab) win.SelectTab(tab);
        if (uri is not null) HandleUri(uri);
        // Verificação visual da CI: só na demonstração, que não toca no computador
        if (AppPaths.Demo && Smoke.FolderFromArgs(e.Args) is { } smoke)
            Dispatcher.BeginInvoke(async () => await Smoke.RunAsync(win, smoke), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // A jump list e o registro das notificações são do app instalado, não da demonstração
        if (!AppPaths.Demo) { _ = Task.Run(Toasts.EnsureRegistered); SetJumpList(); }
        _ = Task.Run(() => { try { new BackupStore(AppPaths.Backups).Prune(DateTime.Now); } catch (Exception ex) { LogError(ex); } }); // Fase 5: backups vencidos
        _overlayTimer.Tick += (_, _) => UpdateOverlay();
        _overlayTimer.Start();
        _ = ListenForOtherInstancesAsync(_cts.Token);
        if (Settings.LastWeeklySummary == DateTime.MinValue) { Settings.LastWeeklySummary = DateTime.Now; Settings.Save(); }
        if (!Settings.FirstRunDone && !e.Args.Contains("--background") && !e.Args.Any(a => a.StartsWith("--tab=")))
            Dispatcher.BeginInvoke(() => { Dialogs.ShowWelcome(win, Settings); ApplySettingsChanged(); win.ReloadSettings(); },
                                   System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (Settings.MiniWidget) SetMiniWidget(true);
        OfferCrashReport();
        _hotkey.Pressed += ToggleWindow;
        ApplyHotkey();
        // M08: Alto Contraste ligado ou desligado com o app aberto
        SystemParameters.StaticPropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.BeginInvoke(() => ApplyTheme());
        };
        _ = RunLoopAsync(_cts.Token);
        _ = CheckUpdatesLaterAsync(_cts.Token);
        _ = SendUsageLaterAsync(_cts.Token);
        UsageStats.WatchWindows();
        _ = SnoozeWatchAsync(_cts.Token);
    }

    // ---------- M02: mini-widget ----------
    public void SetMiniWidget(bool on)
    {
        if (on && _mini is null)
        {
            _mini = new MiniWidget();
            _mini.CloseRequested += () => SetMiniWidget(false);
            _mini.Show();
        }
        else if (!on && _mini is not null)
        {
            _mini.Close();
            _mini = null;
        }
        if (Settings.MiniWidget != on) { Settings.MiniWidget = on; Settings.Save(); }
        _tray.SyncMini(on);
        MiniChanged?.Invoke(on);
    }

    public event Action<bool>? MiniChanged;

    // ---------- M01: atalho global ----------
    private readonly GlobalHotkey _hotkey = new();

    /// <summary>Liga ou desliga o atalho conforme a configuração. Retorna false se outro programa já usa a combinação.</summary>
    public bool ApplyHotkey()
    {
        if (!Settings.GlobalHotkey) { _hotkey.Unregister(); return true; }
        return _hotkey.Register();
    }

    public bool HotkeyActive => _hotkey.IsRegistered;

    /// <summary>Atalho: abre o painel; se ele já está na frente, esconde.</summary>
    private void ToggleWindow()
    {
        if (MainWindow is { IsVisible: true, IsActive: true, WindowState: not WindowState.Minimized }) MainWindow.Hide();
        else ShowWindow();
    }

    // ---------- A50: atualização ----------
    /// <summary>Um minuto depois de abrir, no máximo uma vez por dia. Sem internet ou sem manifesto publicado: silêncio.</summary>
    private async Task CheckUpdatesLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
        if (!Settings.CheckUpdates || (DateTime.Now - Settings.LastUpdateCheck).TotalHours < 24) return;
        var (result, info) = await Updater.CheckAsync();
        if (result is UpdateCheck.Offline) return; // tenta de novo na próxima abertura
        Settings.LastUpdateCheck = DateTime.Now;
        Settings.Save();
        if (result == UpdateCheck.Available && info is not null)
        {
            PendingUpdate = info;
            Notify("Nova versão do Notebook Doctor", Loc.F("Versão {0} disponível. {1}", info.Version, info.Notes).Trim(), ("Ver atualização", "update"));
        }
    }

    /// <summary>M18: com a opção ligada, dois minutos depois de abrir e no máximo uma vez por semana. Desligada, nunca sai nada.</summary>
    private async Task SendUsageLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }
        await UsageStats.SendIfDueAsync();
    }

    public void SetPendingUpdate(UpdateInfo? info) => PendingUpdate = info;

    // ---------- Instância única + links "notebookdoctor:" ----------
    private static string PipeName => $"NotebookDoctor.{Environment.UserName}" + (AppPaths.Demo ? ".Demo" : "");

    private static void SendToRunningInstance(string message)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var w = new StreamWriter(client);
            w.WriteLine(message.Length > 200 ? message[..200] : message);
        }
        catch { /* a outra instância está fechando: nada a fazer */ }
    }

    private async Task ListenForOtherInstancesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In, 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                using var r = new StreamReader(server);
                string? msg = await r.ReadLineAsync(ct);
                if (msg is not null) HandleUri(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogError(ex); await Task.Delay(1000, CancellationToken.None); }
        }
    }

    private bool _onAc;

    private async Task OnPowerSourceChangedAsync()
    {
        bool onAc = SystemParameters.PowerLineStatus == PowerLineStatus.Online;
        if (onAc == _onAc) return; // StatusChange também dispara quando só a carga da bateria muda
        _onAc = onAc;
        _ = RefreshHealthAsync();
        if (!Settings.AlertsEnabled) return;
        var plans = await Task.Run(PowerPlans.List);
        if (PowerAdvice.Suggest(onAc, plans) is not { } s) return;
        if (MainWindow is NotebookDoctor.MainWindow { IsVisible: true } win) win.ShowPowerSuggestion(s);
        else if (!IsQuietNow()) Notify(s.Title, s.Reason, ("Ver energia", Tab(AppTabs.Settings)));
    }

    /// <summary>Ação de botão de notificação que abre uma aba, pelo nome (continua certa se a ordem das abas mudar).</summary>
    private static string Tab(int index) => "tab/" + AppTabs.Name(index);

    /// <summary>
    /// Ações de link. Só navegação: qualquer página web pode abrir um link "notebookdoctor:",
    /// então nada aqui encerra programas, apaga arquivos ou muda configurações.
    /// </summary>
    public void HandleUri(string uri)
    {
        string action = uri.Contains(':') ? uri[(uri.IndexOf(':') + 1)..].Trim().TrimEnd('/').ToLowerInvariant() : "open";
        ShowWindow();
        var win = (MainWindow)MainWindow!;
        switch (action)
        {
            case "heavy": win.SelectTab(AppTabs.Panel); break;
            case "report": win.ExportReport(); break;
            case "update": win.SelectTab(AppTabs.Settings); win.OfferUpdate(); break;
            case "protection": AppInfo.OpenShell("SystemPropertiesProtection.exe"); break; // só abre a tela do Windows
            case var t when t.StartsWith("tab/") && AppTabs.Parse(t[4..]) is int i: win.SelectTab(i); break;
        }
    }

    /// <summary>Notificação nativa com botões; se o Windows não aceitar, usa o balão da bandeja.</summary>
    /// <summary>Aviso de problema: entra no histórico de alertas (M46) e vira notificação.</summary>
    private void Alert(string title, string text, params (string label, string action)[] buttons)
    {
        (title, text) = (Loc.T(title), Loc.T(text)); // o registro de avisos guarda o texto no idioma em que foi mostrado
        bool quiet = IsQuietNow();
        AlertLog.Add(title, text, silenced: quiet);
        if (!quiet) Notify(title, text, buttons);
    }

    /// <summary>M43/M44: horário silencioso ou alguém em tela cheia, jogo ou apresentação.</summary>
    private bool IsQuietNow() => Settings.InQuietHours(DateTime.Now) || (Settings.QuietFullscreen && Native.IsFullscreenOrPresenting());

    public void Notify(string title, string text, params (string label, string action)[] buttons)
    {
        // M13: toast do Windows e balão da bandeja no idioma do app (textos montados chegam prontos; Loc.T não muda o que já está traduzido)
        (title, text) = (Loc.T(title), Loc.T(text));
        buttons = buttons.Select(b => (Loc.T(b.label), b.action)).ToArray();
        if (Settings.NativeNotifications && !AppPaths.Demo && Toasts.Show(title, text, buttons)) return;
        _tray.Notify(title, text);
    }

    // ---------- Leva 10.3: ações rápidas da bandeja ----------
    private void SetPowerPlanFromTray(PowerPlan target)
    {
        var previous = PowerPlans.List().FirstOrDefault(p => p.Active);
        if (previous?.Guid == target.Guid) return;
        if (Settings.SimulationMode) { Notify(Loc.F("Simulação: plano {0}", target.Name), Loc.T("Nada foi alterado.")); return; }
        if (!PowerPlans.SetActive(target.Guid)) { Notify(Loc.T("Plano de energia"), Loc.T("Não foi possível trocar o plano de energia.")); return; }
        ActionLog.Add(Loc.T("Plano de energia alterado"), Loc.F("{0} → {1} (pela bandeja)", previous?.Name ?? "?", target.Name), previous is null ? "" : "power", previous?.Guid ?? "");
    }

    private void ClearClipboard()
    {
        if (Settings.SimulationMode) { Notify(Loc.T("Simulação: limpar a área de transferência"), Loc.T("Nada foi alterado.")); return; }
        try { Clipboard.Clear(); }
        catch (System.Runtime.InteropServices.COMException) { return; } // outro programa está com a área de transferência aberta
        ActionLog.Add("Área de transferência limpa");
    }

    private void RunMiddleAction(TrayMiddleAction action)
    {
        switch (action)
        {
            case TrayMiddleAction.OpenPanel: ShowWindow(); break;
            case TrayMiddleAction.MiniWidget: SetMiniWidget(!Settings.MiniWidget); break;
            case TrayMiddleAction.CleanTemp: _ = QuickCleanAsync(notify: true); break;
            case TrayMiddleAction.ClearClipboard: ClearClipboard(); break;
        }
    }

    /// <summary>Fase 10, leva 10.1: ejetar pelo menu da bandeja. Em uso: abre o destravador já na unidade.</summary>
    private async Task EjectAsync(char letter)
    {
        string drive = $"{letter}:";
        if (Settings.SimulationMode) { Notify(Loc.F("Simulação: ejetar {0}", drive), Loc.T("Nada foi alterado.")); return; }
        var result = await Task.Run(() => FileUnlocker.Eject(letter));
        if (result == EjectResult.InUse)
        {
            ShowWindow();
            (MainWindow as NotebookDoctor.MainWindow)?.OpenFileUnlock($"{letter}:\\");
            return;
        }
        if (result == EjectResult.Ejected) ActionLog.Add(Loc.F("Unidade ejetada: {0}", drive));
        Notify(result == EjectResult.Ejected ? Loc.T("Pendrive ejetado") : Loc.T("Pendrive não ejetado"), FileUnlockWindow.EjectText(result, drive));
    }

    public void ShowWindow()
    {
        MainWindow!.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>M07/B42: depois de importar ou redefinir, relê o arquivo e aplica tudo como se tivesse sido salvo na tela.</summary>
    public void ReloadSettingsFromDisk()
    {
        Settings = AppSettings.Load();
        Settings.ApplyStartWithWindows();
        ApplySettingsChanged();
        SetMiniWidget(Settings.MiniWidget);
        ApplyHotkey();
        (MainWindow as NotebookDoctor.MainWindow)?.ReloadSettings();
    }

    /// <summary>M19: depois de uma falha, avisa uma vez e leva ao "Gerar pacote para o suporte" (nada é enviado sozinho).</summary>
    private void OfferCrashReport()
    {
        if (!Settings.CrashReports || CrashReport.CountSince(CrashLog.Files(), Settings.LastCrashSeen) == 0) return;
        Settings.LastCrashSeen = DateTime.Now;
        Settings.Save();
        string what = CrashLog.LatestHeadline();
        Notify("O Notebook Doctor teve um erro", (what.Length > 0 ? what + ". " : "") +
               Loc.T("O relatório ficou guardado sem dados pessoais e vai no pacote para o suporte, se você quiser enviar."),
               ("Ver em Configurações", Tab(AppTabs.Settings)));
    }

    private string _appliedTheme = "";

    /// <summary>Aplica o tema escolhido; no modo por horário, o claro ou o escuro conforme a hora.</summary>
    private void ApplyTheme()
    {
        _appliedTheme = Settings.EffectiveTheme(DateTime.Now);
        ThemeManager.Apply(_appliedTheme);
    }

    public void ApplySettingsChanged()
    {
        _tray.SyncStartWithWindows(Settings.StartWithWindows);
        CrashLog.Enabled = Settings.CrashReports;
        Settings.ApplyUnits();
        ThemeManager.UseAccent = Settings.UseAccentColor;
        ApplyTheme();
        ApplyDensity();
    }

    /// <summary>B15: modo compacto. Os estilos leem estes recursos com DynamicResource, então a troca vale na hora.</summary>
    private void ApplyDensity()
    {
        bool compact = Settings.Compact;
        Resources["CardPad"] = new Thickness(Density.CardPadding(compact));
        Resources["CardGap"] = new Thickness(Density.CardMargin(compact));
        Resources["RowMin"] = Density.RowHeight(compact);
        (MainWindow as NotebookDoctor.MainWindow)?.ApplyNavDensity();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snap = AppPaths.Demo ? DemoData.Sample(DateTime.Now, _demoTick++) : await Task.Run(_monitor.Sample, ct);
                Latest = snap;
                _recent.Add(snap);
                Sampling.TrimRecent(_recent, snap.Time); // M42: com amostras espaçadas, a nota não usa dados velhos
                Score = Insights.Score(_recent, Health);
                History.Record(snap, Score.Score);
                _tray.Update(snap, Score.Score, Settings.TrayIcon);
                if (Settings.ThemeMode == ThemeSchedule.Mode && Settings.EffectiveTheme(DateTime.Now) != _appliedTheme) ApplyTheme(); // B37
                CheckAlerts(snap);
                CheckHeavyPrograms(snap);
                CheckHeat(snap);
                CheckWeeklySummary();
                Memory.Feed(snap);
                if ((DateTime.Now - _lastProbe).TotalMinutes >= 30) _ = RefreshHealthAsync();
                await CheckWeeklyAsync();
                await CheckJunkMonitorAsync();
                await CheckBrowserCloseAsync();
                SnapshotReady?.Invoke(snap);
                if (_overlay is { IsVisible: true }) _overlay.Update(snap);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogError(ex); }

            // M42: 3 s à vista; em segundo plano, 15 s na tomada e 30–60 s na bateria
            var power = Native.PowerStatus();
            var wait = Sampling.Interval(WindowVisible || _overlay is { IsVisible: true }, power.OnBattery, power.Saver, power.Percent);
            try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Atualiza SMART, bateria, boot, plano de energia e lixo (fora da thread de interface).</summary>
    public async Task RefreshHealthAsync()
    {
        if (_probing) return;
        _probing = true;
        _lastProbe = DateTime.Now;
        try
        {
            Health = AppPaths.Demo ? DemoData.Health() : await HealthProbe.RunAsync();
            HealthUpdated?.Invoke();
        }
        catch (Exception ex) { LogError(ex); }
        finally { _probing = false; }
    }

    /// <summary>
    /// Um programa (não protegido) usando 25%+ da CPU por 3+ minutos vira um alerta com botão "Encerrar".
    /// "Ignorar" silencia esse programa por 1 hora.
    /// </summary>
    private void CheckHeavyPrograms(Snapshot s)
    {
        const double thresholdPct = 25;
        // Programas protegidos e os da lista de exceções (M20) nunca viram aviso
        var heavy = s.Groups.Where(g => g.CpuPct >= thresholdPct && !ProcessTools.IsProtected(g.Name) && !Settings.IsAlertException(g.Name)).ToList();
        foreach (var name in _heavySince.Keys.Where(k => !heavy.Any(g => g.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
            _heavySince.Remove(name);
        foreach (var g in heavy) _heavySince.TryAdd(g.Name, s.Time);

        var culprit = heavy
            .Where(g => (s.Time - _heavySince[g.Name]).TotalMinutes >= 3)
            .Where(g => !_heavyIgnored.TryGetValue(g.Name, out var until) || until < DateTime.Now)
            .OrderByDescending(g => g.CpuPct).FirstOrDefault();

        var next = culprit is null ? null : new HeavyAlertInfo(culprit.Name, culprit.CpuPct, culprit.MemMB, _heavySince[culprit.Name]);
        if (next?.Name == HeavyAlert?.Name) { HeavyAlert = next; return; } // mesmo programa: só atualiza os números
        HeavyAlert = next;
        if (next is not null && Settings.AlertsEnabled)
            Alert("Programa pesado", Loc.F("{0} usa {1:0}% da CPU há mais de 3 minutos.", next.Name, next.CpuPct), ("Abrir painel", "heavy"));
        HeavyAlertChanged?.Invoke();
    }

    /// <summary>
    /// A42: superaquecimento. Com sensor: 90 °C+ por 2 minutos. Sem sensor (comum em notebooks): a CPU sob carga
    /// rodando bem abaixo da frequência máxima por ~1 minuto, que é o sintoma de limitação térmica.
    /// </summary>
    private void CheckHeat(Snapshot s)
    {
        if (!Settings.AlertsEnabled || (DateTime.Now - _lastHeatAlert).TotalMinutes < 30) return;
        var last = _recent.Where(x => (s.Time - x.Time).TotalMinutes <= 2).ToList();
        string? msg = null;
        if (last.Count >= 6 && last.All(x => x.TempC >= 90))
            msg = Loc.F("Temperatura em {0} há 2 minutos.", Units.Temp(s.TempC));
        else
        {
            var loaded = _recent.TakeLast(12).Where(x => x.Cpu >= 70 && x.CpuPerf >= 0).ToList();
            if (loaded.Count >= 8 && loaded.Average(x => x.CpuPerf) < 60)
                msg = Loc.F("O processador está rodando a {0:0}% da velocidade máxima mesmo sob carga — sinal de calor ou limite de energia.", loaded.Average(x => x.CpuPerf));
        }
        if (msg is null) return;
        _lastHeatAlert = DateTime.Now;
        Alert("Possível superaquecimento", msg + " Verifique a ventilação e use a tomada.", ("Abrir painel", "open"));
    }

    /// <summary>A49: uma vez por semana, um resumo dos últimos 7 dias com botão para o relatório completo.</summary>
    private void CheckWeeklySummary()
    {
        if ((DateTime.Now - Settings.LastWeeklySummary).TotalDays < 7) return;
        Settings.LastWeeklySummary = DateTime.Now;
        Settings.Save();
        var week = History.Load(TimeSpan.FromDays(7));
        if (week.Count < 60) return; // menos de 1 h de dados na semana: não há o que resumir
        var scores = week.Where(w => w.Score >= 0).Select(w => w.Score).ToList();
        var top = week.SelectMany(w => w.Top).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key);
        string text = (scores.Count > 0 ? Loc.F("Nota média {0:0}.", scores.Average()) + " " : "") +
                      Loc.F("CPU média {0:0}%, RAM média {1:0}%. Mais pesados: {2}.", week.Average(w => w.Cpu), week.Average(w => w.Ram), string.Join(", ", top));
        Notify("Resumo da semana", text, ("Ver relatório", "report"), ("Abrir histórico", Tab(AppTabs.History)));
    }

    public void IgnoreHeavy(string name)
    {
        _heavyIgnored[name] = DateTime.Now.AddHours(1);
        HeavyAlert = null;
        HeavyAlertChanged?.Invoke();
    }

    private void CheckAlerts(Snapshot s)
    {
        if (!Settings.AlertsEnabled) return;
        // Disco quase cheio tem aviso próprio (a cada 6 h), independente do alerta de CPU/RAM
        if (s.DiskFreePct < 10 && (DateTime.Now - _lastDiskAlert).TotalHours >= 6)
        {
            _lastDiskAlert = DateTime.Now;
            Alert("Pouco espaço no disco", Loc.F("Só {0:0}% livre ({1}).", s.DiskFreePct, Units.Gb(s.DiskFreeGB, "0")), ("Abrir Limpeza", Tab(AppTabs.Clean)), ("Ver o que ocupa", Tab(AppTabs.Space)));
        }
        string? msg = null;
        int minutes = Settings.AlertMinutes;
        if (_cpuHigh.Update(s.Cpu >= Settings.CpuAlertPct, s.Time, minutes))
        {
            var top = s.TopByCpu.FirstOrDefault();
            if (top is not null && Settings.IsAlertException(top.Name)) top = null; // carga esperada: não avisa
            else msg = Loc.F("CPU em {0:0}% há {1} ou mais.", s.Cpu, Pt.N(minutes, "minuto", "minutos")) + (top is null ? "" : " " + Loc.F("Maior consumo: {0} ({1:0}%).", top.Name, top.CpuPct));
        }

        if (_ramHigh.Update(s.RamPct >= Settings.RamAlertPct, s.Time, minutes) && msg is null)
        {
            var top = s.TopByMem.FirstOrDefault();
            if (top is null || !Settings.IsAlertException(top.Name)) msg = Loc.F("Memória em {0:0}%.", s.RamPct) + (top is null ? "" : " " + Loc.F("Maior consumo: {0} ({1} MB).", top.Name, top.MemMB));
        }
        if (msg is not null && (DateTime.Now - _lastAlert).TotalMinutes >= 30)
        {
            _lastAlert = DateTime.Now;
            Alert(msg.StartsWith("CPU") ? "CPU alta" : "Memória alta", msg, ("Abrir painel", "open"), ("Ver processos", Tab(AppTabs.Processes)));
        }
    }

    /// <summary>Limpeza automática (C17): ao abrir, todo dia, toda semana ou todo mês, sempre com o PC parado.</summary>
    private async Task CheckWeeklyAsync()
    {
        if (AppPaths.Demo || !Settings.WeeklyCleanup) return;
        if (!ScheduleRules.Due(ScheduleRules.Parse(Settings.ScheduleKind), Settings.LastWeeklyRun, DateTime.Now, _scheduleRan)) return;
        // A39: manutenção em horário ocioso: 10 minutos sem uso, fora do Modo Foco e na tomada
        if (!MaintenanceRules.CanRunIdleTask(Native.IdleTime(), Focus.Active, Native.PowerStatus().OnBattery)) return;
        _scheduleRan = true;
        Settings.LastWeeklyRun = DateTime.Now;
        Settings.Save();
        await QuickCleanAsync(notify: true);
        if (Settings.ScheduleBrowser) await CleanBrowsersAsync(null, quiet: false);
    }

    /// <summary>C15: a cada hora, com o PC parado, olha o lixo e avisa uma vez por dia quando passa do limite.</summary>
    private async Task CheckJunkMonitorAsync()
    {
        var now = DateTime.Now;
        if (AppPaths.Demo || !Settings.JunkMonitor || !JunkMonitorRules.CheckDue(_lastJunkCheck, now)) return;
        if (!MaintenanceRules.CanRunIdleTask(Native.IdleTime(), Focus.Active, Native.PowerStatus().OnBattery)) return;
        _lastJunkCheck = now;
        int days = Settings.TempDays;
        long bytes = await Task.Run(() => Cleaner.SafeJunkBytes(days));
        long limit = Settings.JunkMonitorLimitMb * 1024L * 1024L;
        if (!JunkMonitorRules.ShouldNotify(bytes, limit, Settings.LastJunkNotice, now)) return;
        Settings.LastJunkNotice = now;
        Settings.Save();
        // Link só navega: a pessoa abre a Limpeza e decide
        Alert("Lixo acumulado", Loc.F("Há cerca de {0} de temporários para limpar.", Cleaner.FormatBytes(bytes)), ("Abrir Limpeza", Tab(AppTabs.Clean)));
    }

    // ---------- Leva 7.3 (C12): modo soneca, acordar ao abrir ----------
    public SnoozeStore Snooze { get; } = new(System.IO.Path.Combine(AppPaths.Dir, "snooze.json"));
    private readonly ISnoozeSystem _snoozeSystem = new WindowsSnoozeSystem();
    private Dictionary<string, int> _snoozeAbsent = new();

    /// <summary>
    /// Enquanto há aplicativo em soneca com "acordar ao abrir", olha de poucos em poucos segundos os nomes dos processos
    /// abertos: quando o programa principal abre, devolve as peças dele; quando fecha, volta a dormir.
    /// </summary>
    private async Task SnoozeWatchAsync(CancellationToken ct)
    {
        if (AppPaths.Demo) return;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait = TimeSpan.FromSeconds(30);
            try
            {
                var apps = Snooze.Load();
                if (apps.Any(a => a.WakeOnOpen && a.MainExes.Count > 0))
                {
                    var running = await Task.Run(SnoozeScanner.RunningNames, ct);
                    _snoozeAbsent = SnoozeRules.CountAbsent(apps, running, _snoozeAbsent);
                    foreach (var (app, action) in SnoozeRules.Decide(apps, running, _snoozeAbsent))
                        await Task.Run(() => ApplySnoozeDecision(app.Key, action), ct);
                    wait = Native.PowerStatus().OnBattery ? TimeSpan.FromSeconds(10) : SnoozeRules.WatchEvery;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogError(ex); }
            try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void ApplySnoozeDecision(string key, WatchAction action)
    {
        string? log = null;
        Snooze.Update(list =>
        {
            var rec = list.FirstOrDefault(a => a.Key == key);
            if (rec is null) return false;
            if (action == WatchAction.Wake)
            {
                SnoozeEngine.Wake(rec, _snoozeSystem);
                rec.Awake = true;
                log = Loc.F("Soneca: {0} acordou porque você abriu", rec.Name);
            }
            else
            {
                SnoozeEngine.Apply(rec, _snoozeSystem); // idempotente; deixa Awake = false
                _snoozeAbsent.Remove(key);
                log = Loc.F("Soneca: {0} voltou a dormir", rec.Name);
            }
            return true;
        });
        if (log is not null) ActionLog.Add(log, "");
    }

    /// <summary>C16: quando um navegador fecha, limpa os dados escolhidos (sem janela).</summary>
    private async Task CheckBrowserCloseAsync()
    {
        if (AppPaths.Demo || !Settings.BrowserCleanOnClose || _browserCleaning || Settings.BrowserKinds.Count == 0) return;
        var running = await Task.Run(BrowserCleaner.RunningIds);
        var closed = _browserWatch.Step(running, DateTime.Now, TimeSpan.FromSeconds(10));
        if (closed.Count == 0) return;
        _browserCleaning = true;
        try { await CleanBrowsersAsync(closed, quiet: true); }
        finally { _browserCleaning = false; }
    }

    /// <summary>
    /// Limpa os dados de navegador escolhidos nas configurações. <paramref name="browserIds"/> nulo = todos os que estiverem fechados.
    /// Navegador aberto é pulado, nunca fechado à força.
    /// </summary>
    public async Task<BrowserCleanResult?> CleanBrowsersAsync(IEnumerable<string>? browserIds, bool quiet)
    {
        var kinds = Settings.BrowserKinds.Select(k => Enum.TryParse<BrowserDataKind>(k, out var kind) ? kind : (BrowserDataKind?)null).OfType<BrowserDataKind>().ToList();
        if (kinds.Count == 0) return null;
        var ids = browserIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keep = Settings.BrowserKeepSites.ToList();
        var places = WindowsPlaces.Current(AppPaths.Dir);
        var result = await Task.Run(() =>
        {
            var running = BrowserCleaner.RunningIds();
            var profiles = BrowserProfiles.Discover(places, new WindowsCleanSystem())
                .Where(p => (ids is null || ids.Contains(p.Browser.Id)) && !running.Contains(p.Browser.Id)).ToList();
            if (profiles.Count == 0) return null;
            if (Settings.SimulationMode)
                return new BrowserCleanResult(profiles.Sum(p => BrowserCleaner.Estimate(p, kinds, keep)), 0, 0, []);
            return BrowserCleaner.Clean(profiles, kinds, keep);
        });
        if (result is null) return null;
        string detail = Loc.F("{0} liberados em dados de navegadores.", Cleaner.FormatBytes(result.Bytes));
        if (Settings.SimulationMode) Notify("Simulação de limpeza", Loc.F("Seriam liberados até {0} de dados de navegadores. Nada foi apagado.", Cleaner.FormatBytes(result.Bytes)));
        else
        {
            ActionLog.Add(Loc.T("Limpeza de navegadores"), detail);
            if (!quiet || result.Bytes > 0) Notify("Navegadores limpos", detail, ("Ver ações", Tab(AppTabs.Actions)));
        }
        return result;
    }

    /// <summary>Limpeza rápida de temporários. A Lixeira só é esvaziada pela aba Limpeza, com confirmação.</summary>
    public async Task<long> QuickCleanAsync(bool notify)
    {
        int days = Settings.TempDays;
        if (Settings.SimulationMode)
        {
            // A38: só mede; nada é apagado nem registrado
            long would = await Task.Run(() => Cleaner.SafeJunkBytes(days));
            if (notify) Notify("Simulação de limpeza", Loc.F("Seriam liberados cerca de {0} de temporários com mais de {1} dias. Nada foi apagado.", Cleaner.FormatBytes(would), days));
            return 0;
        }
        long freed = await Task.Run(() => Cleaner.QuickClean(days));
        ActionLog.Add(Loc.T("Limpeza de temporários"), Loc.F("{0} liberados (arquivos com mais de {1} dias)", Cleaner.FormatBytes(freed), days));
        _ = RefreshHealthAsync();
        if (notify) Notify("Limpeza concluída", Loc.F("{0} liberados de temporários antigos.", Cleaner.FormatBytes(freed)), ("Ver ações", Tab(AppTabs.Actions)));
        return freed;
    }

    /// <summary>Fecha este processo e abre outro com privilégios de administrador (se o usuário aceitar o UAC).</summary>
    public void RestartAsAdmin()
    {
        if (AppInfo.RestartElevated()) Quit();
    }

    // ---------- M13: idioma ----------
    /// <summary>Idioma, formato de números e datas e tradução das telas. Roda uma vez, antes de abrir qualquer janela.</summary>
    private void ApplyLanguage()
    {
        // Só no modo demonstração: as capturas de tela de divulgação saem no idioma pedido, e não no do Windows da máquina
        string? forcado = AppPaths.Demo ? Environment.GetEnvironmentVariable("NOTEBOOKDOCTOR_DEMO_LANG") : null;
        Loc.Use(Loc.Resolve(string.IsNullOrEmpty(forcado) ? Settings.Language : forcado, System.Globalization.CultureInfo.InstalledUICulture.Name));
        var culture = Loc.Culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        Localizer.Install();
        LiveRegions.Install(); // U4: textos marcados como região viva são lidos pelo leitor de tela quando mudam
        WindowFit.Install(); // U2: janelas maiores que a área útil do monitor encolhem para caber
    }

    /// <summary>Reabre o app (troca de idioma). A instância nova espera esta fechar.</summary>
    public void Restart()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,
                $"--wait-parent={Environment.ProcessId}" + (AppPaths.Demo ? " --demo" : "")) { UseShellExecute = false });
            Quit();
        }
        catch { /* sem permissão para iniciar: a pessoa reabre pelo atalho */ }
    }

    // ---------- B26: modo demonstração ----------
    private int _demoTick;

    /// <summary>Começa do zero a cada abertura: apaga a pasta da demonstração anterior e liga a simulação.</summary>
    private static AppSettings StartDemo()
    {
        foreach (var f in Directory.GetFiles(AppPaths.Dir))
            try { File.Delete(f); } catch { }
        var s = new AppSettings
        {
            FirstRunDone = true, SimulationMode = true, CheckUpdates = false, GlobalHotkey = false, NativeNotifications = false,
            WeeklyCleanup = false, StartWithWindows = false, LastWeeklySummary = DateTime.Now, LastUpdateCheck = DateTime.Now,
        };
        s.Save();
        return s;
    }

    // ---------- M03: overlay em jogos ----------
    private OverlayWindow? _overlay;
    private readonly System.Windows.Threading.DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>A cada 2 s confere se tem jogo ou app em tela cheia e mostra ou esconde o overlay no monitor dele.</summary>
    private void UpdateOverlay()
    {
        // Desligado (o padrão) nem consulta o Windows
        bool show = Settings.OverlayMode != Overlay.Off && Latest is not null && Overlay.ShouldShow(Settings.OverlayMode, Native.NotificationState());
        if (!show) { _overlay?.Hide(); return; }
        if (_overlay is null) { _overlay = new OverlayWindow(); }
        if (!_overlay.IsVisible) { _overlay.Update(Latest!); _overlay.Show(); _overlay.UpdateLayout(); } // tamanho certo antes de posicionar
        if (Native.ForegroundMonitor() is { } monitor) _overlay.PlaceOn(monitor, Settings.OverlayCorner);
    }

    /// <summary>B48: atalhos no clique direito do ícone na barra de tarefas e no Menu Iniciar. Cada um abre o app na aba certa.</summary>
    private void SetJumpList()
    {
        if (Environment.ProcessPath is not { } exe) return;
        System.Windows.Shell.JumpTask Item(string title, int tab, string description) => new()
        {
            Title = title, Description = description, Arguments = $"--tab={AppTabs.Name(tab)}",
            ApplicationPath = exe, IconResourcePath = exe, IconResourceIndex = 0,
        };
        try
        {
            var list = new System.Windows.Shell.JumpList(new System.Windows.Shell.JumpItem[]
            {
                Item("Limpeza", AppTabs.Clean, "Ver o que pode ser limpo"),
                Item("Processos", AppTabs.Processes, "Ver os programas em execução"),
                Item("Histórico", AppTabs.History, "Ver o uso dos últimos dias"),
            }, showFrequent: false, showRecent: false);
            System.Windows.Shell.JumpList.SetJumpList(this, list);
            list.Apply();
        }
        catch (Exception ex) { LogError(ex); } // a jump list é só conveniência
    }

    /// <summary>Fecha para o instalador poder substituir o executável.</summary>
    public void QuitForUpdate() => Quit();

    /// <summary>Verdadeiro durante o encerramento: a janela deixa de se esconder na bandeja e fecha de fato.</summary>
    public bool Quitting { get; private set; }

    // ---------- B38: senha das configurações ----------
    public LockSession Lock { get; } = new();

    /// <summary>Hash que vale agora: o da política da empresa (HKLM) ou o definido no app. Null = sem senha.</summary>
    public string? LockHash => AppPaths.Demo ? null
        : SettingsLockPolicy.Read() ?? (PasswordHash.IsValidFormat(Settings.SettingsLock) ? Settings.SettingsLock : null);

    public bool LockFromPolicy => !AppPaths.Demo && SettingsLockPolicy.Read() is not null;

    /// <summary>Sem senha ou já liberado, devolve true na hora; senão pede a senha. "what" completa "Para …, digite a senha".</summary>
    public bool EnsureUnlocked(string what)
    {
        if (LockHash is not { } hash || Lock.IsUnlocked(DateTime.Now)) return true;
        var owner = MainWindow is { IsVisible: true } w ? w : null;
        bool ok = Dialogs.AskPassword(owner, what, pw =>
        {
            var now = DateTime.Now;
            return Lock.TryUnlock(pw, hash, now) switch
            {
                UnlockResult.Ok => null,
                UnlockResult.Blocked => Loc.F("Muitas tentativas erradas. Espere {0:0} segundos e tente de novo.", Math.Ceiling(Lock.BlockedFor(now).TotalSeconds)),
                _ => Loc.T("Senha incorreta."),
            };
        });
        if (ok) (MainWindow as NotebookDoctor.MainWindow)?.RefreshLockCard();
        return ok;
    }

    /// <summary>B10: "fechar encerra o app".</summary>
    public void ExitApp() => Quit();

    private void Quit()
    {
        if (Quitting) return;
        Quitting = true;
        (MainWindow as NotebookDoctor.MainWindow)?.SavePlacement();
        if (Focus.Active) Task.Run(Focus.StopAsync).Wait(3000); // devolve as prioridades e o plano de energia
        _cts.Cancel();
        _mini?.Close();
        _overlayTimer.Stop();
        _overlay?.Close();
        _hotkey.Dispose();
        _tray.Dispose();
        _monitor.Dispose();
        _single?.ReleaseMutex();
        Shutdown();
    }

    private static void LogError(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(AppPaths.Dir, "erros.log"), $"{DateTime.Now:s} {ex}\n\n"); } catch { }
        UsageStats.Failure(); // M18: só a contagem, se a pessoa ligou as estatísticas; o texto do erro nunca sai do computador
    }
}
