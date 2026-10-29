using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record HeavyAlertInfo(string Name, double CpuPct, long MemMB, DateTime Since);

public partial class App : Application
{
    private SingleInstanceIpc? _singleIpc;
    private readonly CancellationTokenSource _cts = new();
    private SystemMonitor _monitor = null!;
    private TrayIcon _tray = null!;
    private MiniWidget? _mini;
    private readonly GlobalHotkey _hotkey = new();
    private OverlayWindow? _overlay;
    private readonly System.Windows.Threading.DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _appliedTheme = "";
    private int _demoTick;
    private bool _presentationHidIcons, _presentationStartedFocus;

    public AppSettings Settings { get; private set; } = new();
    public HistoryStore History { get; private set; } = null!;
    public Snapshot? Latest { get; private set; }
    public IReadOnlyList<Snapshot> Recent => _recent;
    // O mini-widget também precisa de números a cada 3 s
    public bool WindowVisible => MainWindow is { IsVisible: true } || _mini is { IsVisible: true };
    public UpdateInfo? PendingUpdate { get; private set; }
    public event Action<Snapshot>? SnapshotReady;
    /// <summary>Leva 11.2: o notebook saiu (false) ou voltou (true) para a tomada.</summary>
    public event Action<bool>? PowerSourceChanged;
    public event Action<bool>? MiniChanged;
    public event Action<bool>? PresentationChanged;

    public bool HotkeyActive => _hotkey.IsRegistered;
    public bool Presenting { get; private set; }
    /// <summary>Verdadeiro durante o encerramento: a janela deixa de se esconder na bandeja e fecha de fato.</summary>
    public bool Quitting { get; private set; }
    public LockSession Lock { get; } = new();

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Reinício como administrador: espera a instância anterior fechar antes de assumir a instância única
        SingleInstanceIpc.WaitParentExit(e.Args);
        string? uri = e.Args.FirstOrDefault(a => a.StartsWith(Toasts.Scheme + ":", StringComparison.OrdinalIgnoreCase));
        // A demonstração é uma instância à parte: abre mesmo com o app de verdade rodando
        _singleIpc = SingleInstanceIpc.Acquire(AppPaths.Demo);
        if (!_singleIpc.IsFirstInstance)
        {
            // Já está rodando: repassa o pedido (abrir o painel ou o link clicado na notificação) para a instância aberta
            SingleInstanceIpc.TrySend(_singleIpc.PipeName, SingleInstanceIpc.BuildActivationMessage(e.Args, Toasts.Scheme));
            _singleIpc.Dispose();
            _singleIpc = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, a) => { LogError(a.Exception, "Dispatcher"); CrashLog.Save(a.Exception, "tela (o app continuou aberto)"); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is not Exception ex) return;
            LogError(ex, "AppDomain");
            CrashLog.Save(ex, "segundo plano (o app fechou)");
        };
        TaskScheduler.UnobservedTaskException += (_, a) => { LogError(a.Exception, "UnobservedTask"); CrashLog.Save(a.Exception, "tarefa em segundo plano"); a.SetObserved(); };

        Settings = AppPaths.Demo ? StartDemo() : AppSettings.Load();
        ApplyLanguage();
        CrashLog.Enabled = Settings.CrashReports;
        Settings.ApplyUnits();
        ThemeManager.UseAccent = Settings.UseAccentColor;
        ThemeManager.PureBlack = Settings.PureBlack;
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
        try { BatteryLog = BatteryHistory.Parse(File.Exists(BatteryFile) ? File.ReadAllText(BatteryFile) : null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
        _tray.PresentationToggled += on => _ = SetPresentationAsync(on);
        _tray.UnlockRequested += () => { ShowWindow(); (MainWindow as NotebookDoctor.MainWindow)?.OpenFileUnlock(null); };
        _tray.EjectRequested += letter => _ = EjectAsync(letter);
        _tray.PowerPlanRequested += SetPowerPlanFromTray;
        _tray.ClearClipboardRequested += ClearClipboard;
        _tray.MiddleClicked += () => RunMiddleAction(Settings.TrayMiddle);

        var win = new MainWindow();
        MainWindow = win;
        if (e.Args.Contains("--background")) { }
        if (e.Args.Contains("--background") && Settings.DelayedStartup.Count > 0 && !AppPaths.Demo) _ = LaunchDelayedAsync(); // leva 11.3
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
        _ = Task.Run(() => { try { new BackupStore(AppPaths.Backups).Prune(DateTime.Now); } catch (Exception ex) { LogError(ex, "BackupStore.Prune"); } }); // Fase 5: backups vencidos
        _overlayTimer.Tick += (_, _) => UpdateOverlay();
        _overlayTimer.Start();
        string pipeName = _singleIpc.PipeName;
        _ = SingleInstanceIpc.ListenAsync(pipeName, msg => Dispatcher.BeginInvoke(() => HandleUri(msg)), ex => LogError(ex, "SingleInstanceIpc"), _cts.Token);
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

    // ---------- M01: atalho global ----------
    /// <summary>Liga ou desliga o atalho conforme a configuração. Retorna false se outro programa já usa a combinação.</summary>
    public bool ApplyHotkey()
    {
        if (!Settings.GlobalHotkey) { _hotkey.Unregister(); return true; }
        return _hotkey.Register();
    }

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
        var (result, info) = await Updater.CheckAsync(AppInfo.Version);
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
            case "protection": AppInfo.OpenShell(SystemExe.System32("SystemPropertiesProtection.exe")); break; // só abre a tela do Windows
            case var t when t.StartsWith("tab/") && AppTabs.Parse(t[4..]) is int i: win.SelectTab(i); break;
        }
    }

    // ---------- Leva 12.3: modo apresentação ----------
    /// <summary>
    /// Para compartilhar a tela: avisos do app em silêncio, ícones da área de trabalho escondidos, tela sem apagar e Modo Foco.
    /// Desligar devolve cada coisa como estava; sair do app também.
    /// </summary>
    public async Task SetPresentationAsync(bool on)
    {
        if (on == Presenting) return;
        if (on && Settings.SimulationMode) { Notify(Loc.T("Simulação: modo apresentação"), Loc.T("Nada foi alterado.")); _tray.SyncPresentation(false); return; }
        Presenting = on;
        if (!AppPaths.Demo)
        {
            PrivacyProbe.KeepAwake(on);
            if (on) _presentationHidIcons = PrivacyProbe.DesktopIconsVisible() && PrivacyProbe.ToggleDesktopIcons();
            else if (_presentationHidIcons) { PrivacyProbe.ToggleDesktopIcons(); _presentationHidIcons = false; }
            if (on && !Focus.Active && Latest is { } snap) { await Focus.StartAsync(snap, Health); _presentationStartedFocus = true; }
            else if (!on && _presentationStartedFocus) { if (Focus.Active) await Focus.StopAsync(); _presentationStartedFocus = false; }
        }
        _tray.SyncPresentation(on);
        ActionLog.Add(on ? "Modo apresentação ligado" : "Modo apresentação desligado",
                      on ? Loc.T("Avisos em silêncio, ícones da área de trabalho escondidos, tela sempre acesa e Modo Foco") : Loc.T("Tudo voltou como estava"));
        PresentationChanged?.Invoke(on);
    }

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
        ThemeManager.PureBlack = Settings.PureBlack;
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Sem permissão para iniciar: a pessoa reabre pelo atalho
        }
    }

    // ---------- B26: modo demonstração ----------
    /// <summary>Começa do zero a cada abertura: apaga a pasta da demonstração anterior e liga a simulação.</summary>
    private static AppSettings StartDemo()
    {
        foreach (var f in Directory.GetFiles(AppPaths.Dir))
        {
            try { SafeDelete.FileAtExactPath(f, ignoreReadOnly: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var s = new AppSettings
        {
            FirstRunDone = true, SimulationMode = true, CheckUpdates = false, GlobalHotkey = false, NativeNotifications = false,
            WeeklyCleanup = false, StartWithWindows = false, LastWeeklySummary = DateTime.Now, LastUpdateCheck = DateTime.Now,
        };
        s.Save();
        return s;
    }

    // ---------- M03: overlay em jogos ----------
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
        catch (Exception ex) { LogError(ex, "JumpList"); } // a jump list é só conveniência
    }

    /// <summary>Fecha para o instalador poder substituir o executável.</summary>
    public void QuitForUpdate() => Quit();

    // ---------- B38: senha das configurações ----------
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
        if (Presenting && _presentationHidIcons) PrivacyProbe.ToggleDesktopIcons(); // leva 12.3: os ícones voltam
        if (Focus.Active) Task.Run(Focus.StopAsync).Wait(3000); // devolve as prioridades e o plano de energia
        _cts.Cancel();
        _mini?.Close();
        _overlayTimer.Stop();
        _overlay?.Close();
        _hotkey.Dispose();
        _tray.Dispose();
        _monitor.Dispose();
        _singleIpc?.Dispose();
        _singleIpc = null;
        Shutdown();
    }

    internal static void LogError(Exception ex, string? context = null)
    {
        ErrorJournal.Record(ex, context, AppPaths.Dir);
        UsageStats.Failure(); // M18: só a contagem, se a pessoa ligou as estatísticas; o texto do erro nunca sai do computador
    }
}
