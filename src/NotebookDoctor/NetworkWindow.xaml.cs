using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class ProfileRow(WifiProfile profile)
{
    public WifiProfile Profile { get; } = profile;
    public string Ssid => Profile.Ssid;
    public string AuthText => NetworkPrivacy.AuthText(Profile);
    public string StateText => Profile.Weak ? Loc.T("Fraca: vale esquecer") : Loc.T("Protegida");
}

/// <summary>Fase 12, leva 12.2: reparo de rede em etapas, DNS mais rápido, banda do Wi-Fi e VPNs que sobraram.</summary>
public partial class NetworkWindow : Window
{
    private readonly MainWindow _main;
    private string? _fixId;
    private DnsResult? _better;

    public NetworkWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => { await LoadWifiAsync(); await LoadProfilesAsync(); };
    }

    // ---------- conectado, mas não navega ----------

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        FixBtn.Visibility = Visibility.Collapsed;
        StepsInfo.Text = Loc.T("Testando cada etapa…");
        FixInfo.Text = "";
        try
        {
            var checks = AppPaths.Demo
                ? [new NetCheck(NetStep.Adapter, true), new NetCheck(NetStep.Address, true), new NetCheck(NetStep.Router, true), new NetCheck(NetStep.Internet, true),
                   new NetCheck(NetStep.Dns, false, "klivfy.com"), new NetCheck(NetStep.Proxy, true)]
                : await NetProbe.CheckAsync();
            StepsInfo.Text = string.Join("\n", checks.Select(c => (c.Ok ? "✓ " : "✗ ") + NetworkDoctor.StepText(c.Step) + (c.Ok ? "" : " · " + Loc.T("falhou"))));
            if (NetworkDoctor.FirstFailure(checks) is not { } fail)
            {
                FixInfo.Text = Loc.T("Todas as etapas passaram: a conexão está funcionando. Se um site específico não abre, o problema é nele.");
                return;
            }
            var (advice, fixId) = NetworkDoctor.Fix(fail.Step);
            FixInfo.Text = advice;
            _fixId = fixId;
            FixBtn.Visibility = fixId is null ? Visibility.Collapsed : Visibility.Visible;
            FixBtn.Content = Loc.T(fixId switch
            {
                "flushdns" => "Limpar o cache de DNS",
                "renew" => "Pedir um endereço novo",
                "open-wifi" => "Abrir as configurações de Wi-Fi",
                _ => "Abrir as configurações de proxy",
            });
        }
        finally { CheckBtn.IsEnabled = true; }
    }

    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        switch (_fixId)
        {
            case "open-wifi": AppInfo.OpenShell("ms-settings:network-wifi"); return;
            case "open-proxy": AppInfo.OpenShell("ms-settings:network-proxy"); return;
            case "flushdns":
                if (_main.Simulate("Limpar o cache de DNS", "Rodaria ipconfig /flushdns.")) return;
                bool ok = NetProbe.FlushDns();
                FixInfo.Text = ok ? Loc.T("Cache de DNS limpo. Teste de novo.") : Loc.T("O Windows não deixou limpar o cache de DNS.");
                if (ok) ActionLog.Add("Cache de DNS limpo", "ipconfig /flushdns");
                break;
            case "renew":
                if (_main.Simulate("Pedir um endereço novo ao roteador", "Rodaria ipconfig /renew.")) return;
                bool renewed = NetProbe.RenewIp();
                FixInfo.Text = renewed ? Loc.T("Endereço novo recebido. Teste de novo.")
                    : Loc.T("Não deu certo: abra o app como administrador ou reinicie o roteador.");
                if (renewed) ActionLog.Add("Endereço IP renovado", "ipconfig /renew");
                break;
        }
        FixBtn.Visibility = Visibility.Collapsed;
    }

    // ---------- DNS ----------

    private async void Dns_Click(object sender, RoutedEventArgs e)
    {
        DnsBtn.IsEnabled = false;
        DnsUseBtn.Visibility = DnsAutoBtn.Visibility = Visibility.Collapsed;
        DnsInfo.Text = Loc.T("Comparando…");
        try
        {
            var adapter = NetProbe.ActiveAdapter();
            string? current = adapter?.Dns.FirstOrDefault();
            var results = AppPaths.Demo
                ? [new DnsResult(Loc.T("Atual"), "192.168.0.1", 48), new DnsResult("Cloudflare", "1.1.1.1", 12), new DnsResult("Google", "8.8.8.8", 18), new DnsResult("Quad9", "9.9.9.9", 25)]
                : await NetProbe.BenchmarkAsync(current);
            DnsInfo.Text = string.Join("\n", results.Select(r => $"• {r.Name} ({r.Server}): " + (r.Ms is { } ms ? Loc.F("{0:0} ms", ms) : Loc.T("sem resposta"))));
            _better = NetworkDoctor.BetterThanCurrent(results, current ?? "");
            if (_better is not null)
            {
                DnsInfo.Text += "\n\n" + Loc.F("{0} responde bem mais rápido que o seu DNS atual.", _better.Name);
                DnsUseBtn.Content = Loc.F("Usar {0}…", _better.Name);
                DnsUseBtn.Visibility = Visibility.Visible;
            }
            else DnsInfo.Text += "\n\n" + Loc.T("O seu DNS atual já é rápido: trocar não faria diferença.");
            if (adapter is { } a && NetProbe.StaticDns(a.Id).Length > 0) DnsAutoBtn.Visibility = Visibility.Visible;
        }
        finally { DnsBtn.IsEnabled = true; }
    }

    private void DnsUse_Click(object sender, RoutedEventArgs e)
    {
        if (_better is null || NetProbe.ActiveAdapter() is not { } a) return;
        var pair = NetworkDoctor.PublicDns.First(d => d.Primary == _better.Server);
        ChangeDns(a.Name, a.Id, $"{pair.Primary},{pair.Secondary}", Loc.F("DNS trocado para {0}", pair.Name));
    }

    private void DnsAuto_Click(object sender, RoutedEventArgs e)
    {
        if (NetProbe.ActiveAdapter() is not { } a) return;
        ChangeDns(a.Name, a.Id, "", Loc.T("DNS automático (do roteador)"));
    }

    private void ChangeDns(string adapter, string id, string servers, string title)
    {
        string before = NetProbe.StaticDns(id);
        if (_main.Simulate(title, Loc.F("Mudaria o DNS da placa {0}.", adapter))) return;
        if (!AppInfo.IsAdmin) { DnsInfo.Text += "\n\n" + Loc.T("Para trocar o DNS, abra o app como administrador."); return; }
        if (!Dialogs.Confirm(this, title + "?",
                Loc.F("O DNS da placa {0} muda agora. O de antes ({1}) fica guardado em Ações para desfazer.", adapter, before.Length > 0 ? before : Loc.T("automático")),
                "Trocar")) return;
        if (!NetProbe.SetDns(adapter, servers)) { DnsInfo.Text += "\n\n" + Loc.T("O Windows recusou a mudança."); return; }
        ActionLog.Add(title, Loc.F("Placa {0}", adapter), "dns", adapter, before);
        _main.ShowSnack(title, Loc.T("Desfaça em Ações se algum site deixar de abrir."));
        DnsUseBtn.Visibility = DnsAutoBtn.Visibility = Visibility.Collapsed;
    }

    // ---------- Wi-Fi e VPN ----------

    private async Task LoadWifiAsync()
    {
        var (wifi, bss, adapters) = AppPaths.Demo
            ? (new WifiInfo("Casa", 78, 6, "2,4 GHz", "802.11ax (Wi-Fi 6)", 144, 144), [new WifiBss("Casa_5G", 5_180_000, 71)], [("TAP-Windows Adapter V9", false)])
            : await Task.Run(() => (SystemProbe.Wifi(), NetProbe.VisibleBss(), NetProbe.Adapters()));
        if (wifi is null) WifiInfo.Text = Loc.T("Sem Wi-Fi conectado agora.");
        else
        {
            string now = Loc.F("Conectado em {0}: {1}, sinal {2}%.", wifi.Ssid, wifi.Band, wifi.SignalPct);
            WifiInfo.Text = NetworkDoctor.FasterBand(wifi.Ssid, wifi.Band, bss) is { } better
                ? now + " " + Loc.F("A rede {0} está disponível em {1} com sinal bom: conecte-se a ela para mais velocidade e menos interferência (o 2,4 GHz é mais lento e mais congestionado).", better.Ssid, better.Band)
                : now;
        }
        var orphans = NetworkDoctor.OrphanVpnAdapters(adapters);
        VpnInfo.Text = orphans.Count == 0 ? Loc.T("Nenhum adaptador de VPN antiga sobrando.")
            : Loc.F("Adaptadores de VPN desconectados: {0}. Se a VPN já foi desinstalada, eles podem atrasar a volta do Wi-Fi ao abrir a tampa; remova pelo Gerenciador de Dispositivos.", string.Join(", ", orphans));
        DevicesBtn.Visibility = orphans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Devices_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell("devmgmt.msc");

    // ---------- Leva 12.3: redes Wi-Fi salvas ----------

    private async Task LoadProfilesAsync()
    {
        var profiles = AppPaths.Demo
            ? [new WifiProfile("Casa", "WPA2PSK", "AES", "senha-de-exemplo"), new WifiProfile("Cafeteria", "open", "none", null), new WifiProfile("Escritório", "WPA3SAE", "AES", null)]
            : await Task.Run(PrivacyProbe.WifiProfiles);
        ProfilesGrid.ItemsSource = profiles.Select(p => new ProfileRow(p)).ToList();
        int weak = profiles.Count(p => p.Weak);
        ProfilesInfo.Text = profiles.Count == 0 ? Loc.T("Nenhuma rede Wi-Fi salva.")
            : weak == 0 ? Loc.F("{0}, nenhuma aberta ou com proteção antiga.", Pt.N(profiles.Count, "rede salva", "redes salvas"))
            : Loc.F("{0} abertas ou com proteção antiga: esqueça as que não usa mais em Gerenciar redes conhecidas.", Pt.N(weak, "rede", "redes"));
    }

    private void Profiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        QrBtn.IsEnabled = ProfilesGrid.SelectedItem is ProfileRow;
        QrPanel.Visibility = Visibility.Collapsed;
    }

    private void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow { Profile: var p }) return;
        bool open = p.Auth.Equals("open", StringComparison.OrdinalIgnoreCase) && !p.Cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase);
        if (p.Key is null && !open)
        {
            ProfilesInfo.Text = Loc.F("O Windows só entrega a senha de {0} com o app aberto como administrador.", p.Ssid);
            return;
        }
        string auth = p.Cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase) ? "WEP" : p.Auth;
        if (QrCode.Encode(QrCode.WifiPayload(p.Ssid, p.Key ?? "", auth)) is not { } matrix)
        {
            ProfilesInfo.Text = Loc.T("O nome ou a senha da rede são longos demais para o código QR.");
            return;
        }
        QrImage.Source = Render(matrix);
        QrText.Text = Loc.F("Aponte a câmera do celular para entrar em {0}.", p.Ssid) + (p.Key is { } key ? "\n" + Loc.F("Senha: {0}", key) : "");
        QrPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Desenha a matriz do QR com a margem de 4 módulos que a norma pede, preto no branco nos dois temas.</summary>
    private System.Windows.Media.DrawingImage Render(bool[,] m)
    {
        int n = m.GetLength(0), quiet = 4;
        var dark = new System.Windows.Media.GeometryGroup { FillRule = System.Windows.Media.FillRule.Nonzero };
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                if (m[r, c]) dark.Children.Add(new System.Windows.Media.RectangleGeometry(new Rect(c + quiet, r + quiet, 1, 1)));
        var group = new System.Windows.Media.DrawingGroup();
        group.Children.Add(new System.Windows.Media.GeometryDrawing((System.Windows.Media.Brush)FindResource("QrLight"), null,
                                                                    new System.Windows.Media.RectangleGeometry(new Rect(0, 0, n + 2 * quiet, n + 2 * quiet))));
        group.Children.Add(new System.Windows.Media.GeometryDrawing((System.Windows.Media.Brush)FindResource("QrDark"), null, dark));
        var image = new System.Windows.Media.DrawingImage(group);
        image.Freeze();
        return image;
    }

    private void QrHide_Click(object sender, RoutedEventArgs e)
    {
        QrPanel.Visibility = Visibility.Collapsed;
        QrImage.Source = null;
        QrText.Text = "";
    }

    private void Known_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell("ms-settings:network-wifisettings");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
