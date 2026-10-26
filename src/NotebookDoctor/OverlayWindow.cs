using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// M03: faixa pequena com CPU, RAM e temperatura por cima de jogos. Os cliques atravessam (WS_EX_TRANSPARENT) e ela
/// nunca rouba o foco (WS_EX_NOACTIVATE), para não tirar o jogo da tela cheia. Fundo escuro fixo: o jogo não segue o tema do app.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly TextBlock _text = new() { Foreground = Brushes.White, FontSize = 14 };

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Notebook Doctor · overlay";
        _text.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium");
        Content = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 5, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)), Child = _text,
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    public void Update(Snapshot s) => _text.Text = Overlay.Text(s);

    /// <summary>Encosta no canto escolhido do monitor do jogo. Trabalha em pixels para não depender da escala de cada monitor.</summary>
    public void PlaceOn(Box monitor, string corner)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
        var (x, y) = Overlay.Place(corner, monitor, r.Right - r.Left, r.Bottom - r.Top, margin: 12);
        if ((int)x == r.Left && (int)y == r.Top) return;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
