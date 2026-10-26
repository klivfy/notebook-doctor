using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// U2: num notebook 1366 × 768 com escala de 125% sobram cerca de 1093 × 566 para as janelas. Toda janela que abre
/// maior que a área útil do monitor (no tamanho ou no mínimo que pede) encolhe e entra inteira nela, para os botões de
/// baixo das ferramentas não ficarem fora da tela.
/// </summary>
public static class WindowFit
{
    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is Window w) Fit(w); }));

    public static void Fit(Window w)
    {
        // Janela maximizada já cabe; o mini-widget e a sobreposição de jogo cuidam da própria posição
        if (w.WindowState != WindowState.Normal || w.AllowsTransparency || double.IsNaN(w.Left) || double.IsNaN(w.Top)) return;
        var box = new Box(w.Left, w.Top, w.ActualWidth, w.ActualHeight);
        var areas = WorkAreas();
        if (areas.Count == 0 || box.Width <= 0 || box.Height <= 0) return;
        var fit = WindowPlacement.Inside(box, areas.OrderByDescending(a => a.OverlapArea(box)).First());
        if (fit == box) return;
        w.SizeToContent = SizeToContent.Manual;
        w.MinWidth = Math.Min(w.MinWidth, fit.Width);
        w.MinHeight = Math.Min(w.MinHeight, fit.Height);
        (w.Left, w.Top, w.Width, w.Height) = (fit.X, fit.Y, fit.Width, fit.Height);
    }

    /// <summary>Área útil de cada monitor (sem a barra de tarefas), convertida para as unidades do WPF.</summary>
    public static List<Box> WorkAreas()
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        double scale = primary is null || SystemParameters.PrimaryScreenWidth <= 0 ? 1 : primary.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        return System.Windows.Forms.Screen.AllScreens
            .Select(sc => new Box(sc.WorkingArea.X / scale, sc.WorkingArea.Y / scale, sc.WorkingArea.Width / scale, sc.WorkingArea.Height / scale))
            .ToList();
    }
}
