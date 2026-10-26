using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotebookDoctor.Services;

/// <summary>
/// M23: ícone do executável de cada programa. Extrai fora da thread de interface e guarda em cache por nome
/// (inclusive "sem ícone", para não tentar de novo a cada atualização da tabela).
/// </summary>
public static class ProcessIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone já carregado ou null. Se ainda não foi buscado, agenda a busca e chama <paramref name="ready"/> ao terminar.</summary>
    public static ImageSource? Get(string name, Action? ready = null)
    {
        if (Cache.TryGetValue(name, out var img)) return img;
        if (Pending.TryAdd(name, 0))
            Task.Run(() =>
            {
                Cache[name] = Load(name);
                Pending.TryRemove(name, out _);
                ready?.Invoke();
            });
        return null;
    }

    /// <summary>
    /// Fase 5: ícone de um arquivo pelo índice, como no DisplayIcon do registro ("app.exe,0" ou "app.dll,-101").
    /// Mesmo cache e mesma busca em segundo plano dos processos.
    /// </summary>
    public static ImageSource? ForFile(string path, int index, Action? ready = null)
    {
        string key = $"file|{path}|{index}"; // nomes de processo nunca têm "|"
        if (Cache.TryGetValue(key, out var img)) return img;
        if (Pending.TryAdd(key, 0))
            Task.Run(() =>
            {
                Cache[key] = LoadFile(path, index);
                Pending.TryRemove(key, out _);
                ready?.Invoke();
            });
        return null;
    }

    private static ImageSource? Load(string name) => ExePath(name) is { } path ? LoadFile(path, 0) : null;

    private static ImageSource? LoadFile(string path, int index)
    {
        if (!System.IO.File.Exists(path)) return null;
        var large = new IntPtr[1];
        try
        {
            // -1 pediria a quantidade de ícones, não um ícone
            if (index != -1 && ExtractIconEx(path, index, large, null, 1) > 0 && large[0] != IntPtr.Zero) return ToImage(large[0]);
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : ToImage(icon.Handle);
        }
        catch { return null; }
        finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); }
    }

    private static ImageSource ToImage(IntPtr hIcon)
    {
        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
        src.Freeze(); // permite usar na thread de interface
        return src;
    }

    /// <summary>Caminho do executável do primeiro processo com esse nome que o Windows deixa consultar.</summary>
    public static string? ExePath(string name)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            using (p)
                if (PathOfPid(p.Id) is { } path) return path;
        return null;
    }

    /// <summary>Caminho do executável do processo; null quando o Windows não deixa consultar.</summary>
    public static string? PathOfPid(int pid)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION funciona até em processos de outros usuários sem administrador
        IntPtr h = Native.OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Native.CloseHandle(h); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
