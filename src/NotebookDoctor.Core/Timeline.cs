using System.Globalization;

namespace NotebookDoctor.Services;

/// <param name="Kind">"Ação", "Aviso", "Instalação" ou "Pico"</param>
public sealed record TimelineEvent(DateTime Time, string Kind, string Title, string Detail)
{
    public string TimeText => Loc.Stamp(Time);
    // M13: a grade mostra estes (o registro de Ações e de avisos guarda o texto no idioma em que foi gravado)
    public string KindText => Loc.T(Kind);
    public string TitleText => Loc.T(Title);
    public string DetailText => Loc.T(Detail);
}

/// <summary>M47: junta numa linha do tempo o que aconteceu no período (ações, avisos, instalações e picos de uso).</summary>
public static class Timeline
{
    /// <summary>Data de instalação do registro do Windows ("20261003"). Nula quando falta ou é inválida.</summary>
    public static DateTime? ParseInstallDate(string? text) =>
        DateTime.TryParseExact(text?.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Episódios de carga alta no histórico: CPU ≥ 80% ou RAM ≥ 90% por <paramref name="minMinutes"/> minutos seguidos.
    /// Uma falha de até 3 minutos nas amostras (PC suspenso, app fechado) encerra o episódio.
    /// </summary>
    public static List<TimelineEvent> Peaks(IReadOnlyList<HistSample> data, int minMinutes = 5)
    {
        var events = new List<TimelineEvent>();
        var run = new List<HistSample>();
        void Close()
        {
            if (run.Count > 0 && (run[^1].T - run[0].T).TotalMinutes >= minMinutes)
            {
                bool cpu = run.Average(r => r.Cpu) >= 80 || run.Count(r => r.Cpu >= 80) >= run.Count(r => r.Ram >= 90);
                var top = run.SelectMany(r => r.Top).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
                int minutes = (int)Math.Round((run[^1].T - run[0].T).TotalMinutes);
                string what = cpu ? Loc.F("CPU média de {0:0}%", run.Average(r => r.Cpu)) : Loc.F("memória média de {0:0}%", run.Average(r => r.Ram));
                events.Add(new(run[0].T, "Pico", cpu ? "CPU alta" : "Memória alta",
                               Loc.F("{0} por {1}", what, Pt.N(minutes, "minuto", "minutos")) + (top.Count > 0 ? ". " + Loc.F("Mais pesados: {0}", string.Join(", ", top)) : "")));
            }
            run.Clear();
        }
        foreach (var s in data)
        {
            bool high = s.Cpu >= 80 || s.Ram >= 90;
            if (run.Count > 0 && (!high || (s.T - run[^1].T).TotalMinutes > 3)) Close();
            if (high) run.Add(s);
        }
        Close();
        return events;
    }

    public static List<TimelineEvent> Merge(params IEnumerable<TimelineEvent>[] sources) =>
        sources.SelectMany(s => s).OrderByDescending(e => e.Time).ToList();
}
