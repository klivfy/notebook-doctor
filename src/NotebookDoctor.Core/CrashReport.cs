namespace NotebookDoctor.Services;

/// <summary>
/// M19: relatório de falha opcional. Só é gravado com o opt-in ligado, fica no computador (pasta "falhas") e vai no
/// pacote de suporte. Nada é enviado pela internet. O texto passa por <see cref="Redaction"/> antes de ser salvo.
/// </summary>
public static class CrashReport
{
    public const int Keep = 10;

    public static string Build(string exception, string context, string appVersion, string os, DateTime time) =>
        $"Notebook Doctor {appVersion}\r\n" +
        $"Windows: {os}\r\n" +
        $"Quando: {time:yyyy-MM-dd HH:mm:ss}\r\n" +
        $"Onde: {context}\r\n\r\n" +
        exception.ReplaceLineEndings("\r\n") + "\r\n";

    public static string FileName(DateTime time) => $"falha-{time:yyyy-MM-dd_HHmmss}.txt";

    /// <summary>Primeira linha da exceção ("Tipo: mensagem"), para o aviso depois da falha.</summary>
    public static string Headline(string exception)
    {
        string first = exception.Split('\n', 2)[0].Trim();
        return first.Length > 160 ? first[..159] + "…" : first;
    }

    /// <summary>Arquivos a apagar para ficar só com os <see cref="Keep"/> mais recentes (os nomes ordenam por data).</summary>
    public static IEnumerable<string> Excess(IEnumerable<string> files) =>
        files.OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(Keep);

    /// <summary>Relatórios gravados depois de <paramref name="since"/>, pela data no nome do arquivo.</summary>
    public static int CountSince(IEnumerable<string> files, DateTime since) =>
        files.Count(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "'falha-'yyyy-MM-dd_HHmmss",
                             System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) && t > since);
}
