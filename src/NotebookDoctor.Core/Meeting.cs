namespace NotebookDoctor.Services;

/// <summary>Latência média, variação (jitter) e perda de uma série de pings.</summary>
public sealed record PingStats(double AvgMs, double JitterMs, double LossPct, int Sent)
{
    public bool NoReply => Sent > 0 && LossPct >= 100;
}

/// <summary>Onde está o problema da chamada.</summary>
public enum CallVerdict { Good, WifiLocal, Provider, LocalCpu, NoNetwork }

/// <summary>Leva 12.1: quem usou a câmera ou o microfone, e se está usando agora.</summary>
public sealed record MediaUse(string Kind, string App, DateTime? Start, DateTime? Stop)
{
    /// <summary>Começou e ainda não parou: está usando neste momento.</summary>
    public bool InUse => Start is not null && Stop is null;
}

/// <summary>
/// Fase 12, leva 12.1: reuniões sem travar. Estabilidade da conexão (roteador × internet), quem usa câmera e microfone,
/// "por que ninguém me ouve" e downloads de fundo que atrapalham a chamada. Só lê; nada sai do computador além dos pings.
/// </summary>
public static class Meeting
{
    /// <summary>Resume uma série de pings: null é um ping sem resposta.</summary>
    public static PingStats Stats(IReadOnlyList<long?> rtts)
    {
        if (rtts.Count == 0) return new PingStats(0, 0, 100, 0);
        var ok = rtts.Where(r => r is not null).Select(r => (double)r!.Value).ToList();
        double loss = Math.Round((rtts.Count - ok.Count) * 100.0 / rtts.Count, 1);
        if (ok.Count == 0) return new PingStats(0, 0, 100, rtts.Count);
        // Jitter como nas chamadas de voz: média da diferença entre pings seguidos
        double jitter = ok.Count < 2 ? 0 : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new PingStats(Math.Round(ok.Average(), 1), Math.Round(jitter, 1), loss, rtts.Count);
    }

    /// <summary>Instável para voz e vídeo: perda acima de 2%, jitter acima de 30 ms ou latência acima de 150 ms.</summary>
    public static bool Unstable(PingStats s) => s.LossPct > 2 || s.JitterMs > 30 || s.AvgMs > 150;

    /// <summary>
    /// Onde está o problema: roteador ruim é o Wi-Fi da casa; roteador bom e internet ruim é o provedor; os dois bons e a
    /// CPU acima de 85% é o próprio notebook.
    /// </summary>
    public static CallVerdict Diagnose(PingStats? gateway, PingStats internet, double cpuPct)
    {
        if (internet.NoReply && (gateway is null || gateway.NoReply)) return CallVerdict.NoNetwork;
        if (gateway is not null && Unstable(gateway)) return CallVerdict.WifiLocal;
        if (Unstable(internet)) return CallVerdict.Provider;
        return cpuPct >= 85 ? CallVerdict.LocalCpu : CallVerdict.Good;
    }

    public static string VerdictText(CallVerdict v) => v switch
    {
        CallVerdict.WifiLocal => Loc.T("O problema está no Wi-Fi da sua casa: o sinal até o roteador oscila. Chegue mais perto do roteador, use a rede de 5 GHz ou um cabo."),
        CallVerdict.Provider => Loc.T("O Wi-Fi até o roteador está bom; a instabilidade vem da internet (provedor). Reinicie o roteador ou fale com a operadora."),
        CallVerdict.LocalCpu => Loc.T("A conexão está boa, mas o processador está quase no limite: feche programas pesados para a chamada não engasgar."),
        CallVerdict.NoNetwork => Loc.T("Sem resposta nem do roteador nem da internet: o notebook está sem rede."),
        _ => Loc.T("Conexão boa para chamadas de vídeo."),
    };

    public static string StatsText(PingStats s) => s.NoReply ? Loc.T("sem resposta")
        : Loc.F("{0:0} ms, variação {1:0} ms, perda {2:0.#}%", s.AvgMs, s.JitterMs, s.LossPct);

    // ---------- câmera e microfone ----------

    /// <summary>
    /// Nome legível de uma entrada do ConsentStore: apps da Loja vêm como "Microsoft.WindowsCamera_8wekyb3d8bbwe" e os
    /// outros (NonPackaged) como o caminho com # no lugar de \ ("C:#Program Files#Zoom#bin#Zoom.exe").
    /// </summary>
    public static string AppName(string key, bool nonPackaged)
    {
        if (nonPackaged) return key[(key.LastIndexOf('#') + 1)..];
        int u = key.LastIndexOf('_');
        return u > 0 ? key[..u] : key;
    }

    /// <summary>FILETIME do registro; 0 (ou inválido) é "nunca".</summary>
    public static DateTime? FromFileTime(long ft)
    {
        if (ft <= 0) return null;
        try { return DateTime.FromFileTime(ft); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static string KindText(string kind) => kind == "webcam" ? Loc.T("Câmera") : Loc.T("Microfone");

    /// <summary>Quem está usando agora, para o indicador do mini-widget e da bandeja ("Câmera: Zoom · Microfone: Zoom").</summary>
    public static string InUseText(IEnumerable<MediaUse> uses)
    {
        var now = uses.Where(u => u.InUse).GroupBy(u => u.Kind).OrderBy(g => g.Key == "webcam" ? 0 : 1)
            .Select(g => $"{KindText(g.Key)}: {string.Join(", ", g.Select(u => u.App).Distinct())}").ToList();
        return string.Join(" · ", now);
    }

    // ---------- por que ninguém me ouve ----------

    /// <summary>O que pode estar calando o microfone, em ordem de causa mais comum. Vazio quando nada foi achado.</summary>
    public static List<string> MicProblems(bool globalDenied, bool desktopAppsDenied, int captureDevices, IReadOnlyList<string> handsFree)
    {
        var list = new List<string>();
        if (globalDenied) list.Add(Loc.T("O acesso ao microfone está desligado no Windows (Configurações › Privacidade e segurança › Microfone)."));
        else if (desktopAppsDenied) list.Add(Loc.T("Os aplicativos da área de trabalho (Zoom, Teams clássico, navegadores) estão proibidos de usar o microfone em Privacidade e segurança › Microfone."));
        if (captureDevices == 0) list.Add(Loc.T("Nenhum microfone foi encontrado: confira se o fone está conectado ou se o microfone foi desativado em Som › Gravação."));
        if (handsFree.Count > 0)
            list.Add(Loc.F("{0} usa o perfil \"Hands-Free\" quando o microfone dele abre: o som fica em mono e abafado. Para ouvir bem, use o microfone do notebook e o fone só para ouvir.", handsFree[0]));
        return list;
    }

    // ---------- downloads de fundo ----------

    /// <summary>Programas que baixam muito em segundo plano (atualizadores, lojas de jogos, sincronizadores).</summary>
    private static readonly HashSet<string> Downloaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MoUsoCoreWorker", "TiWorker", "TrustedInstaller", "BackgroundTransferHost", "UsoClient", "wuauclt",
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EADesktop", "upc", "Battle.net", "Agent",
        "OneDrive", "Dropbox", "GoogleDriveFS", "iCloudDrive", "MEGAsync",
        "msedgeupdate", "MicrosoftEdgeUpdate", "GoogleUpdate", "updater", "AdobeARM", "OfficeClickToRun",
    };

    /// <summary>Acima disto (disco e rede, KB/s), um baixador de fundo atrapalha a chamada.</summary>
    public const double BusyKBs = 300;

    /// <summary>Com câmera ou microfone em uso, os baixadores de fundo que estão puxando disco ou rede agora.</summary>
    public static List<ProcGroup> BackgroundDownloads(IEnumerable<ProcGroup> groups, bool inCall) =>
        inCall ? groups.Where(g => Downloaders.Contains(g.Name) && g.IoKBs >= BusyKBs).OrderByDescending(g => g.IoKBs).ToList() : [];
}
