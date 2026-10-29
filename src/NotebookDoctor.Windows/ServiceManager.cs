using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// M30: lê os serviços do registro (HKLM\SYSTEM\CurrentControlSet\Services) e muda o tipo de início.
/// Mudar exige administrador; o serviço não é parado nem apagado, só deixa de abrir sozinho no próximo boot.
/// </summary>
public static class ServiceManager
{
    private const string Root = @"SYSTEM\CurrentControlSet\Services";

    public static string SystemRoot => Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

    public static List<ServiceInfo> List()
    {
        var list = new List<ServiceInfo>();
        using var root = Registry.LocalMachine.OpenSubKey(Root);
        if (root is null) return list;
        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var k = root.OpenSubKey(name);
                // 0x10 = processo próprio, 0x20 = compartilhado; drivers (1, 2) e modelos de serviço por usuário (0x40+) ficam de fora
                if (k?.GetValue("Type") is not int type || (type & 0x30) == 0 || (type & 0x40) != 0) continue;
                if (k.GetValue("ImagePath") is not string image || k.GetValue("Start") is not int start) continue;
                string display = k.GetValue("DisplayName") as string ?? name;
                if (display.StartsWith('@')) display = name; // texto em recurso de DLL: o nome curto basta
                list.Add(new ServiceInfo(name, display, image, start, k.GetValue("DelayedAutostart") is int d && d == 1));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { /* chave sem permissão de leitura */ }
        }
        return list;
    }

    /// <summary>Muda o tipo de início (2 automático, 3 manual). Lança UnauthorizedAccessException sem administrador.</summary>
    public static void SetStart(string name, int start)
    {
        if (start is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(start));
        using var k = Registry.LocalMachine.OpenSubKey($@"{Root}\{name}", writable: true) ?? throw new InvalidOperationException($"Serviço {name} não encontrado.");
        k.SetValue("Start", start, RegistryValueKind.DWord);
    }
}

