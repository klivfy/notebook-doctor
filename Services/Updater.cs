using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NotebookDoctor.Services;

/// <summary>
/// A50: atualização assinada. Só HTTPS no domínio klivfy.com; o instalador só roda se o SHA-256 bater com o manifesto,
/// a assinatura Authenticode for válida (WinVerifyTrust) e o certificado for da Klivfy. Nada é instalado sem confirmação.
/// </summary>
public static class Updater
{
    public const string ManifestUrl = KlivfyUrls.Manifest;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<(UpdateCheck Result, UpdateInfo? Info)> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ManifestUrl);
            if (!resp.IsSuccessStatusCode) return (UpdateCheck.NotPublished, null);
            var local = Version.TryParse(AppInfo.Version, out var v) ? v : new Version(0, 0);
            return UpdateManifest.Evaluate(await resp.Content.ReadAsStringAsync(), local);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (UpdateCheck.Offline, null); }
    }

    /// <summary>Baixa e confere o instalador. Retorna o caminho do arquivo verificado ou lança com o motivo em português.</summary>
    public static async Task<string> DownloadAndVerifyAsync(UpdateInfo info, IProgress<int>? progress = null)
    {
        if (!KlivfyUrls.IsTrusted(info.Url)) throw new InvalidOperationException("O endereço do instalador não é da Klivfy.");
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

    /// <summary>Assinatura Authenticode íntegra e confiável (WinVerifyTrust), emitida exatamente para a Klivfy (S2).</summary>
    public static bool IsSignedByPublisher(string file)
    {
        if (VerifyTrust(file) != 0) return false;
        try
        {
#pragma warning disable SYSLIB0057 // leitura do certificado do signatário de um arquivo assinado
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            var subject = cert.SubjectName.EnumerateRelativeDistinguishedNames()
                .Select(r => (r.GetSingleElementType().Value ?? "", r.GetSingleElementValue() ?? ""));
            string key = Convert.ToHexString(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo()));
            return PublisherSignature.Trusted(subject, key);
        }
        catch { return false; }
    }

    // ---------- WinVerifyTrust ----------
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

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

    private static int VerifyTrust(string file)
    {
        var fi = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = file };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fi, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pFile,
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
