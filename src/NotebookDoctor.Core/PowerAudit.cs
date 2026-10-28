using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Leva 11.2: quanto um programa gastou da bateria num período.</summary>
public sealed record BatteryUser(string Name, double SharePct, int Minutes);

/// <summary>Leva 11.2: quem está impedindo o computador de dormir ou de desligar a tela.</summary>
public sealed record PowerRequest(string Kind, string Category, string Name);

/// <summary>
/// Fase 11, leva 11.2: quem gasta bateria, quem não deixa o notebook dormir, a tela em 60 Hz na bateria e o modo
/// eficiência automático para programas fora de foco.
/// </summary>
public static class PowerAudit
{
    /// <summary>
    /// "Vampiros de bateria": nas amostras do histórico gravadas na bateria, a parte do processador que cada programa usou.
    /// A CPU é o que mais pesa no consumo de um notebook, então a fatia de CPU é uma boa estimativa da fatia da bateria;
    /// tela e rádios não entram (não são de nenhum programa).
    /// </summary>
    public static List<BatteryUser> BatteryUsers(IEnumerable<HistSample> samples, DateTime from, int top = 8)
    {
        var onBattery = samples.Where(s => s.B == true && s.T >= from && s.P is not null).ToList();
        if (onBattery.Count == 0) return [];
        var cpu = new Dictionary<string, (double Cpu, int Minutes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in onBattery)
            foreach (var (name, v) in s.P!)
            {
                if (v.Length == 0 || v[0] <= 0 || name.Equals("Idle", StringComparison.OrdinalIgnoreCase)) continue;
                var cur = cpu.GetValueOrDefault(name);
                cpu[name] = (cur.Cpu + v[0], cur.Minutes + 1);
            }
        double total = cpu.Values.Sum(v => v.Cpu);
        if (total <= 0) return [];
        return cpu.Select(kv => new BatteryUser(kv.Key, Math.Round(kv.Value.Cpu * 100 / total, 1), kv.Value.Minutes))
            .OrderByDescending(u => u.SharePct).Take(top).ToList();
    }

    private static readonly Regex Header = new(@"^([A-Z]+):\s*$");
    private static readonly Regex Entry = new(@"^\[(\w+)\]\s*(.+)$");

    /// <summary>
    /// Lê a saída de "powercfg /requests": seções como DISPLAY:, SYSTEM:, EXECUTION: e, dentro delas, linhas "[PROCESS]
    /// \Device\...\app.exe", "[DRIVER] ..." ou "[SERVICE] ...". As seções vazias dizem "None." no idioma do Windows e ficam de fora.
    /// </summary>
    public static List<PowerRequest> ParseRequests(string output)
    {
        var list = new List<PowerRequest>();
        string section = "";
        foreach (var raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (Header.Match(line) is { Success: true } h) { section = h.Groups[1].Value; continue; }
            if (section.Length == 0 || Entry.Match(line) is not { Success: true } e) continue;
            string name = e.Groups[2].Value.Trim();
            // "\Device\HarddiskVolume3\Program Files\App\app.exe" vira "app.exe"; drivers e serviços ficam como vieram
            if (e.Groups[1].Value.Equals("PROCESS", StringComparison.OrdinalIgnoreCase)) name = name[(name.LastIndexOf('\\') + 1)..];
            list.Add(new PowerRequest(section, e.Groups[1].Value.ToUpperInvariant(), name));
        }
        return list;
    }

    /// <summary>"powercfg /devicequery wake_armed": um dispositivo por linha; "NONE" (ou vazio) quando nenhum pode acordar o PC.</summary>
    public static List<string> ParseWakeDevices(string output) =>
        output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Equals("NONE", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

    /// <summary>O que cada seção do pedido significa para a pessoa.</summary>
    public static string KindText(string kind) => kind switch
    {
        "DISPLAY" => Loc.T("mantém a tela acesa"),
        "SYSTEM" => Loc.T("impede o computador de dormir"),
        "AWAYMODE" => Loc.T("deixa o computador acordado com a tela apagada"),
        "EXECUTION" => Loc.T("pede para continuar rodando"),
        "PERFBOOST" => Loc.T("pede desempenho máximo"),
        "ACTIVELOCKSCREEN" => Loc.T("mantém a tela de bloqueio ativa"),
        _ => kind,
    };

    // ---------- tela em 60 Hz na bateria ----------

    /// <summary>
    /// Leva 11.2, tarefa 08: ao sair da tomada, a taxa para baixar (e a de antes, para voltar); ao voltar para a tomada, a
    /// taxa para devolver. Null quando não há nada a fazer.
    /// </summary>
    public static int? DynamicHzTarget(bool nowOnAc, int current, IReadOnlyList<int> supported, int? savedHigh) =>
        nowOnAc ? (savedHigh is { } high && high != current && supported.Contains(high) ? high : null)
                : SurvivalState.SaverRate(current, supported);

    // ---------- modo eficiência fora de foco ----------

    /// <summary>
    /// Leva 11.2, tarefa 10: o que mudar nos programas marcados. Liga o modo eficiência nos que estão abertos e fora de
    /// foco e desliga no que voltou ao foco; os que já estão no estado certo ficam como estão.
    /// </summary>
    public static (List<string> TurnOn, List<string> TurnOff) AutoEco(IReadOnlyCollection<string> marked, IReadOnlyCollection<string> running,
                                                                       string? foreground, IReadOnlyCollection<string> currentlyOn)
    {
        var on = new List<string>();
        var off = new List<string>();
        foreach (var name in marked.Where(m => running.Contains(m, StringComparer.OrdinalIgnoreCase)))
        {
            bool focused = string.Equals(name, foreground, StringComparison.OrdinalIgnoreCase);
            bool isOn = currentlyOn.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (focused && isOn) off.Add(name);
            else if (!focused && !isOn) on.Add(name);
        }
        return (on, off);
    }

    // ---------- suspensão seletiva USB ----------

    /// <summary>
    /// Leva 11.2, tarefa 09: com a suspensão seletiva USB desligada na bateria (valor 0 no plano), webcam, leitor de digitais
    /// e mouse parados ficam ligados o tempo todo e gastam bateria à toa.
    /// </summary>
    public static bool UsbSuspendOff(uint? dcValue) => dcValue == 0;
}
