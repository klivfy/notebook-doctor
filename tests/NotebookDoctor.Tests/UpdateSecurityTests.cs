using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>A50/S2: o manifesto de atualização e os endereços da Klivfy.</summary>
public class UpdateManifestTests
{
    private static readonly Version Local = new(1, 1, 0);
    private const string Hash = "0A62E23026375B867B4334597D5F543B915C6613C47539554C7F1C1EC34124D0";

    private static string Json(string version = "1.2.0", string? url = null, string? sha = null, string notes = "Novidades em pt-BR, en e es") =>
        $$$"""{"version":"{{{version}}}","url":"{{{url ?? "https://klivfy.com/notebookdoctor/downloads/NotebookDoctor-Setup-1.2.0.exe"}}}","sha256":"{{{sha ?? Hash}}}","notes":"{{{notes}}}"}""";

    [Fact]
    public void Manifesto_do_formato_publicado_e_lido_sem_excecao()
    {
        var (result, info) = UpdateManifest.Evaluate(Json(), Local);
        Assert.Equal(UpdateCheck.Available, result);
        Assert.Equal("1.2.0", info!.Version);
        Assert.Equal(Hash, info.Sha256);
        Assert.Equal("https://klivfy.com/notebookdoctor/downloads/NotebookDoctor-Setup-1.2.0.exe", info.Url);
    }

    [Fact]
    public void Versao_igual_ou_menor_nao_oferece()
    {
        Assert.Equal(UpdateCheck.UpToDate, UpdateManifest.Evaluate(Json("1.1.0"), Local).Result);
        Assert.Equal(UpdateCheck.UpToDate, UpdateManifest.Evaluate(Json("1.0.9"), Local).Result);
    }

    [Fact]
    public void Nomes_dos_campos_sem_diferenca_de_maiusculas_e_campos_extras_sao_aceitos()
    {
        string json = $$$"""{"Version":"2.0.0","URL":"https://klivfy.com/a.exe","Sha256":"{{{Hash}}}","Notes":"x","extra":{"a":1}}""";
        Assert.Equal(UpdateCheck.Available, UpdateManifest.Evaluate(json, Local).Result);
    }

    [Fact]
    public void Sem_notas_o_texto_fica_vazio_e_nao_nulo()
    {
        string json = $$$"""{"version":"1.2.0","url":"https://klivfy.com/a.exe","sha256":"{{{Hash}}}"}""";
        var (result, info) = UpdateManifest.Evaluate(json, Local);
        Assert.Equal(UpdateCheck.Available, result);
        Assert.Equal("", info!.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("não é json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":"1.2.0"}""")]
    public void Manifesto_vazio_quebrado_ou_incompleto_vira_nenhuma_atualizacao(string? json)
    {
        var (result, info) = UpdateManifest.Evaluate(json, Local);
        Assert.Equal(UpdateCheck.NotPublished, result);
        Assert.Null(info);
    }

    [Theory]
    [InlineData("http://klivfy.com/a.exe")]
    [InlineData("https://klivfy.com.exemplo.net/a.exe")]
    [InlineData("https://outraklivfy.com/a.exe")]
    [InlineData("https://exemplo.net/klivfy.com/a.exe")]
    [InlineData("https://klivfy.com@exemplo.net/a.exe")]
    [InlineData("https://usuario:senha@klivfy.com/a.exe")]
    [InlineData("ftp://klivfy.com/a.exe")]
    [InlineData("file:///C:/a.exe")]
    [InlineData("\\\\servidor\\pasta\\a.exe")]
    [InlineData("a.exe")]
    public void Endereco_que_nao_e_https_da_Klivfy_e_recusado(string url) =>
        Assert.Equal(UpdateCheck.NotPublished, UpdateManifest.Evaluate(Json(url: url.Replace("\\", "\\\\")), Local).Result);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0A62E23026375B867B4334597D5F543B915C6613C47539554C7F1C1EC34124D")] // 63
    [InlineData("0A62E23026375B867B4334597D5F543B915C6613C47539554C7F1C1EC34124D00")] // 65
    [InlineData("ZA62E23026375B867B4334597D5F543B915C6613C47539554C7F1C1EC34124D0")] // não hexadecimal
    public void Hash_que_nao_e_sha256_e_recusado(string sha) =>
        Assert.Equal(UpdateCheck.NotPublished, UpdateManifest.Evaluate(Json(sha: sha), Local).Result);

    [Fact]
    public void Hash_em_minusculas_e_aceito() =>
        Assert.Equal(UpdateCheck.Available, UpdateManifest.Evaluate(Json(sha: Hash.ToLowerInvariant()), Local).Result);

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("<VERSAO>")]
    public void Versao_invalida_e_recusada(string version) =>
        Assert.Equal(UpdateCheck.NotPublished, UpdateManifest.Evaluate(Json(version), Local).Result);
}

public class KlivfyUrlsTests
{
    [Theory]
    [InlineData(KlivfyUrls.Support)]
    [InlineData(KlivfyUrls.Terms)]
    [InlineData(KlivfyUrls.Privacy)]
    [InlineData(KlivfyUrls.Manifest)]
    [InlineData("https://downloads.klivfy.com/a.exe")]
    [InlineData("HTTPS://KLIVFY.COM/Termos")]
    public void Enderecos_oficiais_sao_confiaveis(string url) => Assert.True(KlivfyUrls.IsTrusted(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("klivfy.com/termos")]
    [InlineData("http://klivfy.com/termos")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("mailto:suporte@klivfy.com")]
    [InlineData("https://klivfy.com.exemplo.net/termos")]
    [InlineData("https://klivfy.com@exemplo.net/termos")]
    public void Endereco_inseguro_ou_de_outro_dominio_nao_e_confiavel(string? url) => Assert.False(KlivfyUrls.IsTrusted(url));

    [Fact]
    public void Termos_e_privacidade_ficam_no_site_da_Klivfy()
    {
        Assert.Equal("https://klivfy.com/termos", KlivfyUrls.Terms);
        Assert.Equal("https://klivfy.com/privacidade", KlivfyUrls.Privacy);
    }
}

/// <summary>S2: chave fixada.</summary>
public class PinnedKeysTests
{
    private static (string, string)[] Klivfy => [("CN", "Klivfy"), ("O", "Klivfy")];

    [Fact]
    public void Cada_chave_fixada_e_um_sha256_em_hexadecimal() =>
        Assert.All(PublisherSignature.PinnedKeys, k => Assert.True(UpdateManifest.IsSha256(k), $"chave fixada inválida: {k}"));

    [Fact]
    public void Nome_certo_com_chave_de_outro_certificado_e_recusado()
    {
        var pinned = new[] { "AABBCCDD" };
        Assert.False(PublisherSignature.Trusted(Klivfy, "11223344", pinned));
        Assert.False(PublisherSignature.Trusted(Klivfy, "", pinned));
    }

    [Fact]
    public void Chave_certa_com_nome_errado_e_recusada()
    {
        var pinned = new[] { "AABBCCDD" };
        Assert.False(PublisherSignature.Trusted([("CN", "Klivfy Games"), ("O", "Klivfy Games")], "AABBCCDD", pinned));
        Assert.False(PublisherSignature.Trusted([("CN", "Klivfy"), ("O", "Outra")], "AABBCCDD", pinned));
    }

    [Fact]
    public void Qualquer_das_chaves_fixadas_serve_para_trocar_o_certificado_sem_quebrar_a_atualizacao()
    {
        var pinned = new[] { "AABBCCDD", "EEFF0011" };
        Assert.True(PublisherSignature.Trusted(Klivfy, "eeff0011", pinned));
        Assert.True(PublisherSignature.Trusted(Klivfy, "AABBCCDD", pinned));
    }
}
