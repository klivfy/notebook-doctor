using System.Text.Json;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class UpdateRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0);
    private static UpdateEntry U(string title, int daysAgo, int result) => new(title, Now.AddDays(-daysAgo), result);
    private static UpdateStatus S(params UpdateEntry[] h) => new(h, false, null);

    [Fact]
    public void Mesma_atualizacao_falhando_duas_vezes_vira_achado()
    {
        var f = Assert.Single(UpdateRules.Evaluate(S(
            U("2026-10 Atualização Cumulativa (KB5031356)", 10, 4),
            U("2026-10 Atualização Cumulativa para Windows 11 (KB5031356)", 3, 4),
            U("Atualização de Inteligência de Segurança (KB2267602)", 1, 2)), Now));
        Assert.Equal("Atualização do Windows falhando", f.Title);
        Assert.Contains("2 tentativas sem sucesso, a última em 19/10", f.Detail);
        Assert.Equal("open-update", f.FixId);
    }

    [Fact]
    public void Falha_seguida_de_sucesso_ou_uma_falha_so_nao_avisa()
    {
        Assert.Empty(UpdateRules.Stuck([U("A (KB5000001)", 10, 4), U("A (KB5000001)", 8, 4), U("A (KB5000001)", 2, 2)]));
        Assert.Empty(UpdateRules.Stuck([U("A (KB5000001)", 2, 4)]));
        // Sucesso antigo não zera falhas novas
        Assert.Single(UpdateRules.Stuck([U("A (KB5000001)", 30, 2), U("A (KB5000001)", 5, 5), U("A (KB5000001)", 1, 4)]));
    }

    [Fact]
    public void Muito_tempo_sem_atualizar_avisa_a_menos_que_esteja_pausado()
    {
        var old = S(U("X (KB5000001)", 50, 2));
        Assert.Equal("Windows sem atualizar há 50 dias", Assert.Single(UpdateRules.Evaluate(old, Now)).Title);
        Assert.Empty(UpdateRules.Evaluate(old with { PausedUntil = Now.AddDays(7) }, Now));
        Assert.Single(UpdateRules.Evaluate(old with { PausedUntil = Now.AddDays(-1) }, Now)); // pausa já acabou
        Assert.Empty(UpdateRules.Evaluate(S(U("X (KB5000001)", 10, 2)), Now));
        Assert.Empty(UpdateRules.Evaluate(S(), Now)); // histórico vazio: sem base para dizer nada
    }

    [Fact]
    public void Servico_desativado_vem_sozinho()
    {
        var u = new UpdateStatus([U("A (KB5000001)", 50, 4), U("A (KB5000001)", 45, 4)], true, null);
        Assert.Equal("Windows Update desativado", Assert.Single(UpdateRules.Evaluate(u, Now)).Title);
        Assert.Empty(UpdateRules.Evaluate(null, Now));
    }

    [Fact]
    public void Le_o_historico_e_a_pausa_do_script()
    {
        using var doc = JsonDocument.Parse("""[{"t":"A (KB5000001)","d":"2026-10-01T08:30:00","r":2},{"t":"","d":"2026-10-01T08:30:00","r":2},{"t":"B","d":"ontem","r":4}]""");
        var e = Assert.Single(UpdateRules.ParseHistory(doc.RootElement));
        Assert.Equal(new DateTime(2026, 10, 1, 8, 30, 0), e.Date);
        Assert.True(e.Succeeded);
        Assert.Equal("KB5000001", UpdateRules.Key("Atualização (kb5000001)"));
        Assert.NotNull(UpdateRules.ParsePause("2026-11-01T00:00:00Z"));
        Assert.Null(UpdateRules.ParsePause(""));
    }
}

public class DriverRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 22);

    [Fact]
    public void Driver_generico_do_Windows()
    {
        var f = Assert.Single(DriverRules.Evaluate([new GpuDriver("Microsoft Basic Display Adapter", "10.0.22621.1", Now.AddDays(-30))], Now));
        Assert.Equal("Vídeo sem o driver do fabricante", f.Title);
        Assert.Equal("open-optional-updates", f.FixId);
        Assert.True(DriverRules.IsBasic("Adaptador de Vídeo Básico da Microsoft"));
    }

    [Fact]
    public void Driver_com_mais_de_2_anos()
    {
        var f = Assert.Single(DriverRules.Evaluate([new GpuDriver("Intel(R) UHD Graphics", "27.20.100.8681", new DateTime(2023, 3, 1))], Now));
        Assert.Equal("Driver de vídeo antigo", f.Title);
        Assert.Equal("Intel(R) UHD Graphics: versão 27.20.100.8681, de 03/2023", f.Detail);
        Assert.Empty(DriverRules.Evaluate([new GpuDriver("NVIDIA GeForce RTX 4060", "32.0.15.6094", Now.AddDays(-200))], Now));
        Assert.Empty(DriverRules.Evaluate([new GpuDriver("AMD Radeon", "1", null)], Now)); // sem data: não dá para dizer
    }

    [Fact]
    public void Adaptadores_virtuais_ficam_de_fora()
    {
        Assert.Empty(DriverRules.Evaluate([
            new GpuDriver("Microsoft Remote Display Adapter", "1", new DateTime(2015, 1, 1)),
            new GpuDriver("Parsec Virtual Display Adapter", "1", new DateTime(2015, 1, 1)),
            new GpuDriver("Microsoft Hyper-V Video", "1", new DateTime(2015, 1, 1)),
        ], Now));
        Assert.Empty(DriverRules.Evaluate(null, Now));
    }

    [Fact]
    public void Le_as_placas_do_script()
    {
        using var doc = JsonDocument.Parse("""{"n":" Intel(R) Iris(R) Xe Graphics ","v":"31.0.101.4502","d":"2023-06-14"}""");
        var g = Assert.Single(DriverRules.Parse(doc.RootElement));
        Assert.Equal("Intel(R) Iris(R) Xe Graphics", g.Name);
        Assert.Equal(new DateTime(2023, 6, 14), g.Date);
        using var noDate = JsonDocument.Parse("""[{"n":"X","v":"1","d":""},{"v":"sem nome"}]""");
        Assert.Null(Assert.Single(DriverRules.Parse(noDate.RootElement)).Date);
    }
}

public class MaintenanceRulesTests
{
    [Theory]
    [InlineData(15, false, false, true)]
    [InlineData(5, false, false, false)] // alguém usando o PC
    [InlineData(15, true, false, false)] // Modo Foco
    [InlineData(15, false, true, false)] // na bateria
    public void Rotina_do_app_so_com_o_PC_parado_e_na_tomada(int idleMin, bool focus, bool battery, bool runs) =>
        Assert.Equal(runs, MaintenanceRules.CanRunIdleTask(TimeSpan.FromMinutes(idleMin), focus, battery));

    [Fact]
    public void Avisa_otimizacao_e_manutencao_desligadas()
    {
        var f = MaintenanceRules.Evaluate(new WindowsMaintenance(false, true), "SSD").ToList();
        Assert.Equal(["Otimização automática das unidades desligada", "Manutenção automática do Windows desativada"], f.Select(x => x.Title));
        Assert.Equal("O SSD não recebe o TRIM semanal", f[0].Detail);
        Assert.Equal("open-defrag", f[0].FixId);
        Assert.Empty(MaintenanceRules.Evaluate(new WindowsMaintenance(true, false), "SSD"));
        Assert.Empty(MaintenanceRules.Evaluate(new WindowsMaintenance(null, false), "HDD")); // não deu para ler: não acusa
        Assert.Empty(MaintenanceRules.Evaluate(null, "SSD"));
    }

    [Fact]
    public void Achados_entram_no_diagnostico_com_explicacao()
    {
        var h = Make.Health() with
        {
            Updates = new UpdateStatus([], true, null),
            Gpus = [new GpuDriver("Microsoft Basic Display Adapter", "1", null)],
            Maintenance = new WindowsMaintenance(false, false),
        };
        var f = Diagnosis.Evaluate(Make.Minute(Make.Snap()), h, null, Make.FreshBoot);
        Assert.Contains(f, x => x.Title == "Windows Update desativado" && x.Why.Length > 0);
        Assert.Contains(f, x => x.Title == "Vídeo sem o driver do fabricante" && x.Why.Length > 0);
        Assert.Contains(f, x => x.Title == "Otimização automática das unidades desligada" && x.Why.Length > 0);
        Assert.DoesNotContain(f, x => x.Title == "Tudo certo");
    }
}
