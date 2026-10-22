using System.IO;

namespace NotebookDoctor.Services;

/// <summary>M28: quem assinou o arquivo, ou o fabricante que ele declara. Usado pela Inicialização e pelo menu de contexto.</summary>
public static class FilePublisher
{
    public static string Describe(string? path)
    {
        if (path is null || !File.Exists(path)) return "Arquivo não encontrado";
        string ext = Path.GetExtension(path);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return "Atalho ou script (veja o local)";
        try
        {
            string subject = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path).Subject;
            string? cn = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))?[3..];
            return $"Assinado: {cn?.Trim('"') ?? subject}";
        }
        catch
        {
            // Sem assinatura Authenticode: mostra o fabricante que o próprio arquivo declara (não comprovado)
            string? company = null;
            try { company = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim(); } catch { }
            return string.IsNullOrEmpty(company) ? "Sem assinatura" : $"Sem assinatura ({company})";
        }
    }
}
