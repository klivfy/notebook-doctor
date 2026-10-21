namespace NotebookDoctor.Tests;

public class MemoryTrendTests
{
    private static Snapshot At(int minute, params ProcGroup[] groups) =>
        Make.Snap(groups: groups, time: Make.T0.AddMinutes(minute));

    [Fact]
    public void Crescimento_continuo_e_suspeito()
    {
        var t = new MemoryTrend();
        for (int m = 0; m <= 25; m++) t.Feed(At(m, new ProcGroup("leaky", 1, 1, 500 + m * 20)));
        var s = Assert.Single(t.Suspects);
        Assert.Equal("leaky", s.Name);
        Assert.Equal(500, s.FromMB);
        Assert.Equal(1000, s.ToMB);
        Assert.Equal(25, s.Minutes);
    }

    [Fact]
    public void Programa_que_devolve_memoria_nao_e_suspeito()
    {
        var t = new MemoryTrend();
        for (int m = 0; m <= 25; m++) t.Feed(At(m, new ProcGroup("cache", 1, 1, m % 5 == 4 ? 300 : 500 + m * 20)));
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Pouco_tempo_de_observacao_nao_basta()
    {
        var t = new MemoryTrend();
        for (int m = 0; m <= 10; m++) t.Feed(At(m, new ProcGroup("leaky", 1, 1, 500 + m * 100)));
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Amostras_com_menos_de_um_minuto_sao_ignoradas()
    {
        var t = new MemoryTrend();
        // 26 amostras a cada 30 s = 12,5 min: menos que o mínimo de 20 min
        for (int i = 0; i <= 25; i++) t.Feed(Make.Snap(groups: [new ProcGroup("leaky", 1, 1, 500 + i * 20)], time: Make.T0.AddSeconds(i * 30)));
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Processos_do_sistema_nunca_sao_suspeitos()
    {
        var t = new MemoryTrend();
        for (int m = 0; m <= 25; m++) t.Feed(At(m, new ProcGroup("svchost", 1, 1, 500 + m * 20)));
        Assert.Empty(t.Suspects);
    }
}
