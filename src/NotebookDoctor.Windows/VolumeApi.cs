using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 8.4 (D02, D03): as chamadas de baixo nível do NTFS para olhar onde os arquivos estão no disco (<c>FSCTL_GET_RETRIEVAL_POINTERS</c>), o que
/// está usado no volume (<c>FSCTL_GET_VOLUME_BITMAP</c>) e mover clusters de um arquivo (<c>FSCTL_MOVE_FILE</c>, a API oficial de
/// desfragmentação: o próprio NTFS garante que o arquivo continua íntegro). Ler os arquivos não exige administrador; abrir o volume, sim.
/// </summary>
public static class VolumeApi
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[]? output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    private const uint GenericRead = 0x80000000, FileReadAttributes = 0x80, ShareAll = 7, OpenExisting = 3, BackupSemantics = 0x02000000;
    private const uint FsctlGetNtfsVolumeData = 0x00090064, FsctlGetVolumeBitmap = 0x0009006F, FsctlGetRetrievalPointers = 0x00090073, FsctlMoveFile = 0x00090074;
    private const int ErrorMoreData = 234, ErrorHandleEof = 38;

    public sealed record VolumeData(long TotalClusters, long FreeClusters, int BytesPerCluster);

    /// <summary>
    /// Abre o volume para leitura. É o acesso que as consultas (<c>FSCTL_GET_NTFS_VOLUME_DATA</c>, bitmap) e o <c>FSCTL_MOVE_FILE</c> pedem; com acesso
    /// zero o Windows responde "Incorrect function" a essas consultas.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Abrir o volume exige administrador.</exception>
    public static SafeFileHandle OpenVolume(char letter)
    {
        var h = CreateFileW($@"\\.\{char.ToUpperInvariant(letter)}:", GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) throw new UnauthorizedAccessException("Abrir o volume exige administrador.");
            throw new Win32Exception(err);
        }
        return h;
    }

    /// <summary>Bytes por cluster da unidade, sem precisar de administrador. 0 se o Windows não responder.</summary>
    public static int ClusterBytes(char letter)
    {
        if (!GetDiskFreeSpaceW($"{char.ToUpperInvariant(letter)}:\\", out uint spc, out uint bps, out _, out _)) return 0;
        long bytes = (long)spc * bps;
        return bytes is > 0 and <= int.MaxValue ? (int)bytes : 0;
    }

    public static SafeFileHandle OpenFile(string path)
    {
        var h = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return h;
    }

    public static VolumeData GetVolumeData(SafeFileHandle volume)
    {
        var buf = new byte[96];
        if (!DeviceIoControl(volume, FsctlGetNtfsVolumeData, null, 0, buf, buf.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        // NTFS_VOLUME_DATA_BUFFER: serial (8), setores (8), clusters totais (8), clusters livres (8), reservados (8), bytes por setor (4), bytes por cluster (4)
        return new VolumeData(BitConverter.ToInt64(buf, 16), BitConverter.ToInt64(buf, 24), BitConverter.ToInt32(buf, 44));
    }

    /// <summary>Todos os trechos do arquivo no disco. Arquivo pequeno, guardado dentro da tabela do NTFS, devolve lista vazia.</summary>
    public static List<Extent> GetExtents(string path)
    {
        using var file = OpenFile(path);
        var all = new List<Extent>();
        long startVcn = 0;
        var buffer = new byte[64 * 1024];
        for (int guard = 0; guard < 100_000; guard++)
        {
            bool ok = DeviceIoControl(file, FsctlGetRetrievalPointers, BitConverter.GetBytes(startVcn), 8, buffer, buffer.Length, out int returned, IntPtr.Zero);
            int err = ok ? 0 : Marshal.GetLastWin32Error();
            if (!ok && err == ErrorHandleEof) return all;
            if (!ok && err != ErrorMoreData) throw new Win32Exception(err);
            var (extents, next) = VolumeStructures.ParseRetrievalPointers(buffer, ok ? returned : buffer.Length);
            all.AddRange(extents);
            if (ok || extents.Count == 0 || next <= startVcn) return all;
            startVcn = next;
        }
        return all;
    }

    /// <summary>O bitmap do volume inteiro. Volumes enormes (bitmap acima do limite) devolvem null: o mapa não vale a memória.</summary>
    public static VolumeBitmap? ReadBitmap(SafeFileHandle volume, long maxBytes = 256L << 20)
    {
        var data = GetVolumeData(volume);
        long total = data.TotalClusters;
        if ((total + 7) / 8 > maxBytes) return null;
        var bits = new byte[(total + 7) / 8];
        var buffer = new byte[1 << 20];
        long next = 0;
        while (next < total)
        {
            bool ok = DeviceIoControl(volume, FsctlGetVolumeBitmap, BitConverter.GetBytes(next), 8, buffer, buffer.Length, out int returned, IntPtr.Zero);
            int err = ok ? 0 : Marshal.GetLastWin32Error();
            if (!ok && err != ErrorMoreData) throw new Win32Exception(err);
            var (start, size, bytes) = VolumeBitmap.ParseChunk(buffer, ok ? returned : buffer.Length);
            if (bytes.Length == 0) break;
            long first = start >> 3;
            Array.Copy(bytes, 0, bits, first, Math.Min(bytes.Length, bits.Length - first));
            long covered = Math.Min(size, bytes.Length * 8L);
            next = start + covered;
            if (ok) break;
        }
        return new VolumeBitmap(bits, total);
    }

    /// <summary>Move <paramref name="count"/> clusters do arquivo, a partir do VCN dado, para o LCN dado (que tem de estar livre).</summary>
    public static void MoveClusters(SafeFileHandle volume, SafeFileHandle file, long startingVcn, long targetLcn, int count)
    {
        // MOVE_FILE_DATA: handle (8 bytes), VCN inicial (8), LCN de destino (8), quantidade (4, mais 4 de enchimento)
        var input = new byte[32];
        BitConverter.GetBytes(file.DangerousGetHandle().ToInt64()).CopyTo(input, 0);
        BitConverter.GetBytes(startingVcn).CopyTo(input, 8);
        BitConverter.GetBytes(targetLcn).CopyTo(input, 16);
        BitConverter.GetBytes(count).CopyTo(input, 24);
        if (!DeviceIoControl(volume, FsctlMoveFile, input, input.Length, null, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}

public enum FileDefragStatus { Defragmented, AlreadyTogether, NoRoom, Skipped, Failed }

public sealed record FileDefragResult(string Path, FileDefragStatus Status, int FragmentsBefore, int FragmentsAfter, string Note);

/// <summary>
/// Leva 8.4 (D02): desfragmenta um arquivo juntando os clusters dele numa corrida livre contínua do volume. Se a corrida for tomada por outro
/// programa no meio do caminho, o Windows recusa o passo e o arquivo continua íntegro (só ficou parte do caminho andada).
/// </summary>
public static class FileDefragmenter
{
    public static int FragmentsOf(string path)
    {
        try { return DefragRules.Fragments(VolumeApi.GetExtents(path)); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException) { return 0; }
    }

    public static FileDefragResult Defrag(SafeFileHandle volume, VolumeBitmap bitmap, int clusterBytes, string path, CancellationToken ct)
    {
        try
        {
            var attr = File.GetAttributes(path);
            long length = new FileInfo(path).Length;
            var (ok, reason) = DefragRules.CanMove(path, attr, length, clusterBytes);
            if (!ok) return new(path, FileDefragStatus.Skipped, 0, 0, reason);

            var extents = VolumeApi.GetExtents(path);
            if (DefragRules.HasHoles(extents)) return new(path, FileDefragStatus.Skipped, 0, 0, "Arquivo esparso");
            int before = DefragRules.Fragments(extents);
            if (before <= 1) return new(path, FileDefragStatus.AlreadyTogether, before, before, "");

            long target = bitmap.FindFreeRun(DefragRules.TotalClusters(extents));
            if (target < 0) return new(path, FileDefragStatus.NoRoom, before, before, "Não há espaço livre contínuo do tamanho do arquivo");

            using var file = VolumeApi.OpenFile(path);
            foreach (var (vcn, lcn, count) in DefragRules.MoveSteps(extents, target))
            {
                ct.ThrowIfCancellationRequested();
                VolumeApi.MoveClusters(volume, file, vcn, lcn, (int)count);
            }
            int after = DefragRules.Fragments(VolumeApi.GetExtents(path));
            return new(path, after < before ? FileDefragStatus.Defragmented : FileDefragStatus.Failed, before, after, after < before ? "" : "Os clusters não ficaram juntos");
        }
        catch (OperationCanceledException) { throw; }
        catch (Win32Exception ex) { return new(path, FileDefragStatus.Failed, 0, 0, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(path, FileDefragStatus.Failed, 0, 0, ex.Message); }
    }
}
