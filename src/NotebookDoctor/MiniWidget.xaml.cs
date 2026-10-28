using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>M02: janelinha sempre visível com CPU, memória e disco. Arrasta para mover; a posição fica salva.</summary>
public partial class MiniWidget : Window
{
    private static App Host => App.Current;
    public event Action? CloseRequested;

    public MiniWidget()
    {
        InitializeComponent();
        var s = Host.Settings;
        var area = SystemParameters.WorkArea;
        // Posição salva, desde que ainda caiba na tela (o monitor pode ter mudado); senão, canto inferior direito
        bool saved = s.MiniLeft is { } x && s.MiniTop is { } y &&
                     x >= SystemParameters.VirtualScreenLeft && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60 &&
                     y >= SystemParameters.VirtualScreenTop && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;
        Left = saved ? s.MiniLeft!.Value : area.Right - 270;
        Top = saved ? s.MiniTop!.Value : area.Bottom - 190;
        Host.SnapshotReady += Update;
        Host.MediaChanged += ShowMedia;
        if (Host.Latest is { } snap) Update(snap);
        ShowMedia(Meeting.InUseText(Host.Media));
        Closed += (_, _) => { Host.SnapshotReady -= Update; Host.MediaChanged -= ShowMedia; };
    }

    // Leva 12.1: câmera ou microfone abertos agora
    private void ShowMedia(string text)
    {
        MediaText.Text = text;
        MediaRow.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(MediaRow, text.Length > 0 ? Loc.F("Em uso agora: {0}", text) : "");
    }

    private void Update(Snapshot s)
    {
        Set(CpuBar, CpuText, s.Cpu);
        Set(RamBar, RamText, s.RamPct);
        Set(DiskBar, DiskText, s.DiskBusy);
        int score = Host.Score.Score;
        ScoreText.Text = score > 0 ? $"Nota {score} · {Host.Score.Label}" : "Nota –";
        var top = s.TopByCpu.FirstOrDefault();
        TopText.Text = top is null ? "" : $"Maior: {top.Name} ({top.CpuPct:0}%)";
    }

    private static void Set(ProgressBar bar, TextBlock text, double pct)
    {
        bar.Value = pct;
        bar.SetResourceReference(ProgressBar.ForegroundProperty, pct >= 85 ? "LevelCrit" : pct >= 70 ? "LevelHigh" : pct >= 50 ? "LevelMid" : "LevelLow");
        text.Text = $"{pct:0}%";
        System.Windows.Automation.AutomationProperties.SetHelpText(bar, $"{pct:0}%");
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;
        // Clique no botão de fechar não arrasta
        for (var d = e.OriginalSource as DependencyObject; d is System.Windows.Media.Visual; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is Button) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        Host.Settings.MiniLeft = Left;
        Host.Settings.MiniTop = Top;
        Host.Settings.Save();
    }

    private void Open(object sender, RoutedEventArgs e) => Host.ShowWindow();
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseRequested?.Invoke();
        else if (e.Key == Key.Enter) Host.ShowWindow();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Não aparece no Alt+Tab (WS_EX_TOOLWINDOW), como os widgets do sistema
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, ex | 0x80);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
}
