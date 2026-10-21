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
            (PdfStyle.Subtitle, $"Relatório gerado em {s.Time:dd/MM/yyyy HH:mm} · {machine}"),
            (PdfStyle.Heading, "Agora"),
            (PdfStyle.Text, $"Processador: {s.Cpu:0}%"),
            (PdfStyle.Text, $"Memória RAM: {s.RamPct:0}% ({Units.GbOf(s.RamUsedGB, s.RamTotalGB)})"),
            (PdfStyle.Text, $"Disco livre: {s.DiskFreePct:0}% ({Units.Gb(s.DiskFreeGB, "0")})"),
            (PdfStyle.Heading, "Diagnóstico"),
        };
        foreach (var f in findings)
        {
            l.Add((PdfStyle.Text, $"[{f.Tag}] {f.Title}"));
            if (f.Detail.Length > 0) l.Add((PdfStyle.Small, f.Detail));
            if (f.Advice.Length > 0) l.Add((PdfStyle.Small, f.Advice));
        }
        l.Add((PdfStyle.Heading, "Programas que mais consomem agora"));
        foreach (var g in s.Heavy.Take(10))
            l.Add((PdfStyle.Text, $"• {g.Name}: {g.CpuPct:0.0}% de CPU, {g.MemMB:N0} MB ({Pt.N(g.Count, "processo", "processos")})"));
        if (week.Count > 1)
        {
            l.Add((PdfStyle.Heading, "Últimos 7 dias"));
            l.Add((PdfStyle.Text, $"CPU: média {week.Average(w => w.Cpu):0}%, pico {week.Max(w => w.Cpu):0}%"));
            l.Add((PdfStyle.Text, $"RAM: média {week.Average(w => w.Ram):0}%, pico {week.Max(w => w.Ram):0}%"));
            var top = week.SelectMany(w => w.Top).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} ({g.Count() * 100 / week.Count}%)");
            l.Add((PdfStyle.Text, $"Mais frequentes entre os maiores consumidores: {string.Join(", ", top)}"));
        }
        l.Add((PdfStyle.Small, ""));
        l.Add((PdfStyle.Small, $"Notebook Doctor {version} · {footer}"));
        return l;
    }

    /// <summary>B18: resumo curto para colar num chamado ou conversa.</summary>
    public static string Summary(Snapshot s, HealthScoreResult score, BottleneckResult bottleneck, IReadOnlyList<Finding> findings) =>
        string.Join(Environment.NewLine, new[]
        {
            $"Notebook Doctor · {s.Time:dd/MM/yyyy HH:mm}",
            $"Nota de saúde: {score.Score} ({score.Label})",
            $"CPU {s.Cpu:0}% · RAM {s.RamPct:0}% ({Units.GbOf(s.RamUsedGB, s.RamTotalGB)}) · Disco livre {s.DiskFreePct:0}% ({Units.Gb(s.DiskFreeGB, "0")})",
            $"Gargalo: {bottleneck.Resource}",
        }.Concat(findings.Where(f => f.Level != "OK").Take(5).Select(f => $"- [{f.Tag}] {f.Title}: {f.Detail}"))
         .Concat(s.Heavy.Any() ? [$"Mais pesados: {string.Join(", ", s.Heavy.Take(3).Select(g => $"{g.Name} ({g.CpuPct:0}% CPU, {g.MemMB:N0} MB)"))}"] : []));
}
