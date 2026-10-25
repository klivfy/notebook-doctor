using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace NotebookDoctor;

/// <summary>
/// U4: no WPF, marcar AutomationProperties.LiveSetting não basta para o Narrador ou o NVDA falarem: o app precisa
/// disparar LiveRegionChanged quando o texto muda. Aqui, todo TextBlock marcado avisa o leitor de tela a cada texto
/// novo; <see cref="Announce"/> serve para o que muda de outro jeito (um aviso que reaparece com o mesmo texto).
/// </summary>
public static class LiveRegions
{
    private static readonly DependencyPropertyDescriptor TextChanged =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
    private static readonly DependencyProperty WatchedProperty =
        DependencyProperty.RegisterAttached("Watched", typeof(bool), typeof(LiveRegions), new PropertyMetadata(false));

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb || (bool)tb.GetValue(WatchedProperty) || AutomationProperties.GetLiveSetting(tb) == AutomationLiveSetting.Off) return;
        tb.SetValue(WatchedProperty, true);
        TextChanged.AddValueChanged(tb, OnTextChanged);
    }

    // O descritor segura uma referência forte: solta quando o texto sai da tela
    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb || !(bool)tb.GetValue(WatchedProperty)) return;
        tb.SetValue(WatchedProperty, false);
        TextChanged.RemoveValueChanged(tb, OnTextChanged);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock { IsVisible: true } tb && tb.Text.Length > 0) Announce(tb);
    }

    /// <summary>Pede ao leitor de tela que leia o elemento de novo; sem leitor ouvindo, não faz nada.</summary>
    public static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
