using NotebookDoctor.Services;
using System.Windows;
using System.Windows.Media;

namespace NotebookDoctor;

/// <summary>Gráfico de linha minimalista (0..Max). Cor e grade vêm do tema via DynamicResource.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _all = Array.Empty<double>();
    private IReadOnlyList<double> _values = Array.Empty<double>(); // trecho à vista (tudo, sem zoom)
    private readonly Services.ChartViewport _view = new();
    private double? _dragX;

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public double Max { get; set; } = 100;

    private IReadOnlyList<DateTime>? _allTimes, _times;
    private int _hover = -1;
    private readonly System.Windows.Controls.ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

    /// <summary>Unidade mostrada na dica ao passar o mouse (ex.: "%").</summary>
    public string Unit { get; set; } = "%";

    /// <summary>Nome do gráfico lido pelo Narrador antes do resumo (ex.: "CPU, últimos 2 minutos").</summary>
    public string Title { get; set; } = "Gráfico";

    /// <summary>M04: roda do mouse aproxima, arrastar move, clique duplo volta. Pelo teclado: + e −, setas, Home.</summary>
    public bool Zoomable
    {
        get => _zoomable;
        set { _zoomable = value; Focusable = value; }
    }
    private bool _zoomable;

    public Sparkline()
    {
        ToolTip = _tip;
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 0);
        Cursor = System.Windows.Input.Cursors.Cross;
    }

    public void SetValues(IReadOnlyList<double> values, IReadOnlyList<DateTime>? times = null)
    {
        _all = values;
        _allTimes = times is not null && times.Count == values.Count ? times : null;
        _view.SetTotal(values.Count);
        Slice();
    }

    /// <summary>Recorta o trecho à vista da série completa e redesenha.</summary>
    private void Slice()
    {
        _values = _view.Zoomed ? _all.Skip(_view.Start).Take(_view.Count).ToList() : _all;
        _times = _allTimes is null ? null : _view.Zoomed ? _allTimes.Skip(_view.Start).Take(_view.Count).ToList() : _allTimes;
        _hover = -1;
        System.Windows.Automation.AutomationProperties.SetName(this, Summary());
        InvalidateVisual();
    }

    /// <summary>Resumo textual do gráfico: o mesmo conteúdo da imagem, para quem não a vê.</summary>
    public string Summary() => _values.Count == 0
        ? Loc.F("{0}: sem dados ainda", Title)
        : Loc.F("{0}{1}: {2} {3:0}{4}, média {5:0}{4}, mínimo {6:0}{4}, pico {7:0}{4}", Title, ZoomText(), Loc.T(_view.Zoomed ? "último ponto" : "agora"), _values[^1], Unit, _values.Average(), _values.Min(), _values.Max());

    private string ZoomText() =>
        !_view.Zoomed ? "" : _times is { Count: > 0 } t ? Loc.F(", com zoom de {0} a {1}", Loc.Stamp(t[0]), Loc.Stamp(t[^1])) : Loc.T(", com zoom");

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

    // ---------- M04: zoom pela roda, arraste, clique duplo e teclado ----------
    private void ZoomBy(double factor, double anchor)
    {
        _view.Zoom(factor, anchor);
        Slice();
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Sem zoom, girar para baixo rola a página como sempre; só aproximar prende a roda no gráfico
        if (!Zoomable || ActualWidth <= 0 || _all.Count < Services.ChartViewport.MinPoints || (e.Delta < 0 && !_view.Zoomed)) return;
        ZoomBy(e.Delta > 0 ? 0.8 : 1.25, e.GetPosition(this).X / ActualWidth);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Zoomable) return;
        Focus();
        if (e.ClickCount == 2) { _view.Reset(); Slice(); return; }
        if (_view.Zoomed) { _dragX = e.GetPosition(this).X; CaptureMouse(); }
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragX = null;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragX = null;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Zoomable) return;
        int step = Math.Max(1, _view.Count / 10);
        switch (e.Key)
        {
            case System.Windows.Input.Key.Add or System.Windows.Input.Key.OemPlus: ZoomBy(0.8, 0.5); break;
            case System.Windows.Input.Key.Subtract or System.Windows.Input.Key.OemMinus: ZoomBy(1.25, 0.5); break;
            case System.Windows.Input.Key.Left: _view.Pan(-step); Slice(); break;
            case System.Windows.Input.Key.Right: _view.Pan(step); Slice(); break;
            case System.Windows.Input.Key.Home: _view.Reset(); Slice(); break;
            default: return;
        }
        e.Handled = true;
    }

    // M04: dica com valor e horário do ponto sob o mouse
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_values.Count < 2 || ActualWidth <= 0) return;
        if (_dragX is { } from && _view.Zoomed)
        {
            // Arrastar para a direita mostra pontos mais antigos, como num mapa
            int moved = (int)Math.Round((from - e.GetPosition(this).X) / ActualWidth * (_view.Count - 1));
            if (moved != 0) { _view.Pan(moved); _dragX = e.GetPosition(this).X; Slice(); }
            return;
        }
        int i = (int)Math.Round(e.GetPosition(this).X / ActualWidth * (_values.Count - 1));
        i = Math.Clamp(i, 0, _values.Count - 1);
        if (i == _hover) return;
        _hover = i;
        _tip.Content = $"{_values[i]:0.#}{Unit}" + (_times is not null ? $" · {Loc.Stamp(_times[i])}" : "")
                       + (!Zoomable ? "" : "\n" + Loc.T(_view.Zoomed ? "Clique duplo volta ao período todo" : "Roda do mouse aproxima"));
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
            dc.DrawEllipse(Stroke, new Pen(TryFindResource("CardFill") as Brush ?? Brushes.White, 1.5), p, 4, 4);
        }
    }
}
