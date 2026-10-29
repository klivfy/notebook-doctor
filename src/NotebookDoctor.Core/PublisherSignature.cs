namespace NotebookDoctor.Services;

/// <summary>
/// S2: quem assinou o instalador da atualização. Antes bastava "Klivfy" aparecer em qualquer parte do nome do
/// certificado, e um certificado válido de outra empresa ("Klivfy Games Ltda", "NotKlivfy") passava. Agora a
/// organização (O) e o nome comum (CN) precisam ser exatamente os da Klivfy, o certificado precisa ter finalidade de
/// assinatura de código (EKU 1.3.6.1.5.5.7.3.3) e, quando a chave do certificado estiver fixada, ela precisa conferir.
/// </summary>
public static class PublisherSignature
{
    public const string Organization = "Klivfy";

    /// <summary>OID X.509 Enhanced Key Usage para Code Signing (id-kp-codeSigning).</summary>
    public const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

    /// <summary>
    /// SHA-256 (hexadecimal) da chave pública dos certificados de assinatura da Klivfy. Fica vazio até o certificado
    /// de assinatura de código existir (A50); com ele preenchido, um certificado novo com o mesmo nome não basta.
    /// </summary>
    public static readonly IReadOnlyList<string> PinnedKeys = Array.Empty<string>();

    /// <summary>Confere se a lista de OIDs de EKU do certificado inclui explicitamente assinatura de código.</summary>
    public static bool HasCodeSigningEku(IEnumerable<string> ekuOids) =>
        ekuOids is not null && ekuOids.Any(oid => oid is not null && oid.Trim().Equals(CodeSigningOid, StringComparison.Ordinal));

    /// <param name="subject">Partes do assunto do certificado, como (OID ou sigla, valor): ("CN", "Klivfy"), ("O", "Klivfy").</param>
    /// <param name="publicKeySha256">SHA-256 da chave pública (SubjectPublicKeyInfo), em hexadecimal.</param>
    /// <param name="pinned">Chaves públicas aceitas; quando nulo, usa <see cref="PinnedKeys"/>.</param>
    /// <param name="requirePin">Quando verdadeiro, recusa se não houver ao menos uma chave pública fixada.</param>
    /// <param name="ekuOids">Quando informado, exige o OID de assinatura de código (<see cref="CodeSigningOid"/>).</param>
    public static bool Trusted(
        IEnumerable<(string Type, string Value)> subject,
        string publicKeySha256,
        IReadOnlyList<string>? pinned = null,
        bool requirePin = false,
        IEnumerable<string>? ekuOids = null)
    {
        if (subject is null) return false;
        if (ekuOids is not null && !HasCodeSigningEku(ekuOids)) return false;

        pinned ??= PinnedKeys;
        if (requirePin && pinned.Count == 0) return false;
        if ((pinned.Count > 0 || requirePin) && string.IsNullOrWhiteSpace(publicKeySha256)) return false;

        var parts = subject.Select(p => (Type: Normalize(p.Type), Value: p.Value.Trim())).ToList();
        // Cada campo aparece uma vez só: dois "O" num assunto forjado não podem ter um deles certo
        var orgs = parts.Where(p => p.Type == "O").ToList();
        var names = parts.Where(p => p.Type == "CN").ToList();
        if (orgs.Count != 1 || names.Count != 1) return false;
        if (!orgs[0].Value.Equals(Organization, StringComparison.Ordinal) || !names[0].Value.Equals(Organization, StringComparison.Ordinal)) return false;
        return pinned.Count == 0 || pinned.Any(k => k.Equals(publicKeySha256, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string type) => type.Trim() switch
    {
        "2.5.4.3" => "CN",
        "2.5.4.10" => "O",
        var t => t.ToUpperInvariant(),
    };
}
