namespace NotebookDoctor.Services;

/// <summary>
/// B40: log de diagnóstico em texto, para colar num chamado ou num e-mail. Versão leve do pacote de suporte:
/// erros recentes do app, relatórios de falha, ações e avisos. Quem chama passa o texto por <see cref="Redaction"/>.
/// </summary>
public static class DiagnosticLog
{
    public const int MaxErrorChars = 20_000;

    public static string Build(string appVersion, string os, DateTime now, IReadOnlyList<Finding> findings, string errorsLog,
                               IReadOnlyList<string> crashHeadlines, IReadOnlyList<string> actions, IReadOnlyList<string> alerts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Notebook Doctor {appVersion} · log de diagnóstico · {now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Windows: {os}");
        Section(sb, "Diagnóstico agora", findings.Select(f => $"[{f.Tag}] {f.Title} — {f.Detail}").DefaultIfEmpty("Nenhum problema encontrado."));
        Section(sb, "Relatórios de falha", crashHeadlines.DefaultIfEmpty("Nenhum (ou a opção está desligada)."));
        Section(sb, "Últimas ações", actions.DefaultIfEmpty("Nenhuma."));
        Section(sb, "Últimos avisos", alerts.DefaultIfEmpty("Nenhum."));
        string errors = errorsLog.Length > MaxErrorChars ? "…\r\n" + errorsLog[^MaxErrorChars..] : errorsLog; // só o final, que é o mais recente
        Section(sb, "Erros do app", errors.Trim().Length == 0 ? ["Nenhum."] : [errors.TrimEnd()]);
        return sb.ToString().ReplaceLineEndings("\r\n");
    }

    private static void Section(System.Text.StringBuilder sb, string title, IEnumerable<string> lines)
    {
        sb.AppendLine().AppendLine($"== {title} ==");
        foreach (var l in lines) sb.AppendLine(l);
    }
}
