using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <param name="P">M05: CPU % e memória MB dos programas mais pesados nesta amostra ({"chrome":[12.5,1830]}).</param>
public sealed record HistSample(DateTime T, double Cpu, double Ram, double Disk, double FreePct, string[] Top, int Score = -1,
                                Dictionary<string, double[]>? P = null);

/// <summary>Histórico leve em JSON Lines: uma amostra por minuto, mantido por 30 dias.</summary>
public sealed class HistoryStore
{
    private DateTime _lastWrite = DateTime.MinValue;
    private readonly object _lock = new();

    public HistoryStore() => Prune(TimeSpan.FromDays(30));

    public void Record(Snapshot s, int score = -1)
    {
        if ((s.Time - _lastWrite).TotalSeconds < 60) return;
        _lastWrite = s.Time;
        var top = s.Heavy.Take(3).Select(g => g.Name).ToArray();
        // Os 8 que mais usam CPU e os 8 que mais usam memória: basta para a linha do tempo sem inchar o arquivo
        var procs = s.Groups.OrderByDescending(g => g.CpuPct).Take(8)
            .Concat(s.Groups.OrderByDescending(g => g.MemMB).Take(8))
            .DistinctBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Name, g => new[] { Math.Round(g.CpuPct, 1), (double)g.MemMB }, StringComparer.OrdinalIgnoreCase);
        var line = JsonSerializer.Serialize(new HistSample(s.Time, Math.Round(s.Cpu, 1), s.RamPct, Math.Round(s.DiskBusy, 1), Math.Round(s.DiskFreePct, 1), top, score, procs));
        lock (_lock) { try { File.AppendAllText(AppPaths.History, line + "\n"); } catch { } }
    }

    public List<HistSample> Load(TimeSpan window)
    {
        var from = DateTime.Now - window;
        var result = new List<HistSample>();
        lock (_lock)
        {
            if (!File.Exists(AppPaths.History)) return result;
            try
            {
                foreach (var line in File.ReadLines(AppPaths.History))
                {
                    try { var h = JsonSerializer.Deserialize<HistSample>(line); if (h is not null && h.T >= from) result.Add(h); }
                    catch { /* linha corrompida: ignora */ }
                }
            }
            catch { }
        }
        return result;
    }

    private void Prune(TimeSpan keep)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(AppPaths.History) || new FileInfo(AppPaths.History).Length < 512 * 1024) return;
                var from = DateTime.Now - keep;
                var kept = File.ReadLines(AppPaths.History).Where(l =>
                {
                    try { return JsonSerializer.Deserialize<HistSample>(l)?.T >= from; } catch { return false; }
                }).ToList();
                File.WriteAllLines(AppPaths.History, kept);
            }
            catch { }
        }
    }

    /// <summary>Horário representativo (meio do bloco) para cada ponto de <see cref="Downsample"/>.</summary>
    public static List<DateTime> DownsampleTimes(IReadOnlyList<DateTime> times, int points)
    {
        if (times.Count <= points) return times.ToList();
        var res = new List<DateTime>(points);
        double step = times.Count / (double)points;
        for (int i = 0; i < points; i++)
        {
            int a = (int)(i * step), b = Math.Max(a + 1, (int)((i + 1) * step));
            res.Add(times[Math.Min(times.Count - 1, (a + b) / 2)]);
        }
        return res;
    }

    /// <summary>Reduz a série a no máximo <paramref name="points"/> pontos usando médias por bloco.</summary>
    public static List<double> Downsample(IReadOnlyList<double> values, int points)
    {
        if (values.Count <= points) return values.ToList();
        var res = new List<double>(points);
        double step = values.Count / (double)points;
        for (int i = 0; i < points; i++)
        {
            int a = (int)(i * step), b = Math.Max(a + 1, (int)((i + 1) * step));
            res.Add(values.Skip(a).Take(b - a).Average());
        }
        return res;
    }
}
