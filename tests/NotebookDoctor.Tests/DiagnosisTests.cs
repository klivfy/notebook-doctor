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

    [Theory]
    [InlineData(90, 10, "tab:1")]
    [InlineData(78, 10, "tab:1")]
    [InlineData(40, 80, "tab:1")]
    [InlineData(40, 45, "tab:1")]
    public void RAM_e_CPU_altas_levam_a_Processos(double ram, double cpu, string fix) =>
        Assert.All(Eval(Make.Snap(cpu: cpu, ram: ram)).Where(f => f.Level != "OK"), f => Assert.Equal(fix, f.FixId));

    [Fact]
    public void Todo_problema_ou_atencao_tem_correcao()
    {
        // Um pouco de tudo de uma vez: cada achado precisa oferecer um botão
        var groups = new[] { new ProcGroup("chrome", 20, 30, 2500), new ProcGroup("vmmemWSL", 1, 1, 4096) };
        var s = Make.Snap(cpu: 80, ram: 90, diskBusy: 90, freePct: 5, groups: groups, cpuPerf: 40, tempC: 92);
        var h = new HealthInfo("SSD", "SSD", "Unhealthy", 90, 75, 50, 900, 120, 100, true, true, "Equilibrado", "abc", 15, 3L << 30,
            ["Wi-Fi"], [new UnsignedApp("x", @"C:\Users\a\AppData\x.exe")],
            new SecurityInfo(false, 10, [], ["Público"]));
        var f = Diagnosis.Evaluate(Make.Minute(s), h, [new LeakSuspect("leaky", 100, 900, 30)], TimeSpan.FromDays(10));
        var semBotao = f.Where(x => !x.HasFix && x.Title != "Disco quente").Select(x => x.Title).ToList();
        Assert.Empty(semBotao);
    }

    [Fact]
    public void Recomendacoes_prioritarias_trazem_transparencia_minima()
    {
        var f = Eval(Make.Snap(cpu: 80, ram: 90, diskBusy: 90, freePct: 5), Make.Health(tempBytes: 3L << 30) with { StartupEnabled = 12, RebootPending = true });

        var ram = Assert.Single(f, x => x.Title == "Memória RAM quase cheia");
        Assert.Contains("85%", ram.EvidenceText);
        Assert.NotEmpty(ram.ProposedChangeText);
        Assert.Equal("Alto", ram.RiskText);
        Assert.Equal("Alta", ram.ConfidenceText);
        Assert.NotEmpty(ram.UndoText);

        var cpu = Assert.Single(f, x => x.Title == "CPU sobrecarregada");
        Assert.Contains("70%", cpu.EvidenceText);
        Assert.Equal("Alto", cpu.RiskText);
        Assert.Equal("Alta", cpu.ConfidenceText);

        var diskBusy = Assert.Single(f, x => x.Title == "Disco saturado");
        Assert.Contains("80%", diskBusy.EvidenceText);
        Assert.Equal("Alto", diskBusy.RiskText);
        Assert.Equal("Alta", diskBusy.ConfidenceText);

        var free = Assert.Single(f, x => x.Title == "Disco quase cheio");
        Assert.Contains("10%", free.EvidenceText);

        var reboot = Assert.Single(f, x => x.Title == "Reinício pendente");
        Assert.Equal("Médio", reboot.RiskText);
        Assert.Equal("Média", reboot.ConfidenceText);
    }

    [Fact]
    public void Achados_de_atencao_ou_problema_tem_transparencia_e_confianca()
    {
        var groups = new[] { new ProcGroup("chrome", 20, 30, 2500), new ProcGroup("vmmemWSL", 1, 1, 4096) };
        var s = Make.Snap(cpu: 82, ram: 88, diskBusy: 92, freePct: 8, groups: groups, cpuPerf: 45, tempC: 91);
        var h = Make.Health(onAc: true, highGuid: "abc", tempBytes: 3L << 30) with
        {
            StartupEnabled = 12,
            RebootPending = true,
            DiskHealth = "Unhealthy",
            DiskWearPct = 85,
            DiskTempC = 72,
            Security = new SecurityInfo(false, 10, [], ["Público"]) 
        };

        var findings = Diagnosis.Evaluate(Make.Minute(s), h, [new LeakSuspect("leaky", 100, 900, 30)], TimeSpan.FromDays(9));

        foreach (var item in findings.Where(x => x.Level is "PROBLEMA" or "ATENCAO"))
        {
            Assert.NotEmpty(item.EvidenceText);
            Assert.NotEmpty(item.ProposedChangeText);
            Assert.NotEmpty(item.RiskText);
            Assert.NotEmpty(item.ConfidenceText);
            Assert.NotEmpty(item.UndoText);
        }
    }

    [Fact]
    public void Reinicio_pendente_oferece_reiniciar() =>
        Assert.Equal("restart", Assert.Single(Eval(Make.Snap(), Make.Health() with { RebootPending = true })).FixId);

    [Fact]
    public void Throttling_com_plano_equilibrado_oferece_alto_desempenho()
    {
        var f = Assert.Single(Eval(Make.Snap(cpu: 80, cpuPerf: 40), Make.Health(highGuid: "abc")), x => x.Title.StartsWith("O processador"));
        Assert.Equal("power-high", f.FixId);
    }

    [Fact]
    public void Alto_desempenho_na_bateria_gera_aviso()
    {
        var f = Assert.Single(Eval(Make.Snap(), Make.Health(onAc: false, plan: "Alto desempenho")));
        Assert.Equal("Plano Alto desempenho na bateria", f.Title);
        Assert.Equal($"tab:{Diagnosis.TabSettings}", f.FixId);
        Assert.True(f.HasWhy);
    }

    [Fact]
    public void Sem_bateria_nao_avisa_sobre_plano_na_bateria() =>
        Assert.Equal("Tudo certo", Assert.Single(Eval(Make.Snap(), Make.Health(onAc: false, plan: "Alto desempenho", battery: null))).Title);

    private static HealthInfo WithAv(bool defender, params string[] othersOn) =>
        Make.Health() with { Security = new SecurityInfo(defender, 0, othersOn, [], othersOn) };

    [Fact]
    public void Dois_antivirus_ativos_geram_aviso()
    {
        var f = Assert.Single(Eval(Make.Snap(), WithAv(false, "Avast", "Norton")));
        Assert.Equal("Mais de um antivírus ativo", f.Title);
        Assert.Equal("open-apps", f.FixId);
        Assert.True(f.HasWhy);
    }

    [Fact]
    public void Defender_e_outro_ativos_ao_mesmo_tempo_geram_aviso() =>
        Assert.Equal("Avast, Microsoft Defender", Assert.Single(Eval(Make.Snap(), WithAv(true, "Avast"))).Detail);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Um_antivirus_so_nao_gera_aviso(bool defender) =>
        Assert.Equal("Tudo certo", Assert.Single(Eval(Make.Snap(), defender ? WithAv(true) : WithAv(false, "Avast"))).Title);

    [Fact]
    public void Antivirus_instalado_mas_desligado_nao_conta() =>
        Assert.Equal("Tudo certo", Assert.Single(Eval(Make.Snap(), Make.Health() with
        { Security = new SecurityInfo(true, 0, ["Avast"], [], []) })).Title);
}
