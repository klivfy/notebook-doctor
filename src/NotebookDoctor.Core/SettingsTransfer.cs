using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotebookDoctor.Services;

/// <summary>
/// M07: exporta e importa as preferências em JSON. Ficam de fora os dados desta máquina (posição do widget,
/// datas da última execução, aba aberta), para que importar num PC novo não traga estado velho.
/// </summary>
public static class SettingsTransfer
{
    public const string Marker = "NotebookDoctorSettings";

    private static readonly HashSet<string> MachineOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "MiniLeft", "MiniTop", "LastTab", "FirstRunDone", "LastWeeklySummary", "LastRestorePoint", "LastWeeklyRun", "LastUpdateCheck", "LastCrashSeen",
        "WinLeft", "WinTop", "WinWidth", "WinHeight", "WinMaximized",
    };

    public static string Export(string settingsJson, string appVersion)
    {
        var obj = JsonNode.Parse(settingsJson) as JsonObject ?? throw new FormatException("Configurações atuais ilegíveis.");
        var result = new JsonObject { ["_tipo"] = Marker, ["_versao"] = appVersion };
        foreach (var (key, value) in obj)
            if (!MachineOnly.Contains(key)) result[key] = value?.DeepClone();
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>B42: volta às preferências padrão, mas mantém os dados desta máquina (datas, posição do widget, boas-vindas já vistas).</summary>
    public static string Reset(string currentJson, string defaultsJson)
    {
        var current = JsonNode.Parse(currentJson) as JsonObject ?? new JsonObject();
        var result = JsonNode.Parse(defaultsJson) as JsonObject ?? throw new FormatException("Padrões ilegíveis.");
        foreach (var (key, value) in current)
            if (MachineOnly.Contains(key)) result[key] = value?.DeepClone();
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Aplica as preferências importadas por cima das atuais. Lança FormatException se o arquivo não for de configurações do app.</summary>
    public static string Merge(string currentJson, string importedJson)
    {
        JsonObject imported;
        try { imported = JsonNode.Parse(importedJson) as JsonObject ?? throw new FormatException(); }
        catch (Exception ex) when (ex is JsonException or FormatException) { throw new FormatException("O arquivo não é um JSON válido."); }
        if (imported["_tipo"]?.GetValue<string>() != Marker)
            throw new FormatException("O arquivo não é uma exportação de configurações do Notebook Doctor.");

        var current = JsonNode.Parse(currentJson) as JsonObject ?? new JsonObject();
        foreach (var (key, value) in imported)
        {
            if (key.StartsWith('_') || MachineOnly.Contains(key)) continue;
            current[key] = value?.DeepClone();
        }
        return current.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
