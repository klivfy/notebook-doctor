using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NotebookDoctor;

/// <summary>
/// Auditoria automática da tela visível (usada pelo modo --screenshots). Não substitui testar com Narrador e olhos humanos,
/// mas pega o que máquina pega: controle sem nome acessível, texto com pouco contraste, texto cortado pelo layout,
/// alvo de clique pequeno e rolagem horizontal. Cada achado é uma linha de texto.
/// </summary>
internal static class A11yAudit
{
    /// <param name="pixels">Captura da própria janela; o contraste é medido nela (cor do texto contra a cor que domina o retângulo do texto).</param>
    public static List<string> Run(FrameworkElement root, BitmapSource pixels)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>();
        void Add(string kind, string msg) { if (seen.Add(kind + msg)) issues.Add($"{kind}: {msg}"); }
        Walk(root, e => Check(e, root, pixels, Add));
        return issues;
    }

    private static void Walk(DependencyObject d, Action<FrameworkElement> visit)
    {
        if (d is UIElement { IsVisible: false }) return;
        if (d is FrameworkElement fe) visit(fe);
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), visit);
    }

    private static string Describe(FrameworkElement e)
    {
        string name = !string.IsNullOrEmpty(e.Name) ? e.Name : e.GetType().Name;
        string text = e switch { TextBlock t => t.Text, ContentControl c when c.Content is string s => s, _ => "" };
        if (text.Length > 40) text = text[..40] + "…";
        return text.Length > 0 ? $"{name} \"{text}\"" : name;
    }

    private static bool IsInteractive(FrameworkElement e) =>
        e is ButtonBase or ComboBox or TextBox or ListBoxItem or TabItem or DataGrid or Slider or PasswordBox
        && !InsideTemplateOf<ScrollBar>(e) && !InsideTemplateOf<ComboBox>(e) && !InsideTemplateOf<DataGridColumnHeader>(e);

    private static bool InsideTemplateOf<T>(FrameworkElement e) where T : FrameworkElement =>
        e.TemplatedParent is T;

    private static void Check(FrameworkElement e, FrameworkElement root, BitmapSource pixels, Action<string, string> add)
    {
        // 1. Nome acessível (é o que o Narrador lê)
        if (IsInteractive(e))
        {
            string? name = null;
            try
            {
                name = UIElementAutomationPeer.CreatePeerForElement(e)?.GetName();
                // LabeledBy também vale: o UI Automation usa o rótulo associado como nome do campo
                if (string.IsNullOrWhiteSpace(name) && AutomationProperties.GetLabeledBy(e) is UIElement lb)
                    name = UIElementAutomationPeer.CreatePeerForElement(lb)?.GetName() ?? (lb as TextBlock)?.Text;
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name)) { if (!e.Name.StartsWith("PART_")) add("SEM NOME", Describe(e)); }
            else if (name.All(c => c is >= '' and <= '' || char.IsWhiteSpace(c))) add("NOME É ÍCONE", Describe(e));

            // 2. Alvo de clique (WCAG 2.2: 24×24 px)
            if (e.ActualWidth > 0 && e.ActualHeight > 0 && (e.ActualWidth < 24 || e.ActualHeight < 24) && e is not ListBoxItem and not DataGrid)
                add("ALVO PEQUENO", $"{Describe(e)} {e.ActualWidth:0}×{e.ActualHeight:0} px");
        }

        // 3. Texto cortado pelo layout
        if (e is TextBlock cut && LayoutInformation.GetLayoutClip(cut) is not null && cut.ActualWidth > 0)
            add("CORTADO", $"{Describe(cut)} [{cut.ActualWidth:0}×{cut.ActualHeight:0} de {cut.DesiredSize.Width:0}×{cut.DesiredSize.Height:0}]{(cut.TextTrimming == TextTrimming.None ? " [sem reticências]" : "")}");

        // 4. Contraste do texto, medido nos pixels da captura (a cor do fundo vem do que foi desenhado, não de propriedades)
        if (e is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) && tb.Foreground is SolidColorBrush fg && tb.ActualWidth > 0 && tb.ActualHeight > 0
            && DominantColor(tb, root, pixels) is { } bg)
        {
            double opacity = fg.Opacity * EffectiveOpacity(tb);
            var fgc = Over(fg.Color, opacity, bg);
            double ratio = Contrast(fgc, bg);
            bool large = tb.FontSize >= 24 || (tb.FontSize >= 18.66 && tb.FontWeight >= FontWeights.SemiBold);
            if (ratio < (large ? 3.0 : 4.5)) add("CONTRASTE", $"{ratio:0.0}:1 em {Describe(tb)} ({Hex(fgc)} sobre {Hex(bg)})");
        }

        // 5. Rolagem horizontal (em tabelas largas é esperado; em página, não)
        if (e is ScrollViewer sv && sv.ScrollableWidth > 1 && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
            add("ROLAGEM HORIZONTAL", $"{Describe(sv)} excede {sv.ScrollableWidth:0} px");
    }

    private static double EffectiveOpacity(DependencyObject d)
    {
        double o = 1;
        for (; d is not null; d = VisualTreeHelper.GetParent(d)) if (d is UIElement u) o *= u.Opacity;
        return o;
    }

    /// <summary>Cor mais frequente no retângulo do texto: o fundo (o texto ocupa uma fração menor dos pixels). Null se o texto está fora da janela.</summary>
    private static Color? DominantColor(FrameworkElement el, FrameworkElement root, BitmapSource bmp)
    {
        var dpi = VisualTreeHelper.GetDpi(root);
        Rect r;
        try { r = el.TransformToVisual(root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); }
        catch (InvalidOperationException) { return null; }
        int x = (int)Math.Floor(r.X * dpi.DpiScaleX), y = (int)Math.Floor(r.Y * dpi.DpiScaleY);
        int w = (int)Math.Ceiling(r.Width * dpi.DpiScaleX), h = (int)Math.Ceiling(r.Height * dpi.DpiScaleY);
        if (x < 0 || y < 0 || w < 2 || h < 2 || x + w > bmp.PixelWidth || y + h > bmp.PixelHeight) return null;
        var buf = new byte[w * h * 4];
        bmp.CopyPixels(new Int32Rect(x, y, w, h), buf, w * 4, 0);
        var counts = new Dictionary<int, int>();
        int best = 0, bestCount = 0;
        for (int i = 0; i < buf.Length; i += 4)
        {
            int key = buf[i] | buf[i + 1] << 8 | buf[i + 2] << 16; // B, G, R
            int n = counts.GetValueOrDefault(key) + 1;
            counts[key] = n;
            if (n > bestCount) { bestCount = n; best = key; }
        }
        return Color.FromRgb((byte)(best >> 16 & 255), (byte)(best >> 8 & 255), (byte)(best & 255));
    }

    /// <summary>Cor sobre fundo opaco, respeitando o alfa da cor e a opacidade do pincel.</summary>
    private static Color Over(Color top, double opacity, Color back)
    {
        double a = top.A / 255.0 * opacity;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromArgb(255, Mix(top.R, back.R), Mix(top.G, back.G), Mix(top.B, back.B));
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
