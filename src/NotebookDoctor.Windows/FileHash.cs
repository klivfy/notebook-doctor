using System.Security.Cryptography;

namespace NotebookDoctor.Services;

/// <summary>SHA-256 de um arquivo, inteiro ou só de uma amostra (início e fim). Null quando não dá para ler (em uso, sem permissão, sumiu).</summary>
public static class FileHash
{
    private const int Sample = 256 * 1024;

    public static string? Of(string path, bool sampleOnly, CancellationToken ct = default)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            using var sha = SHA256.Create();
            if (sampleOnly)
            {
                var edge = new byte[Sample];
                int n = fs.Read(edge, 0, Sample);
                sha.TransformBlock(edge, 0, n, null, 0);
                if (fs.Length > Sample * 2)
                {
                    fs.Seek(-Sample, SeekOrigin.End);
                    n = fs.Read(edge, 0, Sample);
                    sha.TransformBlock(edge, 0, n, null, 0);
                }
            }
            else
            {
                var buf = new byte[1 << 16];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0) { ct.ThrowIfCancellationRequested(); sha.TransformBlock(buf, 0, n, null, 0); }
            }
            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
