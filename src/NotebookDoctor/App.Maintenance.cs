using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class App
{
    private bool _onAc;
    private readonly List<BatteryReading> _battery = new();
    private DateTime _lastBattery = DateTime.MinValue, _lastBatterySave = DateTime.MinValue;
    public BatteryReading? Battery => _battery.Count > 0 ? _battery[^1] : null;
    public IReadOnlyList<BatteryReading> BatteryRecent => _battery;
    public BatteryHistory BatteryLog { get; private set; } = new();
    /// <summary>Leva 11.3: fabricante do notebook, para dizer onde fica o limite de carga.</summary>
    public string Manufacturer { get; private set; } = "";
    private static string BatteryFile => Path.Combine(AppPaths.Dir, "bateria.json");
    private readonly BrowserWatch _browserWatch = new();
    private bool _browserCleaning, _scheduleRan;
    private DateTime _lastJunkCheck = DateTime.MinValue;
    private readonly HashSet<string> _autoEcoOn = new(StringComparer.OrdinalIgnoreCase);

    // ---------- Leva 7.3 (C12): modo soneca, acordar ao abrir ----------
    public SnoozeStore Snooze { get; } = new(Path.Combine(AppPaths.Dir, "snooze.json"));
    private readonly ISnoozeSystem _snoozeSystem = new WindowsSnoozeSystem();
    private Dictionary<string, int> _snoozeAbsent = new();

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

    /// <summary>Leva 11.1: bateria a cada 30 s (watts, histórico de consumo, carregador) e a volta do Modo sobrevivência na tomada.</summary>
    private async Task ReadBatteryAsync()
    {
        _lastBattery = DateTime.Now;
        var b = AppPaths.Demo ? DemoData.Battery(DateTime.Now, _demoTick) : await Task.Run(PowerProbe.ReadBattery);
        if (b is null) return;
        if (Manufacturer.Length == 0)
            Manufacturer = AppPaths.Demo ? "LENOVO" : await Task.Run(() => WmiReader.Query("SELECT Manufacturer FROM Win32_ComputerSystem").Select(m => WmiReader.Str(m, "Manufacturer")).FirstOrDefault() ?? "");
        bool? wasOnAc = _battery.Count > 0 ? _battery[^1].OnAc : null;
        _battery.Add(b);
        if (_battery.Count > 20) _battery.RemoveAt(0);
        if (wasOnAc is { } before && before != b.OnAc)
        {
            ApplyDynamicHz(b.OnAc);
            PowerSourceChanged?.Invoke(b.OnAc);
        }
        if (BatteryLog.Add(b) && (DateTime.Now - _lastBatterySave).TotalMinutes >= 5 && !AppPaths.Demo)
        {
            _lastBatterySave = DateTime.Now;
            try { AtomicFile.WriteAllText(BatteryFile, BatteryLog.Serialize()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (b.OnAc && SurvivalState.Parse(Settings.SurvivalState) is { } state)
            EndSurvival(state, Loc.T("Na tomada: o Modo sobrevivência foi desligado e tudo voltou como estava."));
    }

    /// <summary>Leva 11.3, tarefa 14: abre, 2 minutos depois de entrar no Windows, os itens de inicialização atrasados.</summary>
    private async Task LaunchDelayedAsync()
    {
        await Task.Delay(DelayedStartupItem.Delay);
        foreach (var item in Settings.DelayedStartup.ToList())
        {
            var (exe, args) = CommandLine.Split(item.Command);
            if (exe is null || !File.Exists(exe)) continue;
            try
            {
                // Atalho da pasta Inicializar abre pelo Windows; .exe direto, sem passar por um shell
                bool shortcut = exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = shortcut, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
            }
            catch (System.ComponentModel.Win32Exception ex) { LogError(ex, "LaunchDelayed"); }
        }
    }

    /// <summary>Leva 11.2, tarefa 08: tela em 60 Hz na bateria, a taxa de antes na tomada.</summary>
    private void ApplyDynamicHz(bool onAc)
    {
        if (!Settings.DynamicHz || AppPaths.Demo || Settings.SurvivalState is not null) return;
        if (PowerProbe.RefreshRates() is not { } rates) return;
        if (PowerAudit.DynamicHzTarget(onAc, rates.Current, rates.Supported, Settings.DynamicHzHigh) is not { } target) return;
        if (!PowerProbe.SetRefreshRate(target)) return;
        Settings.DynamicHzHigh = onAc ? null : rates.Current;
        Settings.Save();
    }

    // Leva 11.2, tarefa 10: programas marcados entram no modo eficiência fora de foco e saem ao voltar ao foco
    private void ApplyAutoEco(Snapshot snap)
    {
        if (Settings.AutoEcoApps.Count == 0 && _autoEcoOn.Count == 0) return;
        if (AppPaths.Demo || Settings.SimulationMode) return;
        string? foreground = null;
        try { using var p = System.Diagnostics.Process.GetProcessById(Native.ForegroundProcessId()); foreground = p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        var running = snap.Groups.Select(g => g.Name).ToList();
        // Desmarcado na lista: sai do modo eficiência na hora
        foreach (var gone in _autoEcoOn.Where(n => !Settings.AutoEcoApps.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            ProcessActions.SetEfficiency(gone, false);
            _autoEcoOn.Remove(gone);
        }
        _autoEcoOn.RemoveWhere(n => !running.Contains(n, StringComparer.OrdinalIgnoreCase));
        var (on, off) = PowerAudit.AutoEco(Settings.AutoEcoApps, running, foreground, _autoEcoOn);
        foreach (var n in on) if (ProcessActions.SetEfficiency(n, true) > 0) _autoEcoOn.Add(n);
        foreach (var n in off) { ProcessActions.SetEfficiency(n, false); _autoEcoOn.Remove(n); }
    }

    /// <summary>Devolve o que o Modo sobrevivência mudou e avisa.</summary>
    public void EndSurvival(SurvivalState state, string message)
    {
        Survival.Restore(state);
        Settings.SurvivalState = null;
        Settings.Save();
        ActionLog.Add("Modo sobrevivência desligado", message);
        Notify(Loc.T("Modo sobrevivência"), message);
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
            catch (Exception ex) { LogError(ex, "SnoozeWatch"); }
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
}

