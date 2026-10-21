using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

public class UpdaterTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Manifest(string version = "9.9.9", string url = "https://klivfy.com/dl/setup.exe", string sha = Hash) =>
        $$"""{"version":"{{version}}","url":"{{url}}","sha256":"{{sha}}","notes":"n"}""";

    [Theory]
    [InlineData("https://klivfy.com/dl/setup.exe", true)]
    [InlineData("https://downloads.klivfy.com/setup.exe", true)]
    [InlineData("HTTPS://KLIVFY.COM/setup.exe", true)]
    [InlineData("http://klivfy.com/setup.exe", false)]            // sem TLS
    [InlineData("https://evilklivfy.com/setup.exe", false)]       // sufixo sem ponto
    [InlineData("https://klivfy.com.evil.com/setup.exe", false)]  // domínio confiável só como prefixo
    [InlineData("https://evil.com/klivfy.com/setup.exe", false)]  // domínio confiável só no caminho
    [InlineData("https://klivfy.com@evil.com/setup.exe", false)]  // userinfo enganoso
    [InlineData("ftp://klivfy.com/setup.exe", false)]
    [InlineData("setup.exe", false)]
    [InlineData("", false)]
    public void IsTrustedUrl(string url, bool expected) => Assert.Equal(expected, Updater.IsTrustedUrl(url));

    [Fact]
    public void Manifest_NewerVersion_IsAvailable()
    {
        var (result, info) = Updater.ParseManifest(Manifest("9.9.9"), "1.1.0");
        Assert.Equal(UpdateCheck.Available, result);
        Assert.Equal("9.9.9", info!.Version);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.0.0")]
    public void Manifest_SameOrOlderVersion_IsUpToDate(string remote) =>
        Assert.Equal(UpdateCheck.UpToDate, Updater.ParseManifest(Manifest(remote), "1.1.0").Result);

    [Theory]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("""{"version":"9.9.9"}""")]                                                       // sem url e hash
    [InlineData("""{"version":"abc","url":"https://klivfy.com/s.exe","sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}""")]
    public void Manifest_Invalid_IsNotPublished(string json) =>
        Assert.Equal(UpdateCheck.NotPublished, Updater.ParseManifest(json, "1.1.0").Result);

    [Fact]
    public void Manifest_UntrustedHost_IsRejected() =>
        Assert.Equal(UpdateCheck.NotPublished, Updater.ParseManifest(Manifest(url: "https://evil.com/setup.exe"), "1.1.0").Result);

    [Theory]
    [InlineData("curto")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]   // 64 caracteres, mas não hexadecimal
    public void Manifest_BadHash_IsRejected(string sha) =>
        Assert.Equal(UpdateCheck.NotPublished, Updater.ParseManifest(Manifest(sha: sha), "1.1.0").Result);

    [Theory]
    [InlineData("Klivfy Tecnologia e Consultoria em TI LTDA", null, true)]
    [InlineData("KLIVFY TECNOLOGIA E CONSULTORIA EM TI LTDA", null, true)]
    [InlineData("KLIVFY TECNOLOGIA E CONSULTORIA EM TI LTDA:12345678000199", null, true)]   // ICP-Brasil
    [InlineData(null, "Klivfy Tecnologia e Consultoria em TI LTDA", true)]
    [InlineData("Klivfy", null, false)]                                                      // nome parcial
    [InlineData("Klivfy Tecnologia e Consultoria em TI LTDA Evil", null, false)]
    [InlineData("NotKlivfy Tecnologia e Consultoria em TI LTDA", null, false)]
    [InlineData("Evil Corp (Klivfy Tecnologia e Consultoria em TI LTDA)", null, false)]
    [InlineData("Klivfy Tecnologia e Consultoria em TI LTDA:abc", null, false)]              // sufixo que não é CNPJ
    [InlineData(null, null, false)]
    public void IsExpectedPublisher(string? cn, string? org, bool expected) =>
        Assert.Equal(expected, Updater.IsExpectedPublisher(cn, org));

    [Theory]
    [InlineData("CN=Foo, O=Klivfy Tecnologia e Consultoria em TI LTDA, C=BR", "Klivfy Tecnologia e Consultoria em TI LTDA")]
    [InlineData("CN=Foo, O=\"Klivfy Tecnologia e Consultoria em TI LTDA\", C=BR", "Klivfy Tecnologia e Consultoria em TI LTDA")]
    [InlineData("CN=Foo, C=BR", null)]
    public void ReadOrganization(string subject, string? expected) =>
        Assert.Equal(expected, Authenticode.ReadOrganization(subject));

    [Fact]
    public void UnsignedFile_IsNotSignedByPublisher()
    {
        string file = Path.Combine(Path.GetTempPath(), $"nd-test-{Guid.NewGuid():N}.exe");
        File.WriteAllText(file, "MZ");
        try { Assert.False(Updater.IsSignedByPublisher(file)); }
        finally { File.Delete(file); }
    }
}
