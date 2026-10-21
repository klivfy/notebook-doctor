namespace NotebookDoctor.Tests;

public class InsightsTests
{
    [Fact]
    public void Sem_amostras_mostra_analisando()
    {
        var r = Insights.Score(Array.Empty<Snapshot>(), Make.Health(), Make.FreshBoot);
        Assert.Equal(0, r.Score);
        Assert.Equal("Analisando", r.Label);
    }

    [Fact]
    public void Sistema_saudavel_tem_nota_maxima()
    {
        var r = Insights.Score(Make.Minute(Make.Snap()), Make.Health(), Make.FreshBoot);
        Assert.Equal(100, r.Score);
        Assert.Equal("Excelente", r.Label);
    }

    [Theory]
    [InlineData(95, 80)]
    [InlineData(85, 88)]
    [InlineData(72, 95)]
    [InlineData(60, 100)]
    public void RAM_alta_reduz_a_nota(double ram, int expected) =>
        Assert.Equal(expected, Insights.Score(Make.Minute(Make.Snap(ram: ram)), Make.Health(), Make.FreshBoot).Score);

    [Fact]
    public void Ligado_ha_uma_semana_perde_pontos()
    {
        var r = Insights.Score(Make.Minute(Make.Snap()), Make.Health(), TimeSpan.FromDays(8));
        Assert.Equal(95, r.Score);
    }

    [Fact]
    public void Nota_nunca_fica_negativa()
    {
        var snaps = Make.Minute(Make.Snap(cpu: 99, ram: 99, diskBusy: 99, freePct: 2));
        var h = Make.Health(diskHealth: "Unhealthy", wear: 95, battery: 40, startup: 30, tempBytes: 5L << 30);
        var r = Insights.Score(snaps, h, TimeSpan.FromDays(30));
        Assert.Equal(0, r.Score);
        Assert.Equal("Ruim", r.Label);
    }

    [Fact]
    public void Gargalo_de_CPU_cita_o_programa_mas_nao_o_proprio_app()
    {
        var groups = new[] { new ProcGroup("NotebookDoctor", 1, 50, 100), new ProcGroup("chrome", 12, 40, 2000) };
        var b = Insights.Bottleneck(Make.Minute(Make.Snap(cpu: 90, groups: groups)));
        Assert.Equal("Processador", b.Resource);
        Assert.Contains("chrome", b.Explanation);
        Assert.DoesNotContain("NotebookDoctor", b.Explanation);
    }

    [Theory]
    [InlineData(10, 90, 5, 50, "Memória RAM")]
    [InlineData(10, 40, 90, 50, "Disco")]
    [InlineData(10, 40, 5, 5, "Espaço em disco")]
    [InlineData(50, 40, 5, 50, "Processador (leve)")]
    [InlineData(10, 40, 5, 50, "Nenhum")]
    public void Identifica_o_gargalo(double cpu, double ram, double disk, double free, string expected) =>
        Assert.Equal(expected, Insights.Bottleneck(Make.Minute(Make.Snap(cpu, ram, disk, free))).Resource);
}
