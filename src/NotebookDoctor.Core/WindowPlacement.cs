namespace NotebookDoctor.Services;

/// <summary>Retângulo em unidades da tela (o Core não conhece o WPF).</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public double OverlapArea(Box o) =>
        Math.Max(0, Math.Min(Right, o.Right) - Math.Max(X, o.X)) * Math.Max(0, Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y));
}

/// <summary>
/// B07/B45: reabre a janela no mesmo lugar e no mesmo monitor. Se aquele monitor não existir mais (notebook fora da
/// dock) ou a barra de título ficar fora de alcance, devolve null e a janela abre centralizada.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Quanto da barra de título precisa estar visível para a pessoa conseguir arrastar a janela.</summary>
    public const double MinGrab = 120, TitleHeight = 32;

    public static Box? Fit(Box saved, IReadOnlyList<Box> workAreas, double minWidth, double minHeight)
    {
        if (saved.Width <= 0 || saved.Height <= 0 || double.IsNaN(saved.X) || double.IsNaN(saved.Y) || workAreas.Count == 0) return null;

        var title = new Box(saved.X, saved.Y, saved.Width, TitleHeight);
        var screen = workAreas.OrderByDescending(a => a.OverlapArea(saved)).First();
        if (workAreas.All(a => a.OverlapArea(title) < MinGrab * TitleHeight / 2)) return null;

        // Monitor menor que a janela salva (ex.: trocou de resolução): encolhe para caber, sem passar do mínimo
        double w = Math.Max(minWidth, Math.Min(saved.Width, screen.Width));
        double h = Math.Max(minHeight, Math.Min(saved.Height, screen.Height));
        double x = Math.Clamp(saved.X, screen.X, Math.Max(screen.X, screen.Right - w));
        double y = Math.Clamp(saved.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - h));
        return new Box(x, y, w, h);
    }
}
