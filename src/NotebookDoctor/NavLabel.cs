using System.Windows;

namespace NotebookDoctor;

/// <summary>
/// Redesign V3: dados extras dos itens da navegação. <c>Short</c> é o rótulo curto do trilho (ícone sobre o texto, 80 px); o nome completo
/// continua no título da página, na dica e no Narrador. <c>Badge</c> acende o ponto de pendência sobre o ícone.
/// </summary>
public static class NavLabel
{
    public static readonly DependencyProperty ShortProperty =
        DependencyProperty.RegisterAttached("Short", typeof(string), typeof(NavLabel), new PropertyMetadata(null));

    public static string? GetShort(DependencyObject o) => (string?)o.GetValue(ShortProperty);
    public static void SetShort(DependencyObject o, string? value) => o.SetValue(ShortProperty, value);

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(bool), typeof(NavLabel), new PropertyMetadata(false));

    public static bool GetBadge(DependencyObject o) => (bool)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, bool value) => o.SetValue(BadgeProperty, value);
}
