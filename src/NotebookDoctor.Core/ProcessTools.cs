namespace NotebookDoctor.Services;

/// <summary>Quais programas o app nunca encerra nem tem a prioridade reduzida. As ações em si ficam em ProcessActions.</summary>
public static class ProcessTools
{
    // Nunca encerrados pelo app: derrubam o Windows, a sessão ou a proteção.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
        "explorer", "fontdrvhost", "MsMpEng", "SecurityHealthService", "NotebookDoctor", "Idle", "Memory Compression",
    };

    private static HashSet<string> _user = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>M21: lista do usuário somada à do sistema (a do sistema não pode ser removida).</summary>
    public static void SetUserProtected(IEnumerable<string> names) => _user = new(names, StringComparer.OrdinalIgnoreCase);

    public static bool IsSystem(string name) => Protected.Contains(name);
    public static bool IsUserProtected(string name) => _user.Contains(name);
    public static bool IsProtected(string name) => Protected.Contains(name) || _user.Contains(name);
}
