namespace NotebookDoctor.Tests;

public class PtTests
{
    [Theory]
    [InlineData(1, "1 processo")]
    [InlineData(0, "0 processos")]
    [InlineData(1304, "1.304 processos")]
    public void Plural_e_milhar_em_portugues(long n, string expected) =>
        Assert.Equal(expected, Pt.N(n, "processo", "processos"));

    [Theory]
    [InlineData(512L * 1024, "512 KB")]
    [InlineData(120L << 20, "120 MB")]
    [InlineData(3L << 29, "1,5 GB")]
    public void Tamanhos_em_KB_MB_GB(long bytes, string expected) =>
        Assert.Equal(expected, Pt.Bytes(bytes));
}

public class ProfileTests
{
    private static readonly PowerPlan[] Plans =
    [
        new("1", "Equilibrado", true),
        new("2", "Economia de energia", false),
        new("3", "Alto desempenho", false),
    ];

    [Theory]
    [InlineData("work", "1")]
    [InlineData("battery", "2")]
    [InlineData("performance", "3")]
    public void Cada_perfil_encontra_seu_plano(string id, string guid) =>
        Assert.Equal(guid, Profile.All.Single(p => p.Id == id).FindPlan(Plans)?.Guid);

    [Fact]
    public void Reconhece_nomes_em_ingles()
    {
        Assert.True(Profile.Matches("balanced", "Balanced"));
        Assert.True(Profile.Matches("high", "High performance"));
        Assert.False(Profile.Matches("saver", "Balanced"));
    }

    [Fact]
    public void Sem_o_plano_retorna_nulo() =>
        Assert.Null(Profile.All.Single(p => p.Id == "performance").FindPlan([new PowerPlan("1", "Equilibrado", true)]));
}

public class PowerAdviceTests
{
    private static PowerPlan[] Plans(string active) =>
    [
        new("1", "Equilibrado", active == "1"),
        new("2", "Economia de energia", active == "2"),
        new("3", "Alto desempenho", active == "3"),
    ];

    [Fact]
    public void Na_bateria_com_alto_desempenho_sugere_equilibrado()
    {
        var s = PowerAdvice.Suggest(onAc: false, Plans("3"));
        Assert.NotNull(s);
        Assert.Equal("1", s.Plan.Guid);
    }

    [Fact]
    public void Na_tomada_com_economia_sugere_alto_desempenho()
    {
        var s = PowerAdvice.Suggest(onAc: true, Plans("2"));
        Assert.Equal("3", s?.Plan.Guid);
    }

    [Fact]
    public void Na_tomada_sem_alto_desempenho_sugere_equilibrado() =>
        Assert.Equal("1", PowerAdvice.Suggest(true, [new("1", "Equilibrado", false), new("2", "Economia de energia", true)])?.Plan.Guid);

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "1")]
    [InlineData(true, "3")]
    [InlineData(false, "2")]
    public void Plano_adequado_nao_gera_sugestao(bool onAc, string active) =>
        Assert.Null(PowerAdvice.Suggest(onAc, Plans(active)));

    [Fact]
    public void Sem_plano_ativo_conhecido_nao_sugere() =>
        Assert.Null(PowerAdvice.Suggest(false, [new("3", "Alto desempenho", false)]));
}
