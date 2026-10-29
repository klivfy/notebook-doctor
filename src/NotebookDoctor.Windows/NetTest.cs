using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace NotebookDoctor.Services;

/// <summary>
/// Teste de conexão (só quando o usuário pede): latência até o roteador e até a internet, perda de pacotes e tempo de DNS.
/// Não mede velocidade de download para não gastar a franquia nem depender de serviço de terceiros.
/// </summary>
public static class NetTest
{
    public static async Task<List<string>> RunAsync()
    {
        var lines = new List<string>();
        var gateway = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));

        if (gateway is not null) lines.Add(await PingLine(Loc.T("Roteador"), gateway.ToString()));
        else lines.Add(Loc.T("Roteador: não encontrado (sem rede?)"));
        lines.Add(await PingLine(Loc.T("Internet (Cloudflare 1.1.1.1)"), "1.1.1.1"));
        lines.Add(await PingLine(Loc.T("Internet (Google 8.8.8.8)"), "8.8.8.8"));

        foreach (var host in new[] { "klivfy.com", "github.com", "wikipedia.org" })
        {
            var sw = Stopwatch.StartNew();
            try { await Dns.GetHostAddressesAsync(host); lines.Add(Loc.F("DNS {0}: {1} ms", host, sw.ElapsedMilliseconds)); }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException or InvalidOperationException) { lines.Add(Loc.F("DNS {0}: falhou", host)); }
        }
        return lines;
    }

    private static async Task<string> PingLine(string label, string host)
    {
        using var ping = new Ping();
        var times = new List<long>();
        int lost = 0;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(host, 1500);
                if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
            }
            catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException or InvalidOperationException) { lost++; }
        }
        if (times.Count == 0) return Loc.F("{0}: sem resposta", label);
        string verdict = Loc.T(times.Average() < 30 ? "ótimo" : times.Average() < 80 ? "bom" : times.Average() < 150 ? "lento" : "muito lento");
        return Loc.F("{0}: {1:0} ms (mín {2} / máx {3}), perda {4}% · {5}", label, times.Average(), times.Min(), times.Max(), lost * 20, verdict);
    }
}

