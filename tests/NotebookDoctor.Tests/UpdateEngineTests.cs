using System.Runtime.InteropServices;

namespace NotebookDoctor.Tests;

/// <summary>Prioridade 5 (D2/C14): o motor COM é o principal e a linha de comando assume sozinha quando ele falha.</summary>
public class UpdateEngineTests
{
    private sealed class FakeEngine(string name, Func<Task<UpdateScan>>? scan = null, Func<Task<UpdateDetails?>>? show = null) : IUpdateEngine
    {
        public string Name => name;
        public int Calls;
        public bool? LastTerms;
        public Task<UpdateScan> ListUpgradesAsync(bool terms, CancellationToken ct) { Calls++; LastTerms = terms; return scan!(); }
        public Task<UpdateDetails?> ShowAsync(string id, bool terms, CancellationToken ct) { Calls++; LastTerms = terms; return show!(); }
    }

    private static UpdateScan Scan(string engine, params string[] ids) =>
        new(new WingetList(ids.Select(i => new UpdateCandidate(i, i, "1.0", "2.0", "winget")).ToList(), 0), engine);

    [Fact]
    public async Task Usa_o_motor_principal_quando_ele_responde()
    {
        var com = new FakeEngine("COM", scan: () => Task.FromResult(Scan("COM", "A.B")));
        var cli = new FakeEngine("CLI", scan: () => Task.FromResult(Scan("CLI", "X.Y")));
        var engine = new FailoverUpdateEngine(com, cli);
        var result = await engine.ListUpgradesAsync(true, CancellationToken.None);
        Assert.Equal("COM", result.Engine);
        Assert.Equal(0, cli.Calls);
        Assert.True(engine.UsingPrimary);
        Assert.True(com.LastTerms); // o aceite que a pessoa deu chega ao motor, tal como foi dado
    }

    [Theory]
    [InlineData(typeof(COMException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(UpdateEngineUnavailableException))]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(InvalidCastException))]
    public async Task Passa_para_a_linha_de_comando_quando_o_COM_falha_e_nao_insiste(Type error)
    {
        var com = new FakeEngine("COM", scan: () => Task.FromException<UpdateScan>(
            error == typeof(UpdateEngineUnavailableException) ? new UpdateEngineUnavailableException("sem COM") : (Exception)Activator.CreateInstance(error)!));
        var cli = new FakeEngine("CLI", scan: () => Task.FromResult(Scan("CLI", "X.Y")));
        var engine = new FailoverUpdateEngine(com, cli);

        var first = await engine.ListUpgradesAsync(false, CancellationToken.None);
        Assert.Equal("CLI", first.Engine);
        Assert.False(engine.UsingPrimary);
        Assert.NotNull(engine.LastPrimaryError);

        await engine.ListUpgradesAsync(false, CancellationToken.None);
        Assert.Equal(1, com.Calls); // depois de uma falha o COM fica de lado: nada de pagar a ativação a cada consulta
        Assert.Equal(2, cli.Calls);
        Assert.False(cli.LastTerms);
    }

    [Fact]
    public async Task Sem_motor_principal_a_reserva_responde()
    {
        var cli = new FakeEngine("CLI", show: () => Task.FromResult<UpdateDetails?>(new UpdateDetails("Termos", "CLI")));
        var engine = new FailoverUpdateEngine(null, cli);
        Assert.Equal("CLI", engine.Name);
        Assert.Equal("Termos", (await engine.ShowAsync("A.B", true, CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task Cancelamento_nao_vira_falha_do_COM()
    {
        using var cts = new CancellationTokenSource();
        var com = new FakeEngine("COM", scan: () => { cts.Cancel(); return Task.FromCanceled<UpdateScan>(cts.Token); });
        var cli = new FakeEngine("CLI", scan: () => Task.FromResult(Scan("CLI")));
        var engine = new FailoverUpdateEngine(com, cli);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ListUpgradesAsync(true, cts.Token));
        Assert.True(engine.UsingPrimary);
        Assert.Equal(0, cli.Calls);
    }

    [Fact]
    public async Task Falha_na_reserva_tambem_sobe()
    {
        var com = new FakeEngine("COM", scan: () => Task.FromException<UpdateScan>(new COMException()));
        var cli = new FakeEngine("CLI", scan: () => Task.FromException<UpdateScan>(new UpdateEngineUnavailableException("sem winget")));
        await Assert.ThrowsAsync<UpdateEngineUnavailableException>(() => new FailoverUpdateEngine(com, cli).ListUpgradesAsync(true, CancellationToken.None));
    }

    /// <summary>Os termos continuam sendo da pessoa: nenhum caminho do código passa o aceite sozinho.</summary>
    [Fact]
    public void Nenhum_motor_aceita_termos_por_conta_propria()
    {
        string root = RepoPaths.Root;
        foreach (var file in new[] { "src/NotebookDoctor.WinGetCom/ComUpdateEngine.cs", "src/NotebookDoctor.Windows/UpdateEngine.cs" })
        {
            string code = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("AcceptPackageAgreements = true", code);
            Assert.DoesNotContain("AcceptSourceAgreements = true", code);
            Assert.DoesNotContain("--accept-", code);
        }
        Assert.Contains("AcceptSourceAgreements = sourceTermsAccepted", File.ReadAllText(Path.Combine(root, "src/NotebookDoctor.WinGetCom/ComUpdateEngine.cs")));
    }
}
