using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

public enum ProgramKind { Program, Msi, Store, Update }

public enum ProgramFilter { Programs, Store, Updates, All }

public enum SizeSource { None, Installer, Folder }

/// <summary>Valores de uma chave de desinstalação (…\CurrentVersion\Uninstall\&lt;chave&gt;), como o Windows os guarda.</summary>
public sealed record UninstallKey
{
    public string KeyName { get; init; } = "";
    /// <summary>Caminho completo no formato do Editor do Registro (HKEY_LOCAL_MACHINE\Software\…).</summary>
    public string RegistryPath { get; init; } = "";
    /// <summary>HKLM (todos os usuários) ou HKCU (só este usuário).</summary>
    public bool MachineWide { get; init; }
    /// <summary>Visão de 32 bits do registro (WOW6432Node).</summary>
    public bool Wow64 { get; init; }
    public string? DisplayName { get; init; }
    public string? DisplayVersion { get; init; }
    public string? Publisher { get; init; }
    public string? InstallLocation { get; init; }
    public string? InstallDate { get; init; }
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? DisplayIcon { get; init; }
    public string? ParentKeyName { get; init; }
    public string? ReleaseType { get; init; }
    public int? EstimatedSizeKb { get; init; }
    public bool SystemComponent { get; init; }
    public bool WindowsInstaller { get; init; }
    public bool NoRemove { get; init; }
}

/// <summary>Pacote de app da Microsoft Store, como o Get-AppxPackage informa. <paramref name="DisplayName"/> vem do Menu Iniciar.</summary>
public sealed record StorePackage(string Name, string FullName, string Publisher, string Version, string InstallLocation,
                                  string SignatureKind, bool IsFramework, bool NonRemovable, string Architecture, string? DisplayName);

public sealed record Hotfix(string Id, string Description, DateTime? InstalledOn);

public sealed record UninstallCommand(string File, string Arguments);

public sealed class InstalledProgram
{
    /// <summary>Chave do registro, "store:pacote" ou "kb:número": identifica o item entre uma leitura e outra.</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public ProgramKind Kind { get; init; }
    /// <summary>true: todos os usuários; false: só este usuário; null: não se aplica (atualizações do Windows).</summary>
    public bool? MachineWide { get; init; }
    /// <summary>32 ou 64; 0 quando o registro não diz.</summary>
    public int Bits { get; init; }
    public DateTime? InstallDate { get; init; }
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string? ProductCode { get; init; }
    public string RegistryPath { get; init; } = "";
    public string DisplayIcon { get; init; } = "";
    public string PackageFullName { get; init; } = "";
    /// <summary>Componente de outro programa, atualização de programa ou app do sistema: só aparece com "Mostrar componentes do sistema".</summary>
    public bool IsSystem { get; init; }
    public bool CanUninstall { get; init; }
    public long Bytes { get; set; }
    public SizeSource SizeSource { get; set; }

    public string SizeText => SizeSource == SizeSource.None ? "—" : Units.Bytes(Bytes);
    public string InstallDateText => InstallDate?.ToString("d", CultureInfo.CurrentCulture) ?? "—";
    public string BitsText => Bits == 0 ? "—" : Loc.F("{0} bits", Bits);

    public string KindText => Kind switch
    {
        ProgramKind.Msi => Loc.T("Programa (MSI)"),
        ProgramKind.Store => Loc.T("App da Store"),
        ProgramKind.Update => Loc.T("Atualização do Windows"),
        _ => Loc.T("Programa"),
    };

    public string ScopeText => MachineWide switch
    {
        true => Loc.T("Todos os usuários"),
        false => Loc.T("Só este usuário"),
        null => "—",
    };
}

/// <summary>
/// Fase 5 (aba Programas): monta a lista como o Windows faz em "Aplicativos instalados" e acha o comando de
/// desinstalação. Regras puras: o registro e o PowerShell são lidos em ProgramInventory.
/// </summary>
public static partial class ProgramRules
{
    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\}")]
    private static partial Regex Guid();

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBreak();

    [GeneratedRegex(@"^[0-9A-Fa-f-]{20,}$")]
    private static partial Regex CertificateId();

    private static readonly string[] UpdateTypes = ["Security Update", "Update Rollup", "Hotfix", "Update", "ServicePack"];

    /// <summary>
    /// Mesmo critério do Windows: sem nome, sem comando de desinstalação, componente do sistema (SystemComponent=1)
    /// ou atualização de outro programa (ParentKeyName) não aparecem, a não ser com <paramref name="includeSystem"/>.
    /// </summary>
    public static InstalledProgram? FromKey(UninstallKey k, bool includeSystem)
    {
        string name = (k.DisplayName ?? "").Trim();
        if (name.Length == 0) return null;
        bool removable = !string.IsNullOrWhiteSpace(k.UninstallString) || k.WindowsInstaller;
        bool isSystem = k.SystemComponent || !removable || !string.IsNullOrWhiteSpace(k.ParentKeyName)
                        || UpdateTypes.Contains(k.ReleaseType?.Trim(), StringComparer.OrdinalIgnoreCase);
        if (isSystem && !includeSystem) return null;
        string? code = ProductCodeOf(k.KeyName, k.UninstallString, k.WindowsInstaller);
        long kb = k.EstimatedSizeKb is int v && v > 0 ? v : 0;
        return new InstalledProgram
        {
            Id = k.RegistryPath,
            Name = name,
            Publisher = (k.Publisher ?? "").Trim(),
            Version = (k.DisplayVersion ?? "").Trim(),
            Kind = code is null ? ProgramKind.Program : ProgramKind.Msi,
            MachineWide = k.MachineWide,
            Bits = k.MachineWide ? (k.Wow64 ? 32 : 64) : 0,
            InstallDate = ParseDate(k.InstallDate),
            InstallLocation = (k.InstallLocation ?? "").Trim().Trim('"'),
            UninstallString = (k.UninstallString ?? "").Trim(),
            QuietUninstallString = (k.QuietUninstallString ?? "").Trim(),
            ProductCode = code,
            RegistryPath = k.RegistryPath,
            DisplayIcon = (k.DisplayIcon ?? "").Trim(),
            IsSystem = isSystem,
            CanUninstall = removable && !k.NoRemove,
            Bytes = kb * 1024,
            SizeSource = kb > 0 ? SizeSource.Installer : SizeSource.None,
        };
    }

    /// <summary>Bibliotecas (frameworks) nunca aparecem; apps do sistema e os que o Windows não deixa remover, só com <paramref name="includeSystem"/>.</summary>
    public static InstalledProgram? FromStore(StorePackage s, bool includeSystem)
    {
        if (s.IsFramework) return null;
        bool isSystem = s.NonRemovable || s.SignatureKind.Equals("System", StringComparison.OrdinalIgnoreCase);
        if (isSystem && !includeSystem) return null;
        return new InstalledProgram
        {
            Id = "store:" + s.FullName,
            Name = string.IsNullOrWhiteSpace(s.DisplayName) ? FriendlyPackageName(s.Name) : s.DisplayName.Trim(),
            Publisher = PublisherOfCertificate(s.Publisher),
            Version = s.Version,
            Kind = ProgramKind.Store,
            MachineWide = false,
            Bits = s.Architecture switch { "X64" or "Arm64" => 64, "X86" or "Arm" => 32, _ => 0 },
            InstallLocation = s.InstallLocation,
            PackageFullName = s.FullName,
            IsSystem = isSystem,
            CanUninstall = !s.NonRemovable,
        };
    }

    /// <summary>Atualizações do Windows só são removidas pelo próprio Windows; "Desinstalar" abre a tela dele.</summary>
    public static InstalledProgram FromHotfix(Hotfix h) => new()
    {
        Id = "kb:" + h.Id,
        Name = string.IsNullOrWhiteSpace(h.Description) ? h.Id : $"{h.Id} — {h.Description.Trim()}",
        Publisher = "Microsoft Corporation",
        Kind = ProgramKind.Update,
        InstallDate = h.InstalledOn,
        CanUninstall = true,
    };

    public static ProgramFilter ParseFilter(string? tag) => tag switch
    {
        "store" => ProgramFilter.Store,
        "updates" => ProgramFilter.Updates,
        "all" => ProgramFilter.All,
        _ => ProgramFilter.Programs,
    };

    public static bool Matches(InstalledProgram p, ProgramFilter filter) => filter switch
    {
        ProgramFilter.Programs => p.Kind is ProgramKind.Program or ProgramKind.Msi,
        ProgramFilter.Store => p.Kind == ProgramKind.Store,
        ProgramFilter.Updates => p.Kind == ProgramKind.Update,
        _ => true,
    };

    /// <summary>
    /// O mesmo programa pode estar em mais de uma chave (visões de 32 e 64 bits, HKLM e HKCU). Fica uma linha por
    /// nome e versão, a que tem mais informação.
    /// </summary>
    public static List<InstalledProgram> Dedupe(IEnumerable<InstalledProgram> items) =>
        items.GroupBy(p => p.Kind == ProgramKind.Store ? p.Id : p.Name + "\u0001" + p.Version, StringComparer.OrdinalIgnoreCase)
             .Select(g => g.OrderByDescending(p => p.SizeSource != SizeSource.None)
                           .ThenByDescending(p => p.InstallLocation.Length > 0)
                           .ThenByDescending(p => p.MachineWide == true)
                           .First())
             .ToList();

    /// <summary>Código do produto de pacotes MSI: o nome da chave ({GUID}) ou o GUID do "MsiExec.exe /I{…}".</summary>
    public static string? ProductCodeOf(string keyName, string? uninstallString, bool windowsInstaller)
    {
        bool msiexec = (uninstallString ?? "").Contains("msiexec", StringComparison.OrdinalIgnoreCase);
        if (!windowsInstaller && !msiexec) return null;
        string key = keyName.Trim();
        var m = Guid().Match(key);
        if (m.Success && m.Length == key.Length) return m.Value.ToUpperInvariant();
        m = msiexec ? Guid().Match(uninstallString!) : Match.Empty;
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Separa programa e argumentos: "\"C:\\…\\unins000.exe\" /x" ou, sem aspas, o caminho mais longo que existe
    /// ("C:\Program Files\App\uninstall.exe /x" é comum no registro). Sem nada que exista, vale até o primeiro .exe.
    /// </summary>
    public static UninstallCommand? ParseCommand(string? command, Func<string, bool> fileExists)
    {
        string c = Environment.ExpandEnvironmentVariables((command ?? "").Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"')
        {
            int end = c.IndexOf('"', 1);
            return end < 0 ? new UninstallCommand(c[1..].Trim(), "") : new UninstallCommand(c[1..end], c[(end + 1)..].Trim());
        }
        var words = c.Split(' ');
        string Head(int n) => string.Join(' ', words.Take(n));
        string Tail(int n) => string.Join(' ', words.Skip(n)).Trim();
        for (int n = words.Length; n >= 1; n--)
            if (fileExists(Head(n))) return new UninstallCommand(Head(n), Tail(n));
        for (int n = 1; n <= words.Length; n++)
            if (Head(n).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return new UninstallCommand(Head(n), Tail(n));
        return new UninstallCommand(words[0], Tail(1));
    }

    /// <summary>
    /// Comando que abre a desinstalação. Em pacotes MSI é "msiexec /x", porque muitos registram "/I", que abre a tela
    /// de reparar ou alterar em vez de desinstalar.
    /// </summary>
    public static UninstallCommand? CommandFor(InstalledProgram p, Func<string, bool> fileExists) =>
        p.Kind == ProgramKind.Msi && p.ProductCode is { } code
            ? new UninstallCommand("msiexec.exe", "/x " + code)
            : ParseCommand(p.UninstallString, fileExists);

    /// <summary>"\"C:\\App\\app.exe\",-101" → (C:\App\app.exe, -101). Sem índice, 0.</summary>
    public static (string Path, int Index)? IconSource(string? displayIcon)
    {
        string s = Environment.ExpandEnvironmentVariables((displayIcon ?? "").Trim());
        int index = 0;
        int comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
        {
            index = n;
            s = s[..comma];
        }
        s = s.Trim().Trim('"').Trim();
        return s.Length == 0 ? null : (s, index);
    }

    /// <summary>
    /// Pasta do programa: a InstallLocation; sem ela, a pasta do ícone ou do desinstalador, desde que não seja do
    /// Windows, um cache de instaladores (C:\Windows\Installer, Package Cache) nem Common Files, que são compartilhados.
    /// </summary>
    public static string? FolderOf(InstalledProgram p, Func<string, bool> dirExists, Func<string, bool> fileExists)
    {
        string location = Environment.ExpandEnvironmentVariables(p.InstallLocation).TrimEnd('\\');
        if (location.Length > 0 && dirExists(location)) return location;
        foreach (var file in new[] { IconSource(p.DisplayIcon)?.Path, ParseCommand(p.UninstallString, fileExists)?.File })
        {
            if (string.IsNullOrEmpty(file) || IsSharedPlace(file)) continue;
            int slash = file.LastIndexOf('\\');
            if (slash <= 2) continue;
            string dir = file[..slash];
            if (dirExists(dir)) return dir;
        }
        return null;
    }

    private static bool IsSharedPlace(string path) =>
        path.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Installer\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\Common Files\", StringComparison.OrdinalIgnoreCase)
        || !path.Contains('\\');

    /// <summary>
    /// Só mede pastas do próprio programa. A raiz do disco, Program Files, ProgramData, a pasta do usuário ou algo
    /// dentro do Windows somariam o que não é dele.
    /// </summary>
    public static bool CanMeasure(string folder, IEnumerable<string> sharedRoots, string windowsDir)
    {
        string f = folder.Trim().TrimEnd('\\');
        if (f.Length <= 2) return false;
        string win = windowsDir.TrimEnd('\\');
        if (f.Equals(win, StringComparison.OrdinalIgnoreCase) || f.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        return !sharedRoots.Any(r => r.Length > 0 && string.Equals(r.TrimEnd('\\'), f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Valor de "LastKey" do Editor do Registro. Desde o Windows 10 ele guarda o caminho com o nó raiz na frente
    /// ("Computador\HKEY_…", no idioma do Windows); o prefixo do valor atual é reaproveitado.
    /// </summary>
    public static string RegeditLastKey(string? current, string path)
    {
        int i = current?.IndexOf("HKEY_", StringComparison.OrdinalIgnoreCase) ?? -1;
        return i > 0 ? current![..i] + path : path;
    }

    /// <summary>"Microsoft.WindowsCalculator" → "Windows Calculator"; "SpotifyAB.SpotifyMusic" → "Spotify Music".</summary>
    public static string FriendlyPackageName(string name)
    {
        string last = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        if (last.Length == 0) last = name;
        return WordBreak().Replace(last, " ").Trim();
    }

    /// <summary>"CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond" → "Microsoft Corporation". Ids sem nome (CN=4B3D…) ficam vazios.</summary>
    public static string PublisherOfCertificate(string subject)
    {
        string? Part(string key) => subject.Split(',').Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim().Trim('"');
        string value = Part("O") ?? Part("CN") ?? "";
        return CertificateId().IsMatch(value) ? "" : value;
    }

    /// <summary>InstallDate costuma vir como "20260930"; alguns instaladores gravam "2026-09-30".</summary>
    public static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text?.Trim(), ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Lê o JSON do script de inventário: {"store":[{n,f,p,v,l,s,fw,nr,a,d}…],"kb":[{id,d,t}…]}.</summary>
    public static (List<StorePackage> Store, List<Hotfix> Updates) ParseInventory(string json)
    {
        var store = new List<StorePackage>();
        var updates = new List<Hotfix>();
        if (string.IsNullOrWhiteSpace(json)) return (store, updates);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (store, updates);
            foreach (var x in Items(root, "store"))
            {
                string name = S(x, "n"), full = S(x, "f");
                if (name.Length == 0 || full.Length == 0) continue;
                string display = S(x, "d");
                store.Add(new StorePackage(name, full, S(x, "p"), S(x, "v"), S(x, "l"), S(x, "s"), B(x, "fw"), B(x, "nr"), S(x, "a"),
                                           display.Length > 0 ? display : null));
            }
            foreach (var x in Items(root, "kb"))
            {
                string id = S(x, "id");
                if (id.Length == 0) continue;
                DateTime? on = DateTime.TryParseExact(S(x, "t"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
                updates.Add(new Hotfix(id, S(x, "d"), on));
            }
        }
        catch (JsonException) { }
        return (store, updates);

        static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
            !root.TryGetProperty(name, out var e) ? [] : e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];
        static string S(JsonElement x, string p) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        static bool B(JsonElement x, string p) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>Texto de "Propriedades": só os campos que o programa informa.</summary>
    public static string Describe(InstalledProgram p)
    {
        var lines = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "—") lines.Add($"{label}: {value}");
        }
        Add(Loc.T("Nome"), p.Name);
        Add(Loc.T("Editor"), p.Publisher);
        Add(Loc.T("Versão"), p.Version);
        Add(Loc.T("Tipo"), p.KindText);
        Add(Loc.T("Instalado para"), p.ScopeText);
        Add(Loc.T("Arquitetura"), p.BitsText);
        Add(Loc.T("Instalado em"), p.InstallDateText);
        Add(Loc.T("Tamanho"), p.SizeSource == SizeSource.Folder ? $"{p.SizeText} {Loc.T("(medido na pasta)")}" : p.SizeText);
        Add(Loc.T("Pasta"), p.InstallLocation);
        Add(Loc.T("Desinstalação"), p.UninstallString);
        Add(Loc.T("Desinstalação silenciosa"), p.QuietUninstallString);
        Add(Loc.T("Código do produto (MSI)"), p.ProductCode);
        Add(Loc.T("Chave do registro"), p.RegistryPath);
        Add(Loc.T("Pacote"), p.PackageFullName);
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Exporta a lista da aba Programas em CSV (planilha) ou HTML.</summary>
public static class ProgramExport
{
    private static string[] Header() =>
    [
        Loc.T("Nome"), Loc.T("Editor"), Loc.T("Versão"), Loc.T("Tamanho"), Loc.T("Bytes"), Loc.T("Instalado em"), Loc.T("Tipo"),
        Loc.T("Instalado para"), Loc.T("Arquitetura"), Loc.T("Pasta"), Loc.T("Chave do registro ou pacote"),
    ];

    private static string[] Row(InstalledProgram p) =>
    [
        p.Name, p.Publisher, p.Version, p.SizeText, p.SizeSource == SizeSource.None ? "" : p.Bytes.ToString(CultureInfo.InvariantCulture),
        p.InstallDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", p.KindText, p.ScopeText, p.BitsText, p.InstallLocation,
        p.RegistryPath.Length > 0 ? p.RegistryPath : p.PackageFullName,
    ];

    /// <summary>
    /// <paramref name="separator"/> é o separador de listas do idioma (";" em português, "," em inglês), que o Excel
    /// usa ao abrir o arquivo com clique duplo.
    /// </summary>
    public static string Csv(IEnumerable<InstalledProgram> items, string separator)
    {
        var sb = new StringBuilder();
        void Line(IEnumerable<string> cells) => sb.Append(string.Join(separator, cells.Select(c => Cell(c, separator)))).Append("\r\n");
        Line(Header());
        foreach (var p in items) Line(Row(p));
        return sb.ToString();
    }

    /// <summary>Aspas quando preciso; texto que começa com = + - @ ganha um apóstrofo para a planilha não tratar como fórmula.</summary>
    private static string Cell(string value, string separator)
    {
        string v = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return v.Contains(separator) || v.Contains('"') || v.Contains('\n') || v.Contains('\r') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    public static string Html(IEnumerable<InstalledProgram> items, string computer, DateTime when)
    {
        static string E(string t) => WebUtility.HtmlEncode(t);
        var list = items.ToList();
        string title = Loc.T("Programas instalados");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Loc.Culture.Name).Append("\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(E(title)).Append(" · Notebook Doctor</title>")
          .Append("""
<style>
:root{--bg:#fff;--card:#f2f2f9;--on:#121212;--muted:#555}
@media(prefers-color-scheme:dark){:root{--bg:#121212;--card:#1e1e1e;--on:#fff;--muted:#b8b8b8}}
body{font:14px/1.5 "Segoe UI",sans-serif;background:var(--bg);color:var(--on);margin:0;padding:32px}
h1{font-size:26px;margin:0}.muted{color:var(--muted)}
table{width:100%;border-collapse:collapse;margin-top:16px}td,th{text-align:left;padding:6px 8px;border-bottom:1px solid #8884;vertical-align:top}
th{background:var(--card);position:sticky;top:0}td.n{white-space:nowrap}
</style></head><body>
""");
        sb.Append("<h1>").Append(E(title)).Append("</h1><div class=\"muted\">")
          .Append(E($"{Pt.N(list.Count, Loc.T("item"), Loc.T("itens"))} · {computer} · {when.ToString("g", CultureInfo.CurrentCulture)}"))
          .Append("</div><table><thead><tr>");
        var header = Header();
        foreach (var (h, i) in header.Select((h, i) => (h, i)))
            if (i != 4) sb.Append("<th>").Append(E(h)).Append("</th>"); // a coluna "Bytes" é só para a planilha
        sb.Append("</tr></thead><tbody>");
        foreach (var p in list)
        {
            sb.Append("<tr>");
            var row = Row(p);
            for (int i = 0; i < row.Length; i++)
                if (i != 4) sb.Append(i is 3 or 5 ? "<td class=\"n\">" : "<td>").Append(E(row[i])).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }
}
