namespace NotebookDoctor.Services;

public enum TrayBand { Ok, Amber, Red }

/// <summary>B01/B03: o que o ícone e o menu da bandeja mostram.</summary>
/// <summary>Leva 10.3: o que o ícone da bandeja mostra.</summary>
public enum TrayIconShows { Logo, Cpu, Temperature, Score }

/// <summary>Leva 10.3: o que o clique do meio (roda do mouse) no ícone faz.</summary>
public enum TrayMiddleAction { Nothing, OpenPanel, MiniWidget, CleanTemp, ClearClipboard }

public static class Tray
{
    /// <summary>Cor do ícone pelo recurso mais carregado (CPU ou RAM).</summary>
    public static TrayBand Band(double cpu, double ramPct)
    {
        double load = Math.Max(cpu, ramPct);
        return load >= 85 ? TrayBand.Red : load >= 65 ? TrayBand.Amber : TrayBand.Ok;
    }

    /// <summary>Número da CPU no ícone: até dois dígitos (100% vira 99, que cabe no ícone de 16 px).</summary>
    public static string CpuLabel(double cpu) => Math.Clamp((int)Math.Round(cpu), 0, 99).ToString();

    /// <summary>
    /// Leva 10.3: o número que o ícone mostra, ou null para o logo com a bolinha de estado. Sem sensor de temperatura ou
    /// com a nota ainda sendo calculada, volta para o logo em vez de mostrar um número inventado.
    /// </summary>
    public static string? IconLabel(TrayIconShows shows, Snapshot s, int score) => shows switch
    {
        TrayIconShows.Cpu => CpuLabel(s.Cpu),
        TrayIconShows.Temperature when s.TempC > 0 => Math.Clamp((int)Math.Round(s.TempC), 0, 99).ToString(),
        TrayIconShows.Score when score > 0 => Math.Clamp(score, 0, 99).ToString(),
        _ => null,
    };

    private static readonly char[] Bars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    /// <summary>Leva 10.3: minigráfico em texto (0 a 100%) para a dica do ícone, com <paramref name="width"/> barras.</summary>
    public static string Spark(IReadOnlyList<double> values, int width = 10)
    {
        if (values.Count == 0 || width <= 0) return "";
        var sb = new System.Text.StringBuilder(width);
        int n = Math.Min(width, values.Count);
        for (int i = 0; i < n; i++)
        {
            // Cada barra é a média do seu pedaço da série (a mais antiga à esquerda)
            int from = i * values.Count / n, to = Math.Max(from + 1, (i + 1) * values.Count / n);
            double avg = 0;
            for (int j = from; j < to; j++) avg += values[j];
            avg /= to - from;
            sb.Append(Bars[(int)Math.Round(Math.Clamp(avg, 0, 100) / 100 * (Bars.Length - 1))]);
        }
        return sb.ToString();
    }

    /// <summary>Dica do ícone: uso agora e a CPU dos últimos minutos. O Windows corta em 63 caracteres.</summary>
    public static string Tooltip(Snapshot s, IReadOnlyList<double> recentCpu)
    {
        string text = $"CPU {s.Cpu:0}% · RAM {s.RamPct:0}% · {Loc.T("Disco livre")} {s.DiskFreePct:0}%";
        string spark = recentCpu.Count >= 2 ? $"\n{Loc.F("CPU, {0} min", Math.Max(1, (int)Math.Round(recentCpu.Count * 3 / 60.0)))}: {Spark(recentCpu)}" : "";
        string all = text + spark;
        return all.Length <= 63 ? all : text.Length <= 63 ? text : text[..63];
    }

    /// <summary>Linhas informativas no topo do menu (B03).</summary>
    public static (string Usage, string Health) MenuLines(Snapshot s, int score) =>
        ($"CPU {s.Cpu:0}% · RAM {s.RamPct:0}%" + (s.TempC > 0 ? " · " + Units.Temp(s.TempC) : ""),
         $"Disco livre {s.DiskFreePct:0}%" + (score > 0 ? $" · nota {score}" : ""));
}

/// <summary>B37: tema claro de dia e escuro à noite, nos horários escolhidos.</summary>
public static class ThemeSchedule
{
    public const string Mode = "schedule";

    /// <summary>Escuro de <paramref name="darkAt"/> até <paramref name="lightAt"/> (pode passar da meia-noite). Horário inválido: claro.</summary>
    public static bool IsDark(TimeOnly now, string lightAt, string darkAt) =>
        AlertPolicy.TryParseClock(lightAt, out var light) && AlertPolicy.TryParseClock(darkAt, out var dark)
        && AlertPolicy.InQuietHours(now, dark, light);

    /// <summary>Converte o modo salvo no que o ThemeManager entende ("system", "light" ou "dark").</summary>
    public static string Resolve(string mode, TimeOnly now, string lightAt, string darkAt) =>
        mode == Mode ? (IsDark(now, lightAt, darkAt) ? "dark" : "light") : mode;
}
