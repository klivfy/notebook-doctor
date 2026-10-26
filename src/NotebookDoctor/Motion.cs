using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace NotebookDoctor;

/// <summary>
/// B04/B06: movimentos curtos e discretos. Com "Efeitos de animação" desligado no Windows (acessibilidade),
/// tudo muda na hora, sem animação.
/// </summary>
public static class Motion
{
    public static bool Enabled => Services.ClickEffect.Allowed(SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast);

    // Redesign V6: tempos e curva do Windows 11 (rápido 167 ms, normal 250 ms, desacelerando no fim)
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(Services.ClickEffect.FastMs), Normal = TimeSpan.FromMilliseconds(Services.ClickEffect.NormalMs);
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>B04: o medidor desliza até o valor novo em vez de pular.</summary>
    public static void SetValue(ProgressBar bar, double value)
    {
        if (!Enabled) { bar.BeginAnimation(RangeBase.ValueProperty, null); bar.Value = value; return; }
        var anim = new DoubleAnimation(value, Normal) { EasingFunction = Ease };
        bar.BeginAnimation(RangeBase.ValueProperty, anim);
    }

    /// <summary>B06: o conteúdo da aba nova aparece com um leve fade e sobe 8 px.</summary>
    public static void EnterContent(FrameworkElement? content)
    {
        if (content is null || !Enabled) return;
        var move = new TranslateTransform(0, 8);
        content.RenderTransform = move;
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Fast) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Normal) { EasingFunction = Ease });
    }
}
