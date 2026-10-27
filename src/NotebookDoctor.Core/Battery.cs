namespace NotebookDoctor.Services;

/// <summary>
/// Uma leitura da bateria. <paramref name="RateW"/> é positivo carregando e negativo descarregando (0 = o Windows não
/// informou). Energias em watt-hora.
/// </summary>
public sealed record BatteryReading(DateTime Time, bool OnAc, bool Charging, double RateW, double RemainingWh, double FullWh, double DesignWh, int Percent)
{
    public bool Discharging => RateW < 0;
}

/// <summary>Leva 11.1: quanto a bateria dura em três usos típicos.</summary>
public sealed record BatteryForecast(TimeSpan Light, TimeSpan Call, TimeSpan Heavy, bool FromHistory);

/// <summary>
/// Fase 11, leva 11.1: regras da bateria e do calor. Watts reais de carga e descarga, carregador fraco, autonomia por uso
/// e quanto de velocidade o processador perdeu por calor.
/// </summary>
public static class BatteryRules
{
    /// <summary>"13,8 W saindo" ou "45 W entrando"; vazio quando o Windows não informa a taxa.</summary>
    public static string RateText(BatteryReading b) =>
        b.RateW > 0.05 ? Loc.F("{0:0.0} W entrando", b.RateW)
        : b.RateW < -0.05 ? Loc.F("{0:0.0} W saindo", -b.RateW)
        : "";

    /// <summary>Carregador abaixo disto, com a bateria longe de cheia, carrega devagar demais.</summary>
    public const double SlowChargeW = 10;

    /// <summary>
    /// Carregador fraco ou incompatível: na tomada há pelo menos 4 leituras seguidas (cerca de 2 min) com a bateria abaixo de
    /// 90% e entrando menos de 10 W, ou saindo carga mesmo ligado na tomada (carregador USB-C de celular num notebook).
    /// </summary>
    public static Finding? SlowCharger(IReadOnlyList<BatteryReading> recent)
    {
        var last = recent.TakeLast(4).ToList();
        if (last.Count < 4 || last.Any(b => !b.OnAc || b.Percent >= 90 || b.RateW == 0)) return null;
        double avg = last.Average(b => b.RateW);
        if (avg < 0)
            return new("PROBLEMA", "A bateria descarrega mesmo na tomada",
                Loc.F("Saindo {0:0.0} W com o carregador ligado", -avg),
                "O carregador não dá conta do notebook: use o carregador original ou um USB-C com potência igual ou maior (veja na etiqueta, em W).");
        if (avg < SlowChargeW)
            return new("ATENCAO", "Carregador lento",
                Loc.F("Só {0:0.0} W entrando, com a bateria em {1}%", avg, last[^1].Percent),
                "Carregadores de celular e cabos USB-C finos carregam devagar. Use o carregador original ou um de potência igual ou maior.");
        return null;
    }

    /// <summary>
    /// Quanto dura o que resta da bateria lendo/no Office, em chamada de vídeo e em uso pesado ou jogo. Com histórico (20
    /// leituras de descarga ou mais), usa o próprio consumo da pessoa (percentis 20, 60 e 95); sem ele, parte do consumo
    /// de agora. Null sem bateria, sem energia restante ou sem nenhuma taxa de descarga conhecida.
    /// </summary>
    public static BatteryForecast? Forecast(double remainingWh, double currentDischargeW, IReadOnlyList<double> dischargeHistoryW)
    {
        if (remainingWh <= 0) return null;
        var history = dischargeHistoryW.Where(w => w is > 0.5 and < 300).OrderBy(w => w).ToList();
        double light, call, heavy;
        bool fromHistory = history.Count >= 20;
        if (fromHistory)
        {
            light = Percentile(history, 0.20);
            call = Math.Max(Percentile(history, 0.60), light * 1.2);
            heavy = Math.Max(Percentile(history, 0.95), call * 1.2);
        }
        else
        {
            if (currentDischargeW <= 0.5) return null;
            light = currentDischargeW * 0.75;
            call = currentDischargeW * 1.3;
            heavy = currentDischargeW * 2.2;
        }
        return new(Hours(remainingWh / light), Hours(remainingWh / call), Hours(remainingWh / heavy), fromHistory);
    }

    private static TimeSpan Hours(double h) => TimeSpan.FromMinutes(Math.Round(Math.Min(h, 48) * 60));

    private static double Percentile(List<double> sorted, double p)
    {
        double idx = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    /// <summary>"4 h 10 min", "45 min".</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalMinutes < 60 ? Loc.F("{0} min", (int)t.TotalMinutes) : Loc.F("{0} h {1:00} min", (int)t.TotalHours, t.Minutes);

    public static string ForecastText(BatteryForecast f) =>
        Loc.F("Dura cerca de {0} lendo ou no Office, {1} em chamada de vídeo e {2} em uso pesado", Duration(f.Light), Duration(f.Call), Duration(f.Heavy));

    // ---------- calor ----------

    /// <summary>
    /// Leva 11.1, tarefa 04: velocidade perdida por calor. Só conta como estrangulamento térmico quando o processador está
    /// quente (85 °C ou mais) e abaixo de 80% da velocidade máxima sob carga; devolve os pontos percentuais perdidos.
    /// </summary>
    public static int? ThermalLossPct(double tempC, double cpuPerfPct, double cpuPct)
    {
        if (tempC < 85 || cpuPerfPct < 0 || cpuPct < 50 || cpuPerfPct >= 80) return null;
        return (int)Math.Round(100 - cpuPerfPct);
    }

    // ---------- Modo Colo ----------

    /// <summary>
    /// Valores do "Modo de aumento de desempenho do processador" (PERFBOOSTMODE) no plano de energia. 0 desliga o turbo:
    /// o processador fica na velocidade base, esquenta e faz bem menos barulho.
    /// </summary>
    public const uint BoostOff = 0;

    public static bool BoostEnabled(uint value) => value != BoostOff;

    // ---------- leva 11.3: guardião de carga ----------

    /// <summary>
    /// Sempre na tomada a 100%: nos 3 dias mais recentes com uso (pelo menos 60 leituras, cerca de meia hora), 70% ou mais
    /// do tempo na tomada com a bateria cheia. Ficar cheia o tempo todo, quente, é o que mais gasta uma bateria de lítio.
    /// </summary>
    public static bool AlwaysPluggedFull(IReadOnlyList<DayUsage> usage)
    {
        var days = usage.Where(u => u.Samples >= 60).OrderByDescending(u => u.Day).Take(3).ToList();
        return days.Count == 3 && days.All(d => d.AcFull >= d.Samples * 0.7);
    }

    /// <summary>Onde ligar o limite de carga em 80% (ou parecido) no aplicativo oficial de cada fabricante.</summary>
    public static string ChargeLimitAdvice(string manufacturer)
    {
        string m = manufacturer.ToLowerInvariant();
        return m switch
        {
            _ when m.Contains("lenovo") => Loc.T("No Lenovo Vantage: Dispositivo › Energia › Modo de conservação da bateria (para em cerca de 80%)."),
            _ when m.Contains("dell") => Loc.T("No Dell Power Manager (ou Dell Optimizer): Bateria › “Uso principalmente na tomada”."),
            _ when m.Contains("asus") => Loc.T("No MyASUS: Personalização › Bateria › Modo de Carga Inteligente para a Saúde da Bateria (“Equilibrado”, 80%)."),
            _ when m.Contains("hp") || m.Contains("hewlett") => Loc.T("No HP: ligue o “Adaptive Battery Optimizer” na BIOS (F10) ou no HP Smart / HP Power Manager, quando houver."),
            _ when m.Contains("acer") => Loc.T("No Acer Care Center (ou AcerSense): Bateria › Limite de carga em 80%."),
            _ when m.Contains("samsung") => Loc.T("No Samsung Settings: Gerenciamento de bateria › Proteger a bateria (85%)."),
            _ when m.Contains("microsoft") => Loc.T("No Surface: Configurações › Sistema › Energia e bateria › “Limite de carga inteligente”."),
            _ => Loc.T("Procure no aplicativo do fabricante do notebook (ou na BIOS) uma opção de limite de carga ou de conservação da bateria."),
        };
    }

    // ---------- sobrevivência ----------

    public const int SurvivalPct = 15;

    /// <summary>Leva 11.1, tarefa 05: oferecer o Modo sobrevivência (na bateria, abaixo de 15%).</summary>
    public static bool OfferSurvival(BatteryReading? b) => b is { OnAc: false } && b.Percent > 0 && b.Percent < SurvivalPct;
}

/// <summary>Uma amostra de consumo na bateria (watts saindo).</summary>
public sealed record DischargeSample(DateTime T, double W);

/// <summary>Uma medida diária da capacidade da bateria (curva de desgaste).</summary>
public sealed record CapacitySample(DateTime Day, double FullWh, double DesignWh)
{
    /// <summary>Saúde: capacidade de hoje sobre a de fábrica, em %.</summary>
    public double HealthPct => DesignWh > 0 ? Math.Round(FullWh * 100 / DesignWh, 1) : 0;
}

/// <summary>Leva 11.3: quantas leituras do dia e quantas foram na tomada com a bateria cheia (98% ou mais).</summary>
public sealed record DayUsage(DateTime Day, int Samples, int AcFull);

/// <summary>
/// Leva 11.1: o consumo na bateria dos últimos 7 dias (para a previsão usar o uso real da pessoa) e uma medida da capacidade
/// por dia. Fica em bateria.json, só no computador.
/// </summary>
public sealed class BatteryHistory
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    public List<DischargeSample> Discharge { get; set; } = [];
    public List<CapacitySample> Capacity { get; set; } = [];
    public List<DayUsage> Usage { get; set; } = [];

    /// <summary>Guarda a leitura; devolve true quando algo mudou.</summary>
    public bool Add(BatteryReading b)
    {
        bool changed = false;
        if (b.Discharging && !b.OnAc)
        {
            Discharge.Add(new DischargeSample(b.Time, -b.RateW));
            changed = true;
        }
        Discharge.RemoveAll(d => b.Time - d.T > Keep);
        // Leva 11.3: quanto do dia a bateria passou cheia na tomada (guarda 14 dias)
        int i = Usage.FindIndex(u => u.Day == b.Time.Date);
        var day = i >= 0 ? Usage[i] : new DayUsage(b.Time.Date, 0, 0);
        day = day with { Samples = day.Samples + 1, AcFull = day.AcFull + (b.OnAc && b.Percent >= 98 ? 1 : 0) };
        if (i >= 0) Usage[i] = day; else Usage.Add(day);
        Usage.RemoveAll(u => b.Time.Date - u.Day > TimeSpan.FromDays(14));
        changed = true;
        if (b.FullWh > 0 && b.DesignWh > 0 && Capacity.All(c => c.Day.Date != b.Time.Date))
        {
            Capacity.Add(new CapacitySample(b.Time.Date, Math.Round(b.FullWh, 2), Math.Round(b.DesignWh, 2)));
            if (Capacity.Count > 3 * 366) Capacity.RemoveRange(0, Capacity.Count - 3 * 366);
            changed = true;
        }
        return changed;
    }

    public IReadOnlyList<double> DischargeWatts() => Discharge.Select(d => d.W).ToList();

    public string Serialize() => System.Text.Json.JsonSerializer.Serialize(this);

    public static BatteryHistory Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<BatteryHistory>(json) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }
}

/// <summary>
/// Leva 11.1, tarefa 05: como estava antes do Modo sobrevivência, para devolver tudo ao ligar na tomada ou pelo Desfazer.
/// Campos nulos não foram mudados.
/// </summary>
public sealed record SurvivalState(string? Scheme, uint? SaverAc, uint? SaverDc, int? Hz, int? ThemeApps, int? ThemeSystem, List<string> Eco)
{
    public string Serialize() => System.Text.Json.JsonSerializer.Serialize(this);

    public static SurvivalState? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<SurvivalState>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// A taxa mais baixa que a tela aceita a partir de 60 Hz (59 conta: é como muitas telas anunciam 60). Null quando já está
    /// nela ou quando só há taxas abaixo disso, que cansam a vista.
    /// </summary>
    public static int? SaverRate(int current, IReadOnlyList<int> supported)
    {
        var usable = supported.Where(r => r >= 59 && r < current).ToList();
        return usable.Count > 0 ? usable.Min() : null;
    }
}
