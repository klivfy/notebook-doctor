using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

public enum RegIssueKind { Uninstall, AppPath, Run, SharedDll, Font, ProgId }

/// <summary>
/// Uma entrada do registro com prova de que está quebrada. <see cref="Value"/> null: a chave inteira sai; senão, só esse valor.
/// <see cref="Evidence"/> é a prova em texto (o arquivo que não existe).
/// </summary>
public sealed record RegIssue(RegIssueKind Kind, string Key, string? Value, string Label, string Evidence, bool DefaultOn)
{
    public bool Machine => Key.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
}

/// <summary>O que o verificador precisa do sistema além de <see cref="ILeftoverSystem"/>.</summary>
public interface IRegistryCheckSystem : ILeftoverSystem
{
    /// <summary>Valor numérico (DWORD) do registro, ou null.</summary>
    int? IntValue(string key, string name);
    /// <summary>A raiz da unidade (ex.: "D:\") existe agora. Disco externo desconectado ou rede fora do ar: falso, e nada que esteja nele é prova de coisa alguma.</summary>
    bool DriveReady(string root);
}

/// <summary>
/// Leva 7.4 (C04): verificador de registro restrito. Só seis categorias, todas com prova objetiva (o arquivo apontado não
/// existe, numa unidade que está presente). Fica de fora, de propósito: CLSID e ActiveX, bibliotecas de tipos, MUICache,
/// ordem do Menu Iniciar e sons. Quem executa confere a prova de novo antes de apagar.
/// </summary>
public static class RegistryChecker
{
    private const string CurrentVersion = @"Software\Microsoft\Windows\CurrentVersion";
    private const string Hklm = "HKEY_LOCAL_MACHINE", Hkcu = "HKEY_CURRENT_USER";

    private static readonly Regex DrivePath = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);
    private static readonly string[] Launchable = [".exe", ".bat", ".cmd", ".com", ".scr"];

    // Chaves de estrutura do registro de classes: nunca são ProgID de programa
    private static readonly string[] ClassStructure =
    [
        "Applications", "AppID", "CLSID", "Interface", "TypeLib", "Folder", "Directory", "Drive", "Unknown", "SystemFileAssociations",
        "MIME", "Installer", "WOW6432Node", "AllFilesystemObjects", "Local Settings", "Extensions", "PROTOCOLS", "Wow6432Node",
    ];

    public static string Categoria(RegIssueKind kind) => kind switch
    {
        RegIssueKind.Uninstall => "Programa que não existe mais",
        RegIssueKind.AppPath => "Caminho de aplicativo",
        RegIssueKind.Run => "Início automático quebrado",
        RegIssueKind.SharedDll => "Arquivo compartilhado sumido",
        RegIssueKind.Font => "Fonte sem arquivo",
        _ => "Tipo de arquivo sem programa",
    };

    // ---------- chaves que o verificador conhece (e só elas) ----------

    private static IEnumerable<string> UninstallRoots() =>
        [$@"{Hklm}\{CurrentVersion}\Uninstall", $@"{Hklm}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", $@"{Hkcu}\{CurrentVersion}\Uninstall"];

    private static IEnumerable<string> AppPathRoots() =>
        [$@"{Hklm}\{CurrentVersion}\App Paths", $@"{Hklm}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths", $@"{Hkcu}\{CurrentVersion}\App Paths"];

    private static IEnumerable<string> RunRoots() =>
    [
        $@"{Hklm}\{CurrentVersion}\Run", $@"{Hklm}\{CurrentVersion}\RunOnce", $@"{Hkcu}\{CurrentVersion}\Run", $@"{Hkcu}\{CurrentVersion}\RunOnce",
        $@"{Hklm}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", $@"{Hklm}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
    ];

    private static IEnumerable<string> SharedDllRoots() =>
        [$@"{Hklm}\{CurrentVersion}\SharedDLLs", $@"{Hklm}\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\SharedDLLs"];

    private static IEnumerable<string> FontRoots() =>
        [$@"{Hklm}\Software\Microsoft\Windows NT\CurrentVersion\Fonts", $@"{Hkcu}\Software\Microsoft\Windows NT\CurrentVersion\Fonts"];

    private static IEnumerable<string> ClassRoots() => [$@"{Hklm}\Software\Classes", $@"{Hkcu}\Software\Classes"];

    private static IEnumerable<string> RootsOf(RegIssueKind kind) => kind switch
    {
        RegIssueKind.Uninstall => UninstallRoots(),
        RegIssueKind.AppPath => AppPathRoots(),
        RegIssueKind.Run => RunRoots(),
        RegIssueKind.SharedDll => SharedDllRoots(),
        RegIssueKind.Font => FontRoots(),
        _ => ClassRoots(),
    };

    private static string ParentOf(string key) => key.LastIndexOf('\\') is var i and > 0 ? key[..i] : "";

    /// <summary>
    /// A entrada está num lugar que o verificador conhece, com nome simples. Quem executa recusa o resto, mesmo que a lista
    /// que chegou até ele tenha sido adulterada.
    /// </summary>
    public static bool Allowed(RegIssue issue)
    {
        if (issue.Key.Contains("..") || issue.Key.Contains('/')) return false;
        bool wholeKey = issue.Value is null;
        bool valueKind = issue.Kind is RegIssueKind.Run or RegIssueKind.SharedDll or RegIssueKind.Font;
        if (valueKind == wholeKey) return false; // Run, SharedDLLs e Fontes são valores; as outras categorias são chaves
        string parent = wholeKey ? ParentOf(issue.Key) : issue.Key;
        if (!RootsOf(issue.Kind).Contains(parent, StringComparer.OrdinalIgnoreCase)) return false;
        string name = wholeKey ? issue.Key[(parent.Length + 1)..] : issue.Value!;
        if (name.Length == 0 || name.Length > 260 || name.Contains('\0')) return false;
        if (wholeKey && name.Contains('\\')) return false;
        if (issue.Kind == RegIssueKind.ProgId && (name.StartsWith('.') || ClassStructure.Contains(name, StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }

    // ---------- provas ----------

    private static string? AbsolutePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string p = raw.Trim().Trim('"').Replace('/', '\\');
        return DrivePath.IsMatch(p) && !p.Contains('%') ? p : null;
    }

    /// <summary>Programa que o comando executa, só se for um caminho completo de um tipo executável.</summary>
    private static string? Launched(string? command)
    {
        string? exe = AbsolutePath(CommandLine.ExecutablePath(command));
        return exe is not null && Launchable.Contains(System.IO.Path.GetExtension(exe), StringComparer.OrdinalIgnoreCase) ? exe : null;
    }

    private static bool MissingOnPresentDrive(IRegistryCheckSystem sys, string path) =>
        sys.DriveReady(path[..3]) && !sys.FileExists(path);

    private static bool FolderMissingOnPresentDrive(IRegistryCheckSystem sys, string path) =>
        sys.DriveReady(path[..3]) && !sys.DirectoryExists(path);

    private static string IconFile(string? icon)
    {
        string text = (icon ?? "").Trim();
        int comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _)) text = text[..comma];
        return text.Trim().Trim('"');
    }

    /// <summary>1. Entrada de desinstalação cujo desinstalador, pasta e ícone não existem. MSI, componentes do sistema e atualizações ficam de fora.</summary>
    public static RegIssue? CheckUninstall(IRegistryCheckSystem sys, string key)
    {
        string? name = sys.StringValue(key, "DisplayName");
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (sys.IntValue(key, "SystemComponent") == 1 || sys.IntValue(key, "WindowsInstaller") == 1) return null;
        if (!string.IsNullOrWhiteSpace(sys.StringValue(key, "ParentKeyName")) || !string.IsNullOrWhiteSpace(sys.StringValue(key, "ReleaseType"))) return null;

        string uninstall = sys.StringValue(key, "UninstallString") ?? "";
        if (uninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return null;
        string? exe = Launched(uninstall);
        if (exe is null || !MissingOnPresentDrive(sys, exe)) return null;

        // O desinstalador silencioso, se existir e apontar para outro arquivo, também tem de estar sumido
        string quiet = sys.StringValue(key, "QuietUninstallString") ?? "";
        if (quiet.Length > 0)
        {
            string? quietExe = Launched(quiet);
            if (quietExe is null || !MissingOnPresentDrive(sys, quietExe)) return null;
        }
        // A pasta do programa, se a entrada diz qual é, também tem de ter sumido
        string? folder = AbsolutePath(sys.StringValue(key, "InstallLocation"));
        if ((sys.StringValue(key, "InstallLocation") ?? "").Trim().Length > 0 && (folder is null || !FolderMissingOnPresentDrive(sys, folder.TrimEnd('\\') + "\\"))) return null;
        // E o arquivo do ícone, quando é um caminho completo
        string? icon = AbsolutePath(IconFile(sys.StringValue(key, "DisplayIcon")));
        if (icon is not null && !MissingOnPresentDrive(sys, icon)) return null;

        string version = sys.StringValue(key, "DisplayVersion") ?? "";
        return new RegIssue(RegIssueKind.Uninstall, key, null, version.Length > 0 ? $"{name} {version}" : name!, $"O desinstalador não existe: {exe}", true);
    }

    /// <summary>2. App Paths cujo programa não existe.</summary>
    public static RegIssue? CheckAppPath(IRegistryCheckSystem sys, string key)
    {
        string? exe = AbsolutePath(sys.StringValue(key, null));
        if (exe is null || !MissingOnPresentDrive(sys, exe)) return null;
        return new RegIssue(RegIssueKind.AppPath, key, null, key[(key.LastIndexOf('\\') + 1)..], $"O programa não existe: {exe}", true);
    }

    /// <summary>3. Valor de Run ou RunOnce que abre um programa que não existe.</summary>
    public static RegIssue? CheckRun(IRegistryCheckSystem sys, string key, string valueName)
    {
        if (valueName.Length == 0) return null;
        string? exe = Launched(sys.StringValue(key, valueName));
        if (exe is null || !MissingOnPresentDrive(sys, exe)) return null;
        return new RegIssue(RegIssueKind.Run, key, valueName, valueName, $"O programa não existe: {exe}", true);
    }

    /// <summary>4. SharedDLLs: o nome do valor é o arquivo.</summary>
    public static RegIssue? CheckSharedDll(IRegistryCheckSystem sys, string key, string valueName)
    {
        string? file = AbsolutePath(valueName);
        if (file is null || !MissingOnPresentDrive(sys, file)) return null;
        return new RegIssue(RegIssueKind.SharedDll, key, valueName, System.IO.Path.GetFileName(file), $"O arquivo não existe: {file}", true);
    }

    /// <summary>5. Fonte registrada cujo arquivo não existe. O dado é o nome do arquivo (na pasta Fonts) ou o caminho completo.</summary>
    public static RegIssue? CheckFont(IRegistryCheckSystem sys, string key, string valueName, string fontsDir)
    {
        string data = (sys.StringValue(key, valueName) ?? "").Trim().Trim('"');
        if (data.Length == 0 || data.Contains('%')) return null;
        string? file = AbsolutePath(data) ?? (data.Contains('\\') ? null : AbsolutePath(fontsDir.TrimEnd('\\') + "\\" + data));
        if (file is null || !MissingOnPresentDrive(sys, file)) return null;
        return new RegIssue(RegIssueKind.Font, key, valueName, valueName, $"O arquivo da fonte não existe: {file}", true);
    }

    /// <summary>
    /// 6. ProgID cujo comando "abrir" aponta para um programa que não existe, só quando nenhuma extensão nem protocolo o usa e
    /// ele não é a ponta de um objeto COM. Fora do Windows e desmarcado por padrão: é a categoria que pede mais cuidado.
    /// </summary>
    public static RegIssue? CheckProgId(IRegistryCheckSystem sys, string classesRoot, string name, ISet<string> referenced, string systemRoot)
    {
        if (name.StartsWith('.') || ClassStructure.Contains(name, StringComparer.OrdinalIgnoreCase) || referenced.Contains(name)) return null;
        string key = $@"{classesRoot}\{name}";
        string? exe = Launched(sys.StringValue($@"{key}\shell\open\command", null));
        if (exe is null || !MissingOnPresentDrive(sys, exe)) return null;
        if (exe.StartsWith(systemRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
        if (sys.KeyExists($@"{key}\CLSID")) return null;                                   // ProgID de objeto COM
        if (sys.ValueNames(key).Contains("URL Protocol", StringComparer.OrdinalIgnoreCase)) return null; // protocolo (mailto:, steam:…)
        return new RegIssue(RegIssueKind.ProgId, key, null, name, $"O programa que abre este tipo não existe: {exe}", false);
    }

    // ---------- quem usa cada ProgID ----------

    /// <summary>Todos os ProgIDs citados por alguma extensão, escolha do usuário ou aplicativo registrado.</summary>
    public static HashSet<string> ReferencedProgIds(IRegistryCheckSystem sys)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? id) { if (!string.IsNullOrWhiteSpace(id)) used.Add(id.Trim()); }

        foreach (var root in ClassRoots())
            foreach (var ext in sys.SubKeys(root).Where(e => e.StartsWith('.')))
            {
                Add(sys.StringValue($@"{root}\{ext}", null));
                foreach (var v in sys.ValueNames($@"{root}\{ext}\OpenWithProgids")) Add(v);
            }
        string fileExts = $@"{Hkcu}\{CurrentVersion}\Explorer\FileExts";
        foreach (var ext in sys.SubKeys(fileExts))
        {
            Add(sys.StringValue($@"{fileExts}\{ext}\UserChoice", "ProgId"));
            foreach (var v in sys.ValueNames($@"{fileExts}\{ext}\OpenWithProgids")) Add(v);
        }
        // Aplicativos registrados: cada um aponta para uma chave "Capabilities" que lista as associações
        foreach (var hive in new[] { Hklm, Hkcu })
        {
            string registered = $@"{hive}\Software\RegisteredApplications";
            foreach (var app in sys.ValueNames(registered))
            {
                string cap = (sys.StringValue(registered, app) ?? "").Trim().Trim('\\');
                if (cap.Length == 0 || cap.Contains("..")) continue;
                foreach (var section in new[] { "FileAssociations", "URLAssociations" })
                {
                    string assoc = $@"{hive}\{cap}\{section}";
                    foreach (var v in sys.ValueNames(assoc)) Add(sys.StringValue(assoc, v));
                }
            }
        }
        return used;
    }

    // ---------- varredura e conferência ----------

    public static List<RegIssue> Scan(IRegistryCheckSystem sys, string systemRoot, string fontsDir)
    {
        var issues = new List<RegIssue>();
        foreach (var root in UninstallRoots())
            foreach (var name in sys.SubKeys(root)) if (CheckUninstall(sys, $@"{root}\{name}") is { } u) issues.Add(u);
        foreach (var root in AppPathRoots())
            foreach (var name in sys.SubKeys(root)) if (CheckAppPath(sys, $@"{root}\{name}") is { } a) issues.Add(a);
        foreach (var root in RunRoots())
            foreach (var v in sys.ValueNames(root)) if (CheckRun(sys, root, v) is { } r) issues.Add(r);
        foreach (var root in SharedDllRoots())
            foreach (var v in sys.ValueNames(root)) if (CheckSharedDll(sys, root, v) is { } s) issues.Add(s);
        foreach (var root in FontRoots())
            foreach (var v in sys.ValueNames(root)) if (CheckFont(sys, root, v, fontsDir) is { } f) issues.Add(f);

        var referenced = ReferencedProgIds(sys);
        foreach (var root in ClassRoots())
            foreach (var name in sys.SubKeys(root)) if (CheckProgId(sys, root, name, referenced, systemRoot) is { } p) issues.Add(p);

        return issues.OrderBy(i => i.Kind).ThenBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A prova ainda vale agora? É o que o executor pergunta antes de apagar cada item.</summary>
    public static bool StillBroken(IRegistryCheckSystem sys, RegIssue issue, string systemRoot, string fontsDir)
    {
        if (!Allowed(issue)) return false;
        return issue.Kind switch
        {
            RegIssueKind.Uninstall => CheckUninstall(sys, issue.Key) is not null,
            RegIssueKind.AppPath => CheckAppPath(sys, issue.Key) is not null,
            RegIssueKind.Run => CheckRun(sys, issue.Key, issue.Value!) is not null,
            RegIssueKind.SharedDll => CheckSharedDll(sys, issue.Key, issue.Value!) is not null,
            RegIssueKind.Font => CheckFont(sys, issue.Key, issue.Value!, fontsDir) is not null,
            _ => CheckProgId(sys, ParentOf(issue.Key), issue.Key[(ParentOf(issue.Key).Length + 1)..], ReferencedProgIds(sys), systemRoot) is not null,
        };
    }
}
