using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Leva 6.1: de onde veio a regra. As embutidas são testadas; as da comunidade passam por todas as proteções.</summary>
public enum RuleOrigin { Builtin, Community }

public enum CleanCategory { Windows, Office, Apps, Internet, Junk }

/// <summary>O que a regra faz além de apagar arquivos e chaves. Só as regras embutidas têm ação; o arquivo da comunidade não consegue pedir uma.</summary>
public enum CleanAction { None, FlushDns, ClearClipboard }

public enum ExcludeKind { Path, File, Registry }

/// <summary>
/// Uma linha FileKey: pasta (com curingas e variáveis), padrões de nome, se entra nas subpastas, se a própria pasta sai
/// no fim e, só para as nossas regras, a idade mínima em dias.
/// </summary>
public sealed record FileKey(string Dir, IReadOnlyList<string> Patterns, bool Recurse, bool RemoveSelf, int MinAgeDays = 0);

/// <summary><paramref name="Value"/> nulo: a chave inteira; "*": todos os valores; um nome (com curingas): esses valores.</summary>
public sealed record RegKey(string Key, string? Value);

/// <summary>Path: tudo dentro dessa pasta (ou esse arquivo). File: arquivos com esses nomes nessa pasta. Registry: chave e valores.</summary>
public sealed record ExcludeKey(ExcludeKind Kind, string Path, IReadOnlyList<string> Names);

public sealed class CleanRule
{
    public string Name { get; init; } = "";
    public CleanCategory Category { get; init; } = CleanCategory.Apps;
    public RuleOrigin Origin { get; init; } = RuleOrigin.Community;
    public IReadOnlyList<string> Detect { get; init; } = [];
    public IReadOnlyList<string> DetectFiles { get; init; } = [];
    public string SpecialDetect { get; init; } = "";
    public Version? MinOs { get; init; }
    public Version? MaxOs { get; init; }
    public IReadOnlyList<FileKey> FileKeys { get; init; } = [];
    public IReadOnlyList<RegKey> RegKeys { get; init; } = [];
    public IReadOnlyList<ExcludeKey> Excludes { get; init; } = [];
    public string Warning { get; init; } = "";
    /// <summary>Só as regras embutidas vêm marcadas; as da comunidade sempre começam desmarcadas, seja qual for o Default.</summary>
    public bool DefaultOn { get; init; }
    public CleanAction Action { get; init; }
    /// <summary>Regras embutidas que mexem em algo fora da pasta do usuário já dizem isso aqui; as outras são deduzidas dos caminhos.</summary>
    public bool NeedsAdmin { get; init; }
}

/// <summary>Curingas do Windows: * qualquer trecho, ? uma letra, sem diferenciar maiúsculas. "*.*" é "tudo", como no Windows.</summary>
public static class Glob
{
    public static bool HasWildcard(string s) => s.IndexOfAny(['*', '?']) >= 0;

    public static bool IsMatch(string pattern, string text)
    {
        if (pattern is "*" or "*.*") return true;
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))) { p++; t++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
            else if (star >= 0) { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    public static bool IsMatchAny(IEnumerable<string> patterns, string text) => patterns.Any(p => IsMatch(p, text));
}

/// <summary>Variáveis como %AppData% e %LocalAppData%. Uma variável desconhecida deixa o caminho sem resolução: a regra não age nele.</summary>
public sealed class EnvExpander(IReadOnlyDictionary<string, string> vars)
{
    private static readonly Regex Var = new("%([^%\\\\]+)%", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _vars = new(vars, StringComparer.OrdinalIgnoreCase);

    /// <summary>Texto expandido, ou null quando sobra alguma variável sem valor.</summary>
    public string? Expand(string text)
    {
        bool missing = false;
        string result = Var.Replace(text, m =>
        {
            if (_vars.TryGetValue(m.Groups[1].Value, out var v) && v.Length > 0) return Paths.Clean(v);
            missing = true;
            return m.Value;
        });
        return missing ? null : result;
    }

    /// <summary>Uma cópia com mais uma variável (os testes apontam %TestRoot% para uma pasta de mentira).</summary>
    public EnvExpander With(string name, string value) => new(new Dictionary<string, string>(_vars, StringComparer.OrdinalIgnoreCase) { [name] = value });

    public static EnvExpander FromPlaces(KnownPlaces p, string tempDir = "")
    {
        string temp = tempDir.Length > 0 ? tempDir : Paths.Join(p.Local, "Temp");
        string drive = p.Windows.Length >= 2 ? p.Windows[..2] : "C:";
        return new EnvExpander(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppData"] = p.Roaming, ["LocalAppData"] = p.Local, ["LocalLowAppData"] = p.LocalLow, ["UserProfile"] = p.UserProfile,
            ["ProgramFiles"] = p.ProgramFiles, ["ProgramFiles(x86)"] = p.ProgramFilesX86, ["CommonProgramFiles"] = p.CommonFiles,
            ["CommonProgramFiles(x86)"] = p.CommonFilesX86, ["ProgramData"] = p.ProgramData, ["CommonAppData"] = p.ProgramData,
            ["AllUsersProfile"] = p.ProgramData, ["SystemRoot"] = p.Windows, ["WinDir"] = p.Windows, ["SystemDrive"] = drive,
            ["Temp"] = temp, ["Tmp"] = temp, ["Documents"] = Paths.Join(p.UserProfile, "Documents"),
            ["Pictures"] = Paths.Join(p.UserProfile, "Pictures"), ["Music"] = Paths.Join(p.UserProfile, "Music"),
            ["Videos"] = Paths.Join(p.UserProfile, "Videos"), ["Desktop"] = p.Desktop, ["Public"] = Paths.Join(drive, @"Users\Public"),
        });
    }
}

/// <summary>Chaves do registro: "HKCU\…" e "HKEY_CURRENT_USER\…" são a mesma coisa; o resto do app usa o nome completo.</summary>
public static class RegPath
{
    public static string Normalize(string key)
    {
        string k = key.Trim().Replace('/', '\\').Trim('\\');
        int i = k.IndexOf('\\');
        string hive = i < 0 ? k : k[..i], rest = i < 0 ? "" : k[i..];
        string full = hive.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKU" or "HKEY_USERS" => "HKEY_USERS",
            _ => "",
        };
        return full.Length == 0 ? "" : full + rest;
    }
}

/// <summary>
/// Leitor do formato winapp2.ini: [Nome *], Detect, DetectFile, SpecialDetect, DetectOS, FileKey (RECURSE, REMOVESELF),
/// RegKey, ExcludeKey, Default, Warning e LangSecRef. Lê só o formato (um fato técnico): nenhuma regra vem embutida daqui.
/// Linhas que não entendemos são ignoradas, e uma regra sem FileKey nem RegKey não entra.
/// </summary>
public static class Winapp2Parser
{
    private static readonly Regex Numbered = new(@"^(?<k>Detect|DetectFile|FileKey|RegKey|ExcludeKey)\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<CleanRule> Parse(string text, RuleOrigin origin = RuleOrigin.Community)
    {
        var rules = new List<CleanRule>();
        Builder? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                cur = new Builder(line[1..^1].Trim());
                continue;
            }
            int eq = line.IndexOf('=');
            if (cur is null || eq <= 0) continue;
            cur.Add(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        Flush();
        return rules;

        void Flush()
        {
            if (cur?.Build(origin) is { } r) rules.Add(r);
            cur = null;
        }
    }

    private sealed class Builder(string header)
    {
        private readonly List<string> _detect = [], _detectFiles = [], _fileKeys = [], _regKeys = [], _excludes = [];
        private string _special = "", _warning = "", _lang = "";
        private Version? _minOs, _maxOs;
        private bool _default;

        public void Add(string key, string value)
        {
            var m = Numbered.Match(key);
            if (m.Success)
            {
                switch (m.Groups["k"].Value.ToLowerInvariant())
                {
                    case "detect": _detect.Add(value); break;
                    case "detectfile": _detectFiles.Add(value); break;
                    case "filekey": _fileKeys.Add(value); break;
                    case "regkey": _regKeys.Add(value); break;
                    default: _excludes.Add(value); break;
                }
                return;
            }
            switch (key.ToLowerInvariant())
            {
                case "specialdetect": _special = value; break;
                case "default": _default = value.Equals("True", StringComparison.OrdinalIgnoreCase); break;
                case "warning": _warning = value; break;
                case "langsecref": _lang = value; break;
                case "detectos": (_minOs, _maxOs) = Os(value); break;
            }
        }

        private static (Version?, Version?) Os(string value)
        {
            var parts = value.Split('|');
            static Version? V(string s) => Version.TryParse(s.Trim(), out var v) ? v : null;
            return (parts.Length > 0 ? V(parts[0]) : null, parts.Length > 1 ? V(parts[1]) : null);
        }

        public CleanRule? Build(RuleOrigin origin)
        {
            var files = _fileKeys.Select(ParseFileKey).OfType<FileKey>().ToList();
            var regs = _regKeys.Select(ParseRegKey).OfType<RegKey>().ToList();
            if (files.Count == 0 && regs.Count == 0) return null;
            string name = header.EndsWith('*') ? header[..^1].Trim() : header;
            if (name.Length == 0) return null;
            return new CleanRule
            {
                Name = name,
                Category = _lang switch { "3022" => CleanCategory.Internet, "3025" => CleanCategory.Windows, _ => CleanCategory.Apps },
                Origin = origin,
                Detect = _detect.Select(RegPath.Normalize).Where(k => k.Length > 0).ToList(),
                DetectFiles = _detectFiles,
                SpecialDetect = _special,
                MinOs = _minOs, MaxOs = _maxOs,
                FileKeys = files, RegKeys = regs,
                Excludes = _excludes.Select(ParseExclude).OfType<ExcludeKey>().ToList(),
                Warning = _warning,
                DefaultOn = origin == RuleOrigin.Builtin && _default,
            };
        }
    }

    /// <summary>"pasta|*.log;*.tmp|RECURSE|REMOVESELF". O padrão pode faltar; as marcas podem vir em qualquer posição depois da pasta.</summary>
    public static FileKey? ParseFileKey(string text)
    {
        var parts = text.Split('|');
        string dir = parts[0].Trim();
        if (dir.Length == 0) return null;
        bool recurse = false, self = false;
        int age = 0;
        var patterns = new List<string>();
        foreach (var part in parts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (part.Equals("RECURSE", StringComparison.OrdinalIgnoreCase)) recurse = true;
            else if (part.Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase)) self = true;
            else if (part.StartsWith("AGE", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[3..], out int d) && d > 0) age = d;
            else patterns.AddRange(part.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0));
        }
        if (patterns.Count == 0) patterns.Add("*.*");
        return new FileKey(dir, patterns, recurse, self, age);
    }

    public static RegKey? ParseRegKey(string text)
    {
        int bar = text.IndexOf('|');
        string key = RegPath.Normalize(bar < 0 ? text : text[..bar]);
        if (key.Length == 0) return null;
        string? value = bar < 0 ? null : text[(bar + 1)..].Trim();
        return new RegKey(key, string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>"FILE|pasta|nome;nome", "PATH|pasta" ou "REG|chave|valor".</summary>
    public static ExcludeKey? ParseExclude(string text)
    {
        var parts = text.Split('|');
        if (parts.Length < 2) return null;
        string path = parts[1].Trim();
        var names = parts.Length > 2 ? parts[2].Split(';').Select(n => n.Trim()).Where(n => n.Length > 0).ToList() : new List<string>();
        switch (parts[0].Trim().ToUpperInvariant())
        {
            case "PATH": return path.Length == 0 ? null : new ExcludeKey(ExcludeKind.Path, path, []);
            case "FILE": return path.Length == 0 || names.Count == 0 ? null : new ExcludeKey(ExcludeKind.File, path, names);
            case "REG":
                string key = RegPath.Normalize(path);
                return key.Length == 0 ? null : new ExcludeKey(ExcludeKind.Registry, key, names);
            default: return null;
        }
    }
}
