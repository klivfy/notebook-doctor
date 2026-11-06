namespace NotebookDoctor.Services;

/// <summary>Origem da licença.</summary>
public enum LicenseSource
{
    None = 0,
    Store = 1,
    Stripe = 2,
    ManualKey = 3,
}

/// <summary>Modo efetivo da pessoa no momento.</summary>
public enum LicenseMode
{
    Free = 0,
    Trial = 1,
    Pro = 2,
}

/// <summary>Resultado normalizado de um provedor de licença.</summary>
public sealed record LicenseProviderResult(
    LicenseSource Source,
    bool IsPro,
    bool IsPerpetual,
    DateTime? SubscriptionEndsAtUtc,
    DateTime ValidatedAtUtc);

/// <summary>Contrato único para Store, Stripe e chave manual.</summary>
public interface ILicenseProvider
{
    string Name { get; }
    ValueTask<LicenseProviderResult?> TryValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Licença em cache local, usada para funcionar offline.</summary>
public sealed record CachedLicense(
    LicenseSource Source,
    bool IsPro,
    bool IsPerpetual,
    DateTime? SubscriptionEndsAtUtc,
    DateTime LastValidatedAtUtc);

/// <summary>Estado persistido localmente para calcular trial e proteção contra relógio.</summary>
public sealed record LicenseSnapshot(
    DateTime? TrialStartedAtUtc,
    DateTime? LastSeenAtUtc,
    CachedLicense? Cached);

/// <summary>Saída do motor de licença no momento da avaliação.</summary>
public sealed record LicenseState(
    LicenseMode Mode,
    bool IsPro,
    bool IsPerpetual,
    DateTime? TrialEndsAtUtc,
    int TrialDaysLeft,
    DateTime? SubscriptionEndsAtUtc,
    bool IsClockRollbackDetected,
    bool IsOfflineGrace,
    LicenseSource Source,
    DateTime LastSeenAtUtc,
    LicenseSnapshot UpdatedSnapshot)
{
    public static LicenseState Free(DateTime nowUtc, LicenseSnapshot snapshot, bool rollback = false)
    {
        // Nunca recua a maior hora já vista: com o relógio atrasado, gravar a hora atual apagaria a prova do atraso
        DateTime seen = LicenseEngine.LatestSeen(snapshot.LastSeenAtUtc, nowUtc);
        return new(LicenseMode.Free, false, false, null, 0, null, rollback, false, LicenseSource.None, seen, snapshot with { LastSeenAtUtc = seen });
    }
}

/// <summary>
/// Núcleo da regra de licenciamento: trial local, proteção contra relógio e carência offline de assinatura.
/// Não fala com rede nem com UI.
/// </summary>
public static class LicenseEngine
{
    public static readonly TimeSpan TrialPeriod = TimeSpan.FromDays(15);
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(3);
    public static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(5);

    internal static DateTime LatestSeen(DateTime? previousUtc, DateTime nowUtc) =>
        previousUtc is DateTime prev && prev > nowUtc ? prev : nowUtc;

    public static LicenseState Evaluate(LicenseSnapshot? snapshot, DateTime nowUtc, LicenseProviderResult? provider = null)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Use UTC para evitar erros de fuso.", nameof(nowUtc));

        LicenseSnapshot local = snapshot ?? new LicenseSnapshot(null, null, null);

        if (local.LastSeenAtUtc is DateTime lastSeen && nowUtc + ClockRollbackTolerance < lastSeen)
            return LicenseState.Free(nowUtc, local, rollback: true);

        DateTime seen = LatestSeen(local.LastSeenAtUtc, nowUtc);
        CachedLicense? effectiveCache = local.Cached;

        if (provider is not null)
            effectiveCache = new CachedLicense(provider.Source, provider.IsPro, provider.IsPerpetual, provider.SubscriptionEndsAtUtc, provider.ValidatedAtUtc);

        if (effectiveCache is { IsPro: true })
        {
            if (effectiveCache.IsPerpetual)
            {
                var perpetualSnapshot = local with { LastSeenAtUtc = seen, Cached = effectiveCache };
                return new LicenseState(
                    LicenseMode.Pro,
                    IsPro: true,
                    IsPerpetual: true,
                    TrialEndsAtUtc: null,
                    TrialDaysLeft: 0,
                    SubscriptionEndsAtUtc: null,
                    IsClockRollbackDetected: false,
                    IsOfflineGrace: false,
                    Source: effectiveCache.Source,
                    LastSeenAtUtc: seen,
                    UpdatedSnapshot: perpetualSnapshot);
            }

            if (effectiveCache.SubscriptionEndsAtUtc is DateTime ends)
            {
                if (nowUtc <= ends)
                {
                    var activeSnapshot = local with { LastSeenAtUtc = seen, Cached = effectiveCache };
                    return new LicenseState(
                        LicenseMode.Pro,
                        IsPro: true,
                        IsPerpetual: false,
                        TrialEndsAtUtc: null,
                        TrialDaysLeft: 0,
                        SubscriptionEndsAtUtc: ends,
                        IsClockRollbackDetected: false,
                        IsOfflineGrace: false,
                        Source: effectiveCache.Source,
                        LastSeenAtUtc: seen,
                        UpdatedSnapshot: activeSnapshot);
                }

                DateTime graceUntil = ends + OfflineGrace;
                if (nowUtc <= graceUntil)
                {
                    var graceSnapshot = local with { LastSeenAtUtc = seen, Cached = effectiveCache };
                    return new LicenseState(
                        LicenseMode.Pro,
                        IsPro: true,
                        IsPerpetual: false,
                        TrialEndsAtUtc: null,
                        TrialDaysLeft: 0,
                        SubscriptionEndsAtUtc: ends,
                        IsClockRollbackDetected: false,
                        IsOfflineGrace: true,
                        Source: effectiveCache.Source,
                        LastSeenAtUtc: seen,
                        UpdatedSnapshot: graceSnapshot);
                }
            }
        }

        DateTime trialStart = local.TrialStartedAtUtc ?? nowUtc;
        DateTime trialEnds = trialStart + TrialPeriod;
        bool inTrial = nowUtc < trialEnds;
        int daysLeft = inTrial ? Math.Max(1, (int)Math.Ceiling((trialEnds - nowUtc).TotalDays)) : 0;

        var trialSnapshot = local with { TrialStartedAtUtc = trialStart, LastSeenAtUtc = seen, Cached = effectiveCache };

        if (inTrial)
        {
            return new LicenseState(
                LicenseMode.Trial,
                IsPro: true,
                IsPerpetual: false,
                TrialEndsAtUtc: trialEnds,
                TrialDaysLeft: daysLeft,
                SubscriptionEndsAtUtc: null,
                IsClockRollbackDetected: false,
                IsOfflineGrace: false,
                Source: LicenseSource.None,
                LastSeenAtUtc: seen,
                UpdatedSnapshot: trialSnapshot);
        }

        return new LicenseState(
            LicenseMode.Free,
            IsPro: false,
            IsPerpetual: false,
            TrialEndsAtUtc: trialEnds,
            TrialDaysLeft: 0,
            SubscriptionEndsAtUtc: null,
            IsClockRollbackDetected: false,
            IsOfflineGrace: false,
            Source: LicenseSource.None,
            LastSeenAtUtc: seen,
            UpdatedSnapshot: trialSnapshot);
    }
}
