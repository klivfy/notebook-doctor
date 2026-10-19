using System.Windows;
using System.Windows.Media;

namespace NotebookDoctor;

/// <summary>Gráfico de linha minimalista (0..Max). Cor e grade vêm do tema via DynamicResource.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _values = Array.Empty<double>();

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public double Max { get; set; } = 100;

    private IReadOnlyList<DateTime>? _times;
    private int _hover = -1;
    private readonly System.Windows.Controls.ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

    /// <summary>Unidade mostrada na dica ao passar o mouse (ex.: "%").</summary>
    public string Unit { get; set; } = "%";

    /// <summary>Nome do gráfico lido pelo Narrador antes do resumo (ex.: "CPU, últimos 2 minutos").</summary>
    public string Title { get; set; } = "Gráfico";

    public Sparkline()
    {
        ToolTip = _tip;
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 0);
        Cursor = System.Windows.Input.Cursors.Cross;
    }

    public void SetValues(IReadOnlyList<double> values, IReadOnlyList<DateTime>? times = null)
    {
        _values = values;
        _times = times is not null && times.Count == values.Count ? times : null;
        if (_hover >= values.Count) _hover = -1;
        System.Windows.Automation.AutomationProperties.SetName(this, Summary());
        InvalidateVisual();
    }

    /// <summary>Resumo textual do gráfico: o mesmo conteúdo da imagem, para quem não a vê.</summary>
    public string Summary() => _values.Count == 0
        ? $"{Title}: sem dados ainda"
        : $"{Title}: agora {_values[^1]:0}{Unit}, média {_values.Average():0}{Unit}, mínimo {_values.Min():0}{Unit}, pico {_values.Max():0}{Unit}";

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new SparklinePeer(this);

    private sealed class SparklinePeer(Sparkline owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(Sparkline);
        protected override string GetNameCore() => ((Sparkline)Owner).Summary();
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }

    // M04: dica com valor e horário do ponto sob o mouse
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_values.Count < 2 || ActualWidth <= 0) return;
        int i = (int)Math.Round(e.GetPosition(this).X / ActualWidth * (_values.Count - 1));
        i = Math.Clamp(i, 0, _values.Count - 1);
        if (i == _hover) return;
        _hover = i;
        _tip.Content = $"{_values[i]:0.#}{Unit}" + (_times is not null ? $" · {_times[i]:dd/MM HH:mm}" : "");
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var grid = new Pen(GridBrush, 1) { DashStyle = DashStyles.Dot };
        dc.DrawLine(grid, new Point(0, h * 0.5), new Point(w, h * 0.5));
        dc.DrawLine(new Pen(GridBrush, 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (_values.Count < 2 || w <= 0 || h <= 0) return;

        var pts = new Point[_values.Count];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(i * w / (pts.Length - 1), h - Math.Clamp(_values[i] / Max, 0, 1) * (h - 4) - 2);

        // Linha + área suave sob a linha (como nos gráficos do Material 3)
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, h), true, true);
            ctx.LineTo(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), false, false);
            ctx.LineTo(new Point(pts[^1].X, h), false, false);
        }
        area.Freeze();

        var fill = Stroke.Clone();
        fill.Opacity = 0.14;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 2.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);

        if (_hover >= 0 && _hover < pts.Length)
        {
            var p = pts[_hover];
            dc.DrawLine(new Pen(GridBrush, 1), new Point(p.X, 0), new Point(p.X, h));
            dc.DrawEllipse(Stroke, new Pen(TryFindResource("SurfaceContainer") as Brush ?? Brushes.White, 1.5), p, 4, 4);
        }
    }
}
