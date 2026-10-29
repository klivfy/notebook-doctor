using System.IO;

namespace NotebookDoctor.Services;

/// <summary>M19: grava o relatório de falha (sem dados pessoais) quando a pessoa ligou a opção em Configurações.</summary>
public static class CrashLog
{
    public static string Dir => Path.Combine(AppPaths.Dir, "falhas");

    /// <summary>Espelha AppSettings.CrashReports; desligado, nada é gravado aqui (o erros.log continua como sempre).</summary>
    public static bool Enabled { get; set; }

    public static void Save(Exception ex, string context)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var now = DateTime.Now;
            string text = CrashReport.Build(ex.ToString(), context, AppInfo.Version, Environment.OSVersion.VersionString, now);
            File.WriteAllText(Path.Combine(Dir, CrashReport.FileName(now)), SupportPackage.Redact(text));
            foreach (var old in CrashReport.Excess(Files())) File.Delete(old);
        }
        catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* falhar ao registrar a falha não pode derrubar mais nada */ }
    }

    public static string[] Files() =>
        Directory.Exists(Dir) ? Directory.GetFiles(Dir, "falha-*.txt") : [];

    /// <summary>Primeira linha do erro do relatório mais recente, para o aviso.</summary>
    public static string LatestHeadline()
    {
        var last = Files().OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).FirstOrDefault();
        if (last is null) return "";
        try { return CrashReport.Headline(File.ReadAllText(last).Split("\r\n\r\n", 2).ElementAtOrDefault(1) ?? ""); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}
