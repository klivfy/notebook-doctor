namespace NotebookDoctor.Services;

public enum BadgeLevel { None, Attention, Problem }

public sealed record Badge(BadgeLevel Level, string Description);

/// <summary>
/// B47: selo sobre o ícone na barra de tarefas. Só aparece quando há algo a ver (PROBLEMA ou ATENÇÃO);
/// com tudo OK fica sem selo, para não virar ruído. A descrição é lida pelo Narrador e aparece na dica do botão.
/// </summary>
public static class TaskbarBadge
{
    public static Badge For(int score, IReadOnlyList<Finding> findings)
    {
        int problems = findings.Count(f => f.Level == "PROBLEMA");
        int attention = findings.Count(f => f.Level == "ATENCAO");
        string head = score > 0 ? $"Notebook Doctor · nota {score}" : "Notebook Doctor";
        if (problems > 0) return new(BadgeLevel.Problem, $"{head} · {Pt.N(problems, "problema", "problemas")}");
        if (attention > 0) return new(BadgeLevel.Attention, $"{head} · {Pt.N(attention, "ponto de atenção", "pontos de atenção")}");
        return new(BadgeLevel.None, $"{head} · tudo certo");
    }
}
