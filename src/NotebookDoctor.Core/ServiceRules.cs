namespace NotebookDoctor.Services;

/// <param name="Start">Como o Windows inicia o serviço: 2 = automático, 3 = manual, 4 = desativado</param>
public sealed record ServiceInfo(string Name, string DisplayName, string ImagePath, int Start, bool DelayedStart = false);

public enum ServiceAdvice { CanBeManual, Keep, Review }

/// <summary>
/// M30: quais serviços de terceiros podem deixar de iniciar com o Windows. Os do próprio Windows nunca entram:
/// mudar um deles pode quebrar o sistema. Segurança, drivers e hardware também não, mesmo sendo de terceiros.
/// </summary>
public static class ServiceRules
{
    // Atualizadores e ajudantes: no modo manual continuam funcionando quando o próprio programa os chama
    private static readonly string[] Optional = ["update", "updater", "telemetry", "crash", "helper", "assistant", "agent"];
    // Proteção, hardware e rede: deixar como está
    private static readonly string[] Essential =
    [
        "defender", "antivirus", "security", "firewall", "vpn", "endpoint", "protect", "kaspersky", "avast", "avg", "norton", "mcafee",
        "eset", "bitdefender", "sophos", "malware", "driver", "audio", "realtek", "intel", "amd", "nvidia", "synaptics", "elan",
        "bluetooth", "wifi", "wlan", "network", "touchpad", "fingerprint", "biometric", "backup", "sync",
    ];

    /// <summary>Caminho real do executável: tira aspas e argumentos e resolve "\SystemRoot\" e "system32\".</summary>
    public static string ExecutablePath(string imagePath, string systemRoot)
    {
        // Caminhos do Windows montados à mão: Path.Combine usaria "/" se a biblioteca rodar fora do Windows (testes)
        string root = systemRoot.TrimEnd('\\') + "\\";
        string path = CommandLine.ExecutablePath(imagePath) ?? "";
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) return root + path[12..];
        if (path.StartsWith(@"\??\", StringComparison.Ordinal)) return path[4..];
        if (path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) return root + path;
        return path;
    }

    public static bool IsWindows(ServiceInfo s, string systemRoot)
    {
        string exe = ExecutablePath(s.ImagePath, systemRoot);
        return exe.Length == 0 || exe.StartsWith(systemRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static (ServiceAdvice Advice, string Reason) Classify(ServiceInfo s)
    {
        string text = $"{s.Name} {s.DisplayName} {s.ImagePath}".ToLowerInvariant();
        if (Essential.Any(text.Contains))
            return (ServiceAdvice.Keep, "Proteção, hardware ou rede: deixe como está");
        if (Optional.Any(text.Contains))
            return (ServiceAdvice.CanBeManual, "Atualizador ou ajudante: em manual, o programa ainda o chama quando precisa");
        return (ServiceAdvice.Review, "Se não reconhece ou não usa o programa, pode ficar manual");
    }

    /// <summary>Serviços de terceiros que iniciam sozinhos, do mais recomendável a mudar para o menos.</summary>
    public static List<(ServiceInfo Service, ServiceAdvice Advice, string Reason)> Candidates(IEnumerable<ServiceInfo> all, string systemRoot) =>
        all.Where(s => s.Start == 2 && !IsWindows(s, systemRoot))
           .Select(s => { var (a, r) = Classify(s); return (s, a, r); })
           .OrderBy(x => x.a switch { ServiceAdvice.CanBeManual => 0, ServiceAdvice.Review => 1, _ => 2 })
           .ThenBy(x => x.s.DisplayName, StringComparer.OrdinalIgnoreCase)
           .ToList();
}
