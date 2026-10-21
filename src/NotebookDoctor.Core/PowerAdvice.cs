namespace NotebookDoctor.Services;

public sealed record PowerSuggestion(PowerPlan Plan, string Title, string Reason);

/// <summary>
/// A02: ao ligar ou tirar da tomada, sugere o plano que faz sentido. Só sugere: quem troca é o usuário.
/// Fica quieto quando o plano atual já serve (ex.: Equilibrado vale para os dois casos).
/// </summary>
public static class PowerAdvice
{
    public static PowerSuggestion? Suggest(bool onAc, IReadOnlyList<PowerPlan> plans)
    {
        var active = plans.FirstOrDefault(p => p.Active);
        if (active is null) return null;
        PowerPlan? Find(string kind) => plans.FirstOrDefault(p => !p.Active && Profile.Matches(kind, p.Name));

        if (!onAc && Profile.Matches("high", active.Name) && Find("balanced") is { } balanced)
            return new(balanced, "Notebook na bateria",
                       $"O plano \"{active.Name}\" gasta a bateria mais rápido e esquenta. O \"{balanced.Name}\" dura bem mais.");

        if (onAc && Profile.Matches("saver", active.Name) && (Find("high") ?? Find("balanced")) is { } faster)
            return new(faster, "Notebook na tomada",
                       $"Na tomada, o plano \"{active.Name}\" só deixa o notebook mais lento. O \"{faster.Name}\" aproveita a energia.");

        return null;
    }
}
