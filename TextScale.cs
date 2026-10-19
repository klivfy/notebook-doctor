using System.Windows;
using Microsoft.Win32;

namespace NotebookDoctor;

/// <summary>
/// Segue Configurações › Acessibilidade › Tamanho do texto (100% a 225%).
/// Os estilos usam os tamanhos FsLabel, FsBody… via DynamicResource; aqui eles são multiplicados pelo fator do Windows.
/// </summary>
public static class TextScale
{
    private static readonly (string Key, double Px)[] Sizes =
    [
        ("FsLabel", 13), ("FsBody", 14), ("FsTitleMedium", 16), ("FsTitle", 20),
        ("FsTitleLarge", 22), ("FsDisplay", 40), ("FsHero", 56),
    ];

    public static double Factor { get; private set; } = 1;

    public static double ReadFactor()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            if (k?.GetValue("TextScaleFactor") is int v && v is >= 100 and <= 225) return v / 100.0;
        }
        catch { }
        return 1;
    }

    /// <summary>Aplica o fator atual. Retorna true se mudou (para reajustar o layout).</summary>
    public static bool Apply()
    {
        double f = ReadFactor();
        bool changed = Math.Abs(f - Factor) > 0.001;
        Factor = f;
        var res = Application.Current.Resources;
        foreach (var (key, px) in Sizes) res[key] = Math.Round(px * f, 1);
        return changed;
    }
}
