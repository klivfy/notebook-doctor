using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// M13: traduz as telas sem trocar o XAML. O texto escrito em português (Text, Content, Header, ToolTip, nomes
/// para o Narrador, títulos das colunas) é procurado no dicionário do idioma quando o elemento carrega; o que vem
/// de binding (dados) nunca é tocado. Em português, nada é feito.
/// </summary>
public static class Localizer
{
    private static readonly DependencyProperty[] Plain =
    [
        FrameworkElement.ToolTipProperty, AutomationProperties.NameProperty, AutomationProperties.HelpTextProperty,
    ];

    /// <summary>Liga a tradução para todo elemento que carregar daqui em diante (inclusive itens de modelos e diálogos).</summary>
    public static void Install()
    {
        if (Loc.IsSource) return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is DependencyObject d) Apply(d); }));
        // Toda janela (ferramentas, diálogos) é traduzida inteira quando abre: o Loaded de cada elemento sozinho deixava textos do XAML
        // das janelas de ferramenta em português
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) ApplyTree(w); }));
    }

    /// <summary>Traduz uma janela inteira de uma vez. Usado antes de ler os títulos das colunas (GridMemory).</summary>
    public static void ApplyTree(DependencyObject root)
    {
        if (Loc.IsSource) return;
        Apply(root);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) ApplyTree(child);
    }

    /// <summary>
    /// C10: elemento já traduzido. A janela é traduzida inteira no Loaded dela e depois cada elemento recebia o próprio
    /// Loaded, que traduzia tudo de novo; agora cada elemento passa uma vez só.
    /// </summary>
    private static readonly DependencyProperty DoneProperty = DependencyProperty.RegisterAttached("Done", typeof(bool), typeof(Localizer));

    private static void Apply(DependencyObject d)
    {
        if (d.IsSealed || d is Freezable { IsFrozen: true } || (bool)d.GetValue(DoneProperty)) return; // congelado: não muda nem tem texto a traduzir
        d.SetValue(DoneProperty, true);
        foreach (var p in Plain) Translate(d, p);
        switch (d)
        {
            case TextBlock tb when tb.Inlines.Count <= 1: Translate(d, TextBlock.TextProperty); break;
            case TextBox: Translate(d, FrameworkElement.TagProperty); break; // texto de ajuda dentro da caixa
            case HeaderedContentControl or HeaderedItemsControl: Translate(d, d is HeaderedItemsControl ? HeaderedItemsControl.HeaderProperty : HeaderedContentControl.HeaderProperty); break;
            case Window: Translate(d, Window.TitleProperty); break;
        }
        if (d is ContentControl and not Window) Translate(d, ContentControl.ContentProperty);
        if (d is DataGrid grid)
            foreach (var col in grid.Columns)
                if (col.Header is string h && Loc.TryT(h, out var t)) col.Header = t;
    }

    private static void Translate(DependencyObject d, DependencyProperty p)
    {
        if (BindingOperations.IsDataBound(d, p)) return;
        if (d.GetValue(p) is not string s || s.Length == 0) return;
        if (DependencyPropertyHelper.GetValueSource(d, p).BaseValueSource == BaseValueSource.Default) return;
        if (Loc.TryT(s, out var t)) d.SetValue(p, t);
    }
}
