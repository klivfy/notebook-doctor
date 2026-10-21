namespace NotebookDoctor.Services;

/// <param name="When">Data estimada em que o espaço livre chega a zero</param>
/// <param name="PctPerDay">Quantos pontos percentuais de espaço livre o disco perde por dia</param>
public sealed record DiskForecast(DateTime When, double PctPerDay);

public sealed record PeriodStats(int Samples, double Cpu, double Ram, double? Score);

public sealed record WeekComparison(PeriodStats Today, PeriodStats LastWeek);

/// <summary>Conclusões tiradas do histórico de amostras por minuto.</summary>
public static class HistoryInsights
{
    /// <summary>
    /// M50: tendência do espaço livre (regressão linear). Só considera o trecho depois da última limpeza grande,
    /// senão o salto de espaço liberado esconde a tendência. Nulo quando o disco não está enchendo ou falta histórico.
    /// </summary>
    public static DiskForecast? DiskFull(IReadOnlyList<HistSample> data, DateTime now)
    {
        if (data.Count < 2) return null;
        int start = 0;
        for (int i = 1; i < data.Count; i++)
            if (data[i].FreePct - data[i - 1].FreePct > 3) start = i;
        var pts = data.Skip(start).ToList();
        if (pts.Count < 50 || (pts[^1].T - pts[0].T).TotalDays < 3) return null;

        double t0 = pts[0].T.Ticks;
        var xs = pts.Select(p => (p.T.Ticks - t0) / TimeSpan.TicksPerDay).ToList();
        var ys = pts.Select(p => p.FreePct).ToList();
        double mx = xs.Average(), my = ys.Average();
        double den = xs.Sum(x => (x - mx) * (x - mx));
        if (den <= 0) return null;
        double slope = xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / den; // pontos percentuais por dia
        if (slope > -0.05) return null; // estável ou liberando espaço

        double daysLeft = pts[^1].FreePct / -slope;
        if (daysLeft > 365) return null; // longe demais para valer um aviso
        return new DiskForecast(now.AddDays(daysLeft), -slope);
    }

    /// <summary>M48: últimas 24 h comparadas com as mesmas 24 h da semana passada.</summary>
    public static WeekComparison? CompareWithLastWeek(IReadOnlyList<HistSample> data, DateTime now)
    {
        var today = Stats(data.Where(d => d.T > now.AddDays(-1) && d.T <= now).ToList());
        var last = Stats(data.Where(d => d.T > now.AddDays(-8) && d.T <= now.AddDays(-7)).ToList());
        return today is null || last is null ? null : new(today, last);
    }

    private static PeriodStats? Stats(List<HistSample> s)
    {
        if (s.Count < 30) return null; // menos de meia hora de uso: comparação não diria nada
        var scores = s.Where(x => x.Score >= 0).Select(x => (double)x.Score).ToList();
        return new(s.Count, s.Average(x => x.Cpu), s.Average(x => x.Ram), scores.Count > 0 ? scores.Average() : null);
    }
}
