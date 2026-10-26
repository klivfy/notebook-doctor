namespace NotebookDoctor.Services;

public enum MenuItemKind { Handler, Verb }

/// <summary>
/// Um item do menu de contexto do Explorador. <see cref="Key"/> é a chave inteira do item (com o nome do ramo);
/// <see cref="File"/> é a DLL do manipulador ou o programa do verbo, já com as variáveis expandidas.
/// </summary>
public sealed record ContextMenuItem(
    MenuItemKind Kind, string Hive, string Location, string Name, string Display, string Clsid, string File, bool FileExists, string Command, bool Hidden)
{
    public bool Machine => Hive == "HKEY_LOCAL_MACHINE";
    public string Key => ContextMenuRules.ItemKey(Hive, Location, Kind, Name);
}

/// <summary>
/// Leva 7.3 (C06): lista e protege o menu de contexto. Fontes: manipuladores (<c>shellex\ContextMenuHandlers</c>) e verbos
/// estáticos (<c>shell</c>) em HKLM e HKCU, nos lugares onde o Explorador mostra o menu. Itens do menu novo do
/// Windows 11 vindos de apps empacotados (MSIX) não ficam no registro e não aparecem.
/// </summary>
public static class ContextMenuRules
{
    public const string HideValue = "ProgrammaticAccessOnly";

    public static readonly string[] Locations = ["*", "AllFilesystemObjects", "Directory", @"Directory\Background", "Drive", "Folder"];
    public static readonly string[] Hives = ["HKEY_LOCAL_MACHINE", "HKEY_CURRENT_USER"];

    // Verbos que o Windows usa para abrir e executar: esconder do menu não quebra o duplo clique, mas ficam de fora para não confundir
    private static readonly string[] CoreVerbs = ["open", "explore", "opennew", "opennewprocess", "opennewwindow", "runas", "runasuser", "edit", "print", "printto", "play", "find", "properties", "cmd"];

    public static string LocationLabel(string location) => location switch
    {
        "*" => "Arquivos",
        "AllFilesystemObjects" => "Arquivos e pastas",
        "Directory" => "Pastas",
        @"Directory\Background" => "Fundo de uma pasta",
        "Drive" => "Unidades",
        "Folder" => "Pastas (formato antigo)",
        _ => location,
    };

    /// <summary>Só chaves dos lugares conhecidos e com nome simples (sem caminho, sem ".."): é o que o editor aceita tocar.</summary>
    public static bool Valid(string hive, string location, string name) =>
        Hives.Contains(hive) && Locations.Contains(location) && name.Length is > 0 and < 200
        && name.IndexOfAny(['\\', '/', '\0', '\n', '\r']) < 0 && name != "." && name != "..";

    public static string ItemKey(string hive, string location, MenuItemKind kind, string name) =>
        $@"{hive}\Software\Classes\{location}\{(kind == MenuItemKind.Handler ? @"shellex\ContextMenuHandlers" : "shell")}\{name}";

    /// <summary>Nome legível do verbo: tira o "&amp;" do atalho; textos de recurso ("@dll,-123") não dão para ler aqui, então vale o nome da chave.</summary>
    public static string VerbLabel(string? raw, string fallback)
    {
        string text = (raw ?? "").Replace("&", "").Trim();
        return text.Length == 0 || text.StartsWith('@') ? fallback : text;
    }

    private static string Expand(string path, string systemRoot) =>
        path.Replace("%SystemRoot%", systemRoot, StringComparison.OrdinalIgnoreCase).Replace("%windir%", systemRoot, StringComparison.OrdinalIgnoreCase);

    private static bool UnderWindows(string path, string systemRoot) =>
        path.Length > 0 && path.StartsWith(systemRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O que não pode ser desativado por aqui, com o motivo. Manipulador cuja DLL fica na pasta do Windows é do sistema
    /// (Abrir com, Compartilhar, Versões anteriores…). Manipulador cuja DLL não existe mais é lixo e pode sair.
    /// </summary>
    public static (bool Protected, string Reason) Protection(ContextMenuItem item, string systemRoot)
    {
        if (item.Kind == MenuItemKind.Handler)
            return UnderWindows(item.File, systemRoot) ? (true, "Item do Windows") : (false, "");
        if (CoreVerbs.Contains(item.Name, StringComparer.OrdinalIgnoreCase)) return (true, "Verbo básico do Windows");
        return UnderWindows(item.File, systemRoot) ? (true, "Programa do Windows") : (false, "");
    }

    /// <summary>Lê todos os itens. Duplicatas (mesmo nome no mesmo lugar em HKLM e HKCU) ficam as duas: cada uma tem a sua chave.</summary>
    public static List<ContextMenuItem> Scan(ILeftoverSystem sys, string systemRoot)
    {
        var items = new List<ContextMenuItem>();
        foreach (var hive in Hives)
            foreach (var location in Locations)
            {
                string handlers = $@"{hive}\Software\Classes\{location}\shellex\ContextMenuHandlers";
                foreach (var name in sys.SubKeys(handlers).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    items.Add(ReadHandler(sys, hive, location, name, $@"{handlers}\{name}", systemRoot));

                string verbs = $@"{hive}\Software\Classes\{location}\shell";
                foreach (var name in sys.SubKeys(verbs).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    items.Add(ReadVerb(sys, hive, location, name, $@"{verbs}\{name}", systemRoot));
            }
        return items;
    }

    private static ContextMenuItem ReadHandler(ILeftoverSystem sys, string hive, string location, string name, string key, string systemRoot)
    {
        // O nome da chave costuma ser o do manipulador; o CLSID está no valor padrão, ou é o próprio nome quando ele tem a forma de um GUID
        string raw = (sys.StringValue(key, null) ?? "").Trim();
        string clsid = IsClsid(raw) ? raw : IsClsid(name) ? name : "";
        string display = name, dll = "";
        if (clsid.Length > 0)
        {
            foreach (var root in new[] { @"HKEY_LOCAL_MACHINE\Software\Classes\CLSID", @"HKEY_CURRENT_USER\Software\Classes\CLSID", @"HKEY_LOCAL_MACHINE\Software\Classes\WOW6432Node\CLSID" })
            {
                string clsKey = $@"{root}\{clsid}";
                if (!sys.KeyExists(clsKey)) continue;
                if (dll.Length == 0) dll = Expand((sys.StringValue($@"{clsKey}\InprocServer32", null) ?? "").Trim().Trim('"'), systemRoot);
                string title = (sys.StringValue(clsKey, null) ?? "").Trim();
                if (title.Length > 0 && !title.StartsWith('@') && display == name) display = $"{name} ({title})";
            }
        }
        return new ContextMenuItem(MenuItemKind.Handler, hive, location, name, display, clsid, dll, dll.Length > 0 && sys.FileExists(dll), "", false);
    }

    private static ContextMenuItem ReadVerb(ILeftoverSystem sys, string hive, string location, string name, string key, string systemRoot)
    {
        string label = VerbLabel(sys.StringValue(key, "MUIVerb") ?? sys.StringValue(key, null), name);
        string command = (sys.StringValue($@"{key}\command", null) ?? "").Trim();
        string exe = Expand(CommandLine.ExecutablePath(command) ?? "", systemRoot);
        bool hidden = sys.ValueNames(key).Contains(HideValue, StringComparer.OrdinalIgnoreCase);
        return new ContextMenuItem(MenuItemKind.Verb, hive, location, name, label == name ? name : $"{label} ({name})", "", exe, exe.Length > 0 && sys.FileExists(exe), command, hidden);
    }

    public static bool IsClsid(string text) =>
        text.Length == 38 && text[0] == '{' && text[^1] == '}' && Guid.TryParse(text, out _);

    /// <summary>Os mais recomendáveis a olhar primeiro: arquivo que não existe mais, depois terceiros, depois o Windows.</summary>
    public static List<ContextMenuItem> Order(IEnumerable<ContextMenuItem> items, string systemRoot) =>
        items.OrderBy(i => Protection(i, systemRoot).Protected ? 2 : !i.FileExists && i.File.Length > 0 ? 0 : 1)
             .ThenBy(i => Array.IndexOf(Locations, i.Location))
             .ThenBy(i => i.Display, StringComparer.CurrentCultureIgnoreCase)
             .ToList();
}

/// <summary>Um manipulador desativado por este app: a chave foi guardada em backup e apagada.</summary>
public sealed class DisabledHandler
{
    public string SessionId { get; set; } = "";
    public string Hive { get; set; } = "";
    public string Location { get; set; } = "";
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
    public string File { get; set; } = "";
    public DateTime Disabled { get; set; }
}
