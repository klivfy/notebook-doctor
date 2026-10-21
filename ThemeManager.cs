using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace NotebookDoctor;

/// <summary>Alterna entre tema claro/escuro (Material 3) seguindo o Windows ou a escolha do usuário.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
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
        HighContrast = SimulatedHighContrast is not null || SystemParameters.HighContrast;
        bool dark = mode switch { "dark" => true, "light" => false, _ => SystemPrefersDark() };
        var dicts = Application.Current.Resources.MergedDictionaries;
        // M08: com Alto Contraste do Windows ligado, só as cores do sistema valem (o tema e a paleta ficam de fora)
        var next = HighContrast ? HighContrastTheme()
                                : new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        if (HighContrast) { var w = HcColors().Window; dark = w.R + w.G + w.B < 384; }
        if (_current is not null) dicts.Remove(_current);
        dicts.Insert(0, next); // antes dos estilos, para que o DynamicResource encontre as cores
        _current = next;
        IsDark = dark;
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        Changed?.Invoke();
    }

    public static bool HighContrast { get; private set; }

    /// <summary>
    /// Paleta de Alto Contraste fixa, no lugar de SystemColors. Só o modo --screenshots usa: um runner de CI não tem o tema
    /// de Alto Contraste do Windows de verdade, então isto exercita o mapeamento do app com as cores dos esquemas clássicos.
    /// </summary>
    internal static (Color Window, Color Text, Color Highlight, Color HighlightText)? SimulatedHighContrast { get; set; }

    private static (Color Window, Color Text, Color Highlight, Color HighlightText) HcColors() =>
        SimulatedHighContrast ?? (SystemColors.WindowColor, SystemColors.WindowTextColor, SystemColors.HighlightColor, SystemColors.HighlightTextColor);

    /// <summary>Tokens do app mapeados para as cores do esquema de Alto Contraste escolhido no Windows.</summary>
    private static ResourceDictionary HighContrastTheme()
    {
        var d = new ResourceDictionary();
        void Set(string key, Color c) => d[key] = new SolidColorBrush(c);
        var (win, text, hi, hiText) = HcColors();
        foreach (var k in new[] { "Surface", "SurfaceContainer", "SurfaceVariant", "PrimaryContainer" }) Set(k, win);
        foreach (var k in new[] { "OnSurface", "OnSurfaceVariant", "Outline", "OutlineVariant", "CardBorder", "OnPrimaryContainer",
                                  "ChartCpu", "ChartRam", "ChartDisk" }) Set(k, text);
        Set("StateHover", Color.FromArgb(0x30, hi.R, hi.G, hi.B));
        foreach (var k in new[] { "Primary", "SecondaryContainer", "Error", "Warning", "Success",
                                  "LevelLow", "LevelMid", "LevelHigh", "LevelCrit" }) Set(k, hi);
        foreach (var k in new[] { "OnPrimary", "OnSecondaryContainer", "OnError", "OnWarning", "OnSuccess" }) Set(k, hiText);
        Set("InverseSurface", text); Set("InverseOnSurface", win); Set("InversePrimary", win);
        return d;
    }

    public static Color Get(string key) => (Application.Current.Resources[key] as SolidColorBrush)?.Color ?? Colors.Magenta;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Pinta a barra de título do Windows 11 com as cores do tema.</summary>
    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        if (HighContrast)
        {
            // Alto Contraste: a barra de título volta às cores do sistema
            int def = unchecked((int)0xFFFFFFFF); // DWMWA_COLOR_DEFAULT
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
