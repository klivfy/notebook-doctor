using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>Manifesto publicado pela Klivfy: {"version":"1.2.0","url":"https://…/setup.exe","sha256":"…","notes":"…"}.</summary>
public sealed record UpdateInfo(string Version, string Url, string Sha256, string Notes);

public enum UpdateCheck { UpToDate, Available, NotPublished, Offline }

/// <summary>
/// A50: atualização assinada. Só HTTPS no domínio klivfy.com; o instalador só roda se o SHA-256 bater com o manifesto,
/// a assinatura Authenticode for válida (WinVerifyTrust) e o certificado for da Klivfy. Nada é instalado sem confirmação.
/// </summary>
public static class Updater
{
    public const string ManifestUrl = "https://klivfy.com/notebookdoctor/versao.json";
    private const string TrustedHost = "klivfy.com";
    /// <summary>Razão social exata do certificado de assinatura. Certificados ICP-Brasil costumam trazer "RAZÃO SOCIAL:CNPJ" no CN.</summary>
    internal const string PublisherName = "Klivfy Tecnologia e Consultoria em TI LTDA";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<(UpdateCheck Result, UpdateInfo? Info)> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ManifestUrl);
            if (!resp.IsSuccessStatusCode) return (UpdateCheck.NotPublished, null);
            return ParseManifest(await resp.Content.ReadAsStringAsync(), AppInfo.Version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (UpdateCheck.Offline, null); }
    }

    /// <summary>Interpreta o manifesto e compara com a versão local. Manifesto inválido ou com URL/hash suspeitos conta como não publicado.</summary>
    internal static (UpdateCheck Result, UpdateInfo? Info) ParseManifest(string json, string localVersion)
    {
        UpdateInfo? info;
        try { info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return (UpdateCheck.NotPublished, null); }
        if (info?.Version is null || info.Url is null || info.Sha256 is null
            || !Version.TryParse(info.Version, out var remote) || !IsTrustedUrl(info.Url) || !IsSha256(info.Sha256))
            return (UpdateCheck.NotPublished, null);
        var local = Version.TryParse(localVersion, out var v) ? v : new Version(0, 0);
        return remote > local ? (UpdateCheck.Available, info) : (UpdateCheck.UpToDate, info);
    }

    private static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    internal static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals(TrustedHost, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + TrustedHost, StringComparison.OrdinalIgnoreCase));

    /// <summary>Baixa e confere o instalador. Retorna o caminho do arquivo verificado ou lança com o motivo em português.</summary>
    public static async Task<string> DownloadAndVerifyAsync(UpdateInfo info, IProgress<int>? progress = null)
    {
        if (!IsTrustedUrl(info.Url)) throw new InvalidOperationException("O endereço do instalador não é da Klivfy.");
        string dir = Path.Combine(Path.GetTempPath(), "NotebookDoctor-update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"NotebookDoctor-{info.Version}-setup.exe");

        using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, read = 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((int)(read * 100 / total));
            }
        }

        string hash;
        await using (var fs = File.OpenRead(file)) hash = Convert.ToHexString(await SHA256.HashDataAsync(fs));
        if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) { TryDelete(file); throw new InvalidOperationException("O arquivo baixado não confere com o publicado (hash diferente)."); }
        if (!IsSignedByPublisher(file)) { TryDelete(file); throw new InvalidOperationException("O instalador não tem uma assinatura digital válida da Klivfy."); }
        return file;
    }

    private static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    /// <summary>Assinatura Authenticode íntegra, com revogação checada na cadeia inteira, e emitida para a Klivfy.</summary>
    public static bool IsSignedByPublisher(string file) =>
        Authenticode.Verify(file, checkRevocation: true) is { } signer && IsExpectedPublisher(signer.CommonName, signer.Organization);

    /// <summary>
    /// Compara o nome completo, não um trecho: "Klivfy Evil Ltda" ou "NotKlivfy" não passam.
    /// Aceita o CN igual à razão social, o formato ICP-Brasil "RAZÃO SOCIAL:CNPJ" ou o campo O igual à razão social.
    /// </summary>
    internal static bool IsExpectedPublisher(string? commonName, string? organization)
    {
        static bool Same(string? a, string b) => a is not null && a.Trim().Equals(b, StringComparison.OrdinalIgnoreCase);
        if (Same(organization, PublisherName)) return true;
        if (commonName is null) return false;
        int colon = commonName.IndexOf(':');
        string name = colon >= 0 && commonName[(colon + 1)..].Trim().All(c => char.IsDigit(c) || c is '.' or '/' or '-')
            ? commonName[..colon] : commonName;
        return Same(name, PublisherName);
    }
}
