using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// B05: efeito de clique nos botões (Fluent, sem o exagero do Material). Um brilho tonal na cor do texto do botão nasce no
/// ponto do clique ou do toque e se espalha até o canto mais distante em 250 ms, apagando junto. O estilo base de Button liga
/// o efeito; o desenho é o <c>Border</c> chamado <c>Ripple</c> do modelo, que segue os cantos arredondados do botão.
/// Com as animações do Windows desligadas ou em Alto Contraste (<see cref="Motion.Enabled"/>), nada anima: fica só o
/// estado "pressionado" do modelo, que muda na hora.
/// </summary>
public static class ClickRipple
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ClickRipple), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ButtonBase button) return;
        button.PreviewMouseLeftButtonDown -= OnDown; // mouse, caneta e toque chegam todos como clique do botão esquerdo
        if ((bool)e.NewValue) button.PreviewMouseLeftButtonDown += OnDown;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ButtonBase { IsEnabled: true } button || !Motion.Enabled) return;
        if (button.Template?.FindName("Ripple", button) is not Border ripple) return;
        if (button.Foreground is not SolidColorBrush tint) return;

        var p = e.GetPosition(button);
        var origin = ClickEffect.Origin(p.X, p.Y, button.ActualWidth, button.ActualHeight);
        double end = ClickEffect.EndRadius(origin);

        var color = tint.Color;
        var brush = new RadialGradientBrush
        {
            Center = new Point(origin.X, origin.Y),
            GradientOrigin = new Point(origin.X, origin.Y),
            RadiusX = 0, RadiusY = 0,
            GradientStops = [new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)],
        };
        ripple.Background = brush;
        ripple.Opacity = ClickEffect.PeakOpacity;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        brush.BeginAnimation(RadialGradientBrush.RadiusXProperty, new DoubleAnimation(0, end, Motion.Normal) { EasingFunction = ease });
        brush.BeginAnimation(RadialGradientBrush.RadiusYProperty, new DoubleAnimation(0, end, Motion.Normal) { EasingFunction = ease });
        var fade = new DoubleAnimation(ClickEffect.PeakOpacity, 0, Motion.Normal) { EasingFunction = ease };
        fade.Completed += (_, _) => { if (ReferenceEquals(ripple.Background, brush)) ripple.Background = null; };
        ripple.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
