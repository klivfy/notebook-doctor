namespace NotebookDoctor.Services;

/// <summary>
/// Leva 11.3, tarefa 15: um programa que o próprio Windows mediu atrasando o boot (log Diagnostics-Performance, evento 101):
/// nome do arquivo, nome amigável e quantos milissegundos a mais ele custou.
/// </summary>
public sealed record BootDelay(string File, string Friendly, int DegradationMs);

public static class BootDelays
{
    /// <summary>O maior atraso medido de cada arquivo (o log guarda vários boots).</summary>
    public static Dictionary<string, BootDelay> ByFile(IEnumerable<BootDelay> events) =>
        events.Where(e => e.File.Length > 0 && e.DegradationMs > 0)
            .GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.MaxBy(e => e.DegradationMs)!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Atraso medido para o item de inicialização que abre <paramref name="exePath"/>; null quando o Windows não o mediu.</summary>
    public static BootDelay? For(string? exePath, IReadOnlyDictionary<string, BootDelay> byFile)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        string file = exePath[(exePath.LastIndexOfAny(['\\', '/']) + 1)..];
        return byFile.TryGetValue(file, out var d) ? d : null;
    }

    /// <summary>Lê a lista "slow" do script de saúde: [{"f":"OneDrive.exe","n":"Microsoft OneDrive","ms":5400}, ...].</summary>
    public static List<BootDelay> Parse(System.Text.Json.JsonElement e)
    {
        var list = new List<BootDelay>();
        IEnumerable<System.Text.Json.JsonElement> items = e.ValueKind == System.Text.Json.JsonValueKind.Array ? e.EnumerateArray()
            : e.ValueKind == System.Text.Json.JsonValueKind.Object ? [e] : [];
        foreach (var i in items)
        {
            string S(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";
            int ms = i.TryGetProperty("ms", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.Number && m.TryGetInt32(out int x) ? x : 0;
            if (S("f") is { Length: > 0 } f && ms > 0) list.Add(new BootDelay(f, S("n"), ms));
        }
        return list;
    }

    public static string Text(BootDelay d) => d.DegradationMs >= 1000 ? Loc.F("+{0:0.0} s no boot", d.DegradationMs / 1000.0) : Loc.F("+{0} ms no boot", d.DegradationMs);
}
