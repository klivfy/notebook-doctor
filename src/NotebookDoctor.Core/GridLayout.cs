namespace NotebookDoctor.Services;

/// <summary>B29/B30/B31: ordenação, colunas ocultas e larguras de coluna de uma tabela, guardadas pelo nome da tabela.</summary>
public sealed class GridLayout
{
    /// <summary>Propriedade ordenada (SortMemberPath da coluna); null = ordem padrão da tabela.</summary>
    public string? Sort { get; set; }
    public bool Desc { get; set; }
    /// <summary>Largura em pixels das colunas que a pessoa ajustou, pelo título da coluna.</summary>
    public Dictionary<string, double> Widths { get; set; } = new();
    /// <summary>B30: títulos das colunas que a pessoa escondeu.</summary>
    public List<string> Hidden { get; set; } = new();

    /// <summary>Larguras absurdas (coluna espremida a 0 ou esticada por engano) voltam ao padrão.</summary>
    public static bool IsSaneWidth(double w) => w is >= 24 and <= 2000;
}

/// <summary>
/// B30: quais colunas podem ser escondidas. As primeiras <c>fixedCount</c> colunas identificam a linha
/// (nome do programa; na Limpeza, a caixa de marcar e o local) e nunca somem, e sempre sobra ao menos uma visível.
/// </summary>
public static class ColumnChoice
{
    public static bool CanHide(IReadOnlyList<string> headers, IReadOnlyCollection<string> hidden, string header, int fixedCount = 1)
    {
        int index = IndexOf(headers, header);
        if (index < 0 || index < fixedCount || hidden.Contains(header)) return false;
        return headers.Count(h => !hidden.Contains(h)) > 1;
    }

    /// <summary>Esconde ou mostra a coluna. Pedido inválido (coluna fixa, desconhecida ou a última visível) devolve a lista como estava.</summary>
    public static List<string> Toggle(IReadOnlyList<string> headers, IEnumerable<string> hidden, string header, int fixedCount = 1)
    {
        var list = Sanitize(headers, hidden, fixedCount);
        if (list.Remove(header)) return list;
        if (CanHide(headers, list, header, fixedCount)) list.Add(header);
        return list;
    }

    /// <summary>
    /// Limpa o que veio das preferências: títulos que não existem mais (versão nova do app), colunas fixas e
    /// repetições. Se nada sobraria visível, mostra tudo de novo.
    /// </summary>
    public static List<string> Sanitize(IReadOnlyList<string> headers, IEnumerable<string>? hidden, int fixedCount = 1)
    {
        var list = (hidden ?? [])
            .Where(h => IndexOf(headers, h) >= Math.Max(0, fixedCount))
            .Distinct()
            .ToList();
        return list.Count >= headers.Count ? new List<string>() : list;
    }

    private static int IndexOf(IReadOnlyList<string> headers, string header)
    {
        for (int i = 0; i < headers.Count; i++)
            if (headers[i] == header) return i;
        return -1;
    }
}
