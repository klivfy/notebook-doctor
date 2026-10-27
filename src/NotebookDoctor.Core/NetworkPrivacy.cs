using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NotebookDoctor.Services;

/// <summary>Uma rede Wi-Fi salva no notebook: nome, autenticação, cifra e a senha (só com o app como administrador).</summary>
public sealed record WifiProfile(string Ssid, string Auth, string Cipher, string? Key)
{
    /// <summary>Aberta (sem senha) ou com proteção quebrada (WEP, WPA com TKIP).</summary>
    public bool Weak => Auth.Equals("open", StringComparison.OrdinalIgnoreCase) || Cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase)
                        || Auth.Equals("WPAPSK", StringComparison.OrdinalIgnoreCase) || Cipher.Equals("TKIP", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Uma linha do arquivo hosts que merece atenção, com o motivo.</summary>
public sealed record HostsFinding(string Line, string Host, string Ip, string Reason);

/// <summary>
/// Fase 12, leva 12.3: hotspot de celular e franquia, redes Wi-Fi salvas, arquivo hosts e proxy PAC suspeitos. Regras puras.
/// </summary>
public static class NetworkPrivacy
{
    // ---------- hotspot de celular ----------

    private static readonly Regex PhoneSsid = new(@"iphone|android|galaxy|redmi|xiaomi|poco|moto|motorola|oneplus|pixel|hotspot|realme|oppo|vivo\b|huawei",
                                                  RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Gateways padrão do hotspot do iPhone (172.20.10.1) e do Android (192.168.43.1).</summary>
    private static readonly HashSet<string> PhoneGateways = ["172.20.10.1", "192.168.43.1"];

    /// <summary>Conectado à internet do celular: rede com nome de celular ou gateway típico de hotspot.</summary>
    public static bool LooksLikeHotspot(string? ssid, string? gateway) =>
        ssid is { Length: > 0 } && PhoneSsid.IsMatch(ssid) || gateway is not null && PhoneGateways.Contains(gateway);

    /// <summary>
    /// Na franquia do celular (rede limitada ou com cara de hotspot), os programas sem janela que estão puxando mais de
    /// 500 KB/s gastam os dados da pessoa sem ela ver.
    /// </summary>
    public static List<ProcGroup> DataHogs(IEnumerable<ProcGroup> groups, bool onMobileData) =>
        onMobileData ? groups.Where(g => !g.HasWindow && g.IoKBs >= 500 && !ProcessTools.IsSystem(g.Name)).OrderByDescending(g => g.IoKBs).Take(3).ToList() : [];

    // ---------- redes Wi-Fi salvas ----------

    /// <summary>Lê o XML de um perfil Wi-Fi do Windows (WLANProfile). Null quando o XML não é de um perfil.</summary>
    public static WifiProfile? ParseProfile(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            string? Val(string name) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            string? ssid = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SSID")?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value
                           ?? Val("name");
            if (string.IsNullOrEmpty(ssid)) return null;
            bool protectedKey = string.Equals(Val("protected"), "true", StringComparison.OrdinalIgnoreCase);
            string? key = protectedKey ? null : Val("keyMaterial");
            return new WifiProfile(ssid, Val("authentication") ?? "", Val("encryption") ?? "", string.IsNullOrEmpty(key) ? null : key);
        }
        catch (System.Xml.XmlException) { return null; }
    }

    public static string AuthText(WifiProfile p) => p switch
    {
        _ when p.Auth.Equals("open", StringComparison.OrdinalIgnoreCase) && !p.Cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase) => Loc.T("Aberta (sem senha)"),
        _ when p.Cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase) => Loc.T("WEP (quebrada)"),
        _ when p.Auth.StartsWith("WPA3", StringComparison.OrdinalIgnoreCase) => "WPA3",
        _ when p.Auth.Equals("WPA2PSK", StringComparison.OrdinalIgnoreCase) || p.Auth.Equals("WPA2", StringComparison.OrdinalIgnoreCase) => "WPA2",
        _ when p.Auth.StartsWith("WPA", StringComparison.OrdinalIgnoreCase) && p.Cipher.Equals("TKIP", StringComparison.OrdinalIgnoreCase) || p.Auth.Equals("WPAPSK", StringComparison.OrdinalIgnoreCase) => Loc.T("WPA antigo (fraco)"),
        _ => p.Auth,
    };

    // ---------- hosts ----------

    // Domínios que nunca deveriam ser desviados: bancos, atualizações, antivírus e os grandes serviços
    private static readonly string[] Protected =
    [
        "microsoft.com", "windowsupdate.com", "windows.com", "live.com", "office.com", "msftconnecttest.com",
        "google.com", "gmail.com", "youtube.com", "facebook.com", "instagram.com", "whatsapp.com", "apple.com", "icloud.com",
        "paypal.com", "mercadopago.com", "nubank.com.br", "itau.com.br", "bradesco.com.br", "santander.com.br", "bb.com.br", "caixa.gov.br", "inter.co",
        "gov.br", "receita.fazenda.gov.br",
        "avast.com", "avg.com", "kaspersky.com", "eset.com", "malwarebytes.com", "mcafee.com", "norton.com", "bitdefender.com", "trendmicro.com", "sophos.com",
        "virustotal.com", "klivfy.com",
    ];

    private static bool IsProtected(string host) =>
        Protected.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    private static bool IsBlockAddress(string ip) => ip is "0.0.0.0" or "127.0.0.1" or "::1" or "::" || ip.StartsWith("127.", StringComparison.Ordinal);

    /// <summary>
    /// Linhas do hosts que desviam (para outro endereço) ou bloqueiam (para 0.0.0.0/127.0.0.1) um domínio protegido.
    /// Bloqueadores de anúncio enchem o hosts de 0.0.0.0 para domínios de anúncio, e esses ficam de fora.
    /// </summary>
    public static List<HostsFinding> AuditHosts(IEnumerable<string> lines)
    {
        var list = new List<HostsFinding>();
        foreach (var raw in lines)
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out _)) continue;
            foreach (var host in parts.Skip(1).Where(IsProtected))
                list.Add(new HostsFinding(raw.Trim(), host, parts[0],
                    IsBlockAddress(parts[0]) ? Loc.F("bloqueia {0} (pode impedir atualizações ou o antivírus)", host)
                                             : Loc.F("desvia {0} para {1} (pode levar a um site falso)", host, parts[0])));
        }
        return list;
    }

    /// <summary>Conteúdo do hosts de um Windows recém-instalado (só comentários).</summary>
    public const string DefaultHosts = "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\r\n#\r\n# localhost name resolution is handled within DNS itself.\r\n#\t127.0.0.1       localhost\r\n#\t::1             localhost\r\n";

    // ---------- proxy PAC ----------

    /// <summary>
    /// Script de proxy (PAC) suspeito: endereço sem HTTPS fora da rede local, um IP público, ou um arquivo local. É uma forma
    /// comum de malware de navegador desviar todo o tráfego. Null quando parece de empresa ou de rede local.
    /// </summary>
    public static string? SuspiciousPac(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return Loc.T("o script de proxy é um arquivo do próprio computador");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return Loc.T("o endereço do script de proxy é inválido");
        bool local = u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !u.Host.Contains('.')
                     || IPAddress.TryParse(u.Host, out var ip) && IsPrivate(ip);
        if (local) return null;
        if (IPAddress.TryParse(u.Host, out _)) return Loc.T("o script de proxy vem de um endereço IP da internet, sem nome");
        return u.Scheme == "http" ? Loc.T("o script de proxy vem da internet sem HTTPS") : null;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 127);
    }
}
