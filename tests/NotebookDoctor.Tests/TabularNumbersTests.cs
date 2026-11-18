using System.Text.RegularExpressions;

namespace NotebookDoctor.Tests;

/// <summary>
/// Valores que mudam a cada atualização (uso de CPU, memória, nota) e colunas de números usam algarismos de largura fixa. Com algarismos
/// proporcionais o "1" é mais estreito que o "0" e o valor "treme" de lado a cada leitura. O XAML não avisa quando alguém esquece isto;
/// este teste confere as telas.
/// </summary>
public class TabularNumbersTests
{
    private static string Xaml(string name) => File.ReadAllText(Path.Combine(RepoPaths.App, name));

    private static string Element(string xaml, string tag, string name)
    {
        var m = Regex.Match(xaml, $@"<{tag}\b[^>]*x:Name=""{name}""[^>]*>");
        Assert.True(m.Success, $"{tag} {name} não encontrado");
        return m.Value;
    }

    [Theory]
    [InlineData("MainWindow.xaml", "ScoreText")]
    [InlineData("MainWindow.xaml", "CpuText")]
    [InlineData("MainWindow.xaml", "RamText")]
    [InlineData("MainWindow.xaml", "DiskText")]
    [InlineData("MiniWidget.xaml", "ScoreText")]
    [InlineData("MiniWidget.xaml", "CpuText")]
    [InlineData("MiniWidget.xaml", "RamText")]
    [InlineData("MiniWidget.xaml", "DiskText")]
    public void Valores_ao_vivo_usam_algarismos_tabulares(string file, string name) =>
        Assert.Contains("Typography.NumeralAlignment=\"Tabular\"", Element(Xaml(file), "TextBlock", name));

    [Fact]
    public void Estilos_numericos_existem_e_sao_tabulares()
    {
        string components = Xaml(Path.Combine("Themes", "Components.xaml"));
        foreach (string key in new[] { "Numeric", "NumericCell", "Value" })
        {
            var m = Regex.Match(components, $@"<Style x:Key=""{key}""[^>]*>(.*?)</Style>", RegexOptions.Singleline);
            Assert.True(m.Success, $"Estilo {key} não existe");
            // NumericCell e Value herdam de Numeric; os demais declaram o ajuste
            bool inherits = Regex.IsMatch(m.Value, @"BasedOn=""\{StaticResource Numeric\}""");
            Assert.True(inherits || m.Value.Contains("NumeralAlignment"), $"Estilo {key} não usa algarismos tabulares");
        }
    }

    [Theory]
    [InlineData("{Binding Count}")]
    [InlineData("{Binding CpuPct, StringFormat=0.0}")]
    [InlineData("{Binding MemMB, StringFormat=N0}")]
    [InlineData("{Binding IoKBs, StringFormat=N0}")]
    [InlineData("{Binding Conns}")]
    [InlineData("{Binding Files, StringFormat=N0}")]
    public void Colunas_de_numero_usam_NumericCell(string binding)
    {
        var m = Regex.Match(Xaml("MainWindow.xaml"), @"<DataGridTextColumn\b[^>]*Binding=""" + Regex.Escape(binding) + @"""[^>]*>");
        Assert.True(m.Success, $"Coluna {binding} não encontrada");
        Assert.Contains("ElementStyle=\"{StaticResource NumericCell}\"", m.Value);
    }
}
