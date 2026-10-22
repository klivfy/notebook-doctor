using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <param name="Result">Código do Windows Update: 1 em andamento, 2 sucesso, 3 sucesso com erros, 4 falhou, 5 cancelada</param>
public sealed record UpdateEntry(string Title, DateTime Date, int Result)
{
    public bool Succeeded => Result is 2 or 3;
    public bool Failed => Result is 4 or 5;
}

/// <param name="History">Instalações do histórico do Windows Update, em qualquer ordem</param>
/// <param name="PausedUntil">Atualizações pausadas até esta data (null = não pausadas)</param>
public sealed record UpdateStatus(IReadOnlyList<UpdateEntry> History, bool ServiceDisabled, DateTime? PausedUntil);

public sealed record GpuDriver(string Name, string Version, DateTime? Date);

/// <param name="DriveOptimization">Tarefa semanal "Otimizar unidades" (TRIM no SSD) ligada; null = não deu para ler</param>
/// <param name="AutoMaintenanceOff">Manutenção automática do Windows desligada pelo registro</param>
public sealed record WindowsMaintenance(bool? DriveOptimization, bool AutoMaintenanceOff);

/// <summary>
/// A22: atualizações do Windows travadas. Três sinais: o serviço desativado, a mesma atualização falhando
/// seguidas vezes e muito tempo sem nenhuma instalação. Pausa feita pela pessoa não conta como problema.
/// </summary>
public static partial class UpdateRules
{
    public const int StaleDays = 40;
    public const int FailuresToWarn = 2;

    [GeneratedRegex(@"KB\d{6,8}", RegexOptions.IgnoreCase)]
    private static partial Regex Kb();

    /// <summary>A mesma atualização aparece com títulos um pouco diferentes a cada mês; o número KB identifica.</summary>
    public static string Key(string title) => Kb().Match(title) is { Success: true } m ? m.Value.ToUpperInvariant() : title.Trim();

    /// <summary>Atualizações cuja última tentativa falhou e que falharam pelo menos 2 vezes desde o último sucesso.</summary>
    public static List<(string Title, int Failures, DateTime Last)> Stuck(IEnumerable<UpdateEntry> history)
    {
        var stuck = new List<(string, int, DateTime)>();
        foreach (var g in history.Where(e => e.Result != 1).GroupBy(e => Key(e.Title)))
        {
            var ordered = g.OrderBy(e => e.Date).ToList();
            if (!ordered[^1].Failed) continue;
            int lastOk = ordered.FindLastIndex(e => e.Succeeded);
            int failures = ordered.Skip(lastOk + 1).Count(e => e.Failed);
            if (failures >= FailuresToWarn) stuck.Add((ordered[^1].Title, failures, ordered[^1].Date));
        }
        return stuck.OrderByDescending(s => s.Item3).ToList();
    }

    public static IEnumerable<Finding> Evaluate(UpdateStatus? u, DateTime now)
    {
        if (u is null) yield break;
        if (u.ServiceDisabled)
        {
            yield return new Finding("ATENCAO", "Windows Update desativado", "O serviço de atualização está desligado",
                "Sem ele, o Windows não recebe correções de segurança. Reative em Serviços (Windows Update › Tipo de inicialização: Manual) ou com o programa que o desligou.",
                "open-update", "Abrir Windows Update");
            yield break; // com o serviço parado, falhas e atraso são consequência
        }

        var stuck = Stuck(u.History);
        if (stuck.Count > 0)
        {
            var s = stuck[0];
            string more = stuck.Count > 1 ? $" (e mais {stuck.Count - 1})" : "";
            yield return new Finding("ATENCAO", "Atualização do Windows falhando",
                $"{s.Title}{more}: {Pt.N(s.Failures, "tentativa", "tentativas")} sem sucesso, a última em {s.Last:dd/MM}",
                "Libere espaço no disco, reinicie e tente de novo em Windows Update. Se continuar, use Configurações › Sistema › Solução de problemas › Windows Update.",
                "open-update", "Abrir Windows Update");
        }

        bool paused = u.PausedUntil is { } until && until > now;
        var lastOk = u.History.Where(e => e.Succeeded).Select(e => (DateTime?)e.Date).Max();
        if (!paused && lastOk is { } ok && (now - ok).TotalDays >= StaleDays)
            yield return new Finding("ATENCAO", $"Windows sem atualizar há {(int)(now - ok).TotalDays} dias", $"Última atualização instalada em {ok:dd/MM/yyyy}",
                "O Windows costuma receber correções todo mês. Abra o Windows Update e clique em \"Verificar se há atualizações\".",
                "open-update", "Abrir Windows Update");
    }

    /// <summary>Lê o bloco "wu" do script de saúde: [{"t":título,"d":"2026-10-01T08:30:00","r":2}, …].</summary>
    public static List<UpdateEntry> ParseHistory(JsonElement e)
    {
        var list = new List<UpdateEntry>();
        IEnumerable<JsonElement> items = e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];
        foreach (var x in items)
        {
            if (x.ValueKind != JsonValueKind.Object) continue;
            string title = x.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            int result = x.TryGetProperty("r", out var r) && r.TryGetInt32(out int rv) ? rv : 0;
            if (title.Length == 0 || !x.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            list.Add(new UpdateEntry(title, date, result));
        }
        return list;
    }

    public static DateTime? ParsePause(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime() : null;
}

/// <summary>
/// A23: driver de vídeo. É o driver que mais pesa no desempenho do dia a dia (janelas, vídeo, navegador) e o que
/// mais muda. O "Adaptador de Vídeo Básico" do Windows quer dizer que o driver do fabricante nem está instalado.
/// </summary>
public static class DriverRules
{
    public const int OldDays = 730;
    private static readonly string[] Virtual = ["virtual", "remote", "hyper-v", "parsec", "citrix", "vmware", "virtualbox", "idd ", "indirect display"];

    public static bool IsBasic(string name) =>
        name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Vídeo Básico", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<Finding> Evaluate(IReadOnlyList<GpuDriver>? gpus, DateTime now)
    {
        foreach (var g in (gpus ?? []).Where(g => !Virtual.Any(v => g.Name.Contains(v, StringComparison.OrdinalIgnoreCase))).Take(2))
        {
            if (IsBasic(g.Name))
                yield return new Finding("ATENCAO", "Vídeo sem o driver do fabricante", $"{g.Name} (driver genérico do Windows)",
                    "Instale o driver da placa de vídeo pelo Windows Update › Opções avançadas › Atualizações opcionais, ou pelo site do fabricante do notebook.",
                    "open-optional-updates", "Abrir atualizações opcionais");
            else if (g.Date is { } d && (now - d).TotalDays >= OldDays)
                yield return new Finding("ATENCAO", "Driver de vídeo antigo", $"{g.Name}: versão {g.Version}, de {d:MM/yyyy}",
                    "Atualize pelo app ou site do fabricante (Intel, AMD ou NVIDIA) ou pelo Windows Update › Atualizações opcionais.",
                    "open-optional-updates", "Abrir atualizações opcionais");
        }
    }

    /// <summary>Lê o bloco "gpu" do script de saúde: [{"n":nome,"v":versão,"d":"2024-05-10"}, …].</summary>
    public static List<GpuDriver> Parse(JsonElement e)
    {
        var list = new List<GpuDriver>();
        IEnumerable<JsonElement> items = e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];
        foreach (var x in items)
        {
            if (x.ValueKind != JsonValueKind.Object) continue;
            string S(string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            if (S("n").Length == 0) continue;
            DateTime? date = DateTime.TryParseExact(S("d"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
            list.Add(new GpuDriver(S("n"), S("v"), date));
        }
        return list;
    }
}

/// <summary>
/// A39: manutenção em horário ocioso. O Windows já tem a dele (otimização semanal das unidades com TRIM no SSD
/// e a manutenção automática). O app avisa quando alguma está desligada e roda a própria limpeza semanal só
/// com o computador parado e na tomada.
/// </summary>
public static class MaintenanceRules
{
    public static readonly TimeSpan IdleNeeded = TimeSpan.FromMinutes(10);

    /// <summary>Rotina pesada do app: só com 10 min sem uso, fora do Modo Foco e fora da bateria.</summary>
    public static bool CanRunIdleTask(TimeSpan idle, bool focusActive, bool onBattery) =>
        idle >= IdleNeeded && !focusActive && !onBattery;

    public static IEnumerable<Finding> Evaluate(WindowsMaintenance? m, string diskType)
    {
        if (m is null) yield break;
        if (m.DriveOptimization == false)
        {
            bool ssd = diskType.Contains("SSD", StringComparison.OrdinalIgnoreCase);
            yield return new Finding("ATENCAO", "Otimização automática das unidades desligada",
                ssd ? "O SSD não recebe o TRIM semanal" : "As unidades não são otimizadas toda semana",
                "Religue em Otimizar Unidades › Alterar configurações › \"Executar de acordo com um agendamento\".",
                "open-defrag", "Abrir Otimizar Unidades");
        }
        if (m.AutoMaintenanceOff)
            yield return new Finding("ATENCAO", "Manutenção automática do Windows desativada", "Desligada pelo registro (MaintenanceDisabled)",
                "Alguém ou algum programa desligou a manutenção que o Windows faz com o PC parado. Para religar, um administrador apaga o valor MaintenanceDisabled em HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\Maintenance.",
                "open-maintenance", "Abrir Segurança e Manutenção");
    }
}
