namespace NotebookDoctor.Services;

/// <summary>B29/B31: ordenação e larguras de coluna de uma tabela, guardadas pelo nome da tabela.</summary>
public sealed class GridLayout
{
    /// <summary>Propriedade ordenada (SortMemberPath da coluna); null = ordem padrão da tabela.</summary>
    public string? Sort { get; set; }
    public bool Desc { get; set; }
    /// <summary>Largura em pixels das colunas que a pessoa ajustou, pelo título da coluna.</summary>
    public Dictionary<string, double> Widths { get; set; } = new();

    /// <summary>Larguras absurdas (coluna espremida a 0 ou esticada por engano) voltam ao padrão.</summary>
    public static bool IsSaneWidth(double w) => w is >= 24 and <= 2000;
}
