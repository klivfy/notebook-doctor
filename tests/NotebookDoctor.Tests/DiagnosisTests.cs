namespace NotebookDoctor.Tests;

public class DiagnosisTests : IDisposable
{
    public void Dispose() => ProcessTools.SetUserProtected(Array.Empty<string>());

    private static List<Finding> Eval(Snapshot s, HealthInfo? h = null, TimeSpan? uptime = null, IReadOnlyList<LeakSuspect>? leaks = null) =>
        Diagnosis.Evaluate(Make.Minute(s), h ?? Make.Health(), leaks, uptime ?? Make.FreshBoot);

    [Fact]
    public void Sem_amostras_nao_ha_achados() =>
        Assert.Empty(Diagnosis.Evaluate(Array.Empty<Snapshot>(), Make.Health(), uptime: Make.FreshBoot));

    [Fact]
    public void Sistema_saudavel_diz_tudo_certo()
    {
        var f = Assert.Single(Eval(Make.Snap()));
        Assert.Equal("OK", f.Level);
        Assert.Equal("Tudo certo", f.Title);
    }

    [Fact]
    public void Problemas_vem_antes_de_atencao()
    {
        var f = Eval(Make.Snap(cpu: 45, ram: 90));
        Assert.Equal(["PROBLEMA", "ATENCAO"], f.Select(x => x.Level));
        Assert.Equal("Memória RAM quase cheia", f[0].Title);
        Assert.True(f[0].HasWhy);
    }

    [Fact]
    public void Programa_pesado_pode_ser_encerrado()
    {
        var f = Eval(Make.Snap(groups: [new ProcGroup("chrome", 20, 30, 2500)]));
        var heavy = Assert.Single(f, x => x.Title == "Programa pesado: chrome");
        Assert.Equal("kill", heavy.FixId);
        Assert.Equal("chrome", heavy.FixArg);
    }

    [Fact]
    public void Programa_protegido_pelo_usuario_nao_ganha_botao_encerrar()
    {
        ProcessTools.SetUserProtected(["chrome"]);
        var heavy = Assert.Single(Eval(Make.Snap(groups: [new ProcGroup("chrome", 20, 30, 2500)])), x => x.Title == "Programa pesado: chrome");
        Assert.False(heavy.HasFix);
    }

    [Fact]
    public void Plano_equilibrado_na_tomada_sugere_alto_desempenho_quando_existe()
    {
        var f = Eval(Make.Snap(cpu: 30), Make.Health(onAc: true, highGuid: "abc"));
        var plan = Assert.Single(f, x => x.Title.StartsWith("Plano de energia"));
        Assert.Equal("power-high", plan.FixId);
        Assert.Equal("abc", plan.FixArg);
    }

    [Fact]
    public void Plano_equilibrado_sem_alto_desempenho_abre_configuracoes()
    {
        var plan = Assert.Single(Eval(Make.Snap(cpu: 30), Make.Health(onAc: true)), x => x.Title.StartsWith("Plano de energia"));
        Assert.Equal($"tab:{Diagnosis.TabSettings}", plan.FixId);
    }

    [Fact]
    public void Na_bateria_nao_sugere_trocar_o_plano() =>
        Assert.DoesNotContain(Eval(Make.Snap(cpu: 30), Make.Health(onAc: false, highGuid: "abc")), x => x.Title.StartsWith("Plano de energia"));

    [Fact]
    public void Muito_tempo_ligado_sugere_reiniciar() =>
        Assert.Contains(Eval(Make.Snap(), uptime: TimeSpan.FromDays(9)), x => x.Title == "Computador ligado há muito tempo");

    [Fact]
    public void Temporarios_grandes_oferecem_limpeza()
    {
        var f = Assert.Single(Eval(Make.Snap(), Make.Health(tempBytes: 3L << 30)));
        Assert.Equal("clean", f.FixId);
        Assert.Equal("3,0 GB", f.Detail);
    }

    [Fact]
    public void CPU_lenta_sob_carga_indica_throttling() =>
        Assert.Contains(Eval(Make.Snap(cpu: 80, cpuPerf: 40)), x => x.Title.StartsWith("O processador está reduzindo"));

    [Fact]
    public void Vazamento_de_memoria_vira_achado()
    {
        var f = Eval(Make.Snap(), leaks: [new LeakSuspect("app", 200, 900, 30)]);
        var leak = Assert.Single(f);
        Assert.Equal("Possível vazamento de memória: app", leak.Title);
        Assert.Equal("kill", leak.FixId);
    }
}
