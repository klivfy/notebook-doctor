using System.Globalization;
using System.Text.Json;

namespace NotebookDoctor.Services;

public sealed record RestorePointInfo(int Sequence, string Description, DateTime Created);

/// <summary>
/// M37: pontos de restauração antigos. Cada ponto guarda cópias de arquivos do sistema e pode ocupar gigabytes.
/// O app só apaga os antigos e sempre mantém o mais recente, que é o que serve para voltar atrás.
/// </summary>
public static class RestorePointRules
{
    /// <summary>Lê a saída de Get-ComputerRestorePoint | ConvertTo-Json (um objeto sozinho ou uma lista).</summary>
    public static List<RestorePointInfo> Parse(string json)
    {
        var list = new List<RestorePointInfo>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var e in items)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("SequenceNumber", out var seq) || !seq.TryGetInt32(out int n)) continue;
                string desc = e.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                DateTime created = e.TryGetProperty("CreationTime", out var c) && c.ValueKind == JsonValueKind.String && ParseCim(c.GetString()!) is { } t ? t : DateTime.MinValue;
                list.Add(new RestorePointInfo(n, desc, created));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>Saída do script do app: {"points":[…],"used":12345}. "used" é o espaço das cópias de sombra (pode faltar).</summary>
    public static (List<RestorePointInfo> Points, long UsedBytes) ParseReport(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (new(), 0);
            var points = root.TryGetProperty("points", out var p) ? Parse(p.GetRawText()) : new();
            long used = root.TryGetProperty("used", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetInt64(out long b) ? b : 0;
            return (points, used);
        }
        catch (JsonException) { return (new(), 0); }
    }

    /// <summary>Data no formato do WMI ("20261020153012.123456-180"): os 14 primeiros dígitos são a hora local.</summary>
    public static DateTime? ParseCim(string text) =>
        text.Length >= 14 && DateTime.TryParseExact(text[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Todos menos o mais recente (pela data; empate ou data ilegível, pelo número de sequência).</summary>
    public static List<RestorePointInfo> ToDelete(IEnumerable<RestorePointInfo> points)
    {
        var ordered = points.OrderByDescending(p => p.Created).ThenByDescending(p => p.Sequence).ToList();
        return ordered.Skip(1).OrderBy(p => p.Created).ToList();
    }

    public static string Describe(IReadOnlyList<RestorePointInfo> points, long usedBytes)
    {
        string used = usedBytes > 0 ? " " + Loc.F("Ocupam {0} no disco.", Pt.Bytes(usedBytes)) : "";
        if (points.Count == 0) return Loc.T("Nenhum ponto de restauração neste computador.") + used;
        var newest = points.MaxBy(p => p.Created)!;
        string when = newest.Created == DateTime.MinValue ? "" : " " + Loc.F("O mais recente é de {0:dd/MM/yyyy HH:mm}", newest.Created);
        string what = newest.Description.Length > 0 ? $" ({newest.Description})." : when.Length > 0 ? "." : "";
        return $"{Pt.N(points.Count, "ponto de restauração", "pontos de restauração")}.{when}{what}{used}";
    }
}

public sealed record EventLogFile(string Name, long Bytes)
{
    public string SizeText => Pt.Bytes(Bytes);
}

/// <summary>
/// M36: logs de eventos grandes. O Windows limita cada log (20 MB no padrão), mas programas e políticas às vezes
/// aumentam esse limite. Limpar apaga o histórico de eventos daquele log para sempre. O log de Segurança nunca
/// entra: é o registro de auditoria do computador, e apagá-lo é justamente o que um invasor faria.
/// </summary>
public static class EventLogRules
{
    public const long MinBytes = 32L << 20;
    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase) { "Security" };

    /// <summary>"Microsoft-Windows-TaskScheduler%4Operational.evtx" → "Microsoft-Windows-TaskScheduler/Operational".</summary>
    public static string NameFromFile(string fileName)
    {
        string name = fileName.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) ? fileName[..^5] : fileName;
        return name.Replace("%4", "/");
    }

    public static bool CanClear(string logName) => !Never.Contains(logName);

    public static List<EventLogFile> Candidates(IEnumerable<(string FileName, long Bytes)> files, long minBytes = MinBytes) =>
        files.Where(f => f.FileName.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) && f.Bytes >= minBytes)
             .Select(f => new EventLogFile(NameFromFile(f.FileName), f.Bytes))
             .Where(l => CanClear(l.Name))
             .OrderByDescending(l => l.Bytes)
             .ToList();
}
