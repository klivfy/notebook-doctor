using System.Text.RegularExpressions;

namespace NotebookDoctor.Tests;

/// <summary>
/// O mini-widget tinha largura fixa (230) e coluna de valor fixa (48): com o texto do Windows ampliado, "100%" não cabia. Largura mínima e
/// máxima deixam a janela crescer com o texto sem virar uma faixa enorme quando o nome de um programa é longo.
/// </summary>
public class MiniWidgetLayoutTests
{
    private static string Xaml => File.ReadAllText(Path.Combine(RepoPaths.App, "MiniWidget.xaml"));

    [Fact]
    public void Cartao_tem_largura_minima_e_maxima_e_nao_largura_fixa()
    {
        var border = Regex.Match(Xaml, @"<Border\b[^>]*CornerRadius=""\{StaticResource RadiusCard\}""[^>]*>").Value;
        Assert.NotEmpty(border);
        Assert.Matches(@"\bMinWidth=""\d+""", border);
        Assert.Matches(@"\bMaxWidth=""\d+""", border);
        Assert.DoesNotMatch(@"(?<![A-Za-z])Width=""\d+""", border);
    }

    [Fact]
    public void Coluna_do_valor_cresce_com_o_texto()
    {
        var cols = Regex.Match(Xaml, @"<Grid\.ColumnDefinitions>(.*?)</Grid\.ColumnDefinitions>", RegexOptions.Singleline).Groups[1].Value;
        var defs = Regex.Matches(cols, @"<ColumnDefinition\b[^>]*/>").Select(m => m.Value).ToList();
        Assert.Equal(3, defs.Count);
        Assert.Contains("Width=\"Auto\"", defs[2]);
        Assert.Matches(@"MinWidth=""\d+""", defs[2]);
    }

    [Fact]
    public void Barras_nao_somem_quando_o_texto_cresce() =>
        Assert.Matches(@"<ColumnDefinition Width=""\*"" MinWidth=""\d+""/>", Xaml);
}
