using System.Globalization;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>
/// M13: idiomas do app. O português é o idioma de origem e o próprio texto em português é a chave: o que não tem
/// tradução aparece em português, nunca vazio. As traduções ficam em Idiomas/en.json e Idiomas/es.json.
/// </summary>
public static class Loc
{
    public const string Auto = "auto", Portuguese = "pt", English = "en", Spanish = "es";
    public static readonly string[] Languages = [Portuguese, English, Spanish];

    private static Dictionary<string, string> _map = new();

    /// <summary>Idioma em uso ("pt", "en" ou "es").</summary>
    public static string Language { get; private set; } = Portuguese;

    public static bool IsSource => Language == Portuguese;

    public static CultureInfo Culture => Language switch
    {
        English => new CultureInfo("en-US"),
        Spanish => new CultureInfo("es-ES"),
        _ => new CultureInfo("pt-BR"),
    };

    /// <summary>
    /// "auto" segue o idioma do Windows: português, espanhol ou inglês; qualquer outro idioma vira inglês, que é o
    /// mais provável de a pessoa entender.
    /// </summary>
    public static string Resolve(string? setting, string windowsCulture)
    {
        if (setting is Portuguese or English or Spanish) return setting;
        string w = windowsCulture.ToLowerInvariant();
        return w.StartsWith("pt") ? Portuguese : w.StartsWith("es") ? Spanish : English;
    }

    public static void Use(string language)
    {
        Language = Languages.Contains(language) ? language : Portuguese;
        _map = Language == Portuguese ? new() : Load(Language);
    }

    /// <summary>Texto no idioma em uso (ou o próprio texto, se não houver tradução).</summary>
    public static string T(string pt) => _map.TryGetValue(pt, out var t) ? t : pt;

    public static bool TryT(string pt, out string translated)
    {
        if (_map.TryGetValue(pt, out var t)) { translated = t; return true; }
        translated = pt;
        return false;
    }

    /// <summary>Texto com valores ("{0} processos"). A tradução mantém os mesmos {0}, {1}…</summary>
    /// <summary>Data e hora curtas para listas ("25/10 14:30"; em inglês "10/25 14:30"), sem o ano.</summary>
    public static string Stamp(DateTime t) => t.ToString(Language == "en" ? "MM/dd HH:mm" : "dd/MM HH:mm", CultureInfo.InvariantCulture);

    public static string F(string ptFormat, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(ptFormat), args);

    public static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Idiomas.{language}.json");
        if (stream is null) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }
}
