namespace NotebookDoctor.Services;

/// <summary>Leva 10.2: filtros rápidos acima da tabela de Processos.</summary>
public enum ProcessFilter { All, HighUse, Hung, Windowed, Background, CanClose }

/// <summary>
/// Leva 10.2: o que a aba Processos mostra e em que ordem. Fixados (até 5) primeiro, depois os travados, depois os que
/// mais pesam; os filtros rápidos escolhem um recorte.
/// </summary>
public static class ProcessView
{
    public const int MaxPinned = 5;

    /// <summary>"Uso alto": mais de 10% de CPU.</summary>
    public const double HighCpuPct = 10;

    public static bool Matches(ProcessFilter filter, double cpuPct, bool hung, bool hasWindow, Safety safety, bool isProtected) => filter switch
    {
        ProcessFilter.HighUse => cpuPct > HighCpuPct,
        ProcessFilter.Hung => hung,
        ProcessFilter.Windowed => hasWindow,
        ProcessFilter.Background => !hasWindow,
        ProcessFilter.CanClose => safety == Safety.Safe && !isProtected,
        _ => true,
    };

    /// <summary>Posição na ordem padrão: 0 fixado, 1 travado, 2 o resto (depois por CPU e memória).</summary>
    public static int Rank(bool pinned, bool hung) => pinned ? 0 : hung ? 1 : 2;

    /// <summary>Fixa ou desafixa. Devolve a lista nova, ou null quando já há 5 fixados.</summary>
    public static List<string>? TogglePin(IReadOnlyList<string> pinned, string name)
    {
        var list = pinned.ToList();
        int i = list.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) { list.RemoveAt(i); return list; }
        if (list.Count >= MaxPinned) return null;
        list.Add(name);
        return list;
    }

    /// <summary>
    /// A tabela é atualizada a cada 3 s e reordena as linhas. Com o menu de clique direito aberto, ou com a tecla Espaço
    /// segurada sobre a tabela, ela espera: a linha sob o ponteiro não troca de lugar no meio da ação.
    /// </summary>
    public static bool HoldRefresh(bool contextMenuOpen, bool spaceHeldInGrid) => contextMenuOpen || spaceHeldInGrid;
}
