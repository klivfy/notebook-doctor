using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>Leva 5.4: instalações monitoradas, uma por arquivo JSON na pasta "traces" dos dados do app.</summary>
public static class TraceStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Dir => Path.Combine(AppPaths.Dir, "traces");

    public static void Save(InstallTrace trace)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, trace.Id + ".json"), JsonSerializer.Serialize(trace, Json));
    }

    public static List<InstallTrace> List()
    {
        if (!Directory.Exists(Dir)) return [];
        var list = new List<InstallTrace>();
        foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            if (Read(f) is { } t) list.Add(t);
        return list.OrderByDescending(t => t.Started).ToList();
    }

    private static InstallTrace? Read(string file)
    {
        try { return JsonSerializer.Deserialize<InstallTrace>(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>O trace mais recente do programa com esta chave de desinstalação.</summary>
    public static InstallTrace? ForKey(string uninstallKey) =>
        uninstallKey.Length == 0 ? null : List().FirstOrDefault(t => Paths.Same(t.UninstallKey, uninstallKey));

    public static void Delete(string id)
    {
        if (id.Length == 0 || Path.GetFileName(id) != id) return; // o id vem de arquivo: nunca um caminho
        string file = Path.Combine(Dir, id + ".json");
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>Pela extensão: JSON (para importar em outro PC), TXT ou HTML (relatório).</summary>
    public static void Export(InstallTrace trace, string file)
    {
        string text = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".txt" => TraceRules.Report(trace),
            ".html" or ".htm" => TraceRules.Html(trace),
            _ => JsonSerializer.Serialize(trace, Json),
        };
        File.WriteAllText(file, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Importa um trace de outro computador. Ele fica marcado: as sobras dele entram como Moderado, não Seguro.</summary>
    public static InstallTrace? Import(string file)
    {
        if (Read(file) is not { Name.Length: > 0 } t) return null;
        t.Imported = true;
        t.Id = BackupRules.NewId(DateTime.Now, t.Name);
        Save(t);
        return t;
    }
}
