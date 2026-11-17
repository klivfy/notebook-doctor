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
}
