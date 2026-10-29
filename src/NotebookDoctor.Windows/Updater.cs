using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>
/// Instalador baixado, verificado (SHA-256 + Authenticode com revogação + certificado de código da Klivfy)
/// e mantido aberto apenas com compartilhamento de leitura (<see cref="FileShare.Read"/>) para impedir
/// modificação, substituição ou exclusão (TOCTOU) até o momento em que o processo do instalador é criado.
/// </summary>
public sealed class VerifiedInstaller : IDisposable
{
    private readonly FileStream _readLock;
    private bool _disposed;

    internal VerifiedInstaller(string filePath, FileStream readLock)
    {
        FilePath = filePath;
        _readLock = readLock;
    }

    public string FilePath { get; }

    /// <summary>Abre o instalador mantendo o handle de leitura aberto até o Windows criar o processo.</summary>
    public void Launch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readLock.Dispose();
    }
}

/// <summary>
/// A50 / SEC-02 / SEC-03: atualização assinada. Só HTTPS no domínio klivfy.com; o instalador é gravado numa pasta
/// isolada com ACL restrita ao usuário atual, SYSTEM e Administradores, mantido sob bloqueio de leitura durante
/// a conferência de SHA-256, assinatura Authenticode (com checagem de revogação da cadeia), EKU de Code Signing e
/// identidade da Klivfy, e só aberto sob confirmação explícita.
/// </summary>
public static class Updater
{
    public const string ManifestUrl = KlivfyUrls.Manifest;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<(UpdateCheck Result, UpdateInfo? Info)> CheckAsync(string? currentVersion = null)
    {
        try
        {
            using var resp = await Http.GetAsync(ManifestUrl);
            if (!resp.IsSuccessStatusCode) return (UpdateCheck.NotPublished, null);
            string verText = currentVersion
                ?? (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0").Split('+')[0];
            var local = Version.TryParse(verText, out var v) ? v : new Version(0, 0);
            return UpdateManifest.Evaluate(await resp.Content.ReadAsStringAsync(), local);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (UpdateCheck.Offline, null); }
    }

    /// <summary>
    /// Baixa o instalador para um diretório isolado com ACL restrita, bloqueia contra escrita e confere hash e assinatura.
    /// </summary>
    public static async Task<VerifiedInstaller> DownloadLockedAsync(
        UpdateInfo info,
        IProgress<int>? progress = null,
        string? baseDir = null)
    {
        if (!KlivfyUrls.IsTrusted(info.Url))
            throw new InvalidOperationException("O endereço do instalador não é da Klivfy.");
        if (!UpdateManifest.IsSha256(info.Sha256))
            throw new InvalidOperationException("O arquivo baixado não confere com o publicado (hash diferente).");

        string root = string.IsNullOrWhiteSpace(baseDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookDoctor")
            : baseDir;

        string dir = CreateSecureUpdateDirectory(root);
        string safeVersion = SanitizeVersion(info.Version);
        string file = Path.Combine(dir, $"NotebookDoctor-{safeVersion}-setup.exe");

        try
        {
            using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1, read = 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                if (!SafeDelete.VerifyExclusiveFileHandle(dst.SafeFileHandle, file))
                    throw new InvalidOperationException("Caminho de atualização inválido ou redirecionado.");

                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    read += n;
                    if (total > 0) progress?.Report((int)(read * 100 / total));
                }
                dst.Flush(flushToDisk: true);
            }

            return await VerifyAndLockAsync(file, info.Sha256);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(file)) SafeDelete.FileAtExactPath(file);
                if (Directory.Exists(dir)) SafeDelete.DirectoryAtExactPath(dir);
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Abre o instalador com compartilhamento apenas de leitura (<see cref="FileShare.Read"/>), valida o handle contra
    /// links simbólicos/hardlinks, confere o SHA-256 sobre o mesmo handle e valida a assinatura digital.
    /// </summary>
    public static async Task<VerifiedInstaller> VerifyAndLockAsync(
        string filePath,
        string expectedSha256,
        Func<string, SafeFileHandle, bool>? signatureVerifier = null)
    {
        if (!UpdateManifest.IsSha256(expectedSha256))
            throw new InvalidOperationException("O arquivo baixado não confere com o publicado (hash diferente).");

        var readLock = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        try
        {
            if (!SafeDelete.VerifyExclusiveFileHandle(readLock.SafeFileHandle, filePath))
                throw new InvalidOperationException("Caminho de atualização inválido ou redirecionado.");

            readLock.Position = 0;
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(readLock));
            if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O arquivo baixado não confere com o publicado (hash diferente).");

            bool signed = signatureVerifier is not null
                ? signatureVerifier(filePath, readLock.SafeFileHandle)
                : IsSignedByPublisher(filePath, readLock.SafeFileHandle);
            if (!signed)
                throw new InvalidOperationException("O instalador não tem uma assinatura digital válida da Klivfy.");

            return new VerifiedInstaller(filePath, readLock);
        }
        catch (Exception)
        {
            readLock.Dispose();
            try { if (File.Exists(filePath)) SafeDelete.FileAtExactPath(filePath); }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Cria um subdiretório exclusivo em <c>&lt;baseDir&gt;\updates\&lt;guid&gt;</c> com herança de ACL desligada,
    /// permitindo acesso apenas ao usuário atual, SYSTEM e Administradores, e recusando reparse points.
    /// </summary>
    public static string CreateSecureUpdateDirectory(string baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir)) throw new ArgumentException("Diretório base inválido.", nameof(baseDir));
        string fullBase = Path.GetFullPath(baseDir);
        Directory.CreateDirectory(fullBase);
        if (SafeDelete.Root(fullBase) is null)
            throw new InvalidOperationException("O diretório de dados do aplicativo contém um link simbólico ou junção.");

        string updatesRoot = Path.Combine(fullBase, "updates");
        var rootInfo = new DirectoryInfo(updatesRoot);
        if (rootInfo.Exists && (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("A pasta de atualizações não pode ser um link simbólico ou junção.");

        Directory.CreateDirectory(updatesRoot);
        ApplyRestrictedAcl(updatesRoot);
        CleanStaleUpdateDirectories(updatesRoot);

        string runDir = Path.Combine(updatesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDir);
        var runInfo = new DirectoryInfo(runDir);
        if ((runInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("A pasta temporária da atualização foi redirecionada.");
        ApplyRestrictedAcl(runDir);
        return runDir;
    }

    private static void ApplyRestrictedAcl(string dirPath)
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } currentSid)
            sec.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));

        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));

        new DirectoryInfo(dirPath).SetAccessControl(sec);
    }

    private static void CleanStaleUpdateDirectories(string updatesRoot)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var sub in new DirectoryInfo(updatesRoot).EnumerateDirectories())
            {
                if (sub.LastWriteTimeUtc >= cutoff) continue;
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) SafeDelete.ReparsePoint(sub.FullName, updatesRoot);
                else SafeDelete.Directory(sub.FullName, updatesRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string SanitizeVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed))
            throw new InvalidOperationException("Versão de atualização inválida.");
        return parsed.ToString();
    }

    /// <summary>Confere se o arquivo possui assinatura Authenticode íntegra e não revogada segundo o Windows.</summary>
    public static bool HasValidAuthenticode(string file, SafeFileHandle? openHandle = null) =>
        VerifyTrust(file, openHandle) == 0;

    /// <summary>
    /// Assinatura Authenticode íntegra e confiável (WinVerifyTrust com revogação da cadeia), finalidade Code Signing
    /// (EKU 1.3.6.1.5.5.7.3.3) e certificado emitido exatamente para a Klivfy (S2 / SEC-02).
    /// </summary>
    public static bool IsSignedByPublisher(string file, SafeFileHandle? openHandle = null)
    {
        if (VerifyTrust(file, openHandle) != 0) return false;
        try
        {
#pragma warning disable SYSLIB0057 // leitura do certificado do signatário de um arquivo assinado já validado pelo WinVerifyTrust
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            var subject = cert.SubjectName.EnumerateRelativeDistinguishedNames()
                .Select(r => (r.GetSingleElementType().Value ?? "", r.GetSingleElementValue() ?? ""));
            var ekus = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .SelectMany(ext => ext.EnhancedKeyUsages.Cast<Oid>())
                .Select(oid => oid.Value ?? "")
                .Where(v => v.Length > 0)
                .ToList();
            string key = Convert.ToHexString(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo()));
            return PublisherSignature.Trusted(subject, key, ekuOids: ekus);
        }
        catch (CryptographicException) { return false; }
    }

    // ---------- WinVerifyTrust ----------
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    private static int VerifyTrust(string file, SafeFileHandle? openHandle = null)
    {
        bool addedRef = false;
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var data = new WinTrustData();
        bool stateOpened = false;
        try
        {
            IntPtr hFile = IntPtr.Zero;
            if (openHandle is { IsInvalid: false, IsClosed: false })
            {
                openHandle.DangerousAddRef(ref addedRef);
                hFile = openHandle.DangerousGetHandle();
            }

            var fi = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = file,
                hFile = hFile,
            };
            Marshal.StructureToPtr(fi, pFile, false);

            data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT,
            };
            stateOpened = true;
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
        }
        finally
        {
            if (stateOpened && data.hWVTStateData != IntPtr.Zero)
            {
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                _ = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            }
            if (addedRef) openHandle!.DangerousRelease();
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
