using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class App
{
    private readonly List<Snapshot> _recent = new();           // ~últimos 3 minutos ao vivo
    private readonly Sustained _cpuHigh = new(), _ramHigh = new();
    private DateTime _lastAlert = DateTime.MinValue, _lastDiskAlert = DateTime.MinValue, _lastHeatAlert = DateTime.MinValue;
    private DateTime _lastProbe = DateTime.MinValue;
    private bool _probing;
    private readonly Dictionary<string, DateTime> _heavySince = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _heavyIgnored = new(StringComparer.OrdinalIgnoreCase);
    private bool _callWarned;
    private bool _hotspotWarned;
    private DateTime _lastMobileCheck = DateTime.MinValue;

    // Saúde (dados lentos), nota e alerta de programa pesado
    public HealthInfo Health { get; private set; } = HealthInfo.Empty;
    public HealthScoreResult Score { get; private set; } = new(0, "Analisando");
    public HeavyAlertInfo? HeavyAlert { get; private set; }
    public MemoryTrend Memory { get; } = new();
    public FocusMode Focus { get; } = new();
    public event Action? HealthUpdated, HeavyAlertChanged;

    // ---------- Leva 12.1: câmera e microfone ----------
    public IReadOnlyList<MediaUse> Media { get; private set; } = [];
    public bool InCall => Media.Any(m => m.InUse);
    /// <summary>Leva 12.1: quem está usando câmera ou microfone mudou ("Câmera: Zoom · Microfone: Zoom", ou vazio).</summary>
    public event Action<string>? MediaChanged;

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
                var powerNow = Native.PowerStatus();
                History.Record(snap, Score.Score, powerNow.Percent >= 0 ? powerNow.OnBattery : null);
                ApplyAutoEco(snap);
                CheckMedia(snap);
                if ((DateTime.Now - _lastMobileCheck).TotalSeconds >= 60) { _lastMobileCheck = DateTime.Now; CheckMobileData(snap); }
                _tray.Update(snap, Score.Score, Settings.TrayIcon);
                if (Settings.ThemeMode == ThemeSchedule.Mode && Settings.EffectiveTheme(DateTime.Now) != _appliedTheme) ApplyTheme(); // B37
                CheckAlerts(snap);
                CheckHeavyPrograms(snap);
                CheckHeat(snap);
                CheckWeeklySummary();
                Memory.Feed(snap);
                if ((DateTime.Now - _lastBattery).TotalSeconds >= 30) await ReadBatteryAsync();
                if ((DateTime.Now - _lastProbe).TotalMinutes >= 30) _ = RefreshHealthAsync();
                await CheckWeeklyAsync();
                await CheckJunkMonitorAsync();
                await CheckBrowserCloseAsync();
                SnapshotReady?.Invoke(snap);
                if (_overlay is { IsVisible: true }) _overlay.Update(snap);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogError(ex, "RunLoop"); }

            // M42: 3 s à vista; em segundo plano, 15 s na tomada e 30–60 s na bateria
            var power = Native.PowerStatus();
            var wait = Sampling.Interval(WindowVisible || _overlay is { IsVisible: true }, power.OnBattery, power.Saver, power.Percent);
            try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void CheckMedia(Snapshot snap)
    {
        Media = AppPaths.Demo ? DemoMedia() : MediaProbe.Uses();
        string text = Meeting.InUseText(Media);
        _tray.SetMediaInUse(text);
        MediaChanged?.Invoke(text);
        if (!InCall) { _callWarned = false; return; }
        // Uma vez por chamada: downloads de fundo puxando a rede enquanto a câmera ou o microfone estão abertos
        var busy = Meeting.BackgroundDownloads(snap.Groups, InCall);
        if (busy.Count > 0 && !_callWarned)
        {
            _callWarned = true;
            Notify(Loc.T("Downloads de fundo durante a chamada"),
                   Loc.F("{0} está baixando enquanto você está em chamada. Em Sistema › Videochamadas dá para pôr no modo eficiência.", busy[0].Name));
        }
    }

    private static List<MediaUse> DemoMedia() =>
        [new("webcam", "Zoom.exe", DateTime.Now.AddMinutes(-12), null), new("microphone", "Zoom.exe", DateTime.Now.AddMinutes(-12), null),
         new("microphone", "Microsoft.WindowsSoundRecorder", DateTime.Now.AddHours(-3), DateTime.Now.AddHours(-3).AddMinutes(4))];

    /// <summary>
    /// Na internet do celular (rede limitada ou com cara de hotspot), avisa uma vez por conexão quando programas em segundo
    /// plano estão gastando os dados.
    /// </summary>
    private void CheckMobileData(Snapshot snap)
    {
        bool metered = false;
        try
        {
            var profile = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            var cost = profile?.GetConnectionCost();
            metered = cost is not null && (cost.NetworkCostType is global::Windows.Networking.Connectivity.NetworkCostType.Fixed or global::Windows.Networking.Connectivity.NetworkCostType.Variable || cost.Roaming);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        var adapter = AppPaths.Demo ? null : NetProbe.ActiveAdapter();
        string? ssid = AppPaths.Demo ? null : SystemProbe.Wifi()?.Ssid;
        bool mobile = metered || NetworkPrivacy.LooksLikeHotspot(ssid, adapter?.Gateway);
        if (!mobile) { _hotspotWarned = false; return; }
        var hogs = NetworkPrivacy.DataHogs(snap.Groups, mobile);
        if (hogs.Count == 0 || _hotspotWarned) return;
        _hotspotWarned = true;
        Notify(Loc.T("Usando a internet do celular"),
               Loc.F("{0} está baixando em segundo plano e gastando a franquia.", hogs[0].Name) + " "
               + (metered ? Loc.T("Feche o programa se não precisar dele agora.") : Loc.T("Marque esta rede como conexão limitada (Configurações › Wi-Fi › propriedades da rede) para o Windows segurar as atualizações.")));
    }

    /// <summary>Aviso de problema: entra no histórico de alertas (M46) e vira notificação.</summary>
    private void Alert(string title, string text, params (string label, string action)[] buttons)
    {
        (title, text) = (Loc.T(title), Loc.T(text)); // o registro de avisos guarda o texto no idioma em que foi mostrado
        bool quiet = IsQuietNow();
        AlertLog.Add(title, text, silenced: quiet);
        if (!quiet) Notify(title, text, buttons);
    }

    /// <summary>M43/M44: horário silencioso ou alguém em tela cheia, jogo ou apresentação.</summary>
    private bool IsQuietNow() => Presenting || Settings.InQuietHours(DateTime.Now) || (Settings.QuietFullscreen && Native.IsFullscreenOrPresenting());

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
        catch (Exception ex) { LogError(ex, "RefreshHealth"); }
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
}

