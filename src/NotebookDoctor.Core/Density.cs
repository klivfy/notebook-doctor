namespace NotebookDoctor.Services;

/// <summary>
/// B15: modo compacto. Menu lateral só com ícones, linhas de tabela mais baixas e cartões com menos respiro,
/// para telas pequenas. Sem o modo, o menu também vira só ícones quando a janela fica estreita.
/// </summary>
public static class Density
{
    /// <summary>Largura (em px a 100% de texto) abaixo da qual o menu lateral mostra só os ícones.</summary>
    public const double NavIconsBelow = 960;

    public static bool NavIconsOnly(double windowWidth, double textScale, bool compact) =>
        compact || windowWidth < NavIconsBelow * Math.Max(1, textScale * 0.8);

    /// <summary>Altura mínima das linhas das tabelas. 32 px ainda é alvo de clique confortável.</summary>
    public static double RowHeight(bool compact) => compact ? 32 : 40;

    public static double CardPadding(bool compact) => compact ? 14 : 20;

    public static double CardMargin(bool compact) => compact ? 4 : 6;
}
