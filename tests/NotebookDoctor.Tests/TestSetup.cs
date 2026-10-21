using System.Globalization;
using System.Runtime.CompilerServices;

// ProcessTools guarda a lista de protegidos do usuário em estado estático: os testes não podem rodar em paralelo
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NotebookDoctor.Tests;

internal static class TestSetup
{
    // O app roda em pt-BR; textos com N0 e decimais dependem da cultura
    [ModuleInitializer]
    internal static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("pt-BR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
    }
}

/// <summary>Monta snapshots e HealthInfo com valores "saudáveis" e só o que o teste quer mudar.</summary>
internal static class Make
{
    public static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0);
    public static readonly TimeSpan FreshBoot = TimeSpan.FromHours(2);

    public static Snapshot Snap(double cpu = 10, double ram = 40, double diskBusy = 5, double freePct = 50,
                                IReadOnlyList<ProcGroup>? groups = null, DateTime? time = null, double cpuPerf = -1, double tempC = -1) =>
        new(time ?? T0, cpu, ram, ram / 100 * 16, 16, diskBusy, freePct, freePct / 100 * 500, groups ?? Array.Empty<ProcGroup>(), cpuPerf, tempC);

    /// <summary>12 amostras iguais: o "último minuto" das regras que usam média.</summary>
    public static List<Snapshot> Minute(Snapshot s) => Enumerable.Repeat(s, 12).ToList();

    public static HealthInfo Health(bool onAc = false, string plan = "Equilibrado", string? highGuid = null, int startup = 3,
                                    long tempBytes = 0, int? battery = 95, string diskHealth = "Healthy", int? wear = 5) =>
        new("SSD NVMe", "SSD", diskHealth, wear, 40, battery, 100, 30, 32, false, onAc, plan, highGuid, startup, tempBytes,
            Array.Empty<string>(), Array.Empty<UnsignedApp>(),
            new SecurityInfo(true, 0, Array.Empty<string>(), Array.Empty<string>()));
}
