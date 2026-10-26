namespace NotebookDoctor.Services;

/// <summary>O que o motor precisa saber do computador. Estende a leitura de sobras com arquivos só de uma pasta e data de gravação.</summary>
public interface ICleanSystem : ILeftoverSystem
{
    /// <summary>Arquivos que estão direto nesta pasta (sem entrar nas subpastas), caminhos completos.</summary>
    IEnumerable<string> DirectFiles(string dir);
    DateTime LastWrite(string path);
}

/// <summary>Uma pasta já resolvida (sem curingas nem variáveis) que a regra limpa, mais o que ela tem de poupar.</summary>
public sealed record FileTarget(
    string Dir, IReadOnlyList<string> Patterns, bool Recurse, bool RemoveSelf, int MinAgeDays,
    IReadOnlyList<string> ExcludedPaths, IReadOnlyList<(string Dir, IReadOnlyList<string> Names)> ExcludedFiles);

/// <summary><paramref name="ValueName"/> nulo: a chave inteira.</summary>
public sealed record RegTarget(string Key, string? ValueName);

/// <summary>O resultado da análise de uma regra neste computador.</summary>
public sealed class CleanPlanEntry
{
    public required CleanRule Rule { get; init; }
    public IReadOnlyList<FileTarget> Files { get; init; } = [];
    public IReadOnlyList<RegTarget> Registry { get; init; } = [];
    public long Bytes { get; init; }
    public int FileCount { get; init; }
    public int RegCount { get; init; }
    /// <summary>Quantos caminhos ou chaves as proteções barraram.</summary>
    public int Blocked { get; init; }
    public bool NeedsAdmin { get; init; }
    public bool Selected { get; set; }

    public string Name => Rule.Name;
    public CleanCategory Category => Rule.Category;
    public bool HasWork => FileCount > 0 || RegCount > 0 || Rule.Action != CleanAction.None;
}

public sealed record CleanContext(KnownPlaces Places, EnvExpander Env, Version OsVersion);

/// <summary>
/// Proteções do motor. Valem para toda regra, a nossa ou a da comunidade, e de novo na hora de apagar:
/// uma regra só age dentro dos caminhos que declara, e nunca em senhas, preenchimento automático, cookies, favoritos ou saves.
/// </summary>
public static class CleanSafety
{
    private static readonly string[] BlockedWords =
    [
        "password", "passw", "credential", "login data", "logins", "signons", "autofill", "formhistory", "form history", "intelliforms",
        "cookies", "key3.db", "key4.db", "cert9.db", "places.sqlite", "bookmarks", "web data", "profiles.ini", "keychain",
        "saved games", "savedgames", "savegame", @"\saves\", @"\save\", "wallet", ".kdbx", "licen", "serial", "activation",
        @"\local storage\", @"\indexeddb\",
    ];

    /// <summary>Dentro do Windows, só estas pastas (e os arquivos soltos de <see cref="WindowsRootFiles"/>) podem ser limpas.</summary>
    private static readonly string[] WindowsAllowed =
    [
        "Temp", "Logs", "Minidump", "LiveKernelReports", "Panther", @"SoftwareDistribution\Download",
        @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache",
    ];

    private static readonly string[] WindowsRootFiles = ["MEMORY.DMP"];

    private static readonly string[] BlockedExtensions = [".exe", ".dll", ".sys", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com"];

    /// <summary>Ramos do registro que nunca saem, nem inteiros nem por dentro (relativos a Software\).</summary>
    private static readonly string[] DeniedKeys =
    [
        "Classes", "Policies", "Clients", "RegisteredApplications", "ODBC",
        @"Microsoft\Windows\CurrentVersion\Run", @"Microsoft\Windows\CurrentVersion\RunOnce", @"Microsoft\Windows\CurrentVersion\Uninstall",
        @"Microsoft\Windows\CurrentVersion\App Paths", @"Microsoft\Windows\CurrentVersion\Policies", @"Microsoft\Windows\CurrentVersion\Installer",
        @"Microsoft\Windows\CurrentVersion\Component Based Servicing", @"Microsoft\Windows\CurrentVersion\Internet Settings",
        @"Microsoft\Windows\CurrentVersion\Authentication", @"Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
        @"Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", @"Microsoft\Windows\CurrentVersion\Explorer\StartupApproved",
        @"Microsoft\Windows\CurrentVersion\Explorer\FileExts", @"Microsoft\Windows NT\CurrentVersion", @"Microsoft\Windows Defender",
        @"Microsoft\Cryptography", @"Microsoft\SystemCertificates", @"Microsoft\Protected Storage System Provider",
        @"Microsoft\Internet Explorer\IntelliForms",
    ];

    public static bool HasBlockedWord(string text)
    {
        string t = text.Replace('/', '\\') + "\\";
        return BlockedWords.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEverything(string pattern) => pattern is "*" or "*.*";

    /// <summary>Padrões que sobram depois de tirar os que apontam para programas e scripts (a regra da comunidade não apaga executáveis).</summary>
    public static IReadOnlyList<string> UsablePatterns(IEnumerable<string> patterns, RuleOrigin origin) =>
        patterns.Where(p => p.Length > 0 && !p.Contains('\\') && !p.Contains('/')
                            && (origin == RuleOrigin.Builtin || IsEverything(p) || !BlockedExtensions.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase))))
                .ToList();

    /// <summary>Caminho sem ".." nem "." e sem rede (UNC): o que a regra declara é o que vale.</summary>
    public static bool IsPlainPath(string path)
    {
        string p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.Length < 3 || p[1] != ':') return false;
        return !p.Split('\\').Any(s => s is ".." or ".");
    }

    public static bool DirAllowed(string dir, bool recurse, bool removeSelf, IReadOnlyList<string> patterns, KnownPlaces p)
    {
        string d = Paths.Clean(dir);
        if (d.Length <= 3 || !IsPlainPath(d) || patterns.Count == 0) return false;
        if (Paths.IsUnder(d, p.Windows))
        {
            if (removeSelf) return false;
            if (Paths.Same(d, p.Windows))
                return !recurse && patterns.All(n => WindowsRootFiles.Contains(n, StringComparer.OrdinalIgnoreCase));
            return WindowsAllowed.Any(a => Paths.IsUnder(d, Paths.Join(p.Windows, a)));
        }
        if (p.UserContent.Any(c => Paths.IsUnder(d, c))) return false;
        bool knownFolder = p.Roots.Any(r => Paths.Same(d, r) || Paths.IsUnder(r, d));
        if (!knownFolder) return true;
        if (Paths.Same(d, Paths.Join(p.Local, "Temp"))) return !removeSelf;
        // Pasta conhecida (ou que contém uma): só arquivos soltos de nome específico, nunca "tudo" nem subpastas
        return !recurse && !removeSelf && !patterns.Any(IsEverything);
    }

    public static bool RegKeyAllowed(string key)
    {
        string k = Paths.Clean(key);
        if (k.Contains("..", StringComparison.Ordinal) || HasBlockedWord(k)) return false;
        var segs = k.Split('\\');
        if (segs.Length < 3) return false;
        if (segs[0] is not ("HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE") || !segs[1].Equals("Software", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = segs.Skip(2).ToList();
        if (rest.Count > 0 && rest[0].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) rest.RemoveAt(0);
        if (rest.Count == 0) return false;
        int min = rest[0].Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
        if (rest.Count < min) return false;
        string rel = string.Join('\\', rest);
        return !DeniedKeys.Any(d => Paths.IsUnder(rel, d) || Paths.IsUnder(d, rel));
    }

    public static bool NeedsAdmin(FileTarget t, KnownPlaces p) => !Paths.IsUnder(t.Dir, p.UserProfile);
    public static bool NeedsAdmin(RegTarget t) => t.Key.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Curingas nos caminhos: pastas ("User Data\*\Cache") e chaves do registro, expandidos pelo que existe de verdade.</summary>
public static class PathExpansion
{
    public static List<string> Dirs(string path, ICleanSystem sys)
    {
        string p = Paths.Clean(path).Replace('/', '\\');
        if (!CleanSafety.IsPlainPath(p)) return [];
        var segs = p.Split('\\');
        var current = new List<string> { segs[0] };
        foreach (var seg in segs.Skip(1))
        {
            current = Glob.HasWildcard(seg)
                ? current.SelectMany(c => sys.Directories(c).Where(d => Glob.IsMatch(seg, Paths.Leaf(d)))).ToList()
                : current.Select(c => c + "\\" + seg).Where(sys.DirectoryExists).ToList();
            if (current.Count == 0) break;
        }
        return current;
    }

    public static List<string> Keys(string key, ICleanSystem sys)
    {
        string k = Paths.Clean(key);
        if (k.Contains("..", StringComparison.Ordinal)) return [];
        var segs = k.Split('\\');
        var current = new List<string> { segs[0] };
        foreach (var seg in segs.Skip(1))
        {
            current = Glob.HasWildcard(seg)
                ? current.SelectMany(c => sys.SubKeys(c).Where(s => Glob.IsMatch(seg, s)).Select(s => c + "\\" + s)).ToList()
                : current.Select(c => c + "\\" + seg).Where(sys.KeyExists).ToList();
            if (current.Count == 0) break;
        }
        return current;
    }

    /// <summary>Arquivo ou pasta (o último trecho pode ter curinga).</summary>
    public static bool Exists(string path, ICleanSystem sys)
    {
        string p = Paths.Clean(path).Replace('/', '\\');
        string leaf = Paths.Leaf(p);
        foreach (var parent in Dirs(Paths.Parent(p), sys))
        {
            if (!Glob.HasWildcard(leaf))
            {
                string full = parent + "\\" + leaf;
                if (sys.FileExists(full) || sys.DirectoryExists(full)) return true;
            }
            else if (sys.DirectFiles(parent).Any(f => Glob.IsMatch(leaf, Paths.Leaf(f))) || sys.Directories(parent).Any(d => Glob.IsMatch(leaf, Paths.Leaf(d))))
                return true;
        }
        return false;
    }
}

/// <summary>Detecta as regras que valem neste computador e monta o plano do que cada uma limparia. Só lê.</summary>
public sealed class CleanEngine(ICleanSystem sys, CleanContext ctx)
{
    public bool Detected(CleanRule r)
    {
        if (r.MinOs is { } min && ctx.OsVersion < min) return false;
        if (r.MaxOs is { } max && ctx.OsVersion > max) return false;
        if (r.Detect.Count == 0 && r.DetectFiles.Count == 0 && r.SpecialDetect.Length == 0) return true;
        return r.Detect.Any(k => PathExpansion.Keys(k, sys).Count > 0)
               || r.DetectFiles.Any(f => ctx.Env.Expand(f) is { } p && PathExpansion.Exists(p, sys))
               || (r.SpecialDetect.Length > 0 && Special(r.SpecialDetect));
    }

    private bool Special(string name)
    {
        var p = ctx.Places;
        return name.ToUpperInvariant() switch
        {
            "DET_CHROME" => sys.DirectoryExists(Paths.Join(p.Local, @"Google\Chrome\User Data")) || sys.DirectoryExists(Paths.Join(p.Local, @"Google\Chrome")),
            "DET_FIREFOX" or "DET_MOZILLA" => sys.DirectoryExists(Paths.Join(p.Roaming, @"Mozilla\Firefox")) || sys.KeyExists(@"HKEY_CURRENT_USER\Software\Mozilla\Firefox"),
            "DET_THUNDERBIRD" => sys.DirectoryExists(Paths.Join(p.Roaming, "Thunderbird")),
            "DET_OPERA" => sys.DirectoryExists(Paths.Join(p.Roaming, "Opera Software")),
            _ => false,
        };
    }

    public List<CleanPlanEntry> PlanAll(IEnumerable<CleanRule> rules, CancellationToken ct = default)
    {
        var list = new List<CleanPlanEntry>();
        foreach (var r in rules)
        {
            ct.ThrowIfCancellationRequested();
            if (Plan(r) is { HasWork: true } e) list.Add(e);
        }
        return list.OrderBy(e => e.Rule.Category).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public CleanPlanEntry? Plan(CleanRule r)
    {
        if (!Detected(r)) return null;
        if (CleanSafety.HasBlockedWord(r.Name)) return null;
        var places = ctx.Places;
        int blocked = 0;
        var excludedPaths = new List<string>();
        var excludedFiles = new List<(string Dir, IReadOnlyList<string> Names)>();
        var excludedRegs = new List<(string Key, IReadOnlyList<string> Names)>();
        foreach (var x in r.Excludes)
        {
            if (x.Kind == ExcludeKind.Registry) { excludedRegs.Add((x.Path, x.Names)); continue; }
            if (ctx.Env.Expand(x.Path) is not { } expanded) continue;
            if (x.Kind == ExcludeKind.File)
            {
                foreach (var d in PathExpansion.Dirs(expanded, sys)) excludedFiles.Add((d, x.Names));
                continue;
            }
            // Path: pasta ou arquivo; o último trecho pode ter curinga
            string leaf = Paths.Leaf(expanded);
            foreach (var parent in PathExpansion.Dirs(Paths.Parent(expanded), sys))
                excludedPaths.AddRange(Glob.HasWildcard(leaf)
                    ? sys.Directories(parent).Concat(sys.DirectFiles(parent)).Where(e => Glob.IsMatch(leaf, Paths.Leaf(e)))
                    : [parent + "\\" + leaf]);
        }

        var targets = new List<FileTarget>();
        long bytes = 0;
        int files = 0;
        foreach (var fk in r.FileKeys)
        {
            var patterns = CleanSafety.UsablePatterns(fk.Patterns, r.Origin);
            string? expanded = ctx.Env.Expand(fk.Dir);
            if (expanded is null) continue;
            if (patterns.Count == 0 || CleanSafety.HasBlockedWord(expanded) || patterns.Any(p => CleanSafety.HasBlockedWord(p)))
            {
                blocked++;
                continue;
            }
            foreach (var dir in PathExpansion.Dirs(expanded, sys))
            {
                if (!CleanSafety.DirAllowed(dir, fk.Recurse, fk.RemoveSelf, patterns, places)) { blocked++; continue; }
                var target = new FileTarget(Paths.Clean(dir), patterns, fk.Recurse, fk.RemoveSelf, fk.MinAgeDays, excludedPaths, excludedFiles);
                long b = 0;
                int n = 0;
                foreach (var f in fk.Recurse ? sys.Files(dir) : sys.DirectFiles(dir))
                    if (Matches(target, f, () => sys.LastWrite(f), DateTime.Now)) { b += sys.Size(f); n++; }
                if (n == 0) continue;
                targets.Add(target);
                bytes += b;
                files += n;
            }
        }

        var regs = new List<RegTarget>();
        foreach (var rk in r.RegKeys)
        {
            if (CleanSafety.HasBlockedWord(rk.Key) || (rk.Value is not null && CleanSafety.HasBlockedWord(rk.Value))) { blocked++; continue; }
            foreach (var key in PathExpansion.Keys(rk.Key, sys))
            {
                if (!CleanSafety.RegKeyAllowed(key)) { blocked++; continue; }
                if (rk.Value is null)
                {
                    // Se a regra poupa algo dentro da chave, ela não sai inteira
                    if (excludedRegs.Any(x => Paths.IsUnder(x.Key, key))) continue;
                    regs.Add(new RegTarget(key, null));
                    continue;
                }
                var excluded = excludedRegs.Where(x => Paths.Same(x.Key, key)).SelectMany(x => x.Names.DefaultIfEmpty("*")).ToList();
                foreach (var name in sys.ValueNames(key).Where(n => Glob.IsMatch(rk.Value, n) && !Glob.IsMatchAny(excluded, n)))
                    regs.Add(new RegTarget(key, name));
            }
        }

        return new CleanPlanEntry
        {
            Rule = r, Files = targets, Registry = regs, Bytes = bytes, FileCount = files, RegCount = regs.Count, Blocked = blocked,
            NeedsAdmin = r.NeedsAdmin || targets.Any(t => CleanSafety.NeedsAdmin(t, places)) || regs.Any(CleanSafety.NeedsAdmin),
            Selected = r.DefaultOn,
        };
    }

    /// <summary>
    /// O único filtro de arquivos: a análise e a limpeza usam esta função. O arquivo precisa estar na pasta declarada
    /// (direto, ou em qualquer nível com RECURSE), ter um dos nomes, não estar nas exceções e ter a idade mínima.
    /// </summary>
    public static bool Matches(FileTarget t, string file, Func<DateTime>? lastWrite = null, DateTime? now = null)
    {
        string f = Paths.Clean(file);
        string name = Paths.Leaf(f);
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return false;
        string parent = Paths.Parent(f);
        if (t.Recurse ? !Paths.IsUnder(parent, t.Dir) : !Paths.Same(parent, t.Dir)) return false;
        // As palavras bloqueadas valem também abaixo da pasta da regra: um RECURSE com * em "User Data\Default" não
        // pode levar Network\Cookies, Login Data ou Local Storage
        if (CleanSafety.HasBlockedWord(f[Paths.Clean(t.Dir).Length..])) return false; // começa na barra, para "\local storage\" casar
        if (!Glob.IsMatchAny(t.Patterns, name)) return false;
        if (t.ExcludedPaths.Any(e => Paths.IsUnder(f, e))) return false;
        if (t.ExcludedFiles.Any(x => Paths.IsUnder(parent, x.Dir) && Glob.IsMatchAny(x.Names, name))) return false;
        if (t.MinAgeDays > 0 && (lastWrite is null || lastWrite() > (now ?? DateTime.Now).AddDays(-t.MinAgeDays))) return false;
        return true;
    }
}
