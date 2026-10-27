namespace NotebookDoctor.Services;

/// <summary>
/// Leva 11.3, tarefa 13: variante "preto puro" do tema escuro para telas OLED, onde o pixel preto fica apagado e não gasta
/// energia. Só troca os fundos; os textos e os estados continuam os do Dark.xaml, e o ThemeContrastTests confere o
/// contraste deles sobre estes fundos.
/// </summary>
public static class OledTheme
{
    public static readonly IReadOnlyDictionary<string, string> Overrides = new Dictionary<string, string>
    {
        ["WindowBackdrop"] = "#000000",
        ["LayerFill"] = "#000000",
        ["CardFill"] = "#0E0E0E",
        ["SurfaceVariant"] = "#1A1A1A",
        ["ControlFill"] = "#1C1C1C",
        ["ControlFillHover"] = "#262626",
    };
}
