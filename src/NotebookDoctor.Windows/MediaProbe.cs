using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 12, leva 12.1: câmera, microfone e conexão para chamadas. O Windows anota em
/// HKCU\...\CapabilityAccessManager\ConsentStore quando cada aplicativo começou e parou de usar a câmera e o microfone
/// (é de onde vem o ícone de privacidade da barra de tarefas). Só leitura.
/// </summary>
public static class MediaProbe
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    /// <summary>Quem usou (ou está usando) a câmera e o microfone, do mais recente para o mais antigo.</summary>
    public static List<MediaUse> Uses()
    {
        var list = new List<MediaUse>();
        foreach (var kind in new[] { "webcam", "microphone" })
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{kind}");
                if (root is null) continue;
                Read(root, kind, nonPackaged: false, list);
                using var np = root.OpenSubKey("NonPackaged");
                if (np is not null) Read(np, kind, nonPackaged: true, list);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return list.OrderByDescending(u => u.InUse).ThenByDescending(u => u.Start).ToList();
    }

    private static void Read(RegistryKey parent, string kind, bool nonPackaged, List<MediaUse> list)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (!nonPackaged && name == "NonPackaged") continue;
            using var k = parent.OpenSubKey(name);
            if (k is null) continue;
            var start = Meeting.FromFileTime(k.GetValue("LastUsedTimeStart") is long s ? s : 0);
            if (start is null) continue;
            var stop = Meeting.FromFileTime(k.GetValue("LastUsedTimeStop") is long t ? t : 0);
            list.Add(new MediaUse(kind, Meeting.AppName(name, nonPackaged), start, stop));
        }
    }

    /// <summary>Acesso ao recurso negado para todo mundo (no usuário ou no computador).</summary>
    public static bool GloballyDenied(string kind) =>
        Value(Registry.CurrentUser, $@"{ConsentStore}\{kind}") == "Deny" || Value(Registry.LocalMachine, $@"{ConsentStore}\{kind}") == "Deny";

    /// <summary>Aplicativos da área de trabalho (não da Loja) proibidos de usar o recurso.</summary>
    public static bool DesktopAppsDenied(string kind) => Value(Registry.CurrentUser, $@"{ConsentStore}\{kind}\NonPackaged") == "Deny";

    private static string? Value(RegistryKey hive, string path)
    {
        try { using var k = hive.OpenSubKey(path); return k?.GetValue("Value") as string; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();

    /// <summary>Quantos dispositivos de gravação (microfones) o Windows enxerga agora.</summary>
    public static int CaptureDevices()
    {
        try { return (int)waveInGetNumDevs(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
    }

    /// <summary>Fones Bluetooth conectados com o perfil Hands-Free (o que deixa o som mono quando o microfone abre).</summary>
    public static List<string> HandsFreeDevices() =>
        WmiReader.Query("SELECT Name FROM Win32_PnPEntity WHERE Status = 'OK' AND Name LIKE '%Hands-Free%'")
            .Select(d => WmiReader.Str(d, "Name").Replace("Hands-Free AG Audio", "").Replace("Hands-Free", "").Trim())
            .Where(n => n.Length > 0).Distinct().ToList();

    /// <summary>Roteador (gateway IPv4) da placa ativa; null sem rede.</summary>
    public static string? Gateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))?.ToString();

    /// <summary>Pings seguidos (null = sem resposta), um a cada 200 ms, para medir variação e perda.</summary>
    public static async Task<List<long?>> PingSeries(string host, int count, CancellationToken ct = default)
    {
        var list = new List<long?>();
        using var ping = new Ping();
        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(host, 1000);
                list.Add(r.Status == IPStatus.Success ? r.RoundtripTime : null);
            }
            catch (PingException) { list.Add(null); }
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
        }
        return list;
    }
}
