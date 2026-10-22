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
        _tray.StartWithWindowsToggled += v => { Settings.StartWithWindows = v; Settings.ApplyStartWithWindows(); Settings.Save(); };
        _tray.MiniToggled += SetMiniWidget;

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
        // Atalho: --tab=N abre direto na aba N (0 = Painel). Útil para testes e atalhos do Windows.
        var tabArg = e.Args.FirstOrDefault(a => a.StartsWith("--tab="));
        if (tabArg is not null && int.TryParse(tabArg[6..], out int tab)) win.SelectTab(tab);
        if (uri is not null) HandleUri(uri);

        // A jump list e o registro das notificações são do app instalado, não da demonstração
        if (!AppPaths.Demo) { _ = Task.Run(Toasts.EnsureRegistered); SetJumpList(); }
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
            Notify("Nova versão do Notebook Doctor", $"Versão {info.Version} disponível. {info.Notes}".Trim(), ("Ver atualização", "update"));
        }
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
        else if (!IsQuietNow()) Notify(s.Title, s.Reason, ("Ver energia", $"tab/{Diagnosis.TabSettings}"));
    }

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
            case "heavy": win.SelectTab(0); break;
            case "report": win.ExportReport(); break;
            case "update": win.SelectTab(8); win.OfferUpdate(); break;
            case "protection": AppInfo.OpenShell("SystemPropertiesProtection.exe"); break; // só abre a tela do Windows
            case var t when t.StartsWith("tab/") && int.TryParse(t[4..], out int i): win.SelectTab(i); break;
        }
    }

    /// <summary>Notificação nativa com botões; se o Windows não aceitar, usa o balão da bandeja.</summary>
    /// <summary>Aviso de problema: entra no histórico de alertas (M46) e vira notificação.</summary>
    private void Alert(string title, string text, params (string label, string action)[] buttons)
    {
        bool quiet = IsQuietNow();
        AlertLog.Add(title, text, silenced: quiet);
        if (!quiet) Notify(title, text, buttons);
    }

    /// <summary>M43/M44: horário silencioso ou alguém em tela cheia, jogo ou apresentação.</summary>
    private bool IsQuietNow() => Settings.InQuietHours(DateTime.Now) || (Settings.QuietFullscreen && Native.IsFullscreenOrPresenting());

    public void Notify(string title, string text, params (string label, string action)[] buttons)
    {
        if (Settings.NativeNotifications && !AppPaths.Demo && Toasts.Show(title, text, buttons)) return;
        _tray.Notify(title, text);
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
               "O relatório ficou guardado sem dados pessoais e vai no pacote para o suporte, se você quiser enviar.",
               ("Ver em Configurações", $"tab/{Diagnosis.TabSettings}"));
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
                if (_recent.Count > 36) _recent.RemoveAt(0);
                Score = Insights.Score(_recent, Health);
                History.Record(snap, Score.Score);
                _tray.Update(snap, Score.Score, Settings.TrayCpu);
                if (Settings.ThemeMode == ThemeSchedule.Mode && Settings.EffectiveTheme(DateTime.Now) != _appliedTheme) ApplyTheme(); // B37
                CheckAlerts(snap);
                CheckHeavyPrograms(snap);
                CheckHeat(snap);
                CheckWeeklySummary();
                Memory.Feed(snap);
                if ((DateTime.Now - _lastProbe).TotalMinutes >= 30) _ = RefreshHealthAsync();
                await CheckWeeklyAsync();
                SnapshotReady?.Invoke(snap);
                if (_overlay is { IsVisible: true }) _overlay.Update(snap);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogError(ex); }

            // Janela aberta: atualiza a cada 3 s. Em segundo plano: a cada 15 s para quase não gastar CPU.
            try { await Task.Delay(WindowVisible || _overlay is { IsVisible: true } ? 3000 : 15000, ct); } catch (OperationCanceledException) { break; }
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
            Alert("Programa pesado", $"{next.Name} usa {next.CpuPct:0}% da CPU há mais de 3 minutos.", ("Abrir painel", "heavy"));
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
            msg = $"Temperatura em {Units.Temp(s.TempC)} há 2 minutos.";
        else
        {
            var loaded = _recent.TakeLast(12).Where(x => x.Cpu >= 70 && x.CpuPerf >= 0).ToList();
            if (loaded.Count >= 8 && loaded.Average(x => x.CpuPerf) < 60)
                msg = $"O processador está rodando a {loaded.Average(x => x.CpuPerf):0}% da velocidade máxima mesmo sob carga — sinal de calor ou limite de energia.";
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
        string text = (scores.Count > 0 ? $"Nota média {scores.Average():0}. " : "") +
                      $"CPU média {week.Average(w => w.Cpu):0}%, RAM média {week.Average(w => w.Ram):0}%. Mais pesados: {string.Join(", ", top)}.";
        Notify("Resumo da semana", text, ("Ver relatório", "report"), ("Abrir histórico", "tab/5"));
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
            Alert("Pouco espaço no disco", $"Só {s.DiskFreePct:0}% livre ({Units.Gb(s.DiskFreeGB, "0")}).", ("Abrir Limpeza", "tab/2"), ("Ver o que ocupa", "tab/3"));
        }
        string? msg = null;
        int minutes = Settings.AlertMinutes;
        if (_cpuHigh.Update(s.Cpu >= Settings.CpuAlertPct, s.Time, minutes))
        {
            var top = s.TopByCpu.FirstOrDefault();
            if (top is not null && Settings.IsAlertException(top.Name)) top = null; // carga esperada: não avisa
            else msg = $"CPU em {s.Cpu:0}% há {Pt.N(minutes, "minuto", "minutos")} ou mais." + (top is null ? "" : $" Maior consumo: {top.Name} ({top.CpuPct:0}%).");
        }

        if (_ramHigh.Update(s.RamPct >= Settings.RamAlertPct, s.Time, minutes) && msg is null)
        {
            var top = s.TopByMem.FirstOrDefault();
            if (top is null || !Settings.IsAlertException(top.Name)) msg = $"Memória em {s.RamPct:0}%." + (top is null ? "" : $" Maior consumo: {top.Name} ({top.MemMB} MB).");
        }
        if (msg is not null && (DateTime.Now - _lastAlert).TotalMinutes >= 30)
        {
            _lastAlert = DateTime.Now;
            Alert(msg.StartsWith("CPU") ? "CPU alta" : "Memória alta", msg, ("Abrir painel", "open"), ("Ver processos", "tab/1"));
        }
    }

    private async Task CheckWeeklyAsync()
    {
        if (AppPaths.Demo || !Settings.WeeklyCleanup || (DateTime.Now - Settings.LastWeeklyRun).TotalDays < 7) return;
        // Manutenção em horário ocioso: só roda quando ninguém mexe no computador há 10 minutos (e não durante o Modo Foco)
        if (Native.IdleTime() < TimeSpan.FromMinutes(10) || Focus.Active) return;
        Settings.LastWeeklyRun = DateTime.Now;
        Settings.Save();
        await QuickCleanAsync(notify: true);
    }

    /// <summary>Limpeza rápida de temporários. A Lixeira só é esvaziada pela aba Limpeza, com confirmação.</summary>
    public async Task<long> QuickCleanAsync(bool notify)
    {
        int days = Settings.TempDays;
        if (Settings.SimulationMode)
        {
            // A38: só mede; nada é apagado nem registrado
            long would = await Task.Run(() => Cleaner.SafeJunkBytes(days));
            if (notify) Notify("Simulação de limpeza", $"Seriam liberados cerca de {Cleaner.FormatBytes(would)} de temporários com mais de {days} dias. Nada foi apagado.");
            return 0;
        }
        long freed = await Task.Run(() => Cleaner.QuickClean(days));
        ActionLog.Add("Limpeza de temporários", $"{Cleaner.FormatBytes(freed)} liberados (arquivos com mais de {days} dias)");
        _ = RefreshHealthAsync();
        if (notify) Notify("Limpeza concluída", $"{Cleaner.FormatBytes(freed)} liberados de temporários antigos.", ("Ver ações", "tab/6"));
        return freed;
    }

    /// <summary>Fecha este processo e abre outro com privilégios de administrador (se o usuário aceitar o UAC).</summary>
    public void RestartAsAdmin()
    {
        if (AppInfo.RestartElevated()) Quit();
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
            Title = title, Description = description, Arguments = $"--tab={tab}",
            ApplicationPath = exe, IconResourcePath = exe, IconResourceIndex = 0,
        };
        try
        {
            var list = new System.Windows.Shell.JumpList(new System.Windows.Shell.JumpItem[]
            {
                Item("Limpeza", 2, "Ver o que pode ser limpo"),
                Item("Processos", 1, "Ver os programas em execução"),
                Item("Histórico", 5, "Ver o uso dos últimos dias"),
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
    }
}
