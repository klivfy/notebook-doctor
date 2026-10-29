using System.Net.Http;
using System.Text;
using System.Windows;

namespace NotebookDoctor.Services;

public enum SendResult { Sent, Offline, Rejected }

/// <summary>
/// B25 e M18: o que o app manda à Klivfy, sempre por clique (avaliação) ou por opção ligada (estatísticas). Só HTTPS no domínio da
/// Klivfy, sem cookie, sem cabeçalho de identificação e sem nada além do texto que a pessoa pôde ver antes.
/// </summary>
public static class UsageStats
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly object Lock = new();

    private static AppSettings Cfg => App.Current.Settings;

    /// <summary>Soma um uso da ferramenta. Não faz nada com a opção desligada, na demonstração ou para nome que não é de ferramenta.</summary>
    public static void Use(string tool) => Bump(tool, failure: false);

    /// <summary>Soma uma falha do app (a falha em si, sem texto de erro, caminho nem nome).</summary>
    public static void Failure(string tool = "app") => Bump(tool, failure: true);

    private static void Bump(string tool, bool failure)
    {
        try
        {
            var s = Cfg;
            if (!s.TelemetryEnabled || AppPaths.Demo || !Telemetry.Known(tool)) return;
            lock (Lock) Telemetry.Count(failure ? s.TelemetryFailures : s.TelemetryUses, tool);
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException) { /* contar nunca atrapalha o app */ }
    }

    private static readonly Dictionary<string, string> WindowTools = new()
    {
        ["AdvancedCleanWindow"] = "limpeza-avancada", ["DuplicatesWindow"] = "duplicados", ["UpdatesWindow"] = "atualizacoes",
        ["DriversWindow"] = "drivers", ["RegistryCheckWindow"] = "registro", ["SecureEraseWindow"] = "apagar-de-vez",
        ["RecoveryWindow"] = "recuperacao", ["NtfsRecoveryWindow"] = "recuperacao", ["DefragWindow"] = "desfragmentar",
        ["WindowsToolsWindow"] = "ferramentas-do-windows", ["SecurityWindow"] = "seguranca",
    };

    /// <summary>Conta a abertura das janelas de ferramenta (pelo tipo da janela, nunca pelo título nem pelo conteúdo).</summary>
    public static void WatchWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (WindowTools.TryGetValue(sender.GetType().Name, out var tool)) Use(tool);
        }));

    /// <summary>O texto exato que seria enviado agora; é o que "Ver o que seria enviado" mostra.</summary>
    public static string PendingPayload()
    {
        var s = Cfg;
        lock (Lock) return Telemetry.Payload(s.TelemetryUses, s.TelemetryFailures, AppInfo.Version, Loc.Language, Telemetry.WindowsFamily(Environment.OSVersion.Version.Build), DateTime.Now);
    }

    public static void Clear()
    {
        var s = Cfg;
        lock (Lock) { s.TelemetryUses.Clear(); s.TelemetryFailures.Clear(); }
        s.Save();
    }

    /// <summary>Um dia depois de abrir, se a opção estiver ligada, houver contagem e tiver passado uma semana do último envio.</summary>
    public static async Task SendIfDueAsync()
    {
        var s = Cfg;
        bool due;
        lock (Lock) due = Telemetry.ShouldSend(s.TelemetryEnabled, s.LastTelemetrySend, DateTime.Now, !Telemetry.Empty(s.TelemetryUses, s.TelemetryFailures));
        if (!due || AppPaths.Demo) return;
        string body = PendingPayload();
        if (await PostAsync(KlivfyUrls.Usage, body) != SendResult.Sent) return; // tenta de novo na próxima abertura
        lock (Lock) { s.TelemetryUses.Clear(); s.TelemetryFailures.Clear(); }
        s.LastTelemetrySend = DateTime.Now;
        s.Save();
    }

    public static async Task<SendResult> PostAsync(string url, string json)
    {
        if (!KlivfyUrls.IsTrusted(url)) return SendResult.Rejected;
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            using var resp = await Http.SendAsync(request);
            return resp.IsSuccessStatusCode ? SendResult.Sent : SendResult.Rejected;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return SendResult.Offline; }
    }
}
