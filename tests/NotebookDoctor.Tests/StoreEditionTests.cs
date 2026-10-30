using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public sealed class StoreEditionTests
{
    [Fact]
    public void Edition_propriedades_sao_coerentes_com_a_edicao()
    {
        Assert.Equal(!Edition.IsStore, Edition.SelfUpdate);
        Assert.Equal(!Edition.IsStore, Edition.ThirdPartyInstalls);
    }

    [Fact]
    public void Traducao_da_edicao_store_existe_em_todos_os_idiomas()
    {
        const string chave = "Na edição da Microsoft Store, as atualizações são automáticas pela própria loja.";

        Loc.Use("pt");
        Assert.Equal(chave, Loc.T(chave));

        Loc.Use("en");
        string en = Loc.T(chave);
        Assert.NotEqual(chave, en);
        Assert.Contains("Microsoft Store", en);

        Loc.Use("es");
        string es = Loc.T(chave);
        Assert.NotEqual(chave, es);
        Assert.Contains("Microsoft Store", es);

        Loc.Use("pt");
    }
}

