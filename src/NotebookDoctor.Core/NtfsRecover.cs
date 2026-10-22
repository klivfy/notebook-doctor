using System.Text;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

public enum SignatureCheck { Unknown, Match, Mismatch }

public enum PreviewKind { None, Image, Text }

/// <summary>Os primeiros bytes de cada tipo de arquivo comum ("assinatura"), para conferir se o que voltou combina com o nome.</summary>
public static class FileSignatures
{
    private static readonly Dictionary<string, (int Offset, byte[] Magic)[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = [(0, [0xFF, 0xD8, 0xFF])],
        [".jpeg"] = [(0, [0xFF, 0xD8, 0xFF])],
        [".png"] = [(0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])],
        [".gif"] = [(0, "GIF87a"u8.ToArray()), (0, "GIF89a"u8.ToArray())],
        [".bmp"] = [(0, "BM"u8.ToArray())],
        [".pdf"] = [(0, "%PDF"u8.ToArray())],
        [".zip"] = [(0, [0x50, 0x4B, 0x03, 0x04]), (0, [0x50, 0x4B, 0x05, 0x06])],
        [".docx"] = [(0, [0x50, 0x4B, 0x03, 0x04])],
        [".xlsx"] = [(0, [0x50, 0x4B, 0x03, 0x04])],
        [".pptx"] = [(0, [0x50, 0x4B, 0x03, 0x04])],
        [".odt"] = [(0, [0x50, 0x4B, 0x03, 0x04])],
        [".ods"] = [(0, [0x50, 0x4B, 0x03, 0x04])],
        [".doc"] = [(0, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])],
        [".xls"] = [(0, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])],
        [".ppt"] = [(0, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])],
        [".7z"] = [(0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])],
        [".rar"] = [(0, "Rar!"u8.ToArray())],
        [".exe"] = [(0, "MZ"u8.ToArray())],
        [".dll"] = [(0, "MZ"u8.ToArray())],
        [".mp3"] = [(0, "ID3"u8.ToArray()), (0, [0xFF, 0xFB]), (0, [0xFF, 0xF3]), (0, [0xFF, 0xF2])],
        [".mp4"] = [(4, "ftyp"u8.ToArray())],
        [".mov"] = [(4, "ftyp"u8.ToArray()), (4, "moov"u8.ToArray()), (4, "wide"u8.ToArray())],
        [".m4a"] = [(4, "ftyp"u8.ToArray())],
        [".wav"] = [(0, "RIFF"u8.ToArray())],
        [".avi"] = [(0, "RIFF"u8.ToArray())],
        [".sqlite"] = [(0, "SQLite format 3\0"u8.ToArray())],
        [".db"] = [(0, "SQLite format 3\0"u8.ToArray())],
    };

    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".json", ".md", ".xml", ".ini", ".cfg", ".html", ".htm", ".css", ".js", ".ts", ".cs", ".py", ".java", ".c", ".cpp", ".h", ".sql", ".yml", ".yaml", ".bat", ".ps1", ".sh", ".tsv", ".rtf",
    };

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };

    public static PreviewKind PreviewOf(string extension) =>
        ImageTypes.Contains(extension) ? PreviewKind.Image : TextTypes.Contains(extension) && extension != ".rtf" ? PreviewKind.Text : PreviewKind.None;

    /// <summary>O começo do arquivo combina com a extensão? Tipos sem assinatura conhecida dão <see cref="SignatureCheck.Unknown"/>; textos conferem se parecem texto.</summary>
    public static SignatureCheck Check(string extension, ReadOnlySpan<byte> head)
    {
        if (head.Length == 0) return SignatureCheck.Unknown;
        if (Table.TryGetValue(extension, out var signatures))
        {
            foreach (var (offset, magic) in signatures)
                if (head.Length >= offset + magic.Length && head.Slice(offset, magic.Length).SequenceEqual(magic)) return Verify(extension, head);
            return SignatureCheck.Mismatch;
        }
        if (TextTypes.Contains(extension)) return LooksLikeText(head) ? SignatureCheck.Match : SignatureCheck.Mismatch;
        return SignatureCheck.Unknown;
    }

    // RIFF serve a WAV e a AVI: o tipo vem logo depois
    private static SignatureCheck Verify(string extension, ReadOnlySpan<byte> head)
    {
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return head.Length >= 12 && head.Slice(8, 4).SequenceEqual("WAVE"u8) ? SignatureCheck.Match : SignatureCheck.Mismatch;
        if (extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)) return head.Length >= 12 && head.Slice(8, 4).SequenceEqual("AVI "u8) ? SignatureCheck.Match : SignatureCheck.Mismatch;
        return SignatureCheck.Match;
    }

    /// <summary>Sem bytes nulos e quase só caracteres imprimíveis (UTF-8 ou ANSI).</summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0) return false;
        if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF))) return true; // UTF-16 com marca
        int odd = 0;
        foreach (byte b in head)
        {
            if (b == 0) return false;
            if (b < 9 || (b > 13 && b < 32)) odd++;
        }
        return odd * 50 <= head.Length;
    }

    public static bool AllZero(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0) return false;
        foreach (byte b in head) if (b != 0) return false;
        return true;
    }
}

/// <summary>Regras de segurança do destino e dos nomes dos arquivos recuperados.</summary>
public static class NtfsRecoveryRules
{
    private static readonly Regex DriveFolder = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);
    private static readonly string[] Reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Erro do destino, ou null se serve. Tem de ser pasta com letra de unidade, sem "..", e em OUTRA unidade: gravar na mesma poderia
    /// sobrescrever justamente os clusters que se quer recuperar. <paramref name="resolvedDest"/> é o caminho depois de seguir links e junções.
    /// </summary>
    public static string? CheckDestination(string sourceLetter, string dest, string? resolvedDest)
    {
        if (sourceLetter.Length != 1 || !char.IsAsciiLetter(sourceLetter[0])) return "Escolha a unidade de onde recuperar";
        if (!DriveFolder.IsMatch(dest) || dest.Contains("..")) return "Escolha uma pasta de destino válida (com a letra da unidade)";
        char src = char.ToUpperInvariant(sourceLetter[0]);
        if (char.ToUpperInvariant(dest[0]) == src) return "O destino tem de ficar em OUTRA unidade: gravar na mesma apagaria de vez o que se quer recuperar";
        if (resolvedDest is { Length: >= 2 } && resolvedDest[1] == ':' && char.ToUpperInvariant(resolvedDest[0]) == src)
            return "O destino leva para a unidade de origem (por um link ou junção): escolha outra pasta";
        return null;
    }

    /// <summary>Um nome seguro para o Windows: sem caracteres proibidos, sem nome reservado (CON, NUL…), sem ponto ou espaço no fim, e curto.</summary>
    public static string SafeName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in name ?? "")
            sb.Append(c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c);
        string s = sb.ToString().TrimEnd('.', ' ').TrimStart(' ');
        if (s.Length == 0) return "arquivo";
        string stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        if (Reserved.Contains(stem.Trim(), StringComparer.OrdinalIgnoreCase)) s = "_" + s;
        if (s.Length > 150)
        {
            string ext = System.IO.Path.GetExtension(s);
            if (ext.Length > 20) ext = "";
            s = s[..(150 - ext.Length)] + ext;
        }
        return s;
    }

    /// <summary>Caminho relativo seguro: cada pedaço passa por <see cref="SafeName"/>, e "." e ".." somem. Vazio fica vazio.</summary>
    public static string SafeFolder(string? folder)
    {
        var parts = (folder ?? "").Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0 && p != "." && p != "..").Select(SafeName).Take(32);
        return string.Join('\\', parts);
    }

    /// <summary>Se o caminho já existe, acrescenta " (2)", " (3)"… antes da extensão. Nunca devolve um caminho que existe.</summary>
    public static string UniquePath(string path, Func<string, bool> exists)
    {
        if (!exists(path)) return path;
        string dir = System.IO.Path.GetDirectoryName(path) ?? "", stem = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        for (int n = 2; n < 100_000; n++)
        {
            string candidate = System.IO.Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!exists(candidate)) return candidate;
        }
        return System.IO.Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    /// <summary>Espaço que o destino precisa ter: o total, mais 2% e 64 MB de folga.</summary>
    public static long SpaceNeeded(long totalBytes) => totalBytes + totalBytes / 50 + (64L << 20);

    public static List<string> Warnings(DriveFacts source)
    {
        var list = RecoveryRules.Warnings(source.IsSystem, source.SolidState, RecoveryMode.Regular);
        list.Add("O app só lê a unidade de origem; ele grava apenas na pasta de destino.");
        return list;
    }
}

public sealed record RecoveredFile(string Name, string Path, long Bytes, bool Complete, string? Note);

/// <summary>Copia os dados de um arquivo apagado do volume para um fluxo, só lendo do volume.</summary>
public static class NtfsRecoverer
{
    private const int ChunkBytes = 1 << 20;

    /// <summary>
    /// Escreve até <paramref name="limit"/> bytes do arquivo (o tamanho todo se for 0). Os bytes além do "tamanho inicializado" são zeros por
    /// definição e saem como zeros, sem ler o disco. Devolve quantos bytes escreveu e se foi o arquivo inteiro.
    /// </summary>
    public static (long Written, bool Complete) CopyTo(IVolumeReader volume, NtfsGeometry geo, DeletedFile file, Stream output, CancellationToken ct, long limit = 0)
    {
        long want = limit > 0 ? Math.Min(limit, file.Size) : file.Size;
        if (file.Limit != NtfsLimit.None) throw new NtfsException("Este arquivo não pode ser recuperado pelo app.");
        if (file.ResidentData is { } inside)
        {
            int n = (int)Math.Min(inside.Length, want);
            output.Write(inside, 0, n);
            return (n, n == file.Size);
        }

        long written = 0, readable = Math.Min(want, file.InitializedSize);
        var buffer = new byte[ChunkBytes];
        foreach (var e in file.Extents.OrderBy(e => e.Vcn))
        {
            if (written >= want) break;
            long bytes = e.Clusters * geo.BytesPerCluster;
            if (e.Lcn >= 0 && e.Lcn + e.Clusters > geo.TotalClusters) throw new NtfsException("Os dados do arquivo apontam para fora da unidade.");
            for (long done = 0; done < bytes && written < want; )
            {
                ct.ThrowIfCancellationRequested();
                int chunk = (int)Math.Min(buffer.Length, Math.Min(bytes - done, want - written));
                if (e.Lcn < 0 || written >= readable)
                {
                    Array.Clear(buffer, 0, chunk); // buraco do arquivo esparso, ou depois do que o NTFS chegou a gravar
                }
                else
                {
                    int toRead = (int)Math.Min(chunk, readable - written);
                    int got = volume.Read(e.Lcn * geo.BytesPerCluster + done, buffer.AsSpan(0, toRead));
                    if (got < toRead) { output.Write(buffer, 0, got); return (written + got, false); }
                    if (toRead < chunk) Array.Clear(buffer, toRead, chunk - toRead);
                }
                output.Write(buffer, 0, chunk);
                written += chunk;
                done += chunk;
            }
        }
        return (written, written >= file.Size);
    }
}

/// <summary>Recupera vários arquivos para uma pasta de destino, refazendo as subpastas. Nunca sobrescreve um arquivo que já existe.</summary>
public static class NtfsRecoveryJob
{
    public static List<RecoveredFile> Run(IVolumeReader volume, NtfsScan scan, IReadOnlyList<DeletedFile> files, string outRoot,
        IProgress<(int Done, int Total, string Name)>? progress, CancellationToken ct)
    {
        var results = new List<RecoveredFile>();
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            progress?.Report((i, files.Count, f.Name));
            ct.ThrowIfCancellationRequested();
            string folder = NtfsRecoveryRules.SafeFolder(scan.Folders.PathOf(f.Parent, f.ParentSequence));
            string dir = folder.Length == 0 ? outRoot : System.IO.Path.Combine([outRoot, .. folder.Split('\\')]);
            string path = "";
            try
            {
                Directory.CreateDirectory(dir);
                path = NtfsRecoveryRules.UniquePath(System.IO.Path.Combine(dir, NtfsRecoveryRules.SafeName(f.Name)), p => File.Exists(p) || Directory.Exists(p));
                long written;
                bool complete;
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    (written, complete) = NtfsRecoverer.CopyTo(volume, scan.Geometry, f, output, ct);
                    output.Flush(true);
                }
                if (f.Modified is { } time) { try { File.SetLastWriteTimeUtc(path, time); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException) { } }
                results.Add(new RecoveredFile(f.Name, path, written, complete, complete ? null : "Só parte do arquivo pôde ser lida"));
            }
            catch (OperationCanceledException)
            {
                if (path.Length > 0) TryDelete(path); // arquivo pela metade por cancelamento não fica
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NtfsException)
            {
                if (path.Length > 0) TryDelete(path);
                results.Add(new RecoveredFile(f.Name, path, 0, false, ex.Message));
            }
        }
        progress?.Report((files.Count, files.Count, ""));
        return results;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
