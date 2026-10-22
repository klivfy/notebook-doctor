namespace NotebookDoctor.Services;

/// <summary>
/// As regras próprias (leva 6.1): rastros do Windows, do Explorador e do Office. Escritas por nós a partir da
/// documentação da Microsoft das chaves e pastas; nada vem de lista de terceiros.
/// </summary>
public static class BuiltInRules
{
    private const string Explorer = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string Recent = @"%AppData%\Microsoft\Windows\Recent";

    private static CleanRule Make(string name, CleanCategory cat, string[] files, string[] regs, string warning = "",
        CleanAction action = CleanAction.None, bool admin = false, bool on = false, string[]? detect = null) => new()
    {
        Name = name, Category = cat, Origin = RuleOrigin.Builtin, Warning = warning, Action = action, NeedsAdmin = admin, DefaultOn = on,
        FileKeys = files.Select(f => Winapp2Parser.ParseFileKey(f)).OfType<FileKey>().ToList(),
        RegKeys = regs.Select(r => Winapp2Parser.ParseRegKey(r)).OfType<RegKey>().ToList(),
        Detect = detect ?? [],
    };

    public static IReadOnlyList<CleanRule> All() => [.. Windows(), .. Office()];

    public static IReadOnlyList<CleanRule> Windows() =>
    [
        Make("Documentos recentes", CleanCategory.Windows,
            [$@"{Recent}|*.lnk", $@"{Recent}\AutomaticDestinations|*.automaticDestinations-ms", $@"{Recent}\CustomDestinations|*.customDestinations-ms"],
            [$@"{Explorer}\RecentDocs"],
            warning: "Esvazia “Recentes”, o Acesso rápido e as listas de saltos da barra de tarefas."),
        Make("Executar, endereços digitados e pesquisas do Explorador", CleanCategory.Windows, [],
            [$@"{Explorer}\RunMRU", $@"{Explorer}\TypedPaths", $@"{Explorer}\WordWheelQuery"]),
        Make("Listas das caixas Abrir e Salvar", CleanCategory.Windows, [],
            [$@"{Explorer}\ComDlg32\OpenSavePidlMRU", $@"{Explorer}\ComDlg32\LastVisitedPidlMRU", $@"{Explorer}\ComDlg32\LastVisitedPidlMRULegacy",
             $@"{Explorer}\ComDlg32\CIDSizeMRU"]),
        Make("Cache de DNS", CleanCategory.Windows, [], [], "A próxima visita a cada site consulta o DNS de novo.", CleanAction.FlushDns, admin: true),
        Make("Área de transferência", CleanCategory.Windows, [], [], "Apaga o que foi copiado e ainda não colado.", CleanAction.ClearClipboard),
        Make("Despejos de memória do Windows", CleanCategory.Windows,
            [@"%SystemRoot%\Minidump|*.dmp", @"%SystemRoot%\LiveKernelReports|*.dmp|RECURSE", "%SystemRoot%|MEMORY.DMP"], [],
            "Servem para descobrir a causa de telas azuis. Apague se não for investigar.", admin: true),
        Make("Logs de instalação e do Windows", CleanCategory.Windows,
            [@"%SystemRoot%\Logs\CBS|*.log;*.cab", @"%SystemRoot%\Panther|*.log;*.etl", @"%SystemRoot%\Logs\WindowsUpdate|*.etl"], [], admin: true, on: true),
        Make("Relatórios de erro do Windows (todos os usuários)", CleanCategory.Windows,
            [@"%ProgramData%\Microsoft\Windows\WER\ReportArchive|*.*|RECURSE", @"%ProgramData%\Microsoft\Windows\WER\ReportQueue|*.*|RECURSE",
             @"%ProgramData%\Microsoft\Windows\WER\Temp|*.*|RECURSE"], [], admin: true, on: true),
    ];

    private static readonly string[] OfficeVersions = ["12.0", "14.0", "15.0", "16.0"];
    private static readonly string[] OfficeApps = ["Word", "Excel", "PowerPoint", "Access", "Publisher", "Visio", "OneNote", "Project"];

    public static IReadOnlyList<CleanRule> Office()
    {
        var regs = new List<string>();
        foreach (var v in OfficeVersions)
            foreach (var app in OfficeApps)
            {
                string root = $@"HKCU\Software\Microsoft\Office\{v}\{app}";
                regs.AddRange([$@"{root}\File MRU", $@"{root}\Place MRU", $@"{root}\User MRU\*\File MRU", $@"{root}\User MRU\*\Place MRU"]);
            }
        return
        [
            Make("Office: arquivos e pastas recentes", CleanCategory.Office, [@"%AppData%\Microsoft\Office\Recent|*.lnk"], [.. regs],
                "Word, Excel, PowerPoint, Access, Publisher, Visio, OneNote e Project deixam de mostrar os arquivos recentes."),
        ];
    }
}

public enum JunkKind { Temporary, OfficeLock, CheckDisk, OldDump, Backup, OldFile, LostFolder }

/// <summary>Um arquivo ou pasta que a busca de arquivos inúteis (R15) achou.</summary>
public sealed class JunkItem
{
    public required string Path { get; init; }
    public JunkKind Kind { get; init; }
    public bool IsFolder { get; init; }
    public long Bytes { get; init; }
    public DateTime Modified { get; init; }
    public bool Selected { get; set; }

    public string KindText => Kind switch
    {
        JunkKind.Temporary => Loc.T("Arquivo temporário"),
        JunkKind.OfficeLock => Loc.T("Trava do Office que sobrou"),
        JunkKind.CheckDisk => Loc.T("Fragmento do Chkdsk"),
        JunkKind.OldDump => Loc.T("Despejo de memória antigo"),
        JunkKind.Backup => Loc.T("Cópia de segurança (.bak)"),
        JunkKind.OldFile => Loc.T("Arquivo antigo (.old)"),
        _ => Loc.T("Pasta de arquivos recuperados"),
    };

    public string SizeText => Bytes > 0 ? Units.Bytes(Bytes) : "";
}

/// <summary>
/// R15: arquivos inúteis por nome (.tmp, ~$, .chk, FOUND.nnn, .dmp antigos). .bak, .old e FOUND ficam desmarcados:
/// podem ser da pessoa. Não entra no Windows, em Program Files, no conteúdo da pessoa (só as travas ~$ do Office,
/// que não têm utilidade e o Windows não deixa apagar enquanto o documento está aberto), nem nas pastas que ela indicar.
/// </summary>
public static class JunkRules
{
    public const int DumpDays = 30;
    public const int MaxItems = 5000;

    private static readonly string[] SkipNames = ["$Recycle.Bin", "System Volume Information", "Recovery", "$WinREAgent", "Config.Msi", ".git", "node_modules", "WindowsApps"];

    public static JunkKind? Classify(string fileName, DateTime modified, DateTime now, bool inUserContent)
    {
        string n = fileName;
        if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || n.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) return null;
        if (n.StartsWith("~$", StringComparison.Ordinal) && n.Length > 2) return modified < now.AddDays(-1) ? JunkKind.OfficeLock : null;
        if (inUserContent) return null;
        if (Glob.IsMatch("*.tmp", n) || Glob.IsMatch("*.temp", n)) return JunkKind.Temporary;
        if (Glob.IsMatch("*.chk", n)) return JunkKind.CheckDisk;
        if (Glob.IsMatch("*.dmp", n)) return modified < now.AddDays(-DumpDays) ? JunkKind.OldDump : null;
        if (Glob.IsMatch("*.bak", n)) return JunkKind.Backup;
        if (Glob.IsMatch("*.old", n)) return JunkKind.OldFile;
        return null;
    }

    public static bool IsLostFolder(string dirName) =>
        dirName.StartsWith("FOUND.", StringComparison.OrdinalIgnoreCase) && dirName.Length == 9 && dirName[6..].All(char.IsDigit);

    /// <summary>Pastas em que a busca nunca entra.</summary>
    public static bool SkipDir(string dir, KnownPlaces p, IEnumerable<string> excluded)
    {
        if (SkipNames.Contains(Paths.Leaf(dir), StringComparer.OrdinalIgnoreCase)) return true;
        if (Paths.IsUnder(dir, p.Windows) || Paths.IsUnder(dir, p.ProgramFiles) || Paths.IsUnder(dir, p.ProgramFilesX86)) return true;
        if (Paths.IsUnder(dir, Paths.Join(p.ProgramData, "Microsoft"))) return true;
        return excluded.Any(e => Paths.IsUnder(dir, e));
    }

    public static bool InUserContent(string dir, KnownPlaces p) => p.UserContent.Any(c => Paths.IsUnder(dir, c));

    public static List<JunkItem> Scan(ICleanSystem sys, KnownPlaces p, IEnumerable<string> roots, IEnumerable<string> excluded, DateTime now, CancellationToken ct = default)
    {
        var skip = excluded.ToList();
        var items = new List<JunkItem>();
        foreach (var root in roots) Walk(Paths.Clean(root), false);
        return items.OrderByDescending(i => i.Bytes).ToList();

        void Walk(string dir, bool user)
        {
            if (items.Count >= MaxItems) return;
            ct.ThrowIfCancellationRequested();
            if (SkipDir(dir, p, skip)) return;
            bool inUser = user || InUserContent(dir, p);
            foreach (var f in sys.DirectFiles(dir))
            {
                var when = sys.LastWrite(f);
                if (Classify(Paths.Leaf(f), when, now, inUser) is { } kind)
                    items.Add(new JunkItem
                    {
                        Path = f, Kind = kind, Bytes = sys.Size(f), Modified = when,
                        Selected = kind is JunkKind.Temporary or JunkKind.OfficeLock or JunkKind.CheckDisk or JunkKind.OldDump,
                    });
            }
            foreach (var d in sys.Directories(dir))
            {
                if (!inUser && IsLostFolder(Paths.Leaf(d)))
                {
                    items.Add(new JunkItem { Path = d, Kind = JunkKind.LostFolder, IsFolder = true, Bytes = sys.Size(d), Modified = sys.LastWrite(d) });
                    continue;
                }
                Walk(d, inUser);
            }
        }
    }
}

public enum ModuleRisk { Safe, Care }

/// <summary>Um módulo da Limpeza de Disco do Windows (chave em VolumeCaches).</summary>
public sealed record DiskModule(string Key, string Label, ModuleRisk Risk, string Warning = "")
{
    public bool Selected { get; set; }
}

/// <summary>
/// Limpeza de Disco do Windows (cleanmgr /sagerun): só quem sabe apagar com segurança o que é do sistema.
/// Os módulos que apagam arquivos da pessoa (Downloads) ou que já temos em outro lugar (Lixeira) não entram.
/// </summary>
public static class DiskCleanupRules
{
    public const int SageId = 7771;
    public static string StateFlagsName => $"StateFlags{SageId}";

    private static readonly string[] Blocked = ["DownloadsFolder", "Recycle Bin"];

    private static readonly Dictionary<string, (string Label, ModuleRisk Risk, string Warning)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Active Setup Temp Folders"] = ("Pastas temporárias de configuração", ModuleRisk.Safe, ""),
        ["BranchCache"] = ("Cache do BranchCache", ModuleRisk.Safe, ""),
        ["D3D Shader Cache"] = ("Cache de sombreadores do DirectX", ModuleRisk.Safe, ""),
        ["Delivery Optimization Files"] = ("Arquivos de Otimização de Entrega", ModuleRisk.Safe, ""),
        ["Device Driver Packages"] = ("Pacotes de driver de dispositivo", ModuleRisk.Care, "Drivers antigos podem ser necessários para voltar a uma versão anterior de um driver."),
        ["Diagnostic Data Viewer database files"] = ("Dados do Visualizador de Diagnóstico", ModuleRisk.Safe, ""),
        ["Downloaded Program Files"] = ("Arquivos de programas baixados", ModuleRisk.Safe, ""),
        ["Internet Cache Files"] = ("Arquivos temporários da Internet", ModuleRisk.Safe, ""),
        ["Language Pack"] = ("Pacotes de idioma que não estão em uso", ModuleRisk.Care, "Para usar esses idiomas depois, será preciso baixá-los de novo."),
        ["Old ChkDsk Files"] = ("Arquivos antigos do Chkdsk", ModuleRisk.Safe, ""),
        ["Offline Pages Files"] = ("Páginas da Web offline", ModuleRisk.Safe, ""),
        ["Previous Installations"] = ("Instalação anterior do Windows (Windows.old)", ModuleRisk.Care, "Depois disso, não dá mais para voltar à versão anterior do Windows."),
        ["RetailDemo Offline Content"] = ("Conteúdo de demonstração para lojas", ModuleRisk.Safe, ""),
        ["Setup Log Files"] = ("Logs de instalação", ModuleRisk.Safe, ""),
        ["System error memory dump files"] = ("Despejos de memória de erros do sistema", ModuleRisk.Safe, ""),
        ["System error minidump files"] = ("Minidumps de erros do sistema", ModuleRisk.Safe, ""),
        ["Temporary Files"] = ("Arquivos temporários", ModuleRisk.Safe, ""),
        ["Temporary Setup Files"] = ("Arquivos temporários de instalação", ModuleRisk.Safe, ""),
        ["Thumbnail Cache"] = ("Miniaturas", ModuleRisk.Safe, ""),
        ["Update Cleanup"] = ("Limpeza do Windows Update", ModuleRisk.Safe, "Pode demorar vários minutos."),
        ["Upgrade Discarded Files"] = ("Arquivos descartados na atualização do Windows", ModuleRisk.Care, "Servem para desfazer uma atualização."),
        ["User file versions"] = ("Versões antigas de arquivos (Histórico de Arquivos)", ModuleRisk.Care, "Apaga cópias antigas guardadas pelo Histórico de Arquivos."),
        ["Windows Defender"] = ("Arquivos do Windows Defender", ModuleRisk.Safe, ""),
        ["Windows Error Reporting Files"] = ("Relatórios de erro do Windows", ModuleRisk.Safe, ""),
        ["Windows ESD installation files"] = ("Arquivos de instalação ESD do Windows", ModuleRisk.Care, "São usados para reiniciar o PC (Restaurar o PC) sem mídia de instalação."),
        ["Windows Upgrade Log Files"] = ("Logs de atualização do Windows", ModuleRisk.Safe, ""),
    };

    public static bool IsBlocked(string key) => Blocked.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Os módulos que este Windows tem, em ordem alfabética do nome mostrado. Os de nome desconhecido ficam com cuidado.</summary>
    public static List<DiskModule> Modules(IEnumerable<string> keyNames) =>
        keyNames.Where(k => k.Length > 0 && !IsBlocked(k))
                .Select(k => Known.TryGetValue(k, out var m) ? new DiskModule(k, m.Label, m.Risk, m.Warning) : new DiskModule(k, k, ModuleRisk.Care, "Módulo que o app não conhece: confira no Windows antes."))
                .OrderBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static string Arguments => $"/sagerun:{SageId}";
}
