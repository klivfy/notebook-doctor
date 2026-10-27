using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// Leva 10.3: grifa nas tabelas o trecho buscado. No TextBlock, <c>local:SearchHighlight.Text</c> substitui o Text e
/// <c>local:SearchHighlight.Term</c> recebe a busca (a tabela guarda a busca atual no Tag). O trecho achado fica em negrito
/// e com o fundo de destaque: a cor nunca é o único sinal.
/// </summary>
public static class SearchHighlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty TermProperty = DependencyProperty.RegisterAttached(
        "Term", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", Changed));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetTerm(DependencyObject d) => (string)d.GetValue(TermProperty);
    public static void SetTerm(DependencyObject d, string value) => d.SetValue(TermProperty, value);

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        foreach (var (text, hit) in SearchMatch.Split(GetText(tb) ?? "", GetTerm(tb)))
        {
            var run = new Run(text);
            if (hit)
            {
                run.FontFamily = (System.Windows.Media.FontFamily)tb.FindResource("FontSemi");
                run.SetResourceReference(TextElement.BackgroundProperty, "AccentContainer");
                run.SetResourceReference(TextElement.ForegroundProperty, "OnAccentContainer");
            }
            tb.Inlines.Add(run);
        }
    }
}
