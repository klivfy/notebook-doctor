namespace NotebookDoctor.Services;

/// <summary>Lê o caminho do programa numa linha de comando de inicialização (registro Run ou atalho).</summary>
public static class CommandLine
{
    /// <summary>
    /// "\"C:\\Program Files\\App\\app.exe\" --min" → C:\Program Files\App\app.exe. Sem aspas, junta as palavras
    /// até achar um .exe, porque é comum o registro guardar "C:\Program Files\App\app.exe /bg" sem aspas.
    /// Sem nenhum .exe (atalho .lnk da pasta Inicializar), devolve o texto inteiro. Variáveis como %ProgramFiles% são expandidas.
    /// </summary>
    public static string? ExecutablePath(string? command)
    {
        string c = Environment.ExpandEnvironmentVariables((command ?? "").Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"')
        {
            int end = c.IndexOf('"', 1);
            string quoted = end >= 1 ? c[1..end] : c[1..];
            return quoted.Length > 0 ? quoted : null;
        }
        var words = c.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string candidate = string.Join(' ', words.Take(i + 1));
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return c; // sem .exe (ex.: atalho .lnk na pasta Inicializar): o próprio caminho
    }

    /// <summary>Leva 11.3: o programa e o resto da linha (os argumentos), para abrir o item depois sem passar por um shell.</summary>
    public static (string? Exe, string Args) Split(string? command)
    {
        string c = Environment.ExpandEnvironmentVariables((command ?? "").Trim());
        string? exe = ExecutablePath(c);
        if (exe is null) return (null, "");
        int at = c.StartsWith('"') ? c.IndexOf('"', 1) + 1 : c.IndexOf(exe, StringComparison.OrdinalIgnoreCase) + exe.Length;
        return (exe, at > 0 && at <= c.Length ? c[at..].Trim() : "");
    }
}

/// <summary>Leva 11.3, tarefa 14: item de inicialização que o Notebook Doctor abre 2 minutos depois de entrar no Windows.</summary>
public sealed record DelayedStartupItem(string Name, string Command, string SubKey)
{
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(2);
}
