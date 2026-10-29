using System.IO;

namespace NotebookDoctor.Services;

/// <summary>M28: quem assinou o arquivo, ou o fabricante que ele declara. Usado pela Inicialização e pelo menu de contexto.</summary>
public static class FilePublisher
{
    public static string Describe(string? path)
    {
        if (path is null || !File.Exists(path)) return Loc.T("Arquivo não encontrado");
        string ext = Path.GetExtension(path);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return Loc.T("Atalho ou script (veja o local)");
        try
        {
            string subject = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path).Subject;
            string? cn = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))?[3..];
            return Loc.F("Assinado: {0}", cn?.Trim('"') ?? subject);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Sem assinatura Authenticode: mostra o fabricante que o próprio arquivo declara (não comprovado)
            string? company = null;
            try { company = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim(); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException) { }
            return string.IsNullOrEmpty(company) ? Loc.T("Sem assinatura") : Loc.F("Sem assinatura ({0})", company);
        }
    }
}

