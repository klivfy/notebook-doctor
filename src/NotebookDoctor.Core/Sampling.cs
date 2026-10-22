namespace NotebookDoctor.Services;

/// <summary>
/// M42: de quanto em quanto tempo o app lê CPU, memória e disco. Com a janela ou o overlay à vista, a cada 3 s
/// (a pessoa está olhando). Em segundo plano, 15 s na tomada; na bateria, 30 s; com economia de bateria ligada
/// ou carga baixa, 60 s. Os avisos contam minutos pelo relógio, então continuam valendo com qualquer intervalo.
/// </summary>
public static class Sampling
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Background = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan OnBattery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Saving = TimeSpan.FromSeconds(60);
    public const int LowBatteryPct = 20;

    /// <param name="batteryPct">Carga de 0 a 100; negativo = sem bateria ou desconhecida.</param>
    public static TimeSpan Interval(bool onScreen, bool onBattery, bool batterySaver, int batteryPct)
    {
        if (onScreen) return Visible;
        if (!onBattery) return Background;
        if (batterySaver || batteryPct is >= 0 and <= LowBatteryPct) return Saving;
        return OnBattery;
    }

    /// <summary>Janela das amostras recentes usadas na nota e no diagnóstico: as últimas N, sem passar de 15 minutos.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(15);
    public const int RecentMax = 36;

    public static void TrimRecent(List<Snapshot> recent, DateTime now)
    {
        recent.RemoveAll(s => now - s.Time > RecentWindow);
        if (recent.Count > RecentMax) recent.RemoveRange(0, recent.Count - RecentMax);
    }
}
