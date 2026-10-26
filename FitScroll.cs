using System.Windows;
using System.Windows.Controls;

namespace NotebookDoctor;

/// <summary>
/// B46: num notebook de 1366 × 768 a 150%, a janela de ferramenta encolhe (U2) e as linhas de altura fixa (título,
/// explicação, botões) espremiam a lista até 0 px. Com <c>local:FitScroll.StarMin</c> no ScrollViewer em volta da grade
/// da janela, cada linha "*" ganha pelo menos essa altura; o que passar da janela rola junto. Com espaço sobrando, a
/// grade ocupa a janela toda e nada muda.
/// </summary>
public static class FitScroll
{
    public static readonly DependencyProperty StarMinProperty = DependencyProperty.RegisterAttached(
        "StarMin", typeof(double), typeof(FitScroll), new PropertyMetadata(0.0, OnStarMinChanged));

    public static double GetStarMin(DependencyObject d) => (double)d.GetValue(StarMinProperty);
    public static void SetStarMin(DependencyObject d, double value) => d.SetValue(StarMinProperty, value);

    private static void OnStarMinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if (e.OldValue is double old && old > 0) return; // já ligado
        // Tamanho da janela e texto que muda numa linha "Auto" (aviso, andamento) mudam a altura que a grade precisa
        sv.LayoutUpdated += (_, _) => Fit(sv);
    }

    private static void Fit(ScrollViewer sv)
    {
        if (sv.Content is not Grid grid || sv.ViewportHeight <= 0) return;
        // Com o texto maior, as linhas de tamanho fixo (títulos, filtros, botões) crescem, e a lista precisa crescer junto
        double starMin = GetStarMin(sv) * TextScale.Factor, fixedPart = grid.Margin.Top + grid.Margin.Bottom;
        foreach (var row in grid.RowDefinitions)
            fixedPart += row.Height.IsStar ? Math.Max(starMin, row.MinHeight) : row.ActualHeight;
        double height = Math.Max(sv.ViewportHeight - grid.Margin.Top - grid.Margin.Bottom, fixedPart - grid.Margin.Top - grid.Margin.Bottom);
        if (double.IsNaN(grid.Height) || Math.Abs(grid.Height - height) > 1) grid.Height = height;
    }
}
