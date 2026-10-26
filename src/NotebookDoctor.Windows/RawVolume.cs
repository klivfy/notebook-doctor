using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 8.5: o volume lido por baixo do sistema de arquivos (<c>\\.\X:</c>), SÓ para leitura. O handle é aberto com <c>GENERIC_READ</c> e
/// nunca se chama escrita nele; o Windows exige que offset e tamanho sejam múltiplos do setor, então a leitura é alinhada aqui dentro
/// e quem chama pode pedir qualquer faixa. Exige administrador.
/// </summary>
public sealed class RawVolume : IVolumeReader, IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle file, long distance, out long newPosition, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int toRead, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private readonly SafeFileHandle _handle;
    private readonly object _lock = new();
    private readonly int _align;

    public long Length { get; }

    /// <exception cref="UnauthorizedAccessException">Abrir o volume exige administrador.</exception>
    public RawVolume(char letter)
    {
        _handle = VolumeApi.OpenVolume(letter);
        var info = new byte[8];
        if (!DeviceIoControl(_handle, IoctlDiskGetLengthInfo, IntPtr.Zero, 0, info, info.Length, out _, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(err);
        }
        Length = BitConverter.ToInt64(info, 0);
        // Discos de setor de 4 KB exigem leituras de 4 KB; os de 512 aceitam as duas. O comprimento do volume é sempre múltiplo do setor lógico.
        _align = Length % 4096 == 0 ? 4096 : 512;
    }

    public int Read(long offset, Span<byte> buffer)
    {
        if (buffer.Length == 0 || offset < 0 || offset >= Length) return 0;
        long start = offset & ~(long)(_align - 1);
        long end = Math.Min(Length, (offset + buffer.Length + _align - 1) & ~(long)(_align - 1));
        int span = (int)(end - start);
        var temp = new byte[span];
        int got;
        lock (_lock)
        {
            if (!SetFilePointerEx(_handle, start, out _, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!ReadFile(_handle, temp, span, out got, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        int skip = (int)(offset - start);
        if (got <= skip) return 0;
        int n = Math.Min(buffer.Length, got - skip);
        temp.AsSpan(skip, n).CopyTo(buffer);
        return n;
    }

    public void Dispose() => _handle.Dispose();
}
