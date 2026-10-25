using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NotebookDoctor;

/// <summary>
/// U5: o cabeçalho das tabelas quebra em duas linhas entre palavras, mas uma palavra maior que a coluna era cortada no
/// meio ("Connection / s", "Architectu / re"). Quando o cabeçalho aparece, já traduzido, a coluna ganha largura mínima
/// para a maior palavra dele caber inteira.
/// </summary>
public static class HeaderFit
{
    /// <summary>Preenchimento do cabeçalho (10 + 10), seta de ordenação (6 + 8) e uma folga para arredondamento.</summary>
    private const double Chrome = 20 + 14 + 2;

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(DataGridColumnHeader), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            // Depois do Loaded da janela, quando o Localizer já trocou o texto
            if (s is DataGridColumnHeader h) h.Dispatcher.BeginInvoke(() => Fit(h), System.Windows.Threading.DispatcherPriority.Loaded);
        }));

    private static void Fit(DataGridColumnHeader h)
    {
        if (h.Column is not { } column || h.Content is not string text || text.Length == 0) return;
        string longest = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).MaxBy(w => w.Length) ?? "";
        var typeface = new Typeface(h.FontFamily, h.FontStyle, h.FontWeight, h.FontStretch);
        var formatted = new FormattedText(longest, System.Globalization.CultureInfo.CurrentUICulture, h.FlowDirection, typeface,
            h.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(h).PixelsPerDip);
        double min = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + Chrome);
        if (column.MinWidth < min) column.MinWidth = min;
    }
}
