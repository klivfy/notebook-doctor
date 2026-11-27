using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>
/// Em Alto Contraste verde, laranja e vermelho viram a mesma cor do sistema; o estado do ícone da bandeja tem de ir por outra coisa.
/// Esta regra é a que o ícone consulta para escolher a forma da marca.
/// </summary>
public class TrayHighContrastTests
{
    [Theory]
    [InlineData(TrayBand.Ok, TrayMarker.Ring)]
    [InlineData(TrayBand.Amber, TrayMarker.Dot)]
    [InlineData(TrayBand.Red, TrayMarker.Square)]
    public void Cada_estado_tem_a_sua_forma(TrayBand band, TrayMarker expected) => Assert.Equal(expected, Tray.Marker(band));

    [Fact]
    public void Os_tres_estados_se_distinguem_sem_cor() =>
        Assert.Equal(3, Enum.GetValues<TrayBand>().Select(Tray.Marker).Distinct().Count());

    [Theory]
    [InlineData(10, 20, TrayMarker.Ring)]
    [InlineData(70, 20, TrayMarker.Dot)]
    [InlineData(20, 90, TrayMarker.Square)]
    public void A_forma_acompanha_a_carga_real(double cpu, double ram, TrayMarker expected) =>
        Assert.Equal(expected, Tray.Marker(Tray.Band(cpu, ram)));

    /// <summary>
    /// A CI não fotografa a bandeja nem tem Alto Contraste ligado: a verificação visual desenha o ícone nos esquemas preto e branco
    /// (bandeja.png, com falha se sobrar cor) e fotografa o mini-widget e as abas com o mapeamento de Alto Contraste.
    /// </summary>
    [Fact]
    public void A_verificacao_visual_cobre_bandeja_mini_widget_e_alto_contraste()
    {
        var smoke = File.ReadAllText(Path.Combine(RepoPaths.App, "Smoke.cs"));
        Assert.Contains("new MiniWidget()", smoke);
        Assert.Contains("TraySheet(folder)", smoke);
        Assert.Contains("ThemeManager.SimulateHighContrast = true", smoke);
        Assert.Contains("ThemeManager.SimulateHighContrast = false", smoke);
        Assert.Contains("TrayIcon.Render(", File.ReadAllText(Path.Combine(RepoPaths.App, "Smoke.Bandeja.cs")));
    }

    /// <summary>O desenho de Alto Contraste usa as cores que recebe, para que a folha da CI consiga desenhar os esquemas preto e branco.</summary>
    [Fact]
    public void O_icone_de_alto_contraste_usa_as_cores_recebidas()
    {
        var tray = File.ReadAllText(Path.Combine(RepoPaths.App, "TrayIcon.cs"));
        int start = tray.IndexOf("private static void DrawHighContrast", StringComparison.Ordinal);
        int end = tray.IndexOf("private static Drawing.Drawing2D.GraphicsPath RoundedRect", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.DoesNotContain("SystemColors", tray[start..end]);
    }
}
