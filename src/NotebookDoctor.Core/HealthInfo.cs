namespace NotebookDoctor.Services;

public sealed record UnsignedApp(string Name, string Path);

/// <summary>M40: disco virtual (.vhdx) de uma distribuição do WSL ou do Docker Desktop.</summary>
public sealed record VirtualDisk(string Name, string Path, long Bytes);

/// <param name="DefenderRealtime">Proteção em tempo real do Windows Defender (null = não deu para ler)</param>
/// <param name="OtherAv">Antivírus de terceiros registrados no Windows (quando há, o Defender pode estar desligado de propósito)</param>
/// <param name="OtherAvActive">M32: dos antivírus de terceiros, os que estão com a proteção em tempo real ligada</param>
public sealed record SecurityInfo(bool? DefenderRealtime, int? SignatureAgeDays, IReadOnlyList<string> OtherAv, IReadOnlyList<string> FirewallOff,
                                  IReadOnlyList<string>? OtherAvActive = null)
{
    public static readonly SecurityInfo Empty = new(null, null, Array.Empty<string>(), Array.Empty<string>());
}

public sealed record HealthInfo(
    string DiskName, string DiskType, string DiskHealth, int? DiskWearPct, int? DiskTempC,
    int? BatteryHealthPct, int? BatteryCycles,
    int? BootSeconds, int? BootAvgSeconds,
    bool RebootPending, bool OnAc, string ActivePlan, string? HighPlanGuid,
    int StartupEnabled, long TempBytes,
    IReadOnlyList<string> DriverErrors, IReadOnlyList<UnsignedApp> Unsigned, SecurityInfo? Security = null,
    IReadOnlyList<VirtualDisk>? VirtualDisks = null)
{
    public SecurityInfo Sec => Security ?? SecurityInfo.Empty;
    public static readonly HealthInfo Empty = new("", "", "", null, null, null, null, null, null, false, false, "", null, 0, 0,
        Array.Empty<string>(), Array.Empty<UnsignedApp>());
    public bool HasDisk => DiskName.Length > 0;
    /// <summary>Sem leitura de bateria (desktop ou relatório indisponível), as regras de bateria ficam de fora.</summary>
    public bool HasBattery => BatteryHealthPct is not null;
    public bool DiskHealthy => DiskHealth is "" or "Healthy";
    public bool IsBalancedPlan => ActivePlan.Contains("quilibrado", StringComparison.OrdinalIgnoreCase) || ActivePlan.Contains("balanced", StringComparison.OrdinalIgnoreCase);
}
