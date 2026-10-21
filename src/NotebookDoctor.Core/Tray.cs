namespace NotebookDoctor.Services;

public enum TrayBand { Ok, Amber, Red }

/// <summary>B01/B03: o que o ícone e o menu da bandeja mostram.</summary>
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
