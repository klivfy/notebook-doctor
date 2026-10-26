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

    /// <summary>Largura da janela (em px a 100% de texto) a partir da qual o Início põe o Diagnóstico numa coluna ao lado.</summary>
    public const double SideColumnFrom = 1200;

    /// <summary>
    /// Início em duas colunas (nota, métricas e atalhos à esquerda; Diagnóstico com as correções à direita) em monitor largo. Com o
    /// "Tamanho do texto" do Windows maior, a mesma largura cabe menos conteúdo, então o limite sobe junto (como no menu lateral).
    /// </summary>
    public static bool PainelSideColumn(double windowWidth, double textScale) =>
        windowWidth >= SideColumnFrom * Math.Max(1, textScale * 0.8);

    /// <summary>Altura mínima das linhas das tabelas. 32 px ainda é alvo de clique confortável.</summary>
    public static double RowHeight(bool compact) => compact ? 32 : 40;

    public static double CardPadding(bool compact) => compact ? 12 : 16;

    public static double CardMargin(bool compact) => compact ? 2 : 4;
}
