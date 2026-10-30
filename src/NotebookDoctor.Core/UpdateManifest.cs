using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>Manifesto publicado pela Klivfy: {"version":"1.2.0","url":"https://…/setup.exe","sha256":"…","notes":"…"}.</summary>
public sealed record UpdateInfo(string Version, string Url, string Sha256, string Notes);

public enum UpdateCheck { UpToDate, Available, NotPublished, Offline }

/// <summary>Endereços do site da Klivfy que o app abre ou baixa, e a regra de quais endereços valem.</summary>
public static class KlivfyUrls
{
    public const string Host = "klivfy.com";
    public const string Support = "https://klivfy.com/suporte";
    public const string Terms = "https://klivfy.com/pt-BR/termos-de-uso";
    public const string Privacy = "https://klivfy.com/pt-BR/privacidade";
    public const string TermsEn = "https://klivfy.com/en-US/termos-de-uso";
    public const string TermsEs = "https://klivfy.com/es-ES/termos-de-uso";
    public const string PrivacyEn = "https://klivfy.com/en-US/privacidade";
    public const string PrivacyEs = "https://klivfy.com/es-ES/privacidade";

    public static string TermsFor(string? language = null) => (language ?? Loc.Language) switch
    {
        Loc.English => TermsEn,
        Loc.Spanish => TermsEs,
        _ => Terms,
    };

    public static string PrivacyFor(string? language = null) => (language ?? Loc.Language) switch
    {
        Loc.English => PrivacyEn,
        Loc.Spanish => PrivacyEs,
        _ => Privacy,
    };
    public const string Manifest = "https://klivfy.com/notebookdoctor/versao.json";
    /// <summary>B25 e M18: recebem o que a pessoa mandar ("Enviar avaliação") e as estatísticas anônimas, se ela ligar a opção.</summary>
    public const string Rating = "https://klivfy.com/notebookdoctor/avaliacao";
    public const string Usage = "https://klivfy.com/notebookdoctor/estatisticas";

    /// <summary>Só https, no domínio da Klivfy ou num subdomínio dele, sem usuário e senha no endereço.</summary>
    public static bool IsTrusted(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.UserInfo.Length == 0 &&
        (u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A50: lê o manifesto de atualização. Nunca lança: manifesto vazio, quebrado ou com endereço, versão ou hash inválido vira
/// "nenhuma atualização publicada".
/// </summary>
public static class UpdateManifest
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static (UpdateCheck Result, UpdateInfo? Info) Evaluate(string? json, Version local)
    {
        if (string.IsNullOrWhiteSpace(json)) return (UpdateCheck.NotPublished, null);
        UpdateInfo? info;
        try { info = JsonSerializer.Deserialize<UpdateInfo>(json, Options); }
        catch (JsonException) { return (UpdateCheck.NotPublished, null); }
        if (info is null || !Version.TryParse(info.Version, out var remote) || !KlivfyUrls.IsTrusted(info.Url) || !IsSha256(info.Sha256))
            return (UpdateCheck.NotPublished, null);
        info = info with { Notes = info.Notes ?? "" };
        return remote > local ? (UpdateCheck.Available, info) : (UpdateCheck.UpToDate, info);
    }

    /// <summary>SHA-256 em hexadecimal: 64 caracteres de 0 a 9 e A a F.</summary>
    public static bool IsSha256(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
}
