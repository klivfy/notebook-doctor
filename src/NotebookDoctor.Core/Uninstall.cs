using System.Text;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 5: nomes comparáveis para achar sobras. "7-Zip 24.08 (x64)" → "7-Zip"; "Adobe Systems Incorporated" →
/// "Adobe Systems" e "Adobe". Palavras genéricas ("Update", "Tools", "Microsoft") nunca bastam para uma correspondência.
/// </summary>
public static partial class ProgramNames
{
    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Parens();

    [GeneratedRegex(@"\b[vV]?\d+(?:\.\d+)+[a-z]?\b")]
    private static partial Regex Version();

    [GeneratedRegex(@"\b(?:x64|x86|amd64|arm64|64-bit|32-bit|64 bits|32 bits)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Arch();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static readonly string[] CompanySuffixes =
    [
        "inc", "incorporated", "ltd", "limited", "llc", "gmbh", "corp", "corporation", "co", "company", "sa", "s.a", "sl", "s.l", "bv", "b.v",
        "ag", "plc", "pty", "srl", "s.r.l", "ltda", "eireli", "kg", "oy", "ab", "as", "a/s", "spa", "s.p.a", "s.a.s", "sas",
    ];

    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "install", "installer", "uninstall", "update", "updater", "tool", "tools", "common", "shared", "file", "files", "data", "cache",
        "temp", "program", "programs", "software", "app", "apps", "application", "microsoft", "windows", "system", "driver", "drivers",
        "service", "services", "helper", "plugin", "plugins", "runtime", "client", "server", "user", "users", "documents", "local", "roaming",
        "packages", "the", "and", "for", "free", "pro", "lite", "edition", "version", "x64", "x86", "launcher", "manager", "center", "studio",
    };

    /// <summary>"Mozilla Firefox (x64 pt-BR)" → "Mozilla Firefox"; "Python 3.12.1 (64-bit)" → "Python".</summary>
    public static string Product(string displayName)
    {
        string s = Parens().Replace(displayName, " ");
        s = Version().Replace(s, " ");
        s = Arch().Replace(s, " ");
        s = Spaces().Replace(s, " ").Trim();
        return s.Trim(' ', '-', '–', ',', '.', ':');
    }

    /// <summary>"Spotify AB" → "Spotify"; "Google LLC" → "Google"; "Adobe Inc." → "Adobe".</summary>
    public static string Publisher(string publisher)
    {
        var words = Spaces().Replace(publisher.Replace(",", " "), " ").Trim().Split(' ').ToList();
        while (words.Count > 1 && CompanySuffixes.Contains(words[^1].TrimEnd('.').ToLowerInvariant())) words.RemoveAt(words.Count - 1);
        return string.Join(' ', words).Trim(' ', '.', ',');
    }

    /// <summary>Nomes do editor que uma pasta ou chave costuma usar: o nome inteiro e a primeira palavra ("Adobe Systems" e "Adobe").</summary>
    public static List<string> PublisherAliases(string publisher)
    {
        string full = Publisher(publisher);
        var list = new List<string>();
        if (Usable(full)) list.Add(full);
        string first = full.Split(' ')[0];
        if (first.Length >= 4 && Usable(first) && !list.Contains(first, StringComparer.OrdinalIgnoreCase)) list.Add(first);
        return list;
    }

    /// <summary>Chave de comparação: minúsculas, sem espaços, hífens, pontos e sublinhados ("7-Zip" e "7zip" são iguais).</summary>
    public static string Key(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant())
            if (c is not (' ' or '-' or '_' or '.')) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Iguais pela chave de comparação, com pelo menos 3 caracteres.</summary>
    public static bool Same(string a, string b)
    {
        string ka = Key(a);
        return ka.Length >= 3 && ka == Key(b);
    }

    /// <summary>Um nome que pode identificar um programa: 3 letras ou mais e não genérico.</summary>
    public static bool Usable(string name)
    {
        string trimmed = name.Trim();
        return Key(trimmed).Length >= 3 && !Generic.Contains(trimmed) && !Generic.Contains(Key(trimmed));
    }

    /// <summary>Primeira palavra do produto, quando serve sozinha ("VLC media player" → "VLC"; "Microsoft Edge" → null).</summary>
    public static string? FirstWord(string product)
    {
        string first = product.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length < product.Trim().Length && Usable(first) ? first : null;
    }
}

public enum InstallerEngine { Unknown, Msi, InnoSetup, Nsis, WixBurn, Squirrel, InstallShield }

/// <summary>
/// Tipo do desinstalador, pelo nome do arquivo, pelos argumentos e pelo texto da versão do arquivo (descrição,
/// comentários). Serve para a fila silenciosa (5.3): cada tipo tem os próprios argumentos de modo silencioso.
/// </summary>
public static partial class InstallerDetection
{
    [GeneratedRegex(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InnoName();

    public static InstallerEngine Detect(string file, string arguments, string versionText)
    {
        string name = file.Contains('\\') ? file[(file.LastIndexOf('\\') + 1)..] : file;
        bool Has(string text, string what) => text.Contains(what, StringComparison.OrdinalIgnoreCase);
        if (Has(name, "msiexec")) return InstallerEngine.Msi;
        if (InnoName().IsMatch(name) || Has(versionText, "Inno Setup")) return InstallerEngine.InnoSetup;
        if (Has(versionText, "Nullsoft") || Has(versionText, "NSIS")) return InstallerEngine.Nsis;
        if (name.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) && Has(arguments, "--uninstall")) return InstallerEngine.Squirrel;
        if (Has(versionText, "InstallShield") || Has(arguments, "-runfromtemp") || Has(name, "IsUninst")) return InstallerEngine.InstallShield;
        if (Has(versionText, "WiX") || Has(versionText, "Burn") || (Has(file, @"\Package Cache\") && Has(arguments, "/uninstall"))) return InstallerEngine.WixBurn;
        return InstallerEngine.Unknown;
    }

    /// <summary>Argumentos que deixam o desinstalador sem janelas; null quando o tipo não tem um modo silencioso confiável.</summary>
    public static string? SilentArguments(InstallerEngine engine) => engine switch
    {
        InstallerEngine.InnoSetup => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
        InstallerEngine.Nsis => "/S",
        InstallerEngine.WixBurn => "/quiet /norestart",
        InstallerEngine.Squirrel => "-s",
        InstallerEngine.Msi => "/qn /norestart",
        _ => null,
    };
}

public enum UninstallResult { Ok, RebootNeeded, NotInstalled, Canceled, Busy, Failed }

public static class MsiExit
{
    /// <summary>Códigos de saída do msiexec (Windows Installer).</summary>
    public static UninstallResult Result(int code) => code switch
    {
        0 => UninstallResult.Ok,
        3010 or 1641 => UninstallResult.RebootNeeded,
        1605 or 1614 => UninstallResult.NotInstalled,
        1602 => UninstallResult.Canceled,
        1618 => UninstallResult.Busy,
        _ => UninstallResult.Failed,
    };
}

public sealed record ProcInfo(int Pid, int ParentPid);

/// <summary>
/// Desinstaladores Inno Setup e NSIS se copiam para a pasta temporária, abrem a cópia e fecham na hora. Por isso a
/// espera acompanha a árvore: filhos e netos de quem já está sendo acompanhado entram na lista.
/// </summary>
public static class ProcessTree
{
    /// <summary>
    /// Junta os descendentes e tira os que já fecharam. Um filho aberto pouco antes de o pai fechar ainda aparece com o
    /// PID do pai, então é adotado antes de o pai sair da lista; depois disso, um PID reaproveitado não é confundido.
    /// Devolve quantos processos acompanhados continuam abertos.
    /// </summary>
    public static int Step(HashSet<int> tracked, IReadOnlyCollection<ProcInfo> snapshot)
    {
        bool added;
        do
        {
            added = false;
            foreach (var p in snapshot)
                if (p.Pid != p.ParentPid && !tracked.Contains(p.Pid) && tracked.Contains(p.ParentPid)) { tracked.Add(p.Pid); added = true; }
        } while (added);
        tracked.IntersectWith(snapshot.Select(p => p.Pid));
        return tracked.Count;
    }
}

public enum WaitState { Running, Settling, Done, TimedOut }

/// <summary>Considera o desinstalador terminado quando nenhum processo dele fica aberto por alguns segundos seguidos.</summary>
public sealed class UninstallWatch(DateTime start, TimeSpan settle, TimeSpan limit)
{
    private DateTime? _emptySince;

    public WaitState Observe(DateTime now, int alive)
    {
        if (now - start >= limit) return WaitState.TimedOut;
        if (alive > 0) { _emptySince = null; return WaitState.Running; }
        _emptySince ??= now;
        return now - _emptySince.Value >= settle ? WaitState.Done : WaitState.Settling;
    }
}

/// <summary>
/// Retrato do programa tirado antes de desinstalar: depois, a chave do registro some e não diria mais onde ele
/// estava instalado nem como se chamava.
/// </summary>
public sealed record ProgramFingerprint(string Id, string Name, string Product, IReadOnlyList<string> Publishers, string Version,
                                        IReadOnlyList<string> Dirs, string? ProductCode, string RegistryPath, ProgramKind Kind)
{
    public static ProgramFingerprint Of(InstalledProgram p, Func<string, bool> dirExists, Func<string, bool> fileExists)
    {
        var dirs = new List<string>();
        void Add(string? dir)
        {
            string d = Paths.Clean(dir);
            if (d.Length > 3 && !dirs.Any(x => Paths.Same(x, d))) dirs.Add(d);
        }
        Add(Environment.ExpandEnvironmentVariables(p.InstallLocation));
        Add(ProgramRules.FolderOf(p, dirExists, fileExists));
        return new ProgramFingerprint(p.Id, p.Name, ProgramNames.Product(p.Name), ProgramNames.PublisherAliases(p.Publisher), p.Version,
                                      dirs, p.ProductCode, p.RegistryPath, p.Kind);
    }

    /// <summary>"Procurar sobras" de um programa que já saiu da lista: só o nome que a pessoa digitou e, se ela escolheu, a pasta.</summary>
    public static ProgramFingerprint Manual(string name, string? folder)
    {
        string dir = Paths.Clean(folder);
        return new ProgramFingerprint("manual:" + name.Trim(), name.Trim(), ProgramNames.Product(name), [], "", dir.Length > 3 ? [dir] : [], null, "", ProgramKind.Program);
    }
}

/// <summary>Leva 5.3: fila de desinstalação e "de quem é este programa?".</summary>
public static class UninstallQueue
{
    /// <summary>
    /// Comando para a fila silenciosa: MSI com /qn; o QuietUninstallString que o programa registrou; ou os argumentos
    /// silenciosos do tipo de instalador. Sem nenhum desses, o desinstalador abre normal (Silent = false).
    /// </summary>
    public static (UninstallCommand Command, bool Silent)? Plan(InstalledProgram p, Func<string, bool> fileExists, string versionText)
    {
        if (p.Kind == ProgramKind.Msi && p.ProductCode is { } code) return (new UninstallCommand("msiexec.exe", $"/x {code} /qn /norestart"), true);
        if (ProgramRules.ParseCommand(p.QuietUninstallString, fileExists) is { } quiet) return (quiet, true);
        if (ProgramRules.CommandFor(p, fileExists) is not { } cmd) return null;
        var engine = InstallerDetection.Detect(cmd.File, cmd.Arguments, versionText);
        if (InstallerDetection.SilentArguments(engine) is not { } silent) return (cmd, false);
        var missing = silent.Split(' ').Where(a => !cmd.Arguments.Split(' ').Contains(a, StringComparer.OrdinalIgnoreCase));
        return (cmd with { Arguments = string.Join(' ', new[] { cmd.Arguments }.Concat(missing)).Trim() }, true);
    }

    /// <summary>
    /// Programa dono de um executável (janela mirada, atalho solto na lista, processo): o que tem a pasta mais funda
    /// que contém o arquivo. Pastas protegidas (Program Files, a do usuário…) nunca identificam um programa.
    /// </summary>
    public static InstalledProgram? OwnerOf(string file, IEnumerable<InstalledProgram> programs, KnownPlaces places, Func<string, bool> dirExists, Func<string, bool> fileExists) =>
        programs.Where(p => p.Kind != ProgramKind.Update)
                .Select(p => (p, dir: ProgramRules.FolderOf(p, dirExists, fileExists)))
                .Where(x => x.dir is not null && !LeftoverRules.IsProtectedPath(x.dir, places) && Paths.IsUnder(file, x.dir))
                .OrderByDescending(x => x.dir!.Length)
                .Select(x => x.p)
                .FirstOrDefault();
}

/// <summary>Caminhos do Windows comparados como o Windows compara: sem diferenciar maiúsculas e sem a barra final.</summary>
public static class Paths
{
    public static string Clean(string? path) => (path ?? "").Trim().Trim('"').Trim().TrimEnd('\\', '/');

    public static bool Same(string a, string b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>O próprio diretório ou algo dentro dele ("C:\App" contém "C:\App\x", mas não "C:\Apple").</summary>
    public static bool IsUnder(string path, string dir)
    {
        string p = Clean(path), d = Clean(dir);
        if (p.Length == 0 || d.Length == 0) return false;
        return p.Equals(d, StringComparison.OrdinalIgnoreCase) || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string Leaf(string path)
    {
        string p = Clean(path);
        int i = p.LastIndexOf('\\');
        return i >= 0 ? p[(i + 1)..] : p;
    }

    public static string Parent(string path)
    {
        string p = Clean(path);
        int i = p.LastIndexOf('\\');
        return i > 0 ? p[..i] : "";
    }

    public static string Join(string dir, string name) => Clean(dir) + "\\" + name;
}
