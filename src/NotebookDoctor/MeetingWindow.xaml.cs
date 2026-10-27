using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class MediaRow(MediaUse use)
{
    public MediaUse Use { get; } = use;
    public string KindText => Meeting.KindText(Use.Kind);
    public string App => Use.App;
    public DateTime? Start => Use.Start;
    public string StartText => Use.Start?.ToString("g", Loc.Culture) ?? "";
    public string StateText => Use.InUse ? Loc.T("Em uso agora")
        : Use.Stop is { } stop && Use.Start is { } start ? Loc.F("Usou por {0}", BatteryRules.Duration(stop - start > TimeSpan.Zero ? stop - start : TimeSpan.Zero)) : "";
}

/// <summary>Fase 12, leva 12.1: videochamadas (câmera e microfone, "por que ninguém me ouve", conexão e downloads de fundo).</summary>
public partial class MeetingWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private List<ProcGroup> _busy = [];
    private bool _busyTesting;

    public MeetingWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await ReloadAsync();
        Host.PresentationChanged += OnPresentation;
        Closed += (_, _) => Host.PresentationChanged -= OnPresentation;
        OnPresentation(Host.Presenting);
    }

    private void OnPresentation(bool on) =>
        PresentBtn.Content = on ? Loc.T("Desligar o modo apresentação") : Loc.T("Ligar o modo apresentação");

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var uses = AppPaths.Demo ? Host.Media.ToList() : await Task.Run(MediaProbe.Uses);
        MediaGrid.ItemsSource = uses.Select(u => new MediaRow(u)).ToList();
        string now = Meeting.InUseText(uses);
        MediaNow.Text = now.Length > 0 ? Loc.F("Em uso agora: {0}", now) : Loc.T("Nenhum aplicativo está usando a câmera ou o microfone agora.");

        var (denied, desktopDenied, devices, handsFree) = AppPaths.Demo
            ? (false, false, 1, (List<string>)["Fone Bluetooth"])
            : await Task.Run(() => (MediaProbe.GloballyDenied("microphone"), MediaProbe.DesktopAppsDenied("microphone"), MediaProbe.CaptureDevices(), MediaProbe.HandsFreeDevices()));
        var problems = Meeting.MicProblems(denied, desktopDenied, devices, handsFree);
        MicInfo.Text = problems.Count == 0
            ? Loc.T("Nada bloqueando o microfone: acesso liberado no Windows e microfone encontrado. Se ainda não ouvem você, confira o mudo do próprio aplicativo da reunião.")
            : string.Join("\n", problems.Select(p => "• " + p));

        _busy = Host.Latest is { } snap ? Meeting.BackgroundDownloads(snap.Groups, inCall: true) : [];
        DownloadsInfo.Text = _busy.Count == 0 ? Loc.T("Nenhum atualizador ou sincronizador está baixando muito agora.")
            : Loc.F("Baixando em segundo plano: {0}.", string.Join(", ", _busy.Select(g => Loc.F("{0} ({1:N0} KB/s)", g.Name, g.IoKBs))));
        EcoBtn.Visibility = _busy.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        int recent = AppPaths.Demo ? 37 : await Task.Run(PrivacyProbe.RecentCount);
        ShareInfo.Text = recent == 0 ? Loc.T("Nenhum arquivo recente à mostra.")
            : Loc.F("{0} à mostra no Explorador e no Iniciar.", Pt.N(recent, "item recente", "itens recentes"));
        RecentBtn.IsEnabled = recent > 0;
    }

    private async void Present_Click(object sender, RoutedEventArgs e) => await Host.SetPresentationAsync(!Host.Presenting);

    private async void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, Loc.T("Limpar recentes"),
                Loc.T("Os arquivos recentes do Explorador e do Iniciar, as listas de atalhos da barra de tarefas e o que foi digitado na barra de endereços e no Executar saem da vista. Os arquivos em si não são apagados. Tudo vai para o backup, e o Desfazer em Ações devolve."),
                Loc.T("Limpar"))) return;
        if (_main.Simulate("Limpar recentes", Loc.T("Limparia os itens recentes, com backup."))) return;
        RecentBtn.IsEnabled = false;
        var (id, removed) = await Task.Run(() =>
        {
            using var session = new BackupStore(AppPaths.Backups).Begin(Loc.T("Limpar recentes"));
            return (session.Id, PrivacyProbe.ClearRecent(session));
        });
        ActionLog.Add("Recentes limpos", Pt.N(removed, "item", "itens"), "leftovers", id);
        ShareInfo.Text = Loc.F("{0} fora da vista. Desfaça em Ações.", Pt.N(removed, "item recente", "itens recentes"));
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busyTesting) return;
        _busyTesting = true;
        TestBtn.IsEnabled = false;
        NetInfo.Text = Loc.T("Testando por cerca de 10 segundos…");
        try
        {
            string? gw = MediaProbe.Gateway();
            var tInternet = MediaProbe.PingSeries("1.1.1.1", 30);
            var tGateway = gw is null ? Task.FromResult(new List<long?>()) : MediaProbe.PingSeries(gw, 30);
            await Task.WhenAll(tInternet, tGateway);
            PingStats? gateway = gw is null ? null : Meeting.Stats(tGateway.Result);
            var internet = Meeting.Stats(tInternet.Result);
            var verdict = Meeting.Diagnose(gateway, internet, Host.Latest?.Cpu ?? 0);
            NetInfo.Text = string.Join("\n", new[]
            {
                gateway is null ? Loc.T("Roteador: não encontrado") : Loc.F("Roteador: {0}", Meeting.StatsText(gateway)),
                Loc.F("Internet (1.1.1.1): {0}", Meeting.StatsText(internet)),
                Meeting.VerdictText(verdict),
            });
        }
        finally
        {
            _busyTesting = false;
            TestBtn.IsEnabled = true;
        }
    }

    private void Eco_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.Count == 0) return;
        string names = string.Join(", ", _busy.Select(g => g.Name));
        if (_main.Simulate("Modo eficiência nos downloads", Loc.F("Poria {0} no modo eficiência durante a chamada.", names))) return;
        int done = 0;
        foreach (var g in _busy)
            if (ProcessActions.SetEfficiency(g.Name, true) > 0)
            {
                done++;
                ActionLog.Add(Loc.F("Modo eficiência: {0}", g.Name), Loc.T("Durante uma chamada de vídeo"), "efficiency", g.Name);
            }
        DownloadsInfo.Text = done > 0 ? Loc.F("{0} no modo eficiência. Desfaça em Ações depois da chamada.", Pt.N(done, "programa", "programas"))
                                      : Loc.T("Não foi possível aplicar: falta permissão.");
        EcoBtn.Visibility = Visibility.Collapsed;
    }

    private void MicSettings_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell("ms-settings:privacy-microphone");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
