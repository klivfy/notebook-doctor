using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NotebookDoctor.Services;

public sealed record EraseProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string Current);

/// <param name="Notes">Arquivos que ficaram e por quê (link físico, em uso, nuvem…).</param>
public sealed record EraseResult(int Erased, int Skipped, int Failed, long Bytes, bool Cancelled, bool NeedsFreeSpaceWipe, IReadOnlyList<string> Notes);

/// <summary>O caminho de verdade de um arquivo ou pasta, depois de seguir todos os links e junções do caminho.</summary>
public static class RealPath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file, System.Text.StringBuilder path, uint size, uint flags);

    /// <summary>Null se o caminho não existe. Abre sem pedir acesso aos dados (só atributos), então funciona até com arquivo em uso.</summary>
    public static string? Of(string path)
    {
        // FILE_FLAG_BACKUP_SEMANTICS permite abrir pasta; sem acesso de leitura nem de escrita
        using var h = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var sb = new System.Text.StringBuilder(1024);
        uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity) return null;
        string full = sb.ToString();
        if (full.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + full[8..];
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full[4..] : full;
    }
}

/// <summary>
/// Leva 8.2 (R21): apaga de vez. Para cada arquivo: sobrescreve com uma passada de dados aleatórios, com escrita direta (<c>WriteThrough</c>) e
/// <c>Flush(true)</c>; faz o mesmo com os fluxos alternativos (ADS); renomeia 3 vezes para nomes aleatórios; zera o tamanho; apaga. Não segue
/// links, não toca arquivo com outro nome (link físico), nem arquivo só na nuvem. Em SSD, pen drive e cartão não há garantia, e o app diz isso.
/// </summary>
public static class SecureEraser
{
    private const int Chunk = 1 << 20;

    // ---------- fluxos alternativos ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindStreamData
    {
        public long StreamSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string StreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out FindStreamData data, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextStreamW(IntPtr find, out FindStreamData data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr find);

    /// <summary>Nomes dos fluxos de dados além do principal (":nome:$DATA"). Vazio em sistema de arquivos sem fluxos.</summary>
    public static List<string> AlternateStreams(string path)
    {
        var list = new List<string>();
        IntPtr find = FindFirstStreamW(path, 0, out var data, 0);
        if (find == new IntPtr(-1)) return list;
        try
        {
            do
            {
                // "::$DATA" é o conteúdo normal do arquivo
                if (data.StreamName is { Length: > 7 } n && n.EndsWith(":$DATA", StringComparison.Ordinal) && n != "::$DATA") list.Add(n[..^6]);
            } while (FindNextStreamW(find, out data));
        }
        finally { FindClose(find); }
        return list;
    }

    // ---------- número de nomes do arquivo ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInfo
    {
        public uint Attributes;
        public long Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out ByHandleInfo info);

    /// <summary>Quantos nomes (links físicos) o arquivo tem. 1 é o normal.</summary>
    public static int LinkCount(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(fs.SafeFileHandle, out var info) ? (int)info.Links : 1;
    }

    // ---------- sobrescrever ----------

    /// <summary>Sobrescreve o conteúdo (e os fluxos alternativos) com dados aleatórios, do mesmo tamanho. Não renomeia nem apaga.</summary>
    public static void Overwrite(string path, CancellationToken ct = default, Action<long>? onBytes = null)
    {
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        OverwriteStream(path, ct, onBytes);
        foreach (var stream in AlternateStreams(path))
        {
            try { OverwriteStream(path + ":" + stream.TrimStart(':'), ct, null); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { /* o fluxo some junto com o arquivo */ }
        }
    }

    private static void OverwriteStream(string path, CancellationToken ct, Action<long>? onBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
        long length = fs.Length;
        var buffer = new byte[(int)Math.Min(Chunk, Math.Max(length, 1))];
        for (long pos = 0; pos < length; pos += buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            RandomNumberGenerator.Fill(buffer);
            int n = (int)Math.Min(buffer.Length, length - pos);
            fs.Write(buffer, 0, n);
            onBytes?.Invoke(n);
        }
        fs.Flush(flushToDisk: true);
    }

    // ---------- apagar ----------

    /// <summary>Sobrescreve, renomeia 3 vezes, zera o tamanho e apaga. Devolve o motivo se pulou, ou null se apagou.</summary>
    public static string? EraseFile(string path, Random rng, CancellationToken ct, Action<long>? onBytes)
    {
        var attr = File.GetAttributes(path);
        if (attr.HasFlag(FileAttributes.ReparsePoint) || OneDriveRule.IsOnlineOnly((int)attr)) return "É um link ou um arquivo só na nuvem";
        if (LinkCount(path) > 1) return "Tem outro nome no disco (link físico): sobrescrever mudaria o outro também";
        // O arquivo tem de estar mesmo onde o plano diz: um link ou atalho de pasta no caminho levaria a escrita para outro lugar
        if (RealPath.Of(path) is not { } real || !string.Equals(real, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return "O caminho passa por um link ou atalho de pasta";

        Overwrite(path, ct, onBytes);

        string dir = Path.GetDirectoryName(path)!;
        string current = path;
        for (int i = 0; i < 3; i++)
        {
            string next = Path.Combine(dir, EraseRules.RandomName(rng));
            if (File.Exists(next) || Directory.Exists(next)) continue;
            File.Move(current, next);
            current = next;
        }
        using (var fs = new FileStream(current, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
        {
            fs.SetLength(0);
            fs.Flush(true);
        }
        File.Delete(current);
        return null;
    }

    private static void EraseDirectory(string dir, Random rng)
    {
        if (Directory.EnumerateFileSystemEntries(dir).Any()) return; // sobrou algo (arquivo pulado): a pasta fica
        string current = dir, parent = Path.GetDirectoryName(dir)!;
        for (int i = 0; i < 3; i++)
        {
            string next = Path.Combine(parent, EraseRules.RandomName(rng));
            if (File.Exists(next) || Directory.Exists(next)) continue;
            Directory.Move(current, next);
            current = next;
        }
        Directory.Delete(current, recursive: false);
    }

    public static EraseResult Erase(ErasePlan plan, IProgress<EraseProgress>? progress, CancellationToken ct)
    {
        var rng = new Random();
        var notes = new List<string>();
        int erased = 0, skipped = 0, failed = 0, done = 0;
        long bytes = 0, doneBytes = 0;
        bool cancelled = false, special = false;

        foreach (var file in plan.Files)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            progress?.Report(new EraseProgress(done, plan.Files.Count, doneBytes, plan.Bytes, file));
            try
            {
                var attr = File.GetAttributes(file);
                if (attr.HasFlag(FileAttributes.Compressed) || attr.HasFlag(FileAttributes.SparseFile) || attr.HasFlag(FileAttributes.Encrypted)) special = true;
                long size = new FileInfo(file).Length;
                string? why = EraseFile(file, rng, ct, n => { doneBytes += n; });
                if (why is null) { erased++; bytes += size; }
                else { skipped++; notes.Add($"{file}: {why}"); }
            }
            catch (OperationCanceledException) { cancelled = true; notes.Add($"{file}: interrompido no meio; o arquivo continua no disco, parcialmente sobrescrito"); break; }
            catch (FileNotFoundException) { skipped++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                failed++;
                notes.Add($"{file}: {ex.Message}");
            }
            done++;
        }
        if (!cancelled)
            foreach (var dir in plan.Dirs)
            {
                try { if (Directory.Exists(dir)) EraseDirectory(dir, rng); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notes.Add($"{dir}: {ex.Message}"); }
            }
        progress?.Report(new EraseProgress(done, plan.Files.Count, plan.Bytes, plan.Bytes, ""));
        return new EraseResult(erased, skipped, failed, bytes, cancelled, special, notes);
    }
}

public sealed record WipeProgress(long Written, long Target, int Files);
public sealed record WipeResult(long Written, int Files, bool Cancelled, bool Completed, string Error);

/// <summary>
/// Leva 8.2 (R20): limpa o espaço livre de uma unidade enchendo-o com arquivos temporários de dados aleatórios (blocos de 256 MB), em uma
/// passada (NIST SP 800-88, "clear"), e apagando tudo no fim. Os arquivos abrem com <c>DeleteOnClose</c>: se o app cair no meio, o Windows
/// apaga o que sobrou ao fechar os identificadores.
/// </summary>
public static class FreeSpaceWiper
{
    public static WipeResult Wipe(string driveRoot, string tempDir, long reserveBytes, long? maxBytes, IProgress<WipeProgress>? progress, CancellationToken ct)
    {
        var open = new List<FileStream>();
        long written = 0;
        bool cancelled = false, completed = false;
        string error = "";
        try
        {
            Directory.CreateDirectory(tempDir);
            var drive = new DriveInfo(driveRoot);
            long target = WipeRules.Target(drive.AvailableFreeSpace, reserveBytes, maxBytes);
            var buffer = new byte[4 << 20];
            RandomNumberGenerator.Fill(buffer);

            while (!ct.IsCancellationRequested)
            {
                long block = WipeRules.NextBlock(drive.AvailableFreeSpace, reserveBytes, written, maxBytes);
                if (block <= 0) { completed = true; break; }
                var fs = new FileStream(Path.Combine(tempDir, $"limpeza-{open.Count:D5}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                    FileOptions.WriteThrough | FileOptions.DeleteOnClose);
                open.Add(fs);
                long inFile = 0;
                try
                {
                    while (inFile < block && !ct.IsCancellationRequested)
                    {
                        int n = (int)Math.Min(buffer.Length, block - inFile);
                        // Embaralha o começo do bloco reaproveitado: dois arquivos seguidos não repetem o mesmo padrão byte a byte
                        BitConverter.GetBytes(written + inFile).CopyTo(buffer, 0);
                        fs.Write(buffer, 0, n);
                        inFile += n;
                        written += n;
                        progress?.Report(new WipeProgress(written, target, open.Count));
                    }
                    fs.Flush(flushToDisk: true);
                }
                catch (IOException) { /* disco cheio antes do previsto: é o fim do trabalho, não um erro */ completed = true; break; }
            }
            cancelled = ct.IsCancellationRequested;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
        }
        finally
        {
            foreach (var fs in open) { try { fs.Dispose(); } catch (IOException) { } } // DeleteOnClose apaga cada arquivo
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        progress?.Report(new WipeProgress(written, written, open.Count));
        return new WipeResult(written, open.Count, cancelled, completed && !cancelled && error.Length == 0, error);
    }
}
