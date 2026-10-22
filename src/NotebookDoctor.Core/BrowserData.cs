using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

public enum BrowserFamily { Chromium, Firefox }

/// <summary>
/// O que a limpeza de navegadores sabe apagar. Senhas, favoritos, extensões e configurações não estão aqui de propósito:
/// não existe opção para elas, e <see cref="BrowserSafety"/> barra qualquer passo que chegue perto.
/// </summary>
public enum BrowserDataKind { History, Cookies, Sessions, FormHistory }

public sealed record BrowserInfo(string Id, string Name, BrowserFamily Family, IReadOnlyList<string> Processes);

/// <summary><paramref name="Dir"/>: a pasta de dados do perfil, onde ficam History, Cookies etc.</summary>
public sealed record BrowserProfile(BrowserInfo Browser, string Name, string Dir)
{
    public string Key => $"{Browser.Id}|{Name}";
    public string Display => $"{Browser.Name} · {Name}";
}

public abstract record BrowserStep(BrowserDataKind Kind);

/// <summary>Arquivos do perfil, em caminhos relativos a ele. O último trecho pode ter curinga ("Sessions\*").</summary>
public sealed record DeleteFilesStep(BrowserDataKind Kind, IReadOnlyList<string> Relative) : BrowserStep(Kind);

/// <summary>Comandos DELETE no banco SQLite do perfil, tudo ou nada.</summary>
public sealed record SqlStep(BrowserDataKind Kind, string Db, IReadOnlyList<string> Statements) : BrowserStep(Kind);

public static class BrowserCatalog
{
    public static readonly BrowserInfo Chrome = new("chrome", "Google Chrome", BrowserFamily.Chromium, ["chrome"]);
    public static readonly BrowserInfo Edge = new("edge", "Microsoft Edge", BrowserFamily.Chromium, ["msedge"]);
    public static readonly BrowserInfo Brave = new("brave", "Brave", BrowserFamily.Chromium, ["brave"]);
    public static readonly BrowserInfo Vivaldi = new("vivaldi", "Vivaldi", BrowserFamily.Chromium, ["vivaldi"]);
    public static readonly BrowserInfo Opera = new("opera", "Opera", BrowserFamily.Chromium, ["opera"]);
    public static readonly BrowserInfo OperaGx = new("operagx", "Opera GX", BrowserFamily.Chromium, ["opera"]);
    public static readonly BrowserInfo Firefox = new("firefox", "Mozilla Firefox", BrowserFamily.Firefox, ["firefox"]);

    public static IReadOnlyList<BrowserInfo> All { get; } = [Chrome, Edge, Brave, Vivaldi, Opera, OperaGx, Firefox];

    public static BrowserInfo? ById(string id) => All.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(BrowserDataKind kind) => kind switch
    {
        BrowserDataKind.History => Loc.T("Histórico de navegação"),
        BrowserDataKind.Cookies => Loc.T("Cookies"),
        BrowserDataKind.Sessions => Loc.T("Abas da última sessão"),
        _ => Loc.T("Histórico de formulários"),
    };

    public static string Description(BrowserDataKind kind) => kind switch
    {
        BrowserDataKind.History => Loc.T("Sites visitados, endereços digitados e a lista de downloads (os arquivos baixados ficam). Favoritos ficam."),
        BrowserDataKind.Cookies => Loc.T("Sites em que você estava conectado deixam de reconhecer você, menos os da lista de sites a manter."),
        BrowserDataKind.Sessions => Loc.T("“Continuar de onde parou” deixa de funcionar."),
        _ => Loc.T("Textos digitados em formulários que o navegador sugere de novo. Endereços e cartões salvos ficam."),
    };
}

/// <summary>Um site da lista “a manter”: só o nome do site, em minúsculas, sem esquema, caminho, porta nem “www.”.</summary>
public static class SiteName
{
    private static readonly Regex Valid = new(@"^[a-z0-9]([a-z0-9._-]*[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly string[] SecondLevel = ["com", "co", "org", "net", "gov", "edu", "ac", "mil"];

    /// <summary>Null quando não parece um nome de site (letras fora do ASCII, espaços, aspas…): ele nunca entra num comando SQL.</summary>
    public static string? Normalize(string? input)
    {
        string s = (input ?? "").Trim().ToLowerInvariant();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        int at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        int colon = s.LastIndexOf(':');
        if (colon >= 0 && s[(colon + 1)..].All(char.IsDigit)) s = s[..colon];
        s = s.Trim('.');
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        return s.Length is > 0 and <= 253 && Valid.IsMatch(s) && !s.Contains("..", StringComparison.Ordinal) ? s : null;
    }

    /// <summary>O site de um host de cookie: “.mail.google.com” vira “google.com”; “.x.com.br” vira “x.com.br”.</summary>
    public static string Of(string host)
    {
        string h = (Normalize(host) ?? host.Trim().Trim('.').ToLowerInvariant());
        if (h.Length == 0 || h.All(c => char.IsDigit(c) || c == '.')) return h; // endereço IP
        var labels = h.Split('.');
        if (labels.Length <= 2) return h;
        int keep = labels[^1].Length == 2 && SecondLevel.Contains(labels[^2]) ? 3 : 2;
        return string.Join('.', labels.Skip(labels.Length - keep));
    }
}

/// <summary>Barreiras dos passos de limpeza: nenhum caminho sai do perfil e nenhum comando chega nas senhas, favoritos ou configurações.</summary>
public static class BrowserSafety
{
    private static readonly string[] ForbiddenFiles =
    [
        "Login Data", "Login Data For Account", "logins.json", "logins-backup.json", "key3.db", "key4.db", "cert9.db", "Bookmarks", "Bookmarks.bak",
        "Preferences", "Secure Preferences", "Local State", "places.sqlite", "prefs.js", "user.js", "extensions", "Extensions", "Web Data",
        "Affiliation Database", "Account Web Data",
    ];

    private static readonly string[] AllowedDbs = [@"Network\Cookies", "Cookies", "Web Data", "places.sqlite", "cookies.sqlite", "formhistory.sqlite"];
    private static readonly string[] AllowedTables = ["cookies", "autofill", "moz_cookies", "moz_historyvisits", "moz_inputhistory", "moz_places", "moz_formhistory"];
    private static readonly Regex Delete = new(@"^DELETE FROM (?<t>[a-z_]+)( WHERE [^;]+)?$", RegexOptions.Compiled);

    public static bool IsPlainRelative(string relative)
    {
        string r = relative.Replace('/', '\\');
        return r.Length > 0 && !Path.IsPathRooted(r) && !r.Contains(':') && !r.Split('\\').Any(s => s is ".." or "." or "");
    }

    public static bool IsAllowedFile(string relative)
    {
        if (!IsPlainRelative(relative)) return false;
        string leaf = relative.Replace('/', '\\').Split('\\')[^1];
        return !ForbiddenFiles.Contains(leaf, StringComparer.OrdinalIgnoreCase) && !relative.Contains("Extension", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedStep(BrowserStep step) => step switch
    {
        DeleteFilesStep f => f.Relative.Count > 0 && f.Relative.All(IsAllowedFile),
        SqlStep s => IsPlainRelative(s.Db) && AllowedDbs.Contains(s.Db, StringComparer.OrdinalIgnoreCase) && s.Statements.Count > 0
                     && s.Statements.All(q => Delete.Match(q) is { Success: true } m && AllowedTables.Contains(m.Groups["t"].Value)),
        _ => false,
    };
}

/// <summary>Descobre os navegadores e os perfis que existem neste computador.</summary>
public static class BrowserProfiles
{
    public static List<BrowserProfile> Discover(KnownPlaces p, ICleanSystem sys)
    {
        var list = new List<BrowserProfile>();
        void Chromium(BrowserInfo b, string userData, bool rootIsProfile = false)
        {
            if (rootIsProfile)
            {
                if (sys.FileExists(Paths.Join(userData, "Preferences"))) list.Add(new BrowserProfile(b, Loc.T("Padrão"), userData));
                return;
            }
            foreach (var d in sys.Directories(userData))
            {
                string name = Paths.Leaf(d);
                if ((name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal)) && sys.FileExists(Paths.Join(d, "Preferences")))
                    list.Add(new BrowserProfile(b, name == "Default" ? Loc.T("Padrão") : name, d));
            }
        }
        Chromium(BrowserCatalog.Chrome, Paths.Join(p.Local, @"Google\Chrome\User Data"));
        Chromium(BrowserCatalog.Edge, Paths.Join(p.Local, @"Microsoft\Edge\User Data"));
        Chromium(BrowserCatalog.Brave, Paths.Join(p.Local, @"BraveSoftware\Brave-Browser\User Data"));
        Chromium(BrowserCatalog.Vivaldi, Paths.Join(p.Local, @"Vivaldi\User Data"));
        Chromium(BrowserCatalog.Opera, Paths.Join(p.Roaming, @"Opera Software\Opera Stable"), rootIsProfile: true);
        Chromium(BrowserCatalog.OperaGx, Paths.Join(p.Roaming, @"Opera Software\Opera GX Stable"), rootIsProfile: true);
        foreach (var d in sys.Directories(Paths.Join(p.Roaming, @"Mozilla\Firefox\Profiles")))
            if (sys.FileExists(Paths.Join(d, "prefs.js")) || sys.FileExists(Paths.Join(d, "places.sqlite")))
            {
                string leaf = Paths.Leaf(d);
                int dot = leaf.IndexOf('.');
                list.Add(new BrowserProfile(BrowserCatalog.Firefox, dot >= 0 && dot < leaf.Length - 1 ? leaf[(dot + 1)..] : leaf, d));
            }
        return list;
    }
}

/// <summary>Monta os passos de limpeza de cada tipo de dado, para cada família de navegador.</summary>
public static class BrowserPlans
{
    /// <summary>
    /// Cláusula “este host é de um dos sites a manter”: o próprio site, “.site” e qualquer subdomínio.
    /// Os nomes passam por <see cref="SiteName.Normalize"/>, então só chegam aqui letras, números, ponto, hífen e sublinhado.
    /// </summary>
    public static string KeepClause(string column, IEnumerable<string> sites)
    {
        var parts = sites.Select(SiteName.Normalize).OfType<string>().Distinct()
            .Select(h => $"{column} = '{h}' OR {column} LIKE '%.{h.Replace("_", "\\_")}' ESCAPE '\\'").ToList();
        return string.Join(" OR ", parts);
    }

    /// <summary>
    /// Uma lista com nomes, mas nenhum válido (arquivo de configurações editado à mão, por exemplo), não pode virar
    /// “apagar todos os cookies”: sem lista utilizável, os cookies ficam como estão.
    /// </summary>
    private static bool KeepListUsable(IReadOnlyList<string> keep) => keep.Count == 0 || KeepClause("x", keep).Length > 0;

    private static string DeleteWhereNotKept(string table, string column, IReadOnlyList<string> keep)
    {
        string clause = KeepClause(column, keep);
        return clause.Length == 0 ? $"DELETE FROM {table}" : $"DELETE FROM {table} WHERE NOT ({clause})";
    }

    public static List<BrowserStep> For(BrowserProfile profile, IEnumerable<BrowserDataKind> kinds, IReadOnlyList<string> keepSites)
    {
        var steps = new List<BrowserStep>();
        foreach (var kind in kinds.Distinct())
            steps.AddRange(profile.Browser.Family == BrowserFamily.Chromium ? Chromium(kind, keepSites) : Firefox(kind, keepSites));
        return steps.Where(BrowserSafety.IsAllowedStep).ToList();
    }

    private static IEnumerable<BrowserStep> Chromium(BrowserDataKind kind, IReadOnlyList<string> keep)
    {
        switch (kind)
        {
            case BrowserDataKind.History:
                yield return new DeleteFilesStep(kind,
                [
                    "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal", "Shortcuts", "Shortcuts-journal",
                    "Network Action Predictor", "Network Action Predictor-journal",
                ]);
                break;
            case BrowserDataKind.Cookies:
                if (!KeepListUsable(keep)) break;
                yield return new SqlStep(kind, @"Network\Cookies", [DeleteWhereNotKept("cookies", "host_key", keep)]);
                yield return new SqlStep(kind, "Cookies", [DeleteWhereNotKept("cookies", "host_key", keep)]); // Chromium antigo
                break;
            case BrowserDataKind.Sessions:
                yield return new DeleteFilesStep(kind, [@"Sessions\*", "Current Session", "Last Session", "Current Tabs", "Last Tabs"]);
                break;
            default:
                yield return new SqlStep(kind, "Web Data", ["DELETE FROM autofill"]);
                break;
        }
    }

    private static IEnumerable<BrowserStep> Firefox(BrowserDataKind kind, IReadOnlyList<string> keep)
    {
        switch (kind)
        {
            case BrowserDataKind.History:
                // places.sqlite também guarda os favoritos: só visitas e lugares que ninguém referencia saem
                yield return new SqlStep(kind, "places.sqlite",
                [
                    "DELETE FROM moz_historyvisits",
                    "DELETE FROM moz_inputhistory",
                    "DELETE FROM moz_places WHERE foreign_count = 0 AND id NOT IN (SELECT fk FROM moz_bookmarks WHERE fk IS NOT NULL)",
                ]);
                break;
            case BrowserDataKind.Cookies:
                if (!KeepListUsable(keep)) break;
                yield return new SqlStep(kind, "cookies.sqlite", [DeleteWhereNotKept("moz_cookies", "host", keep)]);
                break;
            case BrowserDataKind.Sessions:
                yield return new DeleteFilesStep(kind, ["sessionstore.jsonlz4", @"sessionstore-backups\*", "sessionCheckpoints.json"]);
                break;
            default:
                yield return new SqlStep(kind, "formhistory.sqlite", ["DELETE FROM moz_formhistory"]);
                break;
        }
    }
}

/// <summary>
/// “Limpar ao fechar”: percebe quando um navegador deixou de rodar. Só avisa depois de um tempo de calma (alguns
/// navegadores fecham e reabrem o processo ao atualizar), uma vez por fechamento.
/// </summary>
public sealed class BrowserWatch
{
    private readonly Dictionary<string, DateTime?> _closedAt = new(); // chave presente = estava aberto; valor = quando sumiu

    public List<string> Step(IReadOnlySet<string> running, DateTime now, TimeSpan settle)
    {
        var done = new List<string>();
        foreach (var id in running) _closedAt[id] = null;
        foreach (var id in _closedAt.Keys.Where(k => !running.Contains(k)).ToList())
        {
            _closedAt[id] ??= now;
            if (now - _closedAt[id]!.Value < settle) continue;
            done.Add(id);
            _closedAt.Remove(id);
        }
        return done;
    }
}

public enum ScheduleKind { Off, AtStart, Daily, Weekly, Monthly }

/// <summary>C17: quando a limpeza agendada roda.</summary>
public static class ScheduleRules
{
    public static ScheduleKind Parse(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "start" or "atstart" => ScheduleKind.AtStart,
        "daily" => ScheduleKind.Daily,
        "monthly" => ScheduleKind.Monthly,
        "off" => ScheduleKind.Off,
        _ => ScheduleKind.Weekly,
    };

    public static string Key(ScheduleKind kind) => kind switch
    {
        ScheduleKind.AtStart => "start", ScheduleKind.Daily => "daily", ScheduleKind.Monthly => "monthly", ScheduleKind.Off => "off", _ => "weekly",
    };

    /// <param name="ranThisSession">Já rodou desde que o app abriu (vale para “ao iniciar”).</param>
    public static bool Due(ScheduleKind kind, DateTime last, DateTime now, bool ranThisSession) => kind switch
    {
        ScheduleKind.AtStart => !ranThisSession,
        ScheduleKind.Daily => last.Date < now.Date,
        ScheduleKind.Weekly => (now - last).TotalDays >= 7,
        ScheduleKind.Monthly => now >= last.AddMonths(1),
        _ => false,
    };

    public static string Describe(ScheduleKind kind) => kind switch
    {
        ScheduleKind.AtStart => Loc.T("Ao abrir o app"),
        ScheduleKind.Daily => Loc.T("Todo dia"),
        ScheduleKind.Weekly => Loc.T("Toda semana"),
        ScheduleKind.Monthly => Loc.T("Todo mês"),
        _ => Loc.T("Desligada"),
    };
}

/// <summary>C15: o app avisa quando o lixo passa do limite, no máximo uma vez por dia.</summary>
public static class JunkMonitorRules
{
    public const long DefaultLimitBytes = 1L << 30;
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);
    public static readonly TimeSpan NotifyEvery = TimeSpan.FromHours(24);

    public static bool CheckDue(DateTime lastCheck, DateTime now) => now - lastCheck >= CheckEvery;

    public static bool ShouldNotify(long bytes, long limitBytes, DateTime lastNotified, DateTime now) =>
        limitBytes > 0 && bytes >= limitBytes && now - lastNotified >= NotifyEvery;
}
