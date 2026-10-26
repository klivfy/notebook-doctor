namespace NotebookDoctor.Services;

/// <summary>B20 e B18: o conteúdo do relatório e do resumo em texto, sem formato (PDF e área de transferência usam o mesmo).</summary>
public static class ReportContent
{
    public static List<(PdfStyle Style, string Text)> Lines(Snapshot s, IReadOnlyList<Finding> findings, IReadOnlyList<HistSample> week,
                                                             string machine, string version, string footer)
    {
        var l = new List<(PdfStyle, string)>
        {
            (PdfStyle.Title, "Notebook Doctor"),
            (PdfStyle.Subtitle, Loc.F("Relatório gerado em {0:g} · {1}", s.Time, machine)),
            (PdfStyle.Heading, Loc.T("Agora")),
            (PdfStyle.Text, Loc.F("Processador: {0:0}%", s.Cpu)),
            (PdfStyle.Text, Loc.F("Memória RAM: {0:0}% ({1})", s.RamPct, Units.GbOf(s.RamUsedGB, s.RamTotalGB))),
            (PdfStyle.Text, Loc.F("Disco livre: {0:0}% ({1})", s.DiskFreePct, Units.Gb(s.DiskFreeGB, "0"))),
            (PdfStyle.Heading, Loc.T("Diagnóstico")),
        };
        // O relatório sai no idioma do app: título, tag e conselho pelos textos de tela (o Finding guarda a chave em português)
        foreach (var f in findings)
        {
            l.Add((PdfStyle.Text, $"[{f.TagText}] {f.TitleText}"));
            if (f.Detail.Length > 0) l.Add((PdfStyle.Small, f.Detail));
            if (f.Advice.Length > 0) l.Add((PdfStyle.Small, f.AdviceText));
        }
        l.Add((PdfStyle.Heading, Loc.T("Programas que mais consomem agora")));
        foreach (var g in s.Heavy.Take(10))
            l.Add((PdfStyle.Text, Loc.F("• {0}: {1:0.0}% de CPU, {2:N0} MB ({3})", g.Name, g.CpuPct, g.MemMB, Pt.N(g.Count, "processo", "processos"))));
        if (week.Count > 1)
        {
            l.Add((PdfStyle.Heading, Loc.T("Últimos 7 dias")));
            l.Add((PdfStyle.Text, Loc.F("CPU: média {0:0}%, pico {1:0}%", week.Average(w => w.Cpu), week.Max(w => w.Cpu))));
            l.Add((PdfStyle.Text, Loc.F("RAM: média {0:0}%, pico {1:0}%", week.Average(w => w.Ram), week.Max(w => w.Ram))));
            var top = week.SelectMany(w => w.Top).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} ({g.Count() * 100 / week.Count}%)");
            l.Add((PdfStyle.Text, Loc.F("Mais frequentes entre os maiores consumidores: {0}", string.Join(", ", top))));
        }
        l.Add((PdfStyle.Small, ""));
        l.Add((PdfStyle.Small, $"Notebook Doctor {version} · {footer}"));
        return l;
    }

    /// <summary>B18: resumo curto para colar num chamado ou conversa.</summary>
    public static string Summary(Snapshot s, HealthScoreResult score, BottleneckResult bottleneck, IReadOnlyList<Finding> findings) =>
        string.Join(Environment.NewLine, new[]
        {
            $"Notebook Doctor · {s.Time:g}",
            Loc.F("Nota de saúde: {0} ({1})", score.Score, Loc.T(score.Label)),
            Loc.F("CPU {0:0}% · RAM {1:0}% ({2}) · Disco livre {3:0}% ({4})", s.Cpu, s.RamPct, Units.GbOf(s.RamUsedGB, s.RamTotalGB), s.DiskFreePct, Units.Gb(s.DiskFreeGB, "0")),
            Loc.F("Gargalo: {0}", Loc.T(bottleneck.Resource)),
        }.Concat(findings.Where(f => f.Level != "OK").Take(5).Select(f => $"- [{f.TagText}] {f.TitleText}: {f.Detail}"))
         .Concat(s.Heavy.Any() ? [Loc.F("Mais pesados: {0}", string.Join(", ", s.Heavy.Take(3).Select(g => $"{g.Name} ({g.CpuPct:0}% CPU, {g.MemMB:N0} MB)")))] : []));
}
