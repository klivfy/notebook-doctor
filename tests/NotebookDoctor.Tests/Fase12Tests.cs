using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Fase 12, leva 12.1: videochamadas, câmera e microfone.</summary>
public class Fase12Tests
{
    private static PingStats S(double avg, double jitter = 2, double loss = 0) => new(avg, jitter, loss, 30);

    [Fact]
    public void Estatisticas_de_ping()
    {
        var s = Meeting.Stats([10, 20, null, 10, 30]);
        Assert.Equal(17.5, s.AvgMs);
        Assert.Equal(20, s.LossPct);
        Assert.Equal(13.3, s.JitterMs);     // |20-10|, |10-20|, |30-10| → média 13,3
        Assert.True(Meeting.Stats([null, null]).NoReply);
        Assert.True(Meeting.Stats([]).NoReply is false && Meeting.Stats([]).LossPct == 100);
    }

    [Fact]
    public void Onde_esta_o_problema_da_chamada()
    {
        Assert.Equal(CallVerdict.Good, Meeting.Diagnose(S(3), S(25), 30));
        Assert.Equal(CallVerdict.WifiLocal, Meeting.Diagnose(S(40, jitter: 45), S(80, jitter: 50), 30));
        Assert.Equal(CallVerdict.Provider, Meeting.Diagnose(S(3), S(60, loss: 5), 30));
        Assert.Equal(CallVerdict.LocalCpu, Meeting.Diagnose(S(3), S(25), 92));
        Assert.Equal(CallVerdict.NoNetwork, Meeting.Diagnose(new PingStats(0, 0, 100, 30), new PingStats(0, 0, 100, 30), 10));
        Assert.Equal(CallVerdict.Provider, Meeting.Diagnose(null, S(200), 10));   // sem roteador conhecido, só a internet conta
    }

    [Theory]
    [InlineData("C:#Program Files#Zoom#bin#Zoom.exe", true, "Zoom.exe")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", false, "Microsoft.WindowsCamera")]
    [InlineData("SemSublinhado", false, "SemSublinhado")]
    public void Nome_do_aplicativo_no_ConsentStore(string key, bool nonPackaged, string expected) =>
        Assert.Equal(expected, Meeting.AppName(key, nonPackaged));

    [Fact]
    public void Em_uso_e_quem_comecou_e_nao_parou()
    {
        var t = new DateTime(2026, 10, 27, 9, 0, 0);
        var uses = new[]
        {
            new MediaUse("microphone", "Zoom.exe", t, null),
            new MediaUse("webcam", "Zoom.exe", t, null),
            new MediaUse("webcam", "Teams", t.AddHours(-1), t.AddHours(-1).AddMinutes(30)),
        };
        Assert.True(uses[0].InUse);
        Assert.False(uses[2].InUse);
        Assert.Equal($"{Loc.T("Câmera")}: Zoom.exe · {Loc.T("Microfone")}: Zoom.exe", Meeting.InUseText(uses));
        Assert.Equal("", Meeting.InUseText([uses[2]]));
        Assert.Null(Meeting.FromFileTime(0));
        Assert.Equal(t, Meeting.FromFileTime(t.ToFileTime()));
    }

    [Fact]
    public void Por_que_ninguem_me_ouve()
    {
        Assert.Empty(Meeting.MicProblems(false, false, 1, []));
        Assert.Single(Meeting.MicProblems(true, true, 1, []));                 // o bloqueio geral já cobre o de apps de área de trabalho
        Assert.Equal(3, Meeting.MicProblems(false, true, 0, ["Fone JBL"]).Count);
        Assert.Contains("Fone JBL", Meeting.MicProblems(false, false, 1, ["Fone JBL"])[0]);
    }

    [Fact]
    public void Downloads_de_fundo_so_contam_durante_a_chamada()
    {
        ProcGroup[] groups = [new("steam", 3, 4, 300, IoKBs: 2500), new("OneDrive", 1, 1, 80, IoKBs: 100), new("chrome", 10, 20, 900, IoKBs: 4000)];
        Assert.Empty(Meeting.BackgroundDownloads(groups, inCall: false));
        var busy = Meeting.BackgroundDownloads(groups, inCall: true);
        Assert.Equal(new[] { "steam" }, busy.Select(g => g.Name));    // o OneDrive baixa pouco; o Chrome é a própria chamada
    }

    [Fact]
    public void Janela_de_videochamadas_no_smoke_e_na_aba_Sistema()
    {
        Assert.Contains("new MeetingWindow(main)", File.ReadAllText(Path.Combine(RepoPaths.App, "Smoke.cs")));
        Assert.Contains("Click=\"SysMeeting_Click\"", File.ReadAllText(Path.Combine(RepoPaths.App, "MainWindow.xaml")));
        Assert.Contains("x:Name=\"MediaRow\"", File.ReadAllText(Path.Combine(RepoPaths.App, "MiniWidget.xaml")));
    }
}

/// <summary>Na CI do Windows: registro de câmera e microfone e contagem de microfones de verdade.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class Fase12IntegrationTests
{
    [WindowsFact]
    public void Camera_e_microfone_sao_lidos_sem_falhar()
    {
        Assert.All(MediaProbe.Uses(), u => Assert.True(u.Kind is "webcam" or "microphone"));
        Assert.True(MediaProbe.CaptureDevices() >= -1);
        MediaProbe.GloballyDenied("microphone");
        MediaProbe.HandsFreeDevices();
    }

    [WindowsFact]
    public void Conexoes_do_proprio_processo_e_DNS_de_verdade()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var mine = NetProbe.TcpConnections(new HashSet<int> { Environment.ProcessId });
        Assert.Contains(mine, c => c.LocalPort == port && c.State == "LISTEN");
        Assert.NotNull(NetProbe.ActiveAdapter());      // o executor tem rede
        NetProbe.VisibleBss();                         // sem Wi-Fi no executor: lista vazia, sem exceção
        NetProbe.Proxy();
    }
}

/// <summary>Fase 12, leva 12.2: reparo de rede, DNS, Wi-Fi e VPN.</summary>
public class Fase12RedeTests
{
    [Fact]
    public void Primeira_etapa_que_falhou_e_a_correcao_dela()
    {
        NetCheck[] checks = [new(NetStep.Adapter, true), new(NetStep.Address, true), new(NetStep.Router, true), new(NetStep.Internet, true), new(NetStep.Dns, false), new(NetStep.Proxy, false)];
        Assert.Equal(NetStep.Dns, NetworkDoctor.FirstFailure(checks)!.Step);
        Assert.Equal("flushdns", NetworkDoctor.Fix(NetStep.Dns).FixId);
        Assert.Equal("renew", NetworkDoctor.Fix(NetStep.Address).FixId);
        Assert.Null(NetworkDoctor.Fix(NetStep.Internet).FixId);     // provedor: nada que o app possa fazer
        Assert.Null(NetworkDoctor.FirstFailure([new(NetStep.Adapter, true)]));
        Assert.True(NetworkDoctor.IsApipa("169.254.10.2"));
        Assert.False(NetworkDoctor.IsApipa("192.168.0.10"));
    }

    [Fact]
    public void DNS_so_troca_quando_a_diferenca_vale()
    {
        DnsResult[] r = [new("Atual", "192.168.0.1", 48), new("Cloudflare", "1.1.1.1", 12), new("Google", "8.8.8.8", 18)];
        Assert.Equal("1.1.1.1", NetworkDoctor.BetterThanCurrent(r, "192.168.0.1")!.Server);
        DnsResult[] close = [new("Atual", "192.168.0.1", 15), new("Cloudflare", "1.1.1.1", 12)];
        Assert.Null(NetworkDoctor.BetterThanCurrent(close, "192.168.0.1"));
        DnsResult[] currentDown = [new("Atual", "192.168.0.1", null), new("Quad9", "9.9.9.9", 30)];
        Assert.Equal("9.9.9.9", NetworkDoctor.BetterThanCurrent(currentDown, "192.168.0.1")!.Server);
        Assert.Equal(15, NetworkDoctor.Median([10, null, 20, 15]));
        Assert.Null(NetworkDoctor.Median([null, null]));
    }

    [Fact]
    public void Wi_Fi_em_2_4_GHz_com_5_GHz_disponivel()
    {
        WifiBss[] seen = [new("Casa", 2_437_000, 90), new("Casa", 5_180_000, 70), new("Vizinho", 5_200_000, 99)];
        Assert.Equal(("Casa", "5 GHz"), NetworkDoctor.FasterBand("Casa", "2,4 GHz", seen));
        Assert.Equal(("Casa_5G", "5 GHz"), NetworkDoctor.FasterBand("Casa", "2,4 GHz", [new("Casa_5G", 5_180_000, 60)]));
        Assert.Null(NetworkDoctor.FasterBand("Casa", "2,4 GHz", [new("Casa", 5_180_000, 30)]));   // sinal fraco no 5 GHz
        Assert.Null(NetworkDoctor.FasterBand("Casa", "5 GHz", seen));                             // já está no 5 GHz
        Assert.Equal("6 GHz", NetworkDoctor.Band(6_115_000));
    }

    [Fact]
    public void VPN_desconectada_que_sobrou()
    {
        var orphans = NetworkDoctor.OrphanVpnAdapters([("TAP-Windows Adapter V9", false), ("Wintun Userspace Tunnel", true), ("Intel(R) Wi-Fi 6 AX201", false), ("WireGuard Tunnel", false)]);
        Assert.Equal(new[] { "TAP-Windows Adapter V9", "WireGuard Tunnel" }, orphans);
    }

    [Fact]
    public void Estados_das_conexoes()
    {
        Assert.Equal("ESTABLISHED", NetworkDoctor.StateName(5));
        Assert.Equal("LISTEN", NetworkDoctor.StateName(2));
        Assert.Equal(Loc.T("conectado"), NetworkDoctor.StateText("ESTABLISHED"));
        Assert.Equal(Loc.T("fechando"), NetworkDoctor.StateText("TIME_WAIT"));
    }

    [Fact]
    public void Janela_de_rede_no_smoke_e_conexoes_em_Processos()
    {
        Assert.Contains("new NetworkWindow(main)", File.ReadAllText(Path.Combine(RepoPaths.App, "Smoke.cs")));
        string main = File.ReadAllText(Path.Combine(RepoPaths.App, "MainWindow.xaml"));
        Assert.Contains("Click=\"SysNetwork_Click\"", main);
        Assert.Contains("Click=\"ProcConnections_Click\"", main);
    }
}
