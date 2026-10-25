using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NotebookDoctor;

/// <summary>
/// U5: o cabeçalho das tabelas quebra em duas linhas entre palavras, mas uma palavra maior que a coluna era cortada no
/// meio ("Connection / s", "Architectu / re"). Quando a tabela aparece, já traduzida, cada coluna ganha largura para a
/// maior palavra do cabeçalho caber inteira.
/// </summary>
public static class HeaderFit
{
    /// <summary>Preenchimento do cabeçalho (10 + 10), seta de ordenação (6 + 8) e uma folga para arredondamento.</summary>
    private const double Chrome = 20 + 14 + 4;

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            // Depois de todos os Loaded da janela, quando o Localizer já trocou os cabeçalhos
            if (s is DataGrid g) g.Dispatcher.BeginInvoke(() => Fit(g), System.Windows.Threading.DispatcherPriority.Loaded);
        }));

    public static void Fit(DataGrid grid)
    {
        // A fonte e o tamanho do estilo do cabeçalho (Themes/Controls.xaml), com o "Tamanho do texto" do Windows
        var family = grid.TryFindResource("FontMedium") as FontFamily ?? grid.FontFamily;
        double size = grid.TryFindResource("FsLabel") is double fs ? fs : grid.FontSize;
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
        foreach (var column in grid.Columns)
        {
            if (column.Header is not string text || text.Length == 0) continue;
            double word = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Max(w => new FormattedText(w, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, dip)
                    .WidthIncludingTrailingWhitespace);
            double min = Math.Ceiling(word + Chrome);
            if (column.MinWidth < min) column.MinWidth = min;
            if (column.Width.IsAbsolute && column.Width.Value < min) column.Width = new DataGridLength(min);
        }
    }
}
