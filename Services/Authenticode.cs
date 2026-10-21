using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace NotebookDoctor.Services;

/// <summary>Quem assinou o arquivo (Authenticode já validado pelo Windows).</summary>
public sealed record SignerInfo(string? CommonName, string? Organization, string Thumbprint);

/// <summary>Verificação de assinatura digital via WinVerifyTrust, usada pelo atualizador e pelo desinstalador assistido.</summary>
public static class Authenticode
{
    /// <summary>Retorna o signatário se a assinatura for íntegra e confiável pelo Windows; senão, null.</summary>
    public static SignerInfo? Verify(string file, bool checkRevocation)
    {
        if (VerifyTrust(file, checkRevocation) != 0) return null;
        try
        {
#pragma warning disable SYSLIB0057 // leitura do certificado do signatário de um arquivo assinado
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return new SignerInfo(
                cert.GetNameInfo(X509NameType.SimpleName, false),
                ReadOrganization(cert.Subject),
                cert.Thumbprint);
        }
        catch { return null; }
    }

    /// <summary>Valor do campo O= do subject (aceita valor entre aspas), ou null.</summary>
    internal static string? ReadOrganization(string subject)
    {
        foreach (var part in subject.Split(','))
        {
            var kv = part.Trim();
            if (kv.StartsWith("O=", StringComparison.OrdinalIgnoreCase)) return kv[2..].Trim().Trim('"');
        }
        return null;
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint WTD_REVOKE_NONE = 0, WTD_REVOKE_WHOLECHAIN = 1;

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

    private static int VerifyTrust(string file, bool checkRevocation)
    {
        var fi = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = file };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fi, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,                                                            // WTD_UI_NONE
                fdwRevocationChecks = checkRevocation ? WTD_REVOKE_WHOLECHAIN : WTD_REVOKE_NONE,
                dwUnionChoice = 1,                                                         // WTD_CHOICE_FILE
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
