namespace NotebookDoctor.Services;

public sealed record LeakSuspect(string Name, long FromMB, long ToMB, int Minutes);

/// <summary>
/// Detecta possível vazamento de memória: o programa só cresce, sem nunca devolver RAM, por 20+ minutos.
/// Amostra uma vez por minuto os 30 programas que mais usam memória e guarda 60 minutos.
/// </summary>
public sealed class MemoryTrend
{
    private const int MinMinutes = 20, MinGrowthMB = 300;
    private readonly Dictionary<string, List<(DateTime t, long mb)>> _series = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSample = DateTime.MinValue;

    public IReadOnlyList<LeakSuspect> Suspects { get; private set; } = Array.Empty<LeakSuspect>();

    public void Feed(Snapshot s)
    {
        if ((s.Time - _lastSample).TotalSeconds < 60) return;
        _lastSample = s.Time;

        var top = s.Groups.OrderByDescending(g => g.MemMB).Take(30).ToList();
        foreach (var g in top)
        {
            if (!_series.TryGetValue(g.Name, out var list)) _series[g.Name] = list = new();
            list.Add((s.Time, g.MemMB));
            list.RemoveAll(p => (s.Time - p.t).TotalMinutes > 60);
        }
        foreach (var gone in _series.Keys.Where(k => top.All(g => !g.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
            _series.Remove(gone);

        Suspects = _series.Select(kv => Evaluate(kv.Key, kv.Value)).Where(x => x is not null).Select(x => x!)
            .Where(x => !ProcessTools.IsProtected(x.Name)).OrderByDescending(x => x.ToMB - x.FromMB).Take(3).ToList();
    }

    private static LeakSuspect? Evaluate(string name, List<(DateTime t, long mb)> pts)
    {
        if (pts.Count < MinMinutes) return null;
        double minutes = (pts[^1].t - pts[0].t).TotalMinutes;
        long growth = pts[^1].mb - pts[0].mb;
        if (minutes < MinMinutes || growth < MinGrowthMB) return null;

        // "Quase sempre subindo": quedas pequenas são ruído, mas se a memória cai bastante em algum momento, o programa a devolve
        int drops = 0;
        for (int i = 1; i < pts.Count; i++)
            if (pts[i].mb < pts[i - 1].mb - 20) drops++;
        if (drops > pts.Count * 0.15) return null;
        if (pts.Max(p => p.mb) - pts[^1].mb > growth * 0.2) return null;
        return new LeakSuspect(name, pts[0].mb, pts[^1].mb, (int)minutes);
    }
}
