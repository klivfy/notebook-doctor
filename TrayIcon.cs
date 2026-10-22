using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Ícone ao lado do relógio: bolinha verde/laranja/vermelha (paleta do app) e menu rápido no tema atual.</summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly Drawing.Color Ok = Drawing.ColorTranslator.FromHtml("#009e49");
    private static readonly Drawing.Color Warn = Drawing.ColorTranslator.FromHtml("#ff8c00");
    private static readonly Drawing.Color Bad = Drawing.ColorTranslator.FromHtml("#e81123");

    private readonly WinForms.NotifyIcon _icon = new() { Visible = true };
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly WinForms.ToolStripMenuItem _startItem, _miniItem, _usageItem, _healthItem;
    private IntPtr _hIcon;
    private string _iconKey = "";

    public event Action? OpenRequested, CleanRequested, ExitRequested, SupportRequested, BalloonClicked;
    public event Action<bool>? StartWithWindowsToggled, MiniToggled;

    public TrayIcon(bool startWithWindows)
    {
        // B03: uso atual no topo do menu; clicar abre o painel
        _usageItem = new WinForms.ToolStripMenuItem(Loc.T("Medindo…"));
        _healthItem = new WinForms.ToolStripMenuItem("");
        _usageItem.Click += (_, _) => OpenRequested?.Invoke();
        _healthItem.Click += (_, _) => OpenRequested?.Invoke();
        _menu.Items.Add(_usageItem);
        _menu.Items.Add(_healthItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(Loc.T("Abrir painel"), null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add(Loc.T("Limpar temporários agora"), null, (_, _) => CleanRequested?.Invoke());
        _miniItem = new WinForms.ToolStripMenuItem(Loc.T("Mini-widget sempre visível")) { CheckOnClick = true };
        _miniItem.Click += (_, _) => MiniToggled?.Invoke(_miniItem.Checked);
        _menu.Items.Add(_miniItem);
        _startItem = new WinForms.ToolStripMenuItem(Loc.T("Iniciar com o Windows")) { Checked = startWithWindows, CheckOnClick = true };
        _startItem.CheckedChanged += (_, _) => StartWithWindowsToggled?.Invoke(_startItem.Checked);
        _menu.Items.Add(_startItem);
        _menu.Items.Add(Loc.T("Suporte (klivfy.com)"), null, (_, _) => SupportRequested?.Invoke());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(Loc.T("Sair"), null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = _menu;
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
        DrawIcon(TrayBand.Ok, null);
        _icon.Text = "Notebook Doctor";
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var bg = ToDrawing(ThemeManager.Get("SurfaceContainer"));
        var fg = ToDrawing(ThemeManager.Get("OnSurface"));
        _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new MenuColors(bg, ToDrawing(ThemeManager.Get("SurfaceVariant"))));
        _menu.BackColor = bg;
        _menu.ForeColor = fg;
        _menu.Font = new Drawing.Font("Google Sans Flex", 10f);
        foreach (WinForms.ToolStripItem i in _menu.Items) { i.BackColor = bg; i.ForeColor = fg; }
    }

    private static Drawing.Color ToDrawing(System.Windows.Media.Color c) => Drawing.Color.FromArgb(c.R, c.G, c.B);

    private sealed class MenuColors(Drawing.Color bg, Drawing.Color hover) : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => bg;
        public override Drawing.Color ImageMarginGradientBegin => bg;
        public override Drawing.Color ImageMarginGradientMiddle => bg;
        public override Drawing.Color ImageMarginGradientEnd => bg;
        public override Drawing.Color MenuBorder => hover;
        public override Drawing.Color MenuItemBorder => hover;
        public override Drawing.Color MenuItemSelected => hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => hover;
        public override Drawing.Color SeparatorDark => hover;
        public override Drawing.Color CheckBackground => hover;
        public override Drawing.Color CheckSelectedBackground => hover;
    }

    /// <param name="showCpu">B01: o ícone mostra o número da CPU em vez do logo com a bolinha.</param>
    public void Update(Snapshot s, int score, bool showCpu)
    {
        var band = Tray.Band(s.Cpu, s.RamPct);
        string? label = showCpu ? Tray.CpuLabel(s.Cpu) : null;
        if ($"{band}{label}" != _iconKey) DrawIcon(band, label);
        _icon.Text = $"CPU {s.Cpu:0}% · RAM {s.RamPct:0}% · Disco livre {s.DiskFreePct:0}%"; // limite do Windows: 63 caracteres
        (_usageItem.Text, _healthItem.Text) = Tray.MenuLines(s, score);
    }

    public void SyncMini(bool value) { if (_miniItem.Checked != value) _miniItem.Checked = value; }

    public void SyncStartWithWindows(bool value) { if (_startItem.Checked != value) _startItem.Checked = value; }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Warning);

    private void DrawIcon(TrayBand band, string? cpuLabel)
    {
        _iconKey = $"{band}{cpuLabel}";
        var color = band switch { TrayBand.Red => Bad, TrayBand.Amber => Warn, _ => Ok };
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (cpuLabel is not null) DrawNumber(g, color, band, cpuLabel);
            else DrawLogo(g, color);
        }
        IntPtr h = bmp.GetHicon();
        _icon.Icon = Drawing.Icon.FromHandle(h); // não possui o HICON: o app o libera abaixo
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon); // evita vazamento de objetos GDI
        _hIcon = h;
    }

    private static void DrawLogo(Drawing.Graphics g, Drawing.Color color)
    {
        // Logo do app (mesma geometria do app.ico): quadrado arredondado azul→roxo com pulso
        using (var path = RoundedRect(new Drawing.RectangleF(1, 1, 30, 30), 7))
        using (var grad = new Drawing.Drawing2D.LinearGradientBrush(new Drawing.PointF(1, 1), new Drawing.PointF(31, 31),
                   Drawing.ColorTranslator.FromHtml("#00188f"), Drawing.ColorTranslator.FromHtml("#68217a")))
            g.FillPath(grad, path);
        using (var pulse = new Drawing.Pen(Drawing.Color.White, 2.6f) { LineJoin = Drawing.Drawing2D.LineJoin.Round, StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round })
            g.DrawLines(pulse, new[] { new Drawing.PointF(4, 16.6f), new Drawing.PointF(10.5f, 16.6f), new Drawing.PointF(13.4f, 9), new Drawing.PointF(17.6f, 24.3f), new Drawing.PointF(20.2f, 14.7f), new Drawing.PointF(22, 16.6f), new Drawing.PointF(25.6f, 16.6f) });
        // Bolinha de status (verde/laranja/vermelha) no canto inferior direito
        using var b = new Drawing.SolidBrush(color);
        g.FillEllipse(b, 17, 17, 14, 14);
        using var pen = new Drawing.Pen(Drawing.Color.White, 2);
        g.DrawEllipse(pen, 17, 17, 14, 14);
    }

    /// <summary>B01: quadrado na cor do estado com o número da CPU. Texto preto no verde e no laranja, branco no vermelho (contraste ≥ 4,5:1).</summary>
    private static void DrawNumber(Drawing.Graphics g, Drawing.Color color, TrayBand band, string label)
    {
        using (var path = RoundedRect(new Drawing.RectangleF(0, 0, 32, 32), 6))
        using (var fill = new Drawing.SolidBrush(color))
            g.FillPath(fill, path);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float size = label.Length == 1 ? 24 : 19;
        using var font = new Drawing.Font("Segoe UI", size, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var ink = new Drawing.SolidBrush(band == TrayBand.Red ? Drawing.Color.White : Drawing.Color.Black);
        using var center = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        g.DrawString(label, font, ink, new Drawing.RectangleF(-2, 0, 36, 33), center);
    }

    private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
    }
}
