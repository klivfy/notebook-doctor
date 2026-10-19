using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

public sealed record AlertEntry(DateTime Time, string Title, string Text, bool Silenced = false)
{
    [System.Text.Json.Serialization.JsonIgnore] public string TimeText => Time.ToString("dd/MM HH:mm");
}

/// <summary>M46: cada aviso disparado (programa pesado, CPU/RAM, disco, calor), guardado por 30 dias em JSON Lines.</summary>
public static class AlertLog
{
    private static readonly object Lock = new();
    private static string FilePath => Path.Combine(AppPaths.Dir, "alertas.jsonl");

    public static void Add(string title, string text, bool silenced = false)
    {
        lock (Lock)
        {
            try { File.AppendAllText(FilePath, JsonSerializer.Serialize(new AlertEntry(DateTime.Now, title, text, silenced)) + "\n"); } catch { }
        }
    }

    public static List<AlertEntry> Load(TimeSpan window)
    {
        var from = DateTime.Now - window;
        var list = new List<AlertEntry>();
        lock (Lock)
        {
            if (!File.Exists(FilePath)) return list;
            try
            {
                foreach (var line in File.ReadLines(FilePath))
                {
                    try { if (JsonSerializer.Deserialize<AlertEntry>(line) is { } a && a.Time >= from) list.Add(a); } catch { }
                }
                // Mantém o arquivo pequeno: reescreve só quando passa de 256 KB
                if (new FileInfo(FilePath).Length > 256 * 1024)
                    File.WriteAllLines(FilePath, Load30().Select(a => JsonSerializer.Serialize(a)));
            }
            catch { }
        }
        return list.OrderByDescending(a => a.Time).ToList();
    }

    private static IEnumerable<AlertEntry> Load30()
    {
        var from = DateTime.Now.AddDays(-30);
        foreach (var line in File.ReadAllLines(FilePath))
        {
            AlertEntry? a = null;
            try { a = JsonSerializer.Deserialize<AlertEntry>(line); } catch { }
            if (a is not null && a.Time >= from) yield return a;
        }
    }
}
