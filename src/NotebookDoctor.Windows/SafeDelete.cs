using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>
/// Apaga pelo identificador do arquivo, e não pelo caminho. Abre o item sem seguir um link nele mesmo, pergunta ao
/// Windows onde ele está de fato (GetFinalPathNameByHandle) e só marca a exclusão se ainda estiver dentro da pasta
/// que está sendo limpa. Assim, trocar uma subpasta por uma junção entre a lista e a exclusão não leva a limpeza para
/// fora: com o app elevado, isso deixaria qualquer usuário apagar arquivos do sistema usando pastas onde todos
/// escrevem, como C:\Windows\Temp.
/// </summary>
public static class SafeDelete
{
    /// <summary>
    /// A pasta da limpeza como o Windows a vê, para passar a <see cref="File"/>. Null se ela não existe, não abre ou
    /// se algum trecho do caminho é um link: uma pasta-mãe trocada por junção (em %ProgramData%, por exemplo) levaria
    /// a regra para outro lugar.
    /// </summary>
    public static string? Root(string dir)
    {
        using var h = Open(dir, Access.ReadAttributes, openReparsePoint: false);
        if (h is null || FinalPath(h) is not { } final) return null;
        return Paths.Same(final, LongPath(dir)) ? final : null;
    }

    /// <summary>
    /// Apaga um arquivo dentro de <paramref name="root"/> (o valor de <see cref="Root"/>). Devolve false, sem apagar,
    /// se o arquivo é um link ou se o caminho final saiu da pasta. Erros (em uso, sem permissão) saem como
    /// <see cref="IOException"/> ou <see cref="UnauthorizedAccessException"/>, como no File.Delete.
    /// </summary>
    public static bool File(string path, string root, bool ignoreReadOnly) => Delete(path, root, ignoreReadOnly, directory: false);

    /// <summary>Apaga uma pasta vazia dentro de <paramref name="root"/> (ou a própria raiz), com as mesmas conferências.</summary>
    public static bool Directory(string path, string root) => Delete(path, root, ignoreReadOnly: false, directory: true);

    private static bool Delete(string path, string root, bool ignoreReadOnly, bool directory)
    {
        // Só pede para mudar atributos quando precisa: apagar exige menos permissão que isso
        var access = Access.Delete | Access.ReadAttributes | (ignoreReadOnly ? Access.WriteAttributes : 0);
        using var h = Open(path, access, openReparsePoint: true) ?? throw Error(Marshal.GetLastWin32Error(), path);
        if (!GetFileInformationByHandle(h, out var info)) throw Error(Marshal.GetLastWin32Error(), path);
        var attributes = (FileAttributes)info.FileAttributes;
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false; // link ou junção: o destino não é desta pasta
        if (attributes.HasFlag(FileAttributes.Directory) != directory) return false; // virou outra coisa desde a lista
        if (FinalPath(h) is not { } final || !Paths.IsUnder(final, root)) return false;

        // Windows 10 1809 ou mais novo: apaga e ignora "somente leitura" numa chamada só
        var ex = new DispositionEx { Flags = DispositionDelete | (ignoreReadOnly ? DispositionIgnoreReadOnly : 0) };
        if (SetFileInformationByHandle(h, FileDispositionInfoEx, ref ex, Marshal.SizeOf<DispositionEx>())) return true;
        int err = Marshal.GetLastWin32Error();
        if (err is not (ErrorInvalidParameter or ErrorInvalidFunction or ErrorNotSupported)) throw Error(err, path);

        // Windows mais antigo: tira o "somente leitura" pelo mesmo identificador e marca a exclusão
        if (ignoreReadOnly && attributes.HasFlag(FileAttributes.ReadOnly))
        {
            var basic = new BasicInfo { FileAttributes = (uint)(attributes & ~FileAttributes.ReadOnly) };
            if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
            if (!SetFileInformationByHandle(h, FileBasicInfo, ref basic, Marshal.SizeOf<BasicInfo>())) throw Error(Marshal.GetLastWin32Error(), path);
        }
        var plain = new Disposition { DeleteFile = 1 };
        if (!SetFileInformationByHandle(h, FileDispositionInfo, ref plain, Marshal.SizeOf<Disposition>())) throw Error(Marshal.GetLastWin32Error(), path);
        return true;
    }

    private static SafeFileHandle? Open(string path, Access access, bool openReparsePoint)
    {
        uint flags = BackupSemantics | (openReparsePoint ? FlagOpenReparsePoint : 0); // BackupSemantics: deixa abrir pastas
        var h = CreateFileW(path, (uint)access, ShareAll, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    private static string? FinalPath(SafeFileHandle h)
    {
        var buffer = new StringBuilder(512);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            uint n = GetFinalPathNameByHandleW(h, buffer, (uint)buffer.Capacity, 0);
            if (n == 0) return null;
            if (n < buffer.Capacity) return Paths.FromFinalPath(buffer.ToString());
            buffer.EnsureCapacity((int)n + 1);
        }
        return null;
    }

    /// <summary>Nomes curtos (C:\Users\JOAOSI~1) viram longos; links continuam como estão.</summary>
    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(512);
        uint n = GetLongPathNameW(path, buffer, (uint)buffer.Capacity);
        if (n > buffer.Capacity) { buffer.EnsureCapacity((int)n + 1); n = GetLongPathNameW(path, buffer, (uint)buffer.Capacity); }
        return n == 0 || n > buffer.Capacity ? path : buffer.ToString();
    }

    private static Exception Error(int err, string path) => err == ErrorAccessDenied
        ? new UnauthorizedAccessException(new Win32Exception(err).Message + " " + path)
        : new IOException(new Win32Exception(err).Message + " " + path, unchecked((int)(0x80070000 | (uint)err)));

    [Flags]
    private enum Access : uint { ReadAttributes = 0x80, WriteAttributes = 0x100, Delete = 0x00010000 }

    private const uint ShareAll = 0x1 | 0x2 | 0x4, OpenExisting = 3, BackupSemantics = 0x02000000, FlagOpenReparsePoint = 0x00200000;
    private const int FileBasicInfo = 0, FileDispositionInfo = 4, FileDispositionInfoEx = 21;
    private const uint DispositionDelete = 0x1, DispositionIgnoreReadOnly = 0x10;
    private const int ErrorInvalidFunction = 1, ErrorAccessDenied = 5, ErrorNotSupported = 50, ErrorInvalidParameter = 87;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispositionEx { public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Disposition { public byte DeleteFile; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }

    // BY_HANDLE_FILE_INFORMATION: os FILETIME são pares de DWORD, sem alinhamento de 8 bytes
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleInfo
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder path, uint size, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle h, out ByHandleInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref DispositionEx info, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref Disposition info, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref BasicInfo info, int size);
}
