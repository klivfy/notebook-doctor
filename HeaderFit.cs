using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NotebookDoctor;

/// <summary>
/// U5: o cabeçalho das tabelas quebra em duas linhas entre palavras, mas uma palavra maior que a coluna era cortada no
/// meio ("Connection / s", "Architectu / re"). Quando a tabela aparece, cada coluna ganha largura para a maior palavra
/// do cabeçalho (no idioma da tela) caber inteira.
/// </summary>
public static class HeaderFit
{
    /// <summary>Preenchimento do cabeçalho (10 + 10), seta de ordenação (6 + 8) e uma folga para arredondamento.</summary>
    private const double Chrome = 20 + 14 + 4;

    /// <summary>
    /// Ligada pelo estilo padrão do DataGrid (Themes/Controls.xaml). O estilo entra quando a tabela é criada, antes do
    /// primeiro layout: numa tabela com coluna "*", o WPF encolhe as outras colunas para caber na largura visível já ali, e
    /// uma largura mínima posta depois (no Loaded) só tiraria espaço da coluna "*", que já está no mínimo.
    /// </summary>
    public static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached("Auto", typeof(bool), typeof(HeaderFit),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not DataGrid g || e.NewValue is not true) return;
            // O estilo pode entrar antes de o XAML terminar de pôr as colunas: aí mede no Initialized, ainda antes do layout
            if (g.IsInitialized) Fit(g); else g.Initialized += (_, _) => Fit(g);
            g.Loaded += (_, _) => Fit(g); // colunas acrescentadas no código, depois de criar a tabela
        }));

    public static bool GetAuto(DependencyObject d) => (bool)d.GetValue(AutoProperty);
    public static void SetAuto(DependencyObject d, bool value) => d.SetValue(AutoProperty, value);

    public static void Fit(DataGrid grid)
    {
        // A fonte e o tamanho do estilo do cabeçalho (Themes/Controls.xaml), com o "Tamanho do texto" do Windows
        var family = grid.TryFindResource("FontMedium") as FontFamily ?? grid.FontFamily;
        double size = grid.TryFindResource("FsLabel") is double fs ? fs : grid.FontSize;
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
        foreach (var column in grid.Columns)
        {
            if (column.Header is not string header || header.Length == 0) continue;
            // O Localizer pode ainda não ter passado por esta tabela: mede o texto que vai aparecer
            string text = Services.Loc.TryT(header, out var translated) ? translated : header;
            double word = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Max(w => new FormattedText(w, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, dip)
                    .WidthIncludingTrailingWhitespace);
            double min = Math.Ceiling(word + Chrome);
            if (column.MinWidth < min) column.MinWidth = min;
            if (column.Width.IsAbsolute && column.Width.Value < min) column.Width = new DataGridLength(min);
        }
    }
}
