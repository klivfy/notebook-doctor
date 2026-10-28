namespace NotebookDoctor.Services;

/// <summary>Leva 10.3: pedaços de um texto, marcando os que batem com a busca (sem diferença de maiúsculas).</summary>
public static class SearchMatch
{
    public static List<(string Text, bool Hit)> Split(string text, string? term)
    {
        var parts = new List<(string, bool)>();
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text)) { parts.Add((text ?? "", false)); return parts; }
        int at = 0;
        while (at < text.Length)
        {
            int i = text.IndexOf(term, at, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) break;
            if (i > at) parts.Add((text[at..i], false));
            parts.Add((text.Substring(i, term.Length), true));
            at = i + term.Length;
        }
        if (at < text.Length) parts.Add((text[at..], false));
        return parts;
    }
}
