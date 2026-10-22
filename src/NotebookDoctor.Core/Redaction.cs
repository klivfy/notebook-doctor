namespace NotebookDoctor.Services;

/// <summary>Troca dados que identificam a pessoa ou o computador por marcadores (pacote de suporte e relatórios de falha).</summary>
public static class Redaction
{
    public static string Apply(string text, string home, string user, string machine, string domain)
    {
        if (home.Length > 3) text = text.Replace(home, "<pasta-pessoal>", StringComparison.OrdinalIgnoreCase)
                                        .Replace(home.Replace("\\", "\\\\"), "<pasta-pessoal>", StringComparison.OrdinalIgnoreCase);
        foreach (var (value, mark) in new[] { (user, "<usuario>"), (machine, "<computador>"), (domain, "<dominio>") })
            if (value.Length >= 3) text = text.Replace(value, mark, StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
