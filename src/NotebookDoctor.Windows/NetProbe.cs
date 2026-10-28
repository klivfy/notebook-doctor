using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 12, leva 12.2: mede a rede para o reparo em etapas, compara servidores de DNS, troca o DNS (com o de antes guardado
/// para o Desfazer), lista os pontos de acesso Wi-Fi e as conexões de cada programa. Os pings e consultas de DNS só
/// acontecem quando a pessoa clica.
/// </summary>
public static class NetProbe
{
    /// <summary>A placa ativa (Wi-Fi ou cabo) com gateway: nome, endereço IPv4, gateway e DNS de agora.</summary>
    public static (string Name, string Id, string? Ip, string? Gateway, List<string> Dns)? ActiveAdapter()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .FirstOrDefault();
            if (nic is null) return null;
            var p = nic.GetIPProperties();
            string? ip = p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            string? gw = p.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))?.Address.ToString();
            var dns = p.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).ToList();
            return (nic.Name, nic.Id, ip, gw, dns);
        }
        catch (NetworkInformationException) { return null; }
    }

    private static async Task<bool> PingOk(string host, int tries = 3)
    {
        using var ping = new Ping();
        for (int i = 0; i < tries; i++)
        {
            try { if ((await ping.SendPingAsync(host, 1500)).Status == IPStatus.Success) return true; }
            catch (PingException) { }
        }
        return false;
    }

    /// <summary>As 6 etapas, em ordem. Depois da primeira falha, as seguintes ainda são medidas (ajudam a explicar).</summary>
    public static async Task<List<NetCheck>> CheckAsync()
    {
        var list = new List<NetCheck>();
        var a = ActiveAdapter();
        list.Add(new NetCheck(NetStep.Adapter, a is not null, a?.Name ?? ""));
        bool ipOk = a?.Ip is { } ip && !NetworkDoctor.IsApipa(ip);
        list.Add(new NetCheck(NetStep.Address, ipOk, a?.Ip ?? ""));
        list.Add(new NetCheck(NetStep.Router, a?.Gateway is { } gw && await PingOk(gw), a?.Gateway ?? ""));
        list.Add(new NetCheck(NetStep.Internet, await PingOk("1.1.1.1") || await PingOk("8.8.8.8"), "1.1.1.1"));
        bool dns;
        try { dns = (await Dns.GetHostAddressesAsync("klivfy.com")).Length > 0; }
        catch (SocketException) { dns = false; }
        list.Add(new NetCheck(NetStep.Dns, dns, "klivfy.com"));
        string? proxy = Proxy();
        list.Add(new NetCheck(NetStep.Proxy, proxy is null, proxy ?? ""));
        return list;
    }

    /// <summary>Proxy ou script de configuração (PAC) ligado para o usuário; null sem proxy.</summary>
    public static string? Proxy()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (k?.GetValue("AutoConfigURL") is string pac && pac.Length > 0) return pac;
            if (k?.GetValue("ProxyEnable") is int on && on == 1 && k.GetValue("ProxyServer") is string server && server.Length > 0) return server;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return null;
    }

    private static int Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe), args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            return p.WaitForExit(60_000) ? p.ExitCode : -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return -1; }
    }

    public static bool FlushDns() => Run("ipconfig.exe", "/flushdns") == 0;

    /// <summary>Pede um endereço novo ao roteador. Costuma exigir administrador.</summary>
    public static bool RenewIp() => Run("ipconfig.exe", "/renew") == 0;

    // ---------- DNS ----------

    /// <summary>Tempo de uma consulta de DNS (registro A) direto no servidor, por UDP; null quando não respondeu em 1,5 s.</summary>
    public static async Task<double?> QueryMs(string server, string domain)
    {
        var query = new List<byte> { 0x4E, 0x44, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        foreach (var label in domain.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }
        query.AddRange(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01 });
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(query.ToArray(), query.Count, new IPEndPoint(IPAddress.Parse(server), 53));
            using var cts = new CancellationTokenSource(1500);
            var reply = await udp.ReceiveAsync(cts.Token);
            return reply.Buffer.Length >= 12 && reply.Buffer[0] == 0x4E && reply.Buffer[1] == 0x44 ? Math.Round(sw.Elapsed.TotalMilliseconds, 1) : null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or FormatException) { return null; }
    }

    /// <summary>Compara o DNS atual com os públicos: 3 sites, 2 vezes cada, e fica a mediana.</summary>
    public static async Task<List<DnsResult>> BenchmarkAsync(string? current)
    {
        var servers = new List<(string Name, string Ip)>();
        if (current is not null) servers.Add((Loc.T("Atual"), current));
        servers.AddRange(NetworkDoctor.PublicDns.Where(d => d.Primary != current).Select(d => (d.Name, d.Primary)));
        string[] domains = ["klivfy.com", "github.com", "wikipedia.org"];
        var results = new List<DnsResult>();
        foreach (var (name, ip) in servers)
        {
            var times = new List<double?>();
            foreach (var d in domains) for (int i = 0; i < 2; i++) times.Add(await QueryMs(ip, d));
            results.Add(new DnsResult(name, ip, NetworkDoctor.Median(times)));
        }
        return results;
    }

    /// <summary>DNS fixo da placa (vazio = automático, pelo roteador). Lido do registro, como o Windows guarda.</summary>
    public static string StaticDns(string adapterId)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterId}");
            return (k?.GetValue("NameServer") as string ?? "").Replace(' ', ',').Trim(',');
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    /// <summary>
    /// Troca o DNS da placa: "servers" com dois endereços separados por vírgula, ou vazio para voltar ao automático. Exige
    /// administrador (o netsh devolve erro sem ele).
    /// </summary>
    public static bool SetDns(string adapterName, string servers)
    {
        if (adapterName.Contains('"')) return false;
        var list = servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => IPAddress.TryParse(s, out _)).ToList();
        if (list.Count == 0) return Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{adapterName}\" source=dhcp") == 0;
        if (Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{adapterName}\" source=static address={list[0]} register=primary validate=no") != 0) return false;
        for (int i = 1; i < list.Count; i++) Run("netsh.exe", $"interface ipv4 add dnsservers name=\"{adapterName}\" address={list[i]} index={i + 1} validate=no");
        FlushDns();
        return true;
    }

    // ---------- Wi-Fi ----------

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetNetworkBssList(IntPtr handle, ref Guid iface, IntPtr ssid, int bssType, bool securityEnabled, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);

    /// <summary>
    /// Pontos de acesso que o Wi-Fi está vendo (da última busca do Windows): rede, frequência e qualidade. Os deslocamentos
    /// de WLAN_BSS_ENTRY (360 bytes) são fixos desde o Windows Vista.
    /// </summary>
    public static List<WifiBss> VisibleBss()
    {
        var result = new List<WifiBss>();
        IntPtr handle = IntPtr.Zero, ifaces = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return result;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out ifaces) != 0 || ifaces == IntPtr.Zero) return result;
            int count = Marshal.ReadInt32(ifaces);
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                var guid = Marshal.PtrToStructure<Guid>(ifaces + 8 + i * 532);
                if (WlanGetNetworkBssList(handle, ref guid, IntPtr.Zero, 1 /* infraestrutura */, false, IntPtr.Zero, out IntPtr list) != 0 || list == IntPtr.Zero) continue;
                try
                {
                    int n = Marshal.ReadInt32(list, 4);
                    for (int j = 0; j < Math.Min(n, 256); j++)
                    {
                        IntPtr e = list + 8 + j * 360;
                        int len = Math.Clamp(Marshal.ReadInt32(e), 0, 32);
                        var ssid = new byte[len];
                        Marshal.Copy(e + 4, ssid, 0, len);
                        result.Add(new WifiBss(Encoding.UTF8.GetString(ssid), (uint)Marshal.ReadInt32(e, 92), Math.Clamp(Marshal.ReadInt32(e, 60), 0, 100)));
                    }
                }
                finally { WlanFreeMemory(list); }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException) { }
        finally
        {
            if (ifaces != IntPtr.Zero) WlanFreeMemory(ifaces);
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
        return result;
    }

    /// <summary>Todas as placas, com a descrição e se estão conectadas (para achar VPNs que sobraram).</summary>
    public static List<(string Description, bool Up)> Adapters()
    {
        try { return NetworkInterface.GetAllNetworkInterfaces().Select(n => (n.Description, n.OperationalStatus == OperationalStatus.Up)).ToList(); }
        catch (NetworkInformationException) { return []; }
    }

    // ---------- conexões por programa ----------

    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);

    private static int Port(IntPtr row, int offset) => (Marshal.ReadByte(row, offset) << 8) | Marshal.ReadByte(row, offset + 1);

    /// <summary>Conexões TCP dos processos indicados (IPv4 e IPv6). Não exige administrador.</summary>
    public static List<TcpConn> TcpConnections(IReadOnlySet<int> pids)
    {
        var list = new List<TcpConn>();
        foreach (var (family, rowSize) in new[] { (2, 24), (23, 56) })
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
            if (size <= 0) continue;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, family, 5, 0) != 0) continue;
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr row = buf + 4 + i * rowSize;
                    if (family == 2)
                    {
                        int pid = Marshal.ReadInt32(row, 20);
                        if (!pids.Contains(pid)) continue;
                        string local = new IPAddress((uint)Marshal.ReadInt32(row, 4)).ToString(), remote = new IPAddress((uint)Marshal.ReadInt32(row, 12)).ToString();
                        list.Add(new TcpConn(pid, local, Port(row, 8), remote, Port(row, 16), NetworkDoctor.StateName(Marshal.ReadInt32(row, 0))));
                    }
                    else
                    {
                        int pid = Marshal.ReadInt32(row, 52);
                        if (!pids.Contains(pid)) continue;
                        var la = new byte[16]; Marshal.Copy(row, la, 0, 16);
                        var ra = new byte[16]; Marshal.Copy(row + 24, ra, 0, 16);
                        list.Add(new TcpConn(pid, new IPAddress(la).ToString(), Port(row, 20), new IPAddress(ra).ToString(), Port(row, 44), NetworkDoctor.StateName(Marshal.ReadInt32(row, 48))));
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return list.OrderBy(c => c.State != "ESTABLISHED").ThenBy(c => c.Remote).ToList();
    }
}
