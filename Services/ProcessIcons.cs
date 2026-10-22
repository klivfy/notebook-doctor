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

    private static ImageSource? Load(string name)
    {
        string? path = ExePath(name);
        if (path is null) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            src.Freeze(); // permite usar na thread de interface
            return src;
        }
        catch { return null; }
    }

    /// <summary>Caminho do executável do primeiro processo com esse nome que o Windows deixa consultar.</summary>
    public static string? ExePath(string name)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            using (p)
            {
                // PROCESS_QUERY_LIMITED_INFORMATION funciona até em processos de outros usuários sem administrador
                IntPtr h = Native.OpenProcess(0x1000, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref size)) return sb.ToString();
                }
                finally { Native.CloseHandle(h); }
            }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
