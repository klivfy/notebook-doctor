namespace NotebookDoctor.Services;

/// <summary>
/// B05: regras puras do efeito de clique dos botões (um brilho tonal que nasce no ponto do clique e se espalha pelo botão).
/// Os tempos são os do Windows 11 (167 ms rápido, 250 ms normal); o desenho fica no app.
/// </summary>
public static class ClickEffect
{
    public const int FastMs = 167, NormalMs = 250;

    /// <summary>Força do brilho no começo (0 a 1 sobre a cor do texto do botão): sutil, no mesmo nível do estado "pressionado".</summary>
    public const double PeakOpacity = 0.18;

    /// <summary>
    /// O efeito só roda com as animações do Windows ligadas e fora do Alto Contraste. Quem desliga os efeitos de
    /// animação (acessibilidade) ou usa Alto Contraste vê só o estado "pressionado", que muda na hora.
    /// </summary>
    public static bool Allowed(bool clientAreaAnimation, bool highContrast) => clientAreaAnimation && !highContrast;

    /// <summary>Ponto do clique em fração do botão (0 a 1). Botão sem tamanho ou clique fora (toque na borda) viram o centro ou a borda.</summary>
    public static (double X, double Y) Origin(double x, double y, double width, double height) =>
        (Fraction(x, width), Fraction(y, height));

    private static double Fraction(double v, double size) => size <= 0 || double.IsNaN(v) ? 0.5 : Math.Clamp(v / size, 0, 1);

    /// <summary>Raio final, em fração do botão, que alcança o canto mais distante a partir da origem.</summary>
    public static double EndRadius((double X, double Y) origin)
    {
        double dx = Math.Max(origin.X, 1 - origin.X), dy = Math.Max(origin.Y, 1 - origin.Y);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
