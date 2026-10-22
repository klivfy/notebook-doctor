namespace NotebookDoctor.Tests;

public class ProcessKnowledgeTests : IDisposable
{
    public void Dispose() => ProcessTools.SetUserProtected(Array.Empty<string>());

    [Fact]
    public void Programa_conhecido_tem_descricao_e_seguranca()
    {
        var a = ProcessKnowledge.Describe("Chrome");
        Assert.Equal(Safety.Safe, a.Safety);
        Assert.Equal("Sim, pode fechar", a.SafetyText);
    }

    [Fact]
    public void Componente_do_Windows_nunca_e_encerrado() =>
        Assert.Equal(Safety.Never, ProcessKnowledge.Describe("svchost").Safety);

    [Fact]
    public void Programa_desconhecido_pede_pesquisa() =>
        Assert.Equal(Safety.Unknown, ProcessKnowledge.Describe("xyz-desconhecido").Safety);

    [Fact]
    public void Protegido_pelo_usuario_vira_nao_encerrar()
    {
        ProcessTools.SetUserProtected(["spotify"]);
        var a = ProcessKnowledge.Describe("spotify");
        Assert.Equal(Safety.Never, a.Safety);
        Assert.Contains("Protegido por você", a.Description);
    }

    [Theory]
    [InlineData("svchost", true)]
    [InlineData("SVCHOST", true)]
    [InlineData("NotebookDoctor", true)]
    [InlineData("chrome", false)]
    public void Lista_de_protegidos_ignora_maiusculas(string name, bool expected) =>
        Assert.Equal(expected, ProcessTools.IsProtected(name));

    [Fact]
    public void Lista_do_usuario_soma_a_do_sistema()
    {
        ProcessTools.SetUserProtected(["Teams"]);
        Assert.True(ProcessTools.IsProtected("teams"));
        Assert.True(ProcessTools.IsUserProtected("teams"));
        Assert.False(ProcessTools.IsUserProtected("svchost"));
        Assert.True(ProcessTools.IsProtected("svchost"));
    }
}
