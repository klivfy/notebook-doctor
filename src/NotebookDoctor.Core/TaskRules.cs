namespace NotebookDoctor.Services;

/// <param name="Path">Caminho no Agendador de Tarefas (ex.: \GoogleUpdateTaskMachineCore)</param>
/// <param name="Command">Programa que a tarefa executa (primeira ação), sem argumentos</param>
/// <param name="AtLogon">Roda quando alguém entra no Windows</param>
/// <param name="AtBoot">Roda quando o Windows liga</param>
/// <param name="Repeats">Tem gatilho de horário (diário, semanal, a cada N minutos…)</param>
public sealed record ScheduledTaskInfo(string Path, string Name, string Author, string Command, bool Enabled,
                                       bool AtLogon, bool AtBoot, bool Repeats, DateTime? LastRun = null);

public enum TaskAdvice { CanDisable, Keep, Review }

/// <summary>
/// M29: tarefas agendadas de terceiros. As do Windows (pasta \Microsoft\) nunca aparecem. Proteção, drivers e
/// atualizadores ficam como estão; o resto é avaliado pela frequência, porque o que roda a cada boot ou login
/// pesa na hora em que a pessoa quer começar a usar o PC.
/// </summary>
public static class TaskRules
{
    // Proteção e hardware: deixar como está
    private static readonly string[] Essential =
    [
        "defender", "antivirus", "security", "firewall", "vpn", "endpoint", "protect", "kaspersky", "avast", "avg", "norton", "mcafee",
        "eset", "bitdefender", "sophos", "malware", "driver", "audio", "realtek", "intel", "amd", "nvidia", "synaptics", "elan",
        "bluetooth", "touchpad", "fingerprint", "biometric", "backup",
    ];
    // Atualizadores mantêm o programa seguro; desativar troca alguns segundos por falhas sem correção
    private static readonly string[] Updaters = ["update", "updater", "upgrade", "autoupdate"];
    // Coletores de uso e "ajudantes" que só servem ao fabricante
    private static readonly string[] Optional = ["telemetry", "ceip", "feedback", "survey", "promo", "offer", "reminder", "crashpad", "crash report", "experience"];

    public static bool IsWindows(ScheduledTaskInfo t) =>
        t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

    public static (TaskAdvice Advice, string Reason) Classify(ScheduledTaskInfo t)
    {
        string text = $"{t.Path} {t.Author} {t.Command}".ToLowerInvariant();
        if (Essential.Any(text.Contains)) return (TaskAdvice.Keep, "Proteção ou hardware: deixe como está");
        if (Updaters.Any(text.Contains)) return (TaskAdvice.Keep, "Atualizador: mantém o programa com correções de segurança");
        if (Optional.Any(text.Contains)) return (TaskAdvice.CanDisable, "Coleta de uso, lembrete ou oferta do fabricante: não faz falta");
        if (t.AtLogon || t.AtBoot) return (TaskAdvice.Review, "Roda toda vez que o Windows liga ou você entra: se não usa o programa, pode desativar");
        return (TaskAdvice.Review, "Roda em horários agendados: se não reconhece o programa, pode desativar");
    }

    public static string When(ScheduledTaskInfo t)
    {
        var parts = new List<string>();
        if (t.AtBoot) parts.Add("ao ligar");
        if (t.AtLogon) parts.Add("ao entrar");
        if (t.Repeats) parts.Add("em horários");
        return parts.Count == 0 ? "sob demanda" : string.Join(", ", parts);
    }

    /// <summary>Tarefas de terceiros ativas, das mais recomendáveis a desativar para as menos.</summary>
    public static List<(ScheduledTaskInfo Task, TaskAdvice Advice, string Reason)> Candidates(IEnumerable<ScheduledTaskInfo> all) =>
        all.Where(t => t.Enabled && !IsWindows(t))
           .Select(t => { var (a, r) = Classify(t); return (t, a, r); })
           .OrderBy(x => x.a switch { TaskAdvice.CanDisable => 0, TaskAdvice.Review => 1, _ => 2 })
           .ThenByDescending(x => x.t.AtLogon || x.t.AtBoot)
           .ThenBy(x => x.t.Name, StringComparer.OrdinalIgnoreCase)
           .ToList();
}
