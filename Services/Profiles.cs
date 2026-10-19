namespace NotebookDoctor.Services;

/// <param name="PlanKind">"balanced", "saver" ou "high": qual plano de energia o perfil usa</param>
public sealed record Profile(string Id, string Name, string Description, string PlanKind, bool AlertsEnabled, bool Focus)
{
    public static readonly Profile[] All =
    {
        new("work", "Trabalho", "Plano Equilibrado e avisos ligados. O uso do dia a dia.", "balanced", true, false),
        new("meeting", "Reunião", "Equilibrado e sem avisos na bandeja, para não interromper chamadas ou apresentações.", "balanced", false, false),
        new("battery", "Bateria", "Plano de Economia de energia (se existir) para durar mais longe da tomada.", "saver", true, false),
        new("performance", "Desempenho", "Alto desempenho (se existir) e Modo Foco: prioridade total ao programa em uso. Pede tomada.", "high", true, true),
    };

    public static bool Matches(string kind, string planName) => kind switch
    {
        "balanced" => planName.Contains("quilibrado", StringComparison.OrdinalIgnoreCase) || planName.Contains("balanced", StringComparison.OrdinalIgnoreCase),
        "saver" => planName.Contains("conomia", StringComparison.OrdinalIgnoreCase) || planName.Contains("saver", StringComparison.OrdinalIgnoreCase),
        "high" => planName.Contains("desempenho", StringComparison.OrdinalIgnoreCase) || planName.Contains("performance", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    public PowerPlan? FindPlan(IEnumerable<PowerPlan> plans) => plans.FirstOrDefault(p => Matches(PlanKind, p.Name));
}
