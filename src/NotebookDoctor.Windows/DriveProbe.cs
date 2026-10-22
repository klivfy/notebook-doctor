using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>Leva 8.2: o que o Windows diz de cada unidade (tipo, barramento, se é SSD) e o <c>format.com</c> com progresso.</summary>
public static class DriveProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    private const uint IoctlStorageQueryProperty = 0x002D1400, OpenExisting = 3, ShareReadWrite = 3;
    private const int PropertyDevice = 0, PropertySeekPenalty = 7;

    private static byte[]? Query(char letter, int propertyId, int outputSize)
    {
        using var h = CreateFileW($@"\\.\{char.ToUpperInvariant(letter)}:", 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var input = new byte[12];                        // STORAGE_PROPERTY_QUERY: PropertyId, QueryType = 0 (padrão), 1 byte extra
        BitConverter.GetBytes(propertyId).CopyTo(input, 0);
        var output = new byte[outputSize];
        return DeviceIoControl(h, IoctlStorageQueryProperty, input, input.Length, output, output.Length, out int got, IntPtr.Zero) && got > 0 ? output : null;
    }

    /// <summary>true = sem penalidade de busca (SSD, flash); false = disco giratório; null = o dispositivo não respondeu.</summary>
    public static bool? IsSolidState(char letter)
    {
        var o = Query(letter, PropertySeekPenalty, 64);
        return o is null ? null : o[8] == 0; // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
    }

    /// <summary>STORAGE_BUS_TYPE do dispositivo (7 = USB, 12 = SD…); 0 se não respondeu.</summary>
    public static int BusType(char letter)
    {
        var o = Query(letter, PropertyDevice, 1024);
        return o is null ? 0 : BitConverter.ToInt32(o, 28);
    }

    public static List<DriveFacts> List(IEnumerable<string> appFolders)
    {
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var appRoots = appFolders.Select(f => Path.GetPathRoot(f) ?? "").Where(r => r.Length > 0).ToList();
        var list = new List<DriveFacts>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                var kind = d.DriveType switch
                {
                    System.IO.DriveType.Fixed => DriveKind.Fixed, System.IO.DriveType.Removable => DriveKind.Removable, System.IO.DriveType.Network => DriveKind.Network,
                    System.IO.DriveType.CDRom => DriveKind.CdRom, System.IO.DriveType.Ram => DriveKind.Ram, _ => DriveKind.Unknown,
                };
                char letter = d.Name[0];
                bool ready = d.IsReady;
                bool local = kind is DriveKind.Fixed or DriveKind.Removable;
                list.Add(new DriveFacts(letter.ToString(), kind, ready, string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase),
                    appRoots.Any(r => string.Equals(r, d.Name, StringComparison.OrdinalIgnoreCase)),
                    local && ready ? BusType(letter) : 0, ready ? d.DriveFormat : "", ready ? d.VolumeLabel : "",
                    ready ? d.TotalSize : 0, ready ? d.AvailableFreeSpace : 0, local && ready ? IsSolidState(letter) : null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unidade que sumiu no meio da leitura */ }
        }
        return list;
    }
}

/// <summary>Roda o <c>format.com</c> da unidade escolhida. Só aceita uma unidade que passou em <see cref="DriveWipeRules.Check"/>.</summary>
public static class DriveFormatter
{
    /// <exception cref="InvalidOperationException">A unidade não passa nas regras.</exception>
    /// <returns>Código de saída do format.com (0 = terminou).</returns>
    public static async Task<int> FormatAsync(DriveFacts drive, IProgress<int>? percent)
    {
        // Última conferência, na hora de formatar: a unidade na letra tem de ser a mesma que a pessoa confirmou (nome, tamanho e sistema de arquivos)
        var now = DriveProbe.List([]).FirstOrDefault(d => d.Letter.Equals(drive.Letter, StringComparison.OrdinalIgnoreCase));
        if (now is null || now.Label != drive.Label || now.TotalBytes != drive.TotalBytes || !now.FileSystem.Equals(drive.FileSystem, StringComparison.OrdinalIgnoreCase)
            || now.BusType != drive.BusType || now.IsSystem != drive.IsSystem)
            throw new InvalidOperationException("A unidade mudou depois da confirmação.");
        var args = DriveWipeRules.FormatArgs(now with { HostsApp = drive.HostsApp });
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "format.com"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui iniciar o format.com.");
        p.OutputDataReceived += (_, e) => { if (e.Data is not null && DriveWipeRules.ParseProgress(e.Data) is { } n) percent?.Report(n); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        // Em disco "fixo" (HD externo), o format pede o nome atual do volume antes de começar; responde-se com o nome que o Windows informa
        await p.StandardInput.WriteLineAsync(drive.Label);
        p.StandardInput.Close();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
