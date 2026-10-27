using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>As 6 etapas do "conectado, mas não navega", em ordem.</summary>
public enum NetStep { Adapter, Address, Router, Internet, Dns, Proxy }

/// <summary>Resultado de uma etapa: passou, ou o que foi visto.</summary>
public sealed record NetCheck(NetStep Step, bool Ok, string Detail = "");

/// <summary>Uma conexão de rede de um programa.</summary>
public sealed record TcpConn(int Pid, string Local, int LocalPort, string Remote, int RemotePort, string State);

/// <summary>Um ponto de acesso visto pelo Wi-Fi: rede, frequência (kHz) e qualidade do sinal (0 a 100).</summary>
public sealed record WifiBss(string Ssid, long FrequencyKHz, int Quality);

/// <summary>Tempo de resposta de um servidor de DNS (null = não respondeu).</summary>
public sealed record DnsResult(string Name, string Server, double? Ms);

/// <summary>
/// Fase 12, leva 12.2: reparo de rede em etapas, escolha de DNS, banda do Wi-Fi, conexões por programa e adaptadores de
/// VPN que sobraram. Regras puras; quem mede é o NetProbe.
/// </summary>
public static class NetworkDoctor
{
    public static string StepText(NetStep s) => s switch
    {
        NetStep.Adapter => Loc.T("Placa de rede ligada"),
        NetStep.Address => Loc.T("Endereço IP recebido do roteador"),
        NetStep.Router => Loc.T("Roteador responde"),
        NetStep.Internet => Loc.T("Internet responde (1.1.1.1)"),
        NetStep.Dns => Loc.T("Nomes de sites são encontrados (DNS)"),
        _ => Loc.T("Sem proxy atrapalhando"),
    };

    /// <summary>A primeira etapa que falhou (a causa); as seguintes costumam falhar por causa dela.</summary>
    public static NetCheck? FirstFailure(IReadOnlyList<NetCheck> checks) => checks.FirstOrDefault(c => !c.Ok);

    /// <summary>O que fazer para a etapa que falhou e, quando o app pode fazer por você, o código da correção.</summary>
    public static (string Advice, string? FixId) Fix(NetStep step) => step switch
    {
        NetStep.Adapter => (Loc.T("O Wi-Fi está desligado ou o modo avião está ligado. Ligue o Wi-Fi (ou conecte o cabo)."), "open-wifi"),
        NetStep.Address => (Loc.T("O roteador não entregou um endereço ao notebook (DHCP). Pedir um endereço novo costuma resolver; se não, reinicie o roteador."), "renew"),
        NetStep.Router => (Loc.T("O notebook não alcança o roteador: o sinal do Wi-Fi está fraco ou o roteador travou. Chegue mais perto ou reinicie o roteador."), null),
        NetStep.Internet => (Loc.T("O roteador responde, mas a internet não: o problema está no provedor ou no modem. Reinicie o modem ou fale com a operadora."), null),
        NetStep.Dns => (Loc.T("A internet funciona, mas os nomes dos sites não são encontrados. Limpar o cache de DNS costuma resolver; se voltar, troque o DNS abaixo."), "flushdns"),
        _ => (Loc.T("Há um proxy configurado. Se você não o configurou (ou não está numa rede de empresa), ele pode estar bloqueando a navegação."), "open-proxy"),
    };

    /// <summary>Endereço que o Windows inventa quando o roteador não respondeu ao pedido de IP (169.254.x.x).</summary>
    public static bool IsApipa(string ip) => ip.StartsWith("169.254.", StringComparison.Ordinal);

    // ---------- DNS ----------

    public static readonly IReadOnlyList<(string Name, string Primary, string Secondary)> PublicDns =
    [
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
    ];

    /// <summary>
    /// O mais rápido dos testados, só se for bem mais rápido que o atual (20% e pelo menos 10 ms): trocar o DNS por uma
    /// diferença pequena não muda nada na prática. Null quando o atual já é bom.
    /// </summary>
    public static DnsResult? BetterThanCurrent(IReadOnlyList<DnsResult> results, string currentServer)
    {
        var current = results.FirstOrDefault(r => r.Server == currentServer);
        var best = results.Where(r => r.Ms is not null && r.Server != currentServer).MinBy(r => r.Ms);
        if (best is null) return null;
        if (current?.Ms is not { } cur) return best;
        return best.Ms!.Value <= cur * 0.8 && cur - best.Ms.Value >= 10 ? best : null;
    }

    /// <summary>Mediana dos tempos de um servidor (resistente a uma consulta que demorou por acaso).</summary>
    public static double? Median(IEnumerable<double?> ms)
    {
        var ok = ms.Where(m => m is not null).Select(m => m!.Value).OrderBy(m => m).ToList();
        if (ok.Count == 0) return null;
        return ok.Count % 2 == 1 ? ok[ok.Count / 2] : (ok[ok.Count / 2 - 1] + ok[ok.Count / 2]) / 2;
    }

    // ---------- Wi-Fi ----------

    public static string Band(long frequencyKHz) => frequencyKHz switch
    {
        < 3_000_000 => "2,4 GHz",
        < 5_925_000 => "5 GHz",
        _ => "6 GHz",
    };

    /// <summary>
    /// Conectado em 2,4 GHz com a mesma rede (ou a "irmã" com 5G no nome) disponível em 5 ou 6 GHz com sinal bom (50 ou
    /// mais): o 2,4 GHz é mais lento e mais congestionado. Devolve a rede e a banda a sugerir.
    /// </summary>
    public static (string Ssid, string Band)? FasterBand(string connectedSsid, string connectedBand, IReadOnlyList<WifiBss> seen)
    {
        if (!connectedBand.StartsWith("2,4", StringComparison.Ordinal) || connectedSsid.Length == 0) return null;
        static string Base(string s) => Regex.Replace(s, @"[\s_\-]*5\s*G(hz)?$", "", RegexOptions.IgnoreCase).Trim();
        var candidate = seen.Where(b => b.FrequencyKHz >= 4_900_000 && b.Quality >= 50
                                        && (b.Ssid == connectedSsid || Base(b.Ssid).Equals(Base(connectedSsid), StringComparison.OrdinalIgnoreCase) && b.Ssid != connectedSsid
                                            && Base(b.Ssid).Length > 0))
            .OrderByDescending(b => b.Quality).FirstOrDefault();
        return candidate is null ? null : (candidate.Ssid, Band(candidate.FrequencyKHz));
    }

    // ---------- VPN que sobrou ----------

    private static readonly Regex VirtualAdapter = new(@"TAP-Windows|TAP-|Wintun|WireGuard|OpenVPN|NordLynx|Hamachi|ZeroTier|TunnelBear|Fortinet|AnyConnect|Juniper|Pulse Secure|SonicWall|ExpressVPN|Surfshark|ProtonVPN|\bTUN\b",
                                                         RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Adaptadores virtuais de VPN desconectados: sobras de VPNs antigas que atrasam a reconexão do Wi-Fi.</summary>
    public static List<string> OrphanVpnAdapters(IEnumerable<(string Description, bool Up)> adapters) =>
        adapters.Where(a => !a.Up && VirtualAdapter.IsMatch(a.Description)).Select(a => a.Description).Distinct().ToList();

    // ---------- conexões por programa ----------

    public static string StateText(string state) => state switch
    {
        "ESTABLISHED" => Loc.T("conectado"),
        "LISTEN" => Loc.T("esperando conexões"),
        "TIME_WAIT" or "CLOSE_WAIT" or "FIN_WAIT1" or "FIN_WAIT2" or "LAST_ACK" or "CLOSING" => Loc.T("fechando"),
        "SYN_SENT" or "SYN_RCVD" => Loc.T("abrindo"),
        _ => state.ToLowerInvariant(),
    };

    /// <summary>Estados do MIB_TCP_STATE (1 a 12).</summary>
    public static string StateName(int code) => code switch
    {
        1 => "CLOSED", 2 => "LISTEN", 3 => "SYN_SENT", 4 => "SYN_RCVD", 5 => "ESTABLISHED", 6 => "FIN_WAIT1",
        7 => "FIN_WAIT2", 8 => "CLOSE_WAIT", 9 => "CLOSING", 10 => "LAST_ACK", 11 => "TIME_WAIT", 12 => "DELETE_TCB", _ => "?",
    };
}
