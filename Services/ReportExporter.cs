using System.IO;
using System.Net;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>Gera um relatório HTML autônomo (sem scripts, sem recursos externos) em Documentos\Notebook Doctor.</summary>
public static class ReportExporter
{
    public static string Export(Snapshot s, IReadOnlyList<Finding> findings, IReadOnlyList<HistSample> week)
    {
        string dir = AppPaths.ExportDir();
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"relatorio-{DateTime.Now:yyyy-MM-dd_HHmm}.html");
        File.WriteAllText(file, Build(s, findings, week), new UTF8Encoding(true));
        return file;
    }

    /// <summary>B20: o mesmo relatório em PDF (texto), para anexar em e-mail ou chamado.</summary>
    public static string ExportPdf(Snapshot s, IReadOnlyList<Finding> findings, IReadOnlyList<HistSample> week)
    {
        string dir = AppPaths.ExportDir();
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"relatorio-{DateTime.Now:yyyy-MM-dd_HHmm}.pdf");
        var lines = ReportContent.Lines(s, findings, week, Environment.MachineName, AppInfo.Version, $"Suporte: {AppInfo.SupportUrl} · {AppInfo.Copyright}");
        File.WriteAllBytes(file, SimplePdf.Build(lines, $"Relatório Notebook Doctor {s.Time:dd/MM/yyyy}"));
        return file;
    }

    private static string E(string t) => WebUtility.HtmlEncode(t);

    private static string Build(Snapshot s, IReadOnlyList<Finding> findings, IReadOnlyList<HistSample> week)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Relatório · Notebook Doctor</title>
<style>
:root{--bg:#fff;--card:#f2f2f9;--on:#121212;--muted:#555;--primary:#00188f;--err:#e81123;--warn:#ff8c00;--ok:#009e49}
@media(prefers-color-scheme:dark){:root{--bg:#121212;--card:#1e1e1e;--on:#fff;--muted:#b8b8b8;--primary:#00bcf2;--ok:#1ed760}}
body{font:15px/1.5 "Google Sans Flex","Segoe UI",sans-serif;background:var(--bg);color:var(--on);margin:0;padding:32px;max-width:860px;margin-inline:auto}
h1{font-size:28px;margin:0}h2{font-size:18px;margin:28px 0 10px}.muted{color:var(--muted)}
.card{background:var(--card);border-radius:16px;padding:18px 20px;margin:10px 0}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}.big{font-size:34px;font-weight:600}
.chip{display:inline-block;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:600;color:#fff;margin-right:8px}
.PROBLEMA{background:var(--err)}.ATENCAO{background:var(--warn);color:#000}.OK{background:var(--ok);color:#000}
table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:6px 8px;border-bottom:1px solid #8884}footer{margin-top:36px;font-size:12px;color:var(--muted)}
</style></head><body>
""");
        sb.Append($"<h1>Notebook Doctor</h1><div class=\"muted\">Relatório gerado em {s.Time:dd/MM/yyyy HH:mm} · {E(Environment.MachineName)}</div>");
        sb.Append("<div class=\"grid\">");
        sb.Append($"<div class=\"card\"><div class=\"muted\">Processador</div><div class=\"big\">{s.Cpu:0}%</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"muted\">Memória RAM</div><div class=\"big\">{s.RamPct:0}%</div><div class=\"muted\">{Units.GbOf(s.RamUsedGB, s.RamTotalGB)}</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"muted\">Disco livre</div><div class=\"big\">{s.DiskFreePct:0}%</div><div class=\"muted\">{Units.Gb(s.DiskFreeGB, "0")}</div></div></div>");

        sb.Append("<h2>Diagnóstico</h2>");
        foreach (var f in findings)
            sb.Append($"<div class=\"card\"><span class=\"chip {f.Level}\">{E(f.Tag)}</span><b>{E(f.Title)}</b><div class=\"muted\">{E(f.Detail)}</div><div>{E(f.Advice)}</div></div>");

        sb.Append("<h2>Programas que mais consomem agora</h2><div class=\"card\"><table><tr><th>Programa</th><th>Processos</th><th>CPU %</th><th>RAM (MB)</th></tr>");
        foreach (var g in s.Heavy.Take(10))
            sb.Append($"<tr><td>{E(g.Name)}</td><td>{g.Count}</td><td>{g.CpuPct:0.0}</td><td>{g.MemMB}</td></tr>");
        sb.Append("</table></div>");

        if (week.Count > 1)
        {
            sb.Append("<h2>Últimos 7 dias</h2><div class=\"card\">");
            sb.Append($"CPU: média {week.Average(w => w.Cpu):0}% · pico {week.Max(w => w.Cpu):0}%<br>");
            sb.Append($"RAM: média {week.Average(w => w.Ram):0}% · pico {week.Max(w => w.Ram):0}%<br>");
            var top = week.SelectMany(w => w.Top).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{E(g.Key)} ({g.Count() * 100 / week.Count}%)");
            sb.Append($"Mais frequentes entre os maiores consumidores: {string.Join(", ", top)}</div>");
        }
        sb.Append($"<footer>Notebook Doctor {E(AppInfo.Version)} · Suporte: {AppInfo.SupportUrl}<br>{E(AppInfo.Copyright)}</footer></body></html>");
        return sb.ToString();
    }
}
