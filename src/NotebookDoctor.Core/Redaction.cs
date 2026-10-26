using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Troca dados que identificam a pessoa ou o computador por marcadores (pacote de suporte, relatórios de falha, avaliação e estatísticas).</summary>
public static partial class Redaction
{
    public static string Apply(string text, string home, string user, string machine, string domain)
    {
        if (home.Length > 3) text = text.Replace(home, "<pasta-pessoal>", StringComparison.OrdinalIgnoreCase)
                                        .Replace(home.Replace("\\", "\\\\"), "<pasta-pessoal>", StringComparison.OrdinalIgnoreCase);
        foreach (var (value, mark) in new[] { (user, "<usuario>"), (machine, "<computador>"), (domain, "<dominio>") })
            if (value.Length >= 3) text = text.Replace(value, mark, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+")] private static partial Regex Email();
    [GeneratedRegex(@"\b[0-9A-Fa-f]{2}([:\-])[0-9A-Fa-f]{2}(\1[0-9A-Fa-f]{2}){4}\b")] private static partial Regex Mac();
    [GeneratedRegex(@"\b[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}\b")] private static partial Regex MacDotted();
    [GeneratedRegex(@"\{?\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b\}?")] private static partial Regex Guid();
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")] private static partial Regex Ipv4();
    [GeneratedRegex(@"(?<![\w:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![\w:])")] private static partial Regex Ipv6();
    [GeneratedRegex(@"(?:\b[A-Za-z]:[\\/]|\\\\)[^\s""'<>|*?]*")] private static partial Regex WindowsPath();

    /// <summary>
    /// Para o que sai do computador (avaliação e estatísticas): além do usuário, do computador, do domínio e da pasta pessoal, tira
    /// e-mail, endereço MAC, IP (v4 e v6), GUID, caminho de arquivo e o número de série informado. A ordem importa: MAC e GUID
    /// antes do IPv6, que também casaria com letras e dois-pontos.
    /// </summary>
    public static string Anonymize(string text, string home = "", string user = "", string machine = "", string domain = "", string serial = "")
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = Apply(text, home, user, machine, domain);
        if (serial.Length >= 4) text = text.Replace(serial, "<serie>", StringComparison.OrdinalIgnoreCase);
        text = Email().Replace(text, "<email>");
        text = WindowsPath().Replace(text, "<caminho>");
        text = Guid().Replace(text, "<id>");
        text = Mac().Replace(text, "<mac>");
        text = MacDotted().Replace(text, "<mac>");
        text = Ipv4().Replace(text, "<ip>");
        text = Ipv6().Replace(text, m => m.Value.Count(c => c == ':') >= 2 && m.Value.Any(Uri.IsHexDigit) ? "<ip>" : m.Value);
        return text;
    }
}
