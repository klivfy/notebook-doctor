namespace NotebookDoctor.Tests;

public class AlertPolicyTests
{
    private static TimeOnly T(string s) => TimeOnly.Parse(s);

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("03:00", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    [InlineData("22:00", true)]
    public void Faixa_que_atravessa_a_meia_noite(string now, bool quiet) =>
        Assert.Equal(quiet, AlertPolicy.InQuietHours(T(now), T("22:00"), T("07:00")));

    [Theory]
    [InlineData("12:30", true)]
    [InlineData("14:00", false)]
    [InlineData("11:59", false)]
    public void Faixa_no_mesmo_dia(string now, bool quiet) =>
        Assert.Equal(quiet, AlertPolicy.InQuietHours(T(now), T("12:00"), T("14:00")));

    [Fact]
    public void Inicio_igual_ao_fim_desliga() =>
        Assert.False(AlertPolicy.InQuietHours(T("10:00"), T("08:00"), T("08:00")));

    [Theory]
    [InlineData("22:00", "22:00")]
    [InlineData("7:30", "07:30")]
    [InlineData(" 7 ", "07:00")]
    public void Le_horarios(string text, string expected)
    {
        Assert.True(AlertPolicy.TryParseClock(text, out var t));
        Assert.Equal(T(expected), t);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Recusa_horarios_invalidos(string? text) => Assert.False(AlertPolicy.TryParseClock(text, out _));

    [Fact]
    public void Sustentado_so_dispara_depois_do_tempo()
    {
        var s = new Sustained();
        var t = Make.T0;
        Assert.False(s.Update(true, t, 3));
        Assert.False(s.Update(true, t.AddMinutes(2), 3));
        Assert.True(s.Update(true, t.AddMinutes(3), 3));
        Assert.False(s.Update(false, t.AddMinutes(4), 3));
        Assert.False(s.Update(true, t.AddMinutes(5), 3)); // recomeça a contar
    }

    [Fact]
    public void Sustentado_com_zero_minutos_e_imediato() => Assert.True(new Sustained().Update(true, Make.T0, 0));
}

public class HistoryInsightsTests
{
    private static List<HistSample> Series(int days, Func<double, double> freeAtDay, int perDay = 24) =>
        Enumerable.Range(0, days * perDay + 1)
            .Select(i => new HistSample(Make.T0.AddHours(i * 24.0 / perDay), 10, 40, 5, freeAtDay(i / (double)perDay), []))
            .ToList();

    [Fact]
    public void Disco_enchendo_tem_previsao()
    {
        var data = Series(10, d => 30 - d); // perde 1 ponto por dia, termina em 20%
        var f = HistoryInsights.DiskFull(data, data[^1].T);
        Assert.NotNull(f);
        Assert.Equal(1, f.PctPerDay, 2);
        Assert.Equal(20, (f.When - data[^1].T).TotalDays, 1);
    }

    [Fact]
    public void Disco_estavel_nao_tem_previsao() =>
        Assert.Null(HistoryInsights.DiskFull(Series(10, _ => 40), Make.T0.AddDays(10)));

    [Fact]
    public void Previsao_ignora_o_que_veio_antes_da_ultima_limpeza()
    {
        // Enchia rápido até a limpeza no dia 5; depois ficou estável
        var data = Series(12, d => d < 5 ? 30 - 4 * d : 40);
        Assert.Null(HistoryInsights.DiskFull(data, data[^1].T));
    }

    [Fact]
    public void Pouco_historico_nao_preve() =>
        Assert.Null(HistoryInsights.DiskFull(Series(2, d => 30 - 5 * d), Make.T0.AddDays(2)));

    [Fact]
    public void Enchendo_muito_devagar_nao_vale_aviso() =>
        Assert.Null(HistoryInsights.DiskFull(Series(10, d => 80 - 0.1 * d), Make.T0.AddDays(10)));

    [Fact]
    public void Compara_hoje_com_a_semana_passada()
    {
        var now = Make.T0.AddDays(8);
        var data = Enumerable.Range(0, 8 * 24 * 60 / 10)
            .Select(i => Make.T0.AddMinutes(i * 10))
            .Select(t => new HistSample(t, t > now.AddDays(-1) ? 50 : 20, 60, 5, 40, [], 80))
            .ToList();
        var c = HistoryInsights.CompareWithLastWeek(data, now);
        Assert.NotNull(c);
        Assert.Equal(50, c.Today.Cpu, 1);
        Assert.Equal(20, c.LastWeek.Cpu, 1);
        Assert.Equal(80, c.Today.Score);
    }

    [Fact]
    public void Sem_dados_da_semana_passada_nao_compara()
    {
        var now = Make.T0.AddDays(1);
        var data = Enumerable.Range(0, 24 * 6).Select(i => new HistSample(Make.T0.AddMinutes(i * 10), 20, 50, 5, 40, [])).ToList();
        Assert.Null(HistoryInsights.CompareWithLastWeek(data, now));
    }
}
