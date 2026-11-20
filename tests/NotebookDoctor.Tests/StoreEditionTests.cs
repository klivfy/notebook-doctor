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
        Assert.Equal(!Edition.IsStore, Edition.RegistryCleanup);
        Assert.Equal(!Edition.IsStore, Edition.CommunityRules);
    }

    /// <summary>
    /// A CI não compila a edição da Store: este teste confere que as duas funções que a certificação pode reprovar
    /// (apagar do registro e regras de limpeza vindas de arquivo externo) ficam atrás de <c>Edition</c> no código.
    /// </summary>
    [Fact]
    public void Registro_e_regras_externas_ficam_atras_da_edicao()
    {
        string app = RepoPaths.App;
        string main = File.ReadAllText(Path.Combine(app, "MainWindow.xaml.cs"));
        Assert.Contains("!Edition.RegistryCleanup && RegistryCheckBtn", main);
        Assert.Contains("x:Name=\"RegistryCheckBtn\"", File.ReadAllText(Path.Combine(app, "MainWindow.xaml")));

        string advanced = File.ReadAllText(Path.Combine(app, "AdvancedCleanWindow.xaml.cs"));
        Assert.Contains("!Edition.CommunityRules", advanced);
        Assert.Contains("Edition.CommunityRules && File.Exists(CommunityFile)", advanced);
        Assert.Contains("x:Name=\"RulesImportBtn\"", File.ReadAllText(Path.Combine(app, "AdvancedCleanWindow.xaml")));
    }

    /// <summary>O manifesto da Store declara o Windows 11 24H2 como a versão testada (a de 21H2 estava velha).</summary>
    [Fact]
    public void Manifesto_declara_versao_testada_recente()
    {
        string manifest = File.ReadAllText(Path.Combine(RepoPaths.Root, "packaging", "store", "AppxManifest.xml"));
        Assert.Contains("MaxVersionTested=\"10.0.26100.0\"", manifest);
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

