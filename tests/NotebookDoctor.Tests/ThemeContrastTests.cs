using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>
/// Redesign V1: confere, direto dos arquivos de tema, os contrastes que o plano promete (WCAG 2): texto 4,5:1 e contornos, gráficos e
/// indicadores 3:1, nos temas claro e escuro.
/// </summary>
public class ThemeContrastTests
{
    private static Dictionary<string, string> Theme(string name)
    {
        // Leva 11.3: o "preto puro" (OLED) é o escuro com os fundos trocados; textos e estados precisam do mesmo contraste
        if (name == "Oled")
        {
            var dark = Theme("Dark");
            foreach (var (key, hex) in NotebookDoctor.Services.OledTheme.Overrides) dark[key] = hex;
            return dark;
        }
        string dir = RepoPaths.App;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(dir, "Themes", name + ".xaml")).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Attribute("Color")!.Value);
    }

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // só cores opacas entram nas contas
        double Channel(int i)
        {
            double c = int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static TheoryData<string, string, string, double> Pairs()
    {
        var data = new TheoryData<string, string, string, double>();
        foreach (var theme in new[] { "Light", "Dark", "Oled" })
        {
            void Add(string fg, string bg, double min) => data.Add(theme, fg, bg, min);
            foreach (var bg in new[] { "WindowBackdrop", "LayerFill", "CardFill" })
            {
                Add("TextPrimary", bg, 4.5);
                Add("TextSecondary", bg, 4.5);
                Add("AccentText", bg, 4.5);
            }
            Add("OnAccent", "Accent", 4.5);
            Add("OnAccentContainer", "AccentContainer", 4.5);
            Add("OnSecondaryContainer", "SecondaryContainer", 4.5);
            Add("TextPrimary", "ControlFill", 4.5);
            Add("TextPrimary", "SurfaceVariant", 4.5);
            foreach (var state in new[] { "Critical", "Caution", "Success" })
            {
                Add("On" + state, state, 4.5);
                Add(state, "CardFill", 4.5);
                Add(state, state + "Container", 4.5);
                Add("TextPrimary", state + "Container", 4.5);
            }
            Add("TextPrimary", "InfoContainer", 4.5);
            Add("Outline", "CardFill", 3);
            // U14: o sublinhado do campo é o que o identifica (a borda fica abaixo de 3:1), sobre o fundo do próprio campo
            Add("Outline", "ControlFill", 3);
            Add("Outline", "ControlFillHover", 3);
            Add("Accent", "CardFill", 3);
            Add("FocusStroke", "WindowBackdrop", 3);
            Add("InverseOnSurface", "InverseSurface", 4.5);
            Add("InversePrimary", "InverseSurface", 4.5);
            foreach (var series in new[] { "ChartCpu", "ChartRam", "ChartDisk", "LevelLow", "LevelMid", "LevelHigh", "LevelCrit" })
                Add(series, "CardFill", 3);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Contraste_minimo(string theme, string fg, string bg, double min)
    {
        var t = Theme(theme);
        double r = Ratio(t[fg], t[bg]);
        Assert.True(r >= min, $"{theme}: {fg} {t[fg]} sobre {bg} {t[bg]} = {r:0.00}:1 (mínimo {min}:1)");
    }

    [Fact]
    public void Preto_puro_so_troca_fundos_que_existem_no_tema_escuro()
    {
        var dark = Theme("Dark");
        Assert.All(NotebookDoctor.Services.OledTheme.Overrides.Keys, k => Assert.True(dark.ContainsKey(k), k));
        Assert.Equal("#000000", NotebookDoctor.Services.OledTheme.Overrides["WindowBackdrop"]);
    }

    [Fact]
    public void Os_dois_temas_tem_as_mesmas_chaves()
    {
        var light = Theme("Light").Keys.Order().ToList();
        var dark = Theme("Dark").Keys.Order().ToList();
        Assert.Equal(light, dark);
    }

    [Fact]
    public void Graficos_nao_usam_a_cor_de_acao()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var t = Theme(theme);
            foreach (var series in new[] { "ChartCpu", "ChartRam", "ChartDisk" })
                Assert.NotEqual(t["Accent"], t[series]);
        }
    }
}

/// <summary>
/// U9: nas telas, margens e preenchimentos ficam na grade de 4 do DESIGN.md. O miolo dos controles (Themes) segue as
/// medidas do WinUI e fica de fora; 1 e valores negativos são ajustes de traço e de borda.
/// </summary>
public class SpacingTests
{
    [Fact]
    public void Margens_das_telas_ficam_na_grade_de_4()
    {
        var dir = new DirectoryInfo(RepoPaths.App);
        var off = new List<string>();
        foreach (var file in dir.EnumerateFiles("*.xaml"))
        {
            string xaml = File.ReadAllText(file.FullName);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xaml,
                         @"(?:\b(?:Margin|Padding)|Property=""(?:Margin|Padding)""\s+Value)=""([-0-9.,\s]+)"""))
                foreach (var v in m.Groups[1].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
                    if (double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) is var n && n > 1 && n % 4 != 0)
                        off.Add($"{file.Name}: {m.Value}");
        }
        Assert.True(off.Count == 0, "Fora da grade de 4:\n" + string.Join("\n", off.Distinct()));
    }

    /// <summary>
    /// Os componentes (cartão, cartão-linha, ladrilho, título de grupo, navegação) também ficam na grade de 4. Valores de 0 a 2 são
    /// meio passo (respiro entre itens, como o CardGap compacto). O miolo dos controles em Controls.xaml segue as medidas do WinUI.
    /// </summary>
    [Fact]
    public void Componentes_ficam_na_grade_de_4()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoPaths.App, "Themes", "Components.xaml"));
        var off = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xaml,
                     @"(?:\b(?:Margin|Padding)|Property=""(?:Margin|Padding)""\s+Value)=""([-0-9.,\s]+)"""))
            foreach (var v in m.Groups[1].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
                if (double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) is var n && n > 2 && n % 4 != 0)
                    off.Add(m.Value);
        Assert.True(off.Count == 0, "Fora da grade de 4 em Components.xaml:\n" + string.Join("\n", off.Distinct()));
    }

}
