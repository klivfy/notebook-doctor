using System.Text.Json;

namespace NotebookDoctor.Services;

/// <param name="AlwaysOn">Tem página de fundo persistente (Manifest V2): fica carregada o tempo todo</param>
/// <param name="AllSites">Pode ler e mudar todas as páginas que você abre</param>
/// <param name="Bytes">Tamanho da pasta da extensão no disco (0 = desconhecido)</param>
public sealed record BrowserExtension(string Browser, string Profile, string Id, string Name, string Version,
                                      bool AlwaysOn, bool AllSites, long Bytes = 0)
{
    public string Weight => BrowserExtensionRules.Weight(this);
}

/// <summary>
/// M31: extensões instaladas no Chrome, Edge, Brave, Vivaldi e Firefox, lidas dos arquivos do perfil (sem abrir
/// o navegador). O navegador não informa memória por extensão para outros programas; o peso é estimado pelo que
/// a extensão pede: ficar sempre carregada e rodar em todos os sites são os dois maiores custos.
/// </summary>
public static class BrowserExtensionRules
{
    private static readonly string[] AllSitesPatterns = ["<all_urls>", "*://*/*", "http://*/*", "https://*/*", "*://*/"];

    public static string Weight(BrowserExtension e) => (e.AlwaysOn, e.AllSites) switch
    {
        (true, true) => "Pesada: sempre carregada e roda em todos os sites",
        (true, false) => "Média: sempre carregada",
        (false, true) => "Média: roda em todos os sites",
        _ => "Leve",
    };

    public static int Rank(BrowserExtension e) => (e.AlwaysOn ? 2 : 0) + (e.AllSites ? 1 : 0);

    /// <summary>
    /// Lê o manifest.json de uma extensão do Chromium. Nomes como "__MSG_appName__" são traduzidos por
    /// <paramref name="message"/> (busca em _locales). Devolve null para manifesto ilegível ou para tema/app.
    /// </summary>
    public static BrowserExtension? FromChromiumManifest(string json, string browser, string profile, string id, Func<string, string?> message, long bytes = 0)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }).RootElement; }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("theme", out _)) return null; // tema não roda código

        string name = Str(root, "name") ?? id;
        if (name.StartsWith("__MSG_", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal))
            name = message(name[6..^2]) ?? id;
        string version = Str(root, "version") ?? "";

        bool alwaysOn = false;
        if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object)
        {
            // MV3 (service_worker) dorme sozinho; MV2 com página ou scripts fica carregado, a menos que "persistent": false
            bool mv2 = bg.TryGetProperty("page", out _) || bg.TryGetProperty("scripts", out _);
            bool persistent = !(bg.TryGetProperty("persistent", out var p) && p.ValueKind == JsonValueKind.False);
            alwaysOn = mv2 && persistent;
        }

        var patterns = Strings(root, "permissions").Concat(Strings(root, "host_permissions")).ToList();
        if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var s in cs.EnumerateArray()) patterns.AddRange(Strings(s, "matches"));
        bool allSites = patterns.Any(p => AllSitesPatterns.Contains(p.Trim()));

        return new BrowserExtension(browser, profile, id, name.Trim(), version, alwaysOn, allSites, bytes);
    }

    /// <summary>Texto de uma mensagem em _locales/xx/messages.json ({"appName": {"message": "…"}}), sem diferenciar maiúsculas.</summary>
    public static string? Message(string messagesJson, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(messagesJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                    return Str(p.Value, "message");
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Ordem das pastas _locales a tentar: a do Windows, pt-BR, pt, inglês e a padrão da extensão.</summary>
    public static IEnumerable<string> LocaleOrder(string? defaultLocale) =>
        new[] { "pt_BR", "pt", "pt_PT", "en", "en_US" }.Append(defaultLocale ?? "").Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    public static string? DefaultLocale(string manifestJson)
    {
        try { return Str(JsonDocument.Parse(manifestJson).RootElement, "default_locale"); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Extensões ativas do Firefox (extensions.json do perfil). As que vêm com o próprio Firefox ficam de fora.
    /// O arquivo não diz o que cada uma pede, então o peso fica como desconhecido (leve).
    /// </summary>
    public static List<BrowserExtension> FromFirefox(string json, string profile)
    {
        var list = new List<BrowserExtension>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array) return list;
            foreach (var a in addons.EnumerateArray())
            {
                if (Str(a, "type") != "extension" || Str(a, "location") != "app-profile") continue;
                if (a.TryGetProperty("active", out var act) && act.ValueKind == JsonValueKind.False) continue;
                string id = Str(a, "id") ?? "";
                string name = a.TryGetProperty("defaultLocale", out var dl) && dl.ValueKind == JsonValueKind.Object ? Str(dl, "name") ?? id : id;
                list.Add(new BrowserExtension("Firefox", profile, id, name, Str(a, "version") ?? "", false, false));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>Mais pesadas primeiro; dentro do mesmo peso, por navegador e nome.</summary>
    public static List<BrowserExtension> Sort(IEnumerable<BrowserExtension> all) =>
        all.OrderByDescending(Rank).ThenBy(e => e.Browser).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IEnumerable<string> Strings(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            : [];
}
