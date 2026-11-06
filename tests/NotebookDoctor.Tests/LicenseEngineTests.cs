using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public sealed class LicenseEngineTests
{
    [Fact]
    public void Trial_comeca_no_primeiro_uso_e_dura_15_dias()
    {
        DateTime now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        LicenseState state = LicenseEngine.Evaluate(snapshot: null, now);

        Assert.Equal(LicenseMode.Trial, state.Mode);
        Assert.True(state.IsPro);
        Assert.False(state.IsPerpetual);
        Assert.NotNull(state.TrialEndsAtUtc);
        Assert.Equal(15, state.TrialDaysLeft);
    }

    [Fact]
    public void Trial_expira_apos_15_dias()
    {
        DateTime start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = new LicenseSnapshot(start, start, null);
        DateTime after = start.AddDays(15).AddMinutes(1);

        LicenseState state = LicenseEngine.Evaluate(snapshot, after);

        Assert.Equal(LicenseMode.Free, state.Mode);
        Assert.False(state.IsPro);
        Assert.Equal(0, state.TrialDaysLeft);
    }

    [Fact]
    public void Rollback_de_relogio_acima_da_tolerancia_bloqueia_estado_pro()
    {
        DateTime lastSeen = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        DateTime now = lastSeen.AddMinutes(-20);
        var cached = new CachedLicense(LicenseSource.Stripe, true, false, lastSeen.AddDays(30), lastSeen);
        var snapshot = new LicenseSnapshot(lastSeen.AddDays(-5), lastSeen, cached);

        LicenseState state = LicenseEngine.Evaluate(snapshot, now);

        Assert.Equal(LicenseMode.Free, state.Mode);
        Assert.False(state.IsPro);
        Assert.True(state.IsClockRollbackDetected);
    }

    [Fact]
    public void Licenca_perpetua_em_cache_mantem_pro()
    {
        DateTime now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var cached = new CachedLicense(LicenseSource.ManualKey, true, true, null, now.AddMonths(-2));
        var snapshot = new LicenseSnapshot(now.AddDays(-20), now.AddDays(-1), cached);

        LicenseState state = LicenseEngine.Evaluate(snapshot, now);

        Assert.Equal(LicenseMode.Pro, state.Mode);
        Assert.True(state.IsPro);
        Assert.True(state.IsPerpetual);
        Assert.False(state.IsOfflineGrace);
    }

    [Fact]
    public void Assinatura_expirada_permanece_pro_na_carencia_offline()
    {
        DateTime now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime end = now.AddDays(-2);
        var cached = new CachedLicense(LicenseSource.Store, true, false, end, now.AddDays(-4));
        var snapshot = new LicenseSnapshot(now.AddDays(-20), now.AddDays(-1), cached);

        LicenseState state = LicenseEngine.Evaluate(snapshot, now);

        Assert.Equal(LicenseMode.Pro, state.Mode);
        Assert.True(state.IsPro);
        Assert.True(state.IsOfflineGrace);
        Assert.Equal(end, state.SubscriptionEndsAtUtc);
    }

    [Fact]
    public void Assinatura_expirada_fora_da_carencia_cai_para_trial_ou_free()
    {
        DateTime now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime end = now.AddDays(-5);
        var cached = new CachedLicense(LicenseSource.Store, true, false, end, now.AddDays(-10));
        var snapshot = new LicenseSnapshot(now.AddDays(-1), now.AddDays(-1), cached);

        LicenseState state = LicenseEngine.Evaluate(snapshot, now);

        Assert.Equal(LicenseMode.Trial, state.Mode);
        Assert.True(state.IsPro);
        Assert.False(state.IsOfflineGrace);
    }

    [Fact]
    public void Rollback_de_relogio_persiste_em_avaliacoes_consecutivas()
    {
        DateTime lastSeen = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var cached = new CachedLicense(LicenseSource.Stripe, true, false, lastSeen.AddDays(30), lastSeen);
        var snapshot = new LicenseSnapshot(lastSeen.AddDays(-5), lastSeen, cached);

        DateTime firstRollback = lastSeen.AddHours(-2);
        LicenseState first = LicenseEngine.Evaluate(snapshot, firstRollback);

        Assert.Equal(LicenseMode.Free, first.Mode);
        Assert.False(first.IsPro);
        Assert.True(first.IsClockRollbackDetected);
        Assert.Equal(lastSeen, first.UpdatedSnapshot.LastSeenAtUtc);

        // Segunda abertura com o relógio ainda atrasado continua bloqueada
        DateTime secondRollback = firstRollback.AddMinutes(10);
        LicenseState second = LicenseEngine.Evaluate(first.UpdatedSnapshot, secondRollback);

        Assert.Equal(LicenseMode.Free, second.Mode);
        Assert.False(second.IsPro);
        Assert.True(second.IsClockRollbackDetected);
        Assert.Equal(lastSeen, second.UpdatedSnapshot.LastSeenAtUtc);
    }

    [Fact]
    public void Rollback_dentro_da_tolerancia_de_5_minutos_nao_bloqueia_mas_preserva_maior_hora()
    {
        DateTime lastSeen = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var cached = new CachedLicense(LicenseSource.Store, true, true, null, lastSeen);
        var snapshot = new LicenseSnapshot(lastSeen.AddDays(-3), lastSeen, cached);

        // 5 minutos exatos de diferença estão dentro da tolerância
        DateTime slightDrift = lastSeen.AddMinutes(-5);
        LicenseState first = LicenseEngine.Evaluate(snapshot, slightDrift);

        Assert.Equal(LicenseMode.Pro, first.Mode);
        Assert.True(first.IsPro);
        Assert.False(first.IsClockRollbackDetected);
        Assert.Equal(lastSeen, first.UpdatedSnapshot.LastSeenAtUtc);

        // Recuar mais 1 minuto ultrapassa a tolerância em relação à maior hora vista
        LicenseState second = LicenseEngine.Evaluate(first.UpdatedSnapshot, slightDrift.AddMinutes(-1));

        Assert.Equal(LicenseMode.Free, second.Mode);
        Assert.False(second.IsPro);
        Assert.True(second.IsClockRollbackDetected);
    }

    [Fact]
    public void Trial_no_instante_exato_de_15_dias_ja_esta_expirado()
    {
        DateTime start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = new LicenseSnapshot(start, start, null);
        DateTime exactEnd = start + LicenseEngine.TrialPeriod;

        LicenseState state = LicenseEngine.Evaluate(snapshot, exactEnd);

        Assert.Equal(LicenseMode.Free, state.Mode);
        Assert.False(state.IsPro);
        Assert.Equal(0, state.TrialDaysLeft);
        Assert.Equal(exactEnd, state.TrialEndsAtUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Data_fora_de_utc_lanca_ArgumentException(DateTimeKind kind)
    {
        DateTime invalidNow = new(2026, 11, 1, 10, 0, 0, kind);

        Assert.Throws<ArgumentException>(() => LicenseEngine.Evaluate(snapshot: null, invalidNow));
    }

    [Fact]
    public void Resultado_do_provedor_substitui_cache_e_ativa_pro()
    {
        DateTime start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        DateTime now = start.AddDays(20);
        var expiredSnapshot = new LicenseSnapshot(start, now.AddHours(-1), null);
        DateTime subEnd = now.AddDays(30);
        var providerResult = new LicenseProviderResult(LicenseSource.Stripe, IsPro: true, IsPerpetual: false, subEnd, now);

        LicenseState state = LicenseEngine.Evaluate(expiredSnapshot, now, providerResult);

        Assert.Equal(LicenseMode.Pro, state.Mode);
        Assert.True(state.IsPro);
        Assert.Equal(LicenseSource.Stripe, state.Source);
        Assert.Equal(subEnd, state.SubscriptionEndsAtUtc);
        Assert.NotNull(state.UpdatedSnapshot.Cached);
        Assert.Equal(LicenseSource.Stripe, state.UpdatedSnapshot.Cached!.Source);
    }

    [Fact]
    public void Licenca_pro_nao_perpetua_sem_data_de_termino_nao_concede_pro_infinito()
    {
        DateTime start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        DateTime now = start.AddDays(20);
        var malformedCache = new CachedLicense(LicenseSource.Stripe, IsPro: true, IsPerpetual: false, SubscriptionEndsAtUtc: null, now);
        var snapshot = new LicenseSnapshot(start, now.AddHours(-1), malformedCache);

        LicenseState state = LicenseEngine.Evaluate(snapshot, now);

        Assert.Equal(LicenseMode.Free, state.Mode);
        Assert.False(state.IsPro);
    }
}
