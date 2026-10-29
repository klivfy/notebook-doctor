using System.Collections.Frozen;

namespace NotebookDoctor.Services;

/// <summary>ARC-05: abstração imutável e segura entre threads para consulta de processos protegidos.</summary>
public interface IProcessProtectionPolicy
{
    bool IsSystem(string name);
    bool IsUserProtected(string name);
    bool IsProtected(string name);
}

public sealed class ProcessProtectionPolicy : IProcessProtectionPolicy
{
    private readonly FrozenSet<string> _system;
    private readonly FrozenSet<string> _user;

    public ProcessProtectionPolicy(IEnumerable<string>? userNames = null)
        : this(ProcessTools.SystemProtectedNames, userNames ?? [])
    {
    }

    public ProcessProtectionPolicy(IEnumerable<string> systemNames, IEnumerable<string> userNames)
    {
        _system = (systemNames ?? []).Select(ProcessTools.NormalizeName).Where(n => n.Length > 0).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _user = (userNames ?? []).Select(ProcessTools.NormalizeName).Where(n => n.Length > 0).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSystem(string name) => _system.Contains(ProcessTools.NormalizeName(name));
    public bool IsUserProtected(string name) => _user.Contains(ProcessTools.NormalizeName(name));
    public bool IsProtected(string name) => IsSystem(name) || IsUserProtected(name);
}

/// <summary>Quais programas o app nunca encerra nem tem a prioridade reduzida. As ações em si ficam em ProcessActions.</summary>
public static class ProcessTools
{
    // Nunca encerrados pelo app: derrubam o Windows, a sessão ou a proteção.
    private static readonly FrozenSet<string> Protected = new[]
    {
        "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
        "explorer", "fontdrvhost", "MsMpEng", "SecurityHealthService", "NotebookDoctor", "Idle", "Memory Compression",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static volatile FrozenSet<string> _user = FrozenSet<string>.Empty;

    public static IReadOnlyCollection<string> SystemProtectedNames => Protected;

    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    /// <summary>M21: lista do usuário somada à do sistema (a do sistema não pode ser removida). Atômica entre threads.</summary>
    public static void SetUserProtected(IEnumerable<string> names) =>
        _user = (names ?? []).Select(NormalizeName).Where(n => n.Length > 0).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IProcessProtectionPolicy SnapshotPolicy() => new ProcessProtectionPolicy(Protected, _user);

    public static bool IsSystem(string name) => Protected.Contains(NormalizeName(name));
    public static bool IsUserProtected(string name) => _user.Contains(NormalizeName(name));
    public static bool IsProtected(string name)
    {
        string norm = NormalizeName(name);
        return Protected.Contains(norm) || _user.Contains(norm);
    }
}
