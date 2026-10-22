using System.Runtime.InteropServices;

namespace NotebookDoctor.Services;

public sealed record DuplicateRemoval(long Bytes, int Files, List<string> Skipped);

/// <summary>
/// Leva 6.3: apaga cópias escolhidas de grupos de arquivos idênticos. Antes de cada arquivo, confere no disco que ele
/// ainda é idêntico a uma cópia que fica; se algo mudou desde a busca, o arquivo é pulado. Um grupo nunca perde a última cópia.
/// </summary>
public static class DuplicateRemover
{
    /// <param name="permanent">Falso: Lixeira (o Windows avisa se o arquivo for grande demais para ela).</param>
    public static DuplicateRemoval Remove(IEnumerable<(DuplicateGroup Group, IReadOnlyList<string> Chosen)> work, KnownPlaces places, KeepRule fallback, bool permanent, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0;
        var skipped = new List<string>();
        foreach (var (group, chosen) in work)
        {
            ct.ThrowIfCancellationRequested();
            var doomed = DuplicateRules.ToDelete(group, chosen, fallback);
            var survivors = group.Files.Select(f => f.Path).Where(p => !doomed.Any(d => Paths.Same(d, p))).ToList();
            // Uma cópia que fica, ainda no disco, com o tamanho do grupo: é com ela que as outras são comparadas
            string? reference = null, referenceHash = null;
            foreach (var s in survivors)
            {
                if (!File.Exists(s) || new FileInfo(s).Length != group.Size) continue;
                if (FileHash.Of(s, sampleOnly: false, ct) is { } h) { reference = s; referenceHash = h; break; }
            }
            if (reference is null) { skipped.AddRange(doomed); continue; }

            foreach (var path in doomed)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (DuplicateRules.IsSystemPath(path, places) || !CleanSafety.IsPlainPath(path)) { skipped.Add(path); continue; }
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length != group.Size) { skipped.Add(path); continue; }
                    if (FileHash.Of(path, sampleOnly: false, ct) != referenceHash) { skipped.Add(path); continue; } // mudou desde a busca
                    if (permanent)
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    else if (!Recycle(path)) { skipped.Add(path); continue; }
                    bytes += group.Size;
                    files++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped.Add(path); }
            }
        }
        return new DuplicateRemoval(bytes, files, skipped);
    }

    /// <summary>Lixeira, sem apagar de vez em silêncio: se o arquivo não couber nela, o Windows pergunta.</summary>
    private static bool Recycle(string path)
    {
        var op = new ShFileOp { wFunc = FoDelete, pFrom = path + "\0\0", fFlags = FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofNoErrorUi };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    private const uint FoDelete = 3;
    private const ushort FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOp op);
}
