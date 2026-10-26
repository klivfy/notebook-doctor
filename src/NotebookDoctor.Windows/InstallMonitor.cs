using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>Leva 5.4: fotografa o registro (só carimbos, ver <see cref="RegistrySnapshot"/>).</summary>
public static class RegistrySnapshotter
{
    /// <param name="progress">Chamado a cada 5.000 chaves com o total lido até ali.</param>
    public static RegistrySnapshot Take(IEnumerable<string> roots, Action<int>? progress = null, CancellationToken ct = default)
    {
        var snap = new RegistrySnapshot();
        int count = 0;
        foreach (var root in roots)
        {
            using var key = RegistryPaths.Open(root);
            if (key is not null) Walk(key, Paths.Clean(root));
        }
        return snap;

        void Walk(RegistryKey key, string path)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                snap.Add(path, key.GetValueNames().Select(n => (n, Stamp(key, n))));
                if (++count % 5000 == 0) progress?.Invoke(count);
                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(name);
                        if (sub is not null) Walk(sub, $@"{path}\{name}");
                    }
                    catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { } // chave sem permissão de leitura
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }

    private static string Stamp(RegistryKey key, string name)
    {
        try
        {
            var kind = key.GetValueKind(name);
            object? data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return data is null ? kind.ToString() : $"{kind}:{RegistryData.Encode(kind, data)}";
        }
        catch (Exception ex) when (ex is IOException or InvalidCastException or ArgumentException) { return "?"; } // tipo raro (REG_NONE, REG_LINK…)
    }

    /// <summary>Nomes dos valores da chave agora (para o <see cref="TraceRules.Diff"/>).</summary>
    public static IReadOnlyList<string> ValueNames(string key)
    {
        try
        {
            using var k = RegistryPaths.Open(key);
            return k?.GetValueNames() ?? [];
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return []; }
    }

    public static string? DisplayName(string uninstallKey)
    {
        using var k = RegistryPaths.Open(uninstallKey);
        return k?.GetValue("DisplayName") as string;
    }
}

/// <summary>
/// Leva 5.4: o diário de mudanças (USN) de cada unidade NTFS fixa. <see cref="Start"/> guarda onde o diário está;
/// <see cref="Changes"/> lê tudo o que aconteceu desde então e traduz para caminhos. Exige administrador.
/// </summary>
public sealed class UsnWatcher
{
    private readonly List<(string Drive, ulong JournalId, long StartUsn)> _volumes = new();

    /// <summary>Unidades sem diário ativo ou fora do NTFS ficam de fora.</summary>
    public static UsnWatcher Start()
    {
        var w = new UsnWatcher();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.DriveFormat == "NTFS"))
        {
            using var h = OpenVolume(d.Name);
            if (h is not null && Query(h) is { } q) w._volumes.Add((d.Name, q.JournalId, q.NextUsn));
        }
        return w;
    }

    public int Volumes => _volumes.Count;

    public List<(string Path, bool Directory, bool Created)> Changes(CancellationToken ct = default)
    {
        var result = new List<(string, bool, bool)>();
        foreach (var (drive, journalId, startUsn) in _volumes)
        {
            using var h = OpenVolume(drive);
            if (h is null) continue;
            var records = new List<UsnRecord>();
            long usn = startUsn;
            var buffer = new byte[1 << 16];
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var input = new ReadUsnJournalData { StartUsn = usn, ReasonMask = 0xFFFFFFFF, UsnJournalId = journalId };
                if (!DeviceIoControl(h, FsctlReadUsnJournal, ref input, Marshal.SizeOf<ReadUsnJournalData>(), buffer, buffer.Length, out int read, IntPtr.Zero) || read <= 8) break;
                var (next, list) = UsnJournal.Parse(buffer.AsSpan(0, read));
                records.AddRange(list);
                if (next <= usn) break;
                usn = next;
            }
            foreach (var c in UsnJournal.Summarize(records))
                if (PathOf(h, c.FileRef) is { } path) result.Add((path, c.Directory, c.Created));
        }
        return result;
    }

    private static SafeFileHandle? OpenVolume(string drive)
    {
        var h = CreateFile($@"\\.\{drive.TrimEnd('\\')}", GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    private static (ulong JournalId, long NextUsn)? Query(SafeFileHandle volume)
    {
        var data = new byte[64];
        return DeviceIoControl(volume, FsctlQueryUsnJournal, IntPtr.Zero, 0, data, data.Length, out _, IntPtr.Zero)
            ? (BitConverter.ToUInt64(data, 0), BitConverter.ToInt64(data, 16)) // UsnJournalID, NextUsn
            : null;
    }

    /// <summary>Caminho atual do arquivo pelo número dele no NTFS; null se ele não existe mais.</summary>
    private static string? PathOf(SafeFileHandle volume, ulong fileRef)
    {
        var id = new FileIdDescriptor { Size = Marshal.SizeOf<FileIdDescriptor>(), Type = 0, FileId = (long)fileRef };
        using var h = OpenFileById(volume, ref id, 0, FileShareReadWrite | 0x4, IntPtr.Zero, BackupSemantics);
        if (h.IsInvalid) return null;
        var sb = new StringBuilder(1024);
        int n = GetFinalPathNameByHandle(h, sb, sb.Capacity, 0);
        if (n <= 0 || n >= sb.Capacity) return null;
        string p = sb.ToString();
        return p.StartsWith(@"\\?\", StringComparison.Ordinal) ? p[4..] : p;
    }

    private const uint GenericRead = 0x80000000, FileShareReadWrite = 0x3, OpenExisting = 3, BackupSemantics = 0x02000000;
    private const uint FsctlQueryUsnJournal = 0x000900f4, FsctlReadUsnJournal = 0x000900bb;

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public int Size;
        public int Type;
        public long FileId;
        public long Padding; // o union com o ObjectId de 16 bytes
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref ReadUsnJournalData input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(SafeFileHandle volume, ref FileIdDescriptor id, uint access, uint share, IntPtr security, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int size, uint flags);
}
