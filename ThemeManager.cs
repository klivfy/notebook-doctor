using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Alterna entre tema claro/escuro (Fluent, Windows 11) seguindo o Windows ou a escolha do usuário, e aplica o fundo Mica.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    /// <summary>M09: aplicar a cor de destaque do Windows por cima da paleta do tema.</summary>
    public static bool UseAccent { get; set; }
    public static event Action? Changed;

    private static ResourceDictionary? _current;

    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <param name="mode">"system", "light" ou "dark"</param>
    public static void Apply(string mode)
    {
        if (Application.Current is null) return; // app encerrando (ex.: "Sair" com a tela de boas-vindas aberta)
        HighContrast = SystemParameters.HighContrast;
        bool dark = mode switch { "dark" => true, "light" => false, _ => SystemPrefersDark() };
        var dicts = Application.Current.Resources.MergedDictionaries;
        // M08: com Alto Contraste do Windows ligado, só as cores do sistema valem (o tema e a paleta ficam de fora)
        var next = HighContrast ? HighContrastTheme()
                                : new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        if (HighContrast) dark = SystemColors.WindowColor.R + SystemColors.WindowColor.G + SystemColors.WindowColor.B < 384;
        else if (UseAccent && ReadAccent() is { } accent) ApplyAccent(next, accent, dark);
        if (_current is not null) dicts.Remove(_current);
        dicts.Insert(0, next); // antes dos estilos, para que o DynamicResource encontre as cores
        _current = next;
        IsDark = dark;
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        Changed?.Invoke();
    }

    public static bool HighContrast { get; private set; }

    /// <summary>Cor de destaque escolhida em Configurações › Personalização › Cores (formato 0xAABBGGRR).</summary>
    private static Rgb? ReadAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            return k?.GetValue("AccentColor") is int v ? Rgb.FromAbgr(unchecked((uint)v)) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Troca as cores de ação pela de destaque, clareando ou escurecendo até ter contraste 4,5:1 com o fundo,
    /// para que botões e textos continuem legíveis com qualquer cor que o usuário escolha no Windows.
    /// </summary>
    private static void ApplyAccent(ResourceDictionary d, Rgb accent, bool dark)
    {
        static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);
        void Set(string key, Rgb c) => d[key] = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        if (d["Surface"] is not SolidColorBrush surfaceBrush) return;
        var surface = ToRgb(surfaceBrush.Color);
        var primary = ColorMath.Readable(accent, surface);
        var container = ColorMath.Mix(accent, surface, dark ? 0.75 : 0.85);
        var onContainer = ColorMath.Readable(accent, container);
        // Valores clicáveis ficam sobre os cartões: o contraste é medido contra o cartão
        var card = d["SurfaceContainer"] is SolidColorBrush cardBrush ? ToRgb(cardBrush.Color) : surface;
        Set("Primary", primary);
        Set("OnPrimary", ColorMath.OnColor(primary));
        Set("AccentText", ColorMath.Readable(accent, card));
        Set("PrimaryContainer", container);
        Set("OnPrimaryContainer", onContainer);
        Set("InfoContainer", container);
        // SecondaryContainer é o preenchimento neutro dos controles no Fluent: não leva a cor de destaque
    }

    /// <summary>Tokens do app mapeados para as cores do esquema de Alto Contraste escolhido no Windows.</summary>
    private static ResourceDictionary HighContrastTheme()
    {
        var d = new ResourceDictionary();
        void Set(string key, Color c) => d[key] = new SolidColorBrush(c);
        Color win = SystemColors.WindowColor, text = SystemColors.WindowTextColor, hi = SystemColors.HighlightColor,
              hiText = SystemColors.HighlightTextColor;
        foreach (var k in new[] { "Surface", "SurfaceContainer", "SurfaceVariant", "PrimaryContainer", "LayerFill", "ControlFill", "ControlFillHover",
                                  "ErrorContainer", "WarningContainer", "SuccessContainer", "InfoContainer" }) Set(k, win);
        foreach (var k in new[] { "OnSurface", "OnSurfaceVariant", "Outline", "OutlineVariant", "CardBorder", "OnPrimaryContainer", "LayerStroke",
                                  "ControlStroke", "FocusStroke", "AccentText", "ChartCpu", "ChartRam", "ChartDisk" }) Set(k, text);
        Set("StateHover", Color.FromArgb(0x30, hi.R, hi.G, hi.B));
        Set("StatePressed", Color.FromArgb(0x50, hi.R, hi.G, hi.B));
        foreach (var k in new[] { "Primary", "SecondaryContainer", "Error", "Warning", "Success",
                                  "LevelLow", "LevelMid", "LevelHigh", "LevelCrit" }) Set(k, hi);
        foreach (var k in new[] { "OnPrimary", "OnSecondaryContainer", "OnError", "OnWarning", "OnSuccess" }) Set(k, hiText);
        Set("InverseSurface", text); Set("InverseOnSurface", win); Set("InversePrimary", win);
        return d;
    }

    public static Color Get(string key) => (Application.Current.Resources[key] as SolidColorBrush)?.Color ?? Colors.Magenta;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    /// <summary>
    /// Redesign V1: fundo Mica do Windows 11 (o material que deixa a moldura no tom do papel de parede). Ligado por padrão; a decisão
    /// final fica com a pessoa (PLANO-VISUAL-FLUENT.md, V-D2).
    /// </summary>
    public static bool UseMica { get; set; } = true;

    /// <summary>
    /// Mica só a partir do Windows 11 22H2 (build 22621), com "Efeitos de transparência" ligado e fora do Alto Contraste. Nos demais casos
    /// a janela fica com a cor sólida do tema, que já é a mesma da moldura.
    /// </summary>
    public static bool MicaAvailable()
    {
        if (!UseMica || HighContrast || Environment.OSVersion.Version.Build < 22621) return false;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }

    // Janelas sem moldura do sistema (mini-widget, overlay) desenham o próprio fundo
    private static bool CanHaveBackdrop(Window w) => !w.AllowsTransparency && w.WindowStyle != WindowStyle.None;

    /// <summary>Barra de título no tom do tema e, quando possível, fundo Mica na janela inteira.</summary>
    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE é 20 a partir do Windows 10 20H1; nas versões 1809–1909 era 19 (B44)
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
        int def = unchecked((int)0xFFFFFFFF); // DWMWA_COLOR_DEFAULT

        if (CanHaveBackdrop(w) && MicaAvailable())
        {
            // A moldura se estende sobre a janela toda e o WPF desenha com fundo transparente: o Mica aparece atrás do conteúdo
            DwmSetWindowAttribute(hwnd, 35, ref def, 4);
            DwmSetWindowAttribute(hwnd, 36, ref def, 4);
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            int mica = 2; // DWMSBT_MAINWINDOW
            DwmSetWindowAttribute(hwnd, 38, ref mica, 4); // DWMWA_SYSTEMBACKDROP_TYPE
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            w.Background = Brushes.Transparent;
            return;
        }

        if (CanHaveBackdrop(w) && w.Background == Brushes.Transparent)
        {
            // Saiu do Mica (Alto Contraste ou transparência desligada): volta ao fundo sólido do tema
            int none = 1; // DWMSBT_NONE
            DwmSetWindowAttribute(hwnd, 38, ref none, 4);
            var zero = new Margins();
            DwmExtendFrameIntoClientArea(hwnd, ref zero);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Get("Surface");
            w.SetResourceReference(Control.BackgroundProperty, "Surface");
        }
        if (HighContrast)
        {
            // Alto Contraste: a barra de título volta às cores do sistema
            DwmSetWindowAttribute(hwnd, 35, ref def, 4);
            DwmSetWindowAttribute(hwnd, 36, ref def, 4);
            return;
        }
        int caption = ToColorRef(Get("Surface"));
        int text = ToColorRef(Get("OnSurface"));
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
        DwmSetWindowAttribute(hwnd, 36, ref text, 4);    // DWMWA_TEXT_COLOR
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
