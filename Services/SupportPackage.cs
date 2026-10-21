using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>
/// M17: .zip para enviar ao suporte. Leva só dados técnicos (hardware, diagnóstico, 7 dias de histórico, ações, alertas,
/// erros do app). Nome de usuário, nome do computador e o caminho da pasta pessoal são trocados por marcadores.
/// Nada é enviado pela internet: o arquivo fica em Documentos\Notebook Doctor (ou na pasta de dados do app, se Documentos estiver protegida) para a pessoa anexar.
/// </summary>
public static class SupportPackage
{
    public static string Create(Snapshot? s, IReadOnlyList<Finding> findings, HealthInfo h, IReadOnlyList<HistSample> week)
    {
        string dir = AppPaths.ExportDir();
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, $"diagnostico-suporte-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        if (File.Exists(zip)) File.Delete(zip);

        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        void Add(string name, string content) =>
            WriteEntry(archive, name, Redact(content));

        Add("LEIA-ME.txt",
            "Pacote de diagnóstico do Notebook Doctor para o suporte Klivfy (klivfy.com/suporte).\r\n" +
            "Contém apenas dados técnicos. Nome de usuário, nome do computador e a pasta pessoal foram substituídos por <usuario>, <computador> e <pasta-pessoal>.\r\n" +
            "Confira o conteúdo antes de enviar, se quiser.\r\n");
        Add("sistema.txt", SystemText(s, h));
        Add("diagnostico.txt", string.Join("\r\n", findings.Select(f => $"[{f.Level}] {f.Title} — {f.Detail}")));
        Add("historico-7-dias.csv", HistoryCsv.Build(week));
        Add("configuracoes.json", JsonSerializer.Serialize(Services.AppSettings.Load(), new JsonSerializerOptions { WriteIndented = true }));
        Add("acoes.json", JsonSerializer.Serialize(ActionLog.Load(), new JsonSerializerOptions { WriteIndented = true }));
        Add("alertas.txt", string.Join("\r\n", AlertLog.Load(TimeSpan.FromDays(30)).Select(a => $"{a.TimeText}  {a.Title}: {a.Text}")));
        string errors = Path.Combine(AppPaths.Dir, "erros.log");
        if (File.Exists(errors))
        {
            string text = File.ReadAllText(errors);
            Add("erros.log", text.Length > 200_000 ? text[^200_000..] : text); // só o final, se for grande
        }
        // M19: relatórios de falha (só existem com o opt-in ligado; já foram gravados sem dados pessoais)
        foreach (var f in CrashLog.Files())
            Add("falhas/" + Path.GetFileName(f), File.ReadAllText(f));
        return zip;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var e = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(true));
        w.Write(content);
    }

    /// <summary>Troca dados que identificam a pessoa ou o computador por marcadores.</summary>
    public static string Redact(string text) =>
        Redaction.Apply(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        Environment.UserName, Environment.MachineName, Environment.UserDomainName);

    private static string SystemText(Snapshot? s, HealthInfo h)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Notebook Doctor {AppInfo.Version} · {(AppInfo.IsAdmin ? "administrador" : "usuário padrão")}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} · {(Environment.Is64BitOperatingSystem ? "64" : "32")} bits");
        sb.AppendLine($"Processadores lógicos: {Environment.ProcessorCount}");
        sb.AppendLine($"Ligado há: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm}");
        if (s is not null)
        {
            sb.AppendLine($"CPU agora: {s.Cpu:0}% · RAM: {s.RamUsedGB:0.0} de {s.RamTotalGB:0.0} GB ({s.RamPct:0}%)");
            sb.AppendLine($"Disco do sistema: {s.DiskFreeGB:0} GB livres ({s.DiskFreePct:0}%) · atividade {s.DiskBusy:0}%");
            if (s.TempC > 0) sb.AppendLine($"Temperatura: {s.TempC:0} °C");
            sb.AppendLine("Mais pesados agora: " + string.Join(", ", s.TopByCpu.Take(5).Select(g => $"{g.Name} {g.CpuPct:0}%/{g.MemMB} MB")));
        }
        if (h.HasDisk) sb.AppendLine($"Disco: {h.DiskType} · {(h.DiskHealthy ? "saudável" : h.DiskHealth)}" + (h.DiskWearPct is { } w ? $" · desgaste {w}%" : ""));
        if (h.BatteryHealthPct is { } b) sb.AppendLine($"Bateria: {b}% da capacidade original" + (h.BatteryCycles is { } c ? $", {c} ciclos" : ""));
        if (h.BootSeconds is { } bs) sb.AppendLine($"Último boot: {bs} s");
        sb.AppendLine($"Plano de energia: {h.ActivePlan}");
        sb.AppendLine($"Programas na inicialização: {h.StartupEnabled}");
        return sb.ToString();
    }
}

/// <summary>M06: histórico em CSV no formato do Excel brasileiro (ponto e vírgula, vírgula decimal).</summary>
public static class HistoryCsv
{
    public static string Build(IEnumerable<HistSample> data)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder("Data e hora;CPU %;Memória %;Disco ocupado %;Disco livre %;Nota de saúde;Mais pesados\r\n");
        foreach (var d in data)
            sb.Append(string.Format(pt, "{0:dd/MM/yyyy HH:mm};{1:0.0};{2:0};{3:0.0};{4:0.0};{5};{6}\r\n",
                d.T, d.Cpu, d.Ram, d.Disk, d.FreePct, d.Score >= 0 ? d.Score.ToString() : "", string.Join(", ", d.Top)));
        return sb.ToString();
    }

    public static string Export(IEnumerable<HistSample> data, string periodLabel)
    {
        string dir = AppPaths.ExportDir();
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"historico-{periodLabel}-{DateTime.Now:yyyy-MM-dd_HHmm}.csv");
        File.WriteAllText(file, Build(data), new UTF8Encoding(true)); // BOM: o Excel reconhece os acentos
        return file;
    }
}
