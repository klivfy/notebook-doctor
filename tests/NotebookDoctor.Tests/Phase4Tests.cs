using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class ChartViewportTests
{
    private static ChartViewport View(int total) { var v = new ChartViewport(); v.SetTotal(total); return v; }

    [Fact]
    public void Sem_zoom_mostra_tudo_e_acompanha_pontos_novos()
    {
        var v = View(100);
        Assert.False(v.Zoomed);
        v.SetTotal(120);
        Assert.Equal((0, 120), (v.Start, v.Count));
    }

    [Fact]
    public void Zoom_mantem_o_ponto_sob_o_cursor()
    {
        var v = View(101);
        int before = v.IndexAt(0.25);
        v.Zoom(0.5, 0.25);
        Assert.True(v.Zoomed);
        Assert.Equal(50, v.Count);
        Assert.InRange(v.IndexAt(0.25) - before, -1, 1);
    }

    [Fact]
    public void Zoom_para_no_minimo_de_pontos_e_volta_ao_total()
    {
        var v = View(200);
        for (int i = 0; i < 50; i++) v.Zoom(0.5, 0.5);
        Assert.Equal(ChartViewport.MinPoints, v.Count);
        for (int i = 0; i < 50; i++) v.Zoom(2, 0.5);
        Assert.Equal(200, v.Count);
        Assert.False(v.Zoomed);
    }

    [Fact]
    public void Arrastar_nao_passa_das_pontas()
    {
        var v = View(100);
        v.Zoom(0.2, 1);
        v.Pan(1000);
        Assert.Equal(100 - v.Count, v.Start);
        v.Pan(-1000);
        Assert.Equal(0, v.Start);
    }

    [Fact]
    public void Com_zoom_o_trecho_fica_parado_quando_chegam_pontos()
    {
        var v = View(100);
        v.Zoom(0.2, 0);
        var (start, count) = (v.Start, v.Count);
        v.SetTotal(110);
        Assert.Equal((start, count), (v.Start, v.Count));
        v.Reset();
        Assert.Equal((0, 110), (v.Start, v.Count));
    }

    [Fact]
    public void Serie_curta_nao_tem_zoom()
    {
        var v = View(5);
        v.Zoom(0.5, 0.5);
        Assert.False(v.Zoomed);
    }
}

public class WindowPlacementTests
{
    private static readonly Box Main = new(0, 0, 1920, 1040), Right = new(1920, 0, 1280, 984);

    // U2: 1366 × 768 com escala de 125%, sem a barra de tarefas
    private static readonly Box Notebook = new(0, 0, 1092, 566);

    [Fact]
    public void Ferramenta_maior_que_o_notebook_encolhe_e_entra_inteira()
    {
        var fit = WindowPlacement.Inside(new Box(-44, -117, 1180, 800), Notebook);
        Assert.Equal(new Box(0, 0, 1092, 566), fit);
    }

    [Fact]
    public void Janela_que_cabe_fica_onde_esta()
    {
        var w = new Box(100, 50, 900, 500);
        Assert.Equal(w, WindowPlacement.Inside(w, Notebook));
    }

    [Fact]
    public void Janela_que_passa_da_borda_volta_para_dentro_sem_encolher()
    {
        Assert.Equal(new Box(192, 66, 900, 500), WindowPlacement.Inside(new Box(400, 200, 900, 500), Notebook));
    }

    [Fact]
    public void Volta_no_mesmo_lugar_e_no_segundo_monitor()
    {
        var saved = new Box(2000, 100, 1000, 700);
        Assert.Equal(saved, WindowPlacement.Fit(saved, [Main, Right], 720, 560));
    }

    [Fact]
    public void Monitor_desconectado_abre_centralizado()
    {
        Assert.Null(WindowPlacement.Fit(new Box(2000, 100, 1000, 700), [Main], 720, 560));
    }

    [Fact]
    public void Barra_de_titulo_acima_da_tela_abre_centralizado()
    {
        Assert.Null(WindowPlacement.Fit(new Box(100, -600, 1000, 700), [Main], 720, 560));
    }

    [Fact]
    public void Janela_maior_que_a_tela_encolhe_e_entra_inteira()
    {
        var fit = WindowPlacement.Fit(new Box(1900, 50, 1600, 1200), [Main, Right], 720, 560)!.Value;
        Assert.Equal(new Box(1920, 0, 1280, 984), fit);
    }

    [Fact]
    public void Meio_para_fora_da_borda_e_puxada_para_dentro()
    {
        var fit = WindowPlacement.Fit(new Box(1500, 200, 1000, 700), [Main], 720, 560)!.Value;
        Assert.Equal(new Box(920, 200, 1000, 700), fit);
    }

    [Fact]
    public void Posicao_da_janela_nao_vai_na_exportacao()
    {
        string json = SettingsTransfer.Export("""{"WinLeft":10,"WinTop":20,"WinWidth":800,"WinHeight":600,"WinMaximized":true,"TempDays":7}""", "1.2.0");
        Assert.DoesNotContain("Win", json);
        Assert.Contains("TempDays", json);
    }
}

public class TaskbarBadgeTests
{
    private static Finding F(string level) => new(level, "t", "d", "a");

    [Fact]
    public void Problema_vence_atencao()
    {
        var b = TaskbarBadge.For(48, [F("ATENCAO"), F("PROBLEMA"), F("PROBLEMA")]);
        Assert.Equal(BadgeLevel.Problem, b.Level);
        Assert.Equal("Notebook Doctor · nota 48 · 2 problemas", b.Description);
    }

    [Fact]
    public void Atencao_no_singular()
    {
        var b = TaskbarBadge.For(70, [F("ATENCAO"), F("OK")]);
        Assert.Equal(BadgeLevel.Attention, b.Level);
        Assert.EndsWith("1 ponto de atenção", b.Description);
    }

    [Fact]
    public void Tudo_certo_fica_sem_selo_e_sem_nota_enquanto_calcula()
    {
        var b = TaskbarBadge.For(0, [F("OK")]);
        Assert.Equal(BadgeLevel.None, b.Level);
        Assert.Equal("Notebook Doctor · tudo certo", b.Description);
    }
}

public class DemoDataTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 16, 0, 0); // quarta-feira

    [Fact]
    public void Mesmo_tick_gera_o_mesmo_dado()
    {
        var a = DemoData.Sample(Now, 42);
        var b = DemoData.Sample(Now, 42);
        Assert.Equal(a.Cpu, b.Cpu);
        Assert.Equal(a.Groups.Select(g => g.MemMB), b.Groups.Select(g => g.MemMB));
    }

    [Fact]
    public void Leituras_ficam_em_faixas_plausiveis()
    {
        for (int i = 0; i < 500; i++)
        {
            var s = DemoData.Sample(Now, i);
            Assert.InRange(s.Cpu, 0, 100);
            Assert.InRange(s.RamPct, 0, 100);
            Assert.InRange(s.TempC, 40, 80);
            Assert.True(s.RamUsedGB <= s.RamTotalGB);
        }
    }

    [Fact]
    public void Chrome_fica_pesado_em_parte_do_tempo()
    {
        var chrome = Enumerable.Range(0, 160).Select(i => DemoData.Sample(Now, i).Groups.First(g => g.Name == "chrome").CpuPct).ToList();
        Assert.Contains(chrome, c => c >= 25);
        Assert.Contains(chrome, c => c < 25);
    }

    [Fact]
    public void Semana_so_tem_horario_de_uso_e_nao_passa_de_agora()
    {
        var week = DemoData.Week(Now).ToList();
        Assert.NotEmpty(week);
        Assert.All(week, w =>
        {
            bool weekend = w.Snap.Time.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            Assert.InRange(w.Snap.Time.Hour, weekend ? 10 : 8, weekend ? 13 : 18);
            Assert.True(w.Snap.Time < Now);
            Assert.InRange(w.Score, 40, 98);
        });
    }

    [Fact]
    public void Semana_tem_limpeza_no_meio_que_libera_espaco()
    {
        var free = DemoData.Week(Now).Select(w => w.Snap.DiskFreePct).ToList();
        Assert.Contains(free.Zip(free.Skip(1)), p => p.Second - p.First > 2);
    }

    [Fact]
    public void Diagnostico_da_demonstracao_mostra_o_disco_do_WSL()
    {
        var recent = Enumerable.Range(0, 36).Select(i => DemoData.Sample(Now.AddSeconds(i * 3), i)).ToList();
        var findings = Diagnosis.Evaluate(recent, DemoData.Health());
        Assert.Contains(findings, f => f.Title.StartsWith("Disco virtual do"));
    }
}

public class OverlayTests
{
    [Theory]
    [InlineData("off", 2, false)]
    [InlineData("fullscreen", 2, true)]
    [InlineData("fullscreen", 3, true)]
    [InlineData("fullscreen", 4, false)] // apresentação: sem números no canto do slide
    [InlineData("fullscreen", 5, false)]
    [InlineData("always", 0, true)]
    public void Quando_mostrar(string mode, int state, bool expected) =>
        Assert.Equal(expected, Overlay.ShouldShow(mode, state));

    [Fact]
    public void Texto_curto_com_temperatura_e_disco_so_quando_importa()
    {
        var s = Make.Snap(cpu: 45.4, ram: 72);
        Assert.Equal("CPU 45% · RAM 72%", Overlay.Text(s));
        Assert.Equal("CPU 45% · RAM 72% · 64 °C · disco 80%", Overlay.Text(s with { TempC = 64, DiskBusy = 80 }));
    }

    [Theory]
    [InlineData("top-left", 1932, 12)]
    [InlineData("top-right", 3508, 12)]
    [InlineData("bottom-left", 1932, 1388)]
    [InlineData("bottom-right", 3508, 1388)]
    public void Encosta_no_canto_do_monitor_certo(string corner, double x, double y)
    {
        var monitor = new Box(1920, 0, 1800, 1440); // segundo monitor
        Assert.Equal((x, y), Overlay.Place(corner, monitor, 200, 40, 12));
    }
}

public class UnitsTests : IDisposable
{
    public void Dispose() { Units.Size = SizeUnit.Windows; Units.Temperature = TempUnit.Celsius; }

    [Fact]
    public void Padrao_e_como_o_Windows()
    {
        Assert.Equal("1,5 GB", Units.Bytes(1610612736));
        Assert.Equal("15,9 GB", Units.Gb(15.9));
        Assert.Equal("64 °C", Units.Temp(64.4));
    }

    [Fact]
    public void Decimal_bate_com_a_caixa_do_disco()
    {
        Units.Size = SizeUnit.Decimal;
        Assert.Equal("1,5 GB", Units.Bytes(1_500_000_000));
        Assert.Equal("476 GB", Units.Gb(443.3, "0")); // SSD de "512 GB" que o Windows mostra como 476 GB
        Assert.Equal("6,7 de 17,1 GB", Units.GbOf(6.2, 15.9));
    }

    [Fact]
    public void Binario_usa_GiB_MiB_KiB()
    {
        Units.Size = SizeUnit.Binary;
        Assert.Equal("1,5 GiB", Units.Bytes(1610612736));
        Assert.Equal("120 MiB", Pt.Bytes(120L << 20));
        Assert.Equal("6,2 de 15,9 GiB", Units.GbOf(6.2, 15.9));
    }

    [Fact]
    public void Fahrenheit()
    {
        Units.Temperature = TempUnit.Fahrenheit;
        Assert.Equal("147 °F", Units.Temp(64));
        Assert.Equal("Temperatura muito alta", Diagnosis.Evaluate([Make.Snap(tempC: 92)], HealthInfo.Empty).First(f => f.Title.StartsWith("Temperatura")).Title);
        Assert.Equal("198 °F", Diagnosis.Evaluate([Make.Snap(tempC: 92)], HealthInfo.Empty).First(f => f.Title.StartsWith("Temperatura")).Detail);
    }

    [Theory]
    [InlineData("decimal", SizeUnit.Decimal)]
    [InlineData("binary", SizeUnit.Binary)]
    [InlineData("qualquer", SizeUnit.Windows)]
    [InlineData(null, SizeUnit.Windows)]
    public void Le_a_preferencia(string? v, SizeUnit expected) => Assert.Equal(expected, Units.ParseSize(v));

    [Fact]
    public void Larguras_absurdas_sao_ignoradas()
    {
        Assert.False(GridLayout.IsSaneWidth(0));
        Assert.False(GridLayout.IsSaneWidth(5000));
        Assert.True(GridLayout.IsSaneWidth(120));
    }

    [Fact]
    public void Layout_das_tabelas_nao_vai_na_exportacao()
    {
        string json = SettingsTransfer.Export("""{"Grids":{"ProcGrid":{"Sort":"MemMB","Desc":true}},"SizeUnit":"binary"}""", "1.2.0");
        Assert.DoesNotContain("Grids", json);
        Assert.Contains("SizeUnit", json);
    }
}

public class TrayAndThemeTests
{
    [Theory]
    [InlineData(10, 40, TrayBand.Ok)]
    [InlineData(70, 40, TrayBand.Amber)]
    [InlineData(20, 90, TrayBand.Red)]
    public void Cor_pelo_recurso_mais_carregado(double cpu, double ram, TrayBand expected) => Assert.Equal(expected, Tray.Band(cpu, ram));

    [Theory]
    [InlineData(7.4, "7")]
    [InlineData(45.6, "46")]
    [InlineData(100, "99")]
    [InlineData(-1, "0")]
    public void Numero_cabe_no_icone(double cpu, string expected) => Assert.Equal(expected, Tray.CpuLabel(cpu));

    [Fact]
    public void Menu_mostra_uso_e_nota()
    {
        var (usage, health) = Tray.MenuLines(Make.Snap(cpu: 45, ram: 72, freePct: 18, tempC: 61), 82);
        Assert.Equal("CPU 45% · RAM 72% · 61 °C", usage);
        Assert.Equal("Disco livre 18% · nota 82", health);
        Assert.Equal("Disco livre 18%", Tray.MenuLines(Make.Snap(freePct: 18), 0).Health);
    }

    [Theory]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("18:59", false)]
    [InlineData("19:00", true)]
    [InlineData("23:30", true)]
    public void Escuro_a_noite(string now, bool dark) =>
        Assert.Equal(dark, ThemeSchedule.IsDark(TimeOnly.Parse(now), "07:00", "19:00"));

    [Fact]
    public void Modo_por_horario_vira_claro_ou_escuro_e_os_outros_passam_direto()
    {
        Assert.Equal("dark", ThemeSchedule.Resolve("schedule", new TimeOnly(22, 0), "07:00", "19:00"));
        Assert.Equal("light", ThemeSchedule.Resolve("schedule", new TimeOnly(12, 0), "07:00", "19:00"));
        Assert.Equal("system", ThemeSchedule.Resolve("system", new TimeOnly(22, 0), "07:00", "19:00"));
        Assert.Equal("light", ThemeSchedule.Resolve("schedule", new TimeOnly(22, 0), "xx", "19:00")); // horário inválido: claro
    }

    [Fact]
    public void Aviso_da_bandeja_nao_vai_na_exportacao() =>
        Assert.DoesNotContain("TrayHintShown", SettingsTransfer.Export("""{"TrayHintShown":true,"TrayCpu":true}""", "1.2.0"));
}

public class TableTotalsTests
{
    [Fact]
    public void Processos_soma_o_que_esta_a_vista()
    {
        var rows = new List<(int, double, long)> { (18, 9.5, 1900), (6, 4.2, 950) };
        Assert.Equal("2 programas · 24 processos · CPU 14% · memória 2,8 GB", TableTotals.Processes(rows, 2));
        Assert.StartsWith("2 de 85 programas", TableTotals.Processes(rows, 85));
    }

    [Fact]
    public void Espaco_e_inicializacao()
    {
        Assert.Equal("1 item · 512 MB", TableTotals.Space([512L << 20], 1));
        Assert.Equal("3 de 22 programas (19 ocultos pelo filtro) · 1 ativado", TableTotals.Startup(1, 3, 22));
        Assert.Equal("22 programas · 14 ativados", TableTotals.Startup(14, 22, 22));
    }
}

public class SeverityAndCleanTests
{
    [Theory]
    [InlineData(55, 100, 16, "high")]   // CPU alta
    [InlineData(5, 4200, 16, "high")]   // um quarto da RAM
    [InlineData(25, 100, 16, "mid")]
    [InlineData(2, 1700, 16, "mid")]    // 10% da RAM
    [InlineData(2, 300, 16, "")]
    [InlineData(2, 300, 0, "")]         // RAM total desconhecida
    public void Processo(double cpu, long mem, double ram, string expected) => Assert.Equal(expected, RowSeverity.Process(cpu, mem, ram));

    [Theory]
    [InlineData("Alto (30 s de CPU, 400 MB)", "high")]
    [InlineData("Médio (6 s de CPU, 120 MB)", "mid")]
    [InlineData("Não está em execução", "")]
    public void Inicializacao(string impact, string expected) => Assert.Equal(expected, RowSeverity.Startup(impact));

    [Fact]
    public void Narrador_le_o_peso() => Assert.Equal("uso alto", RowSeverity.Spoken("high"));

    [Fact]
    public void Comemora_a_partir_de_1_GB()
    {
        var big = CleanOutcome.Describe(3L << 30);
        Assert.True(big.Celebrate);
        Assert.Equal("Ótimo: 3,0 GB liberados!", big.Title);
        Assert.Contains("1.024 fotos", big.Text);

        var small = CleanOutcome.Describe(200L << 20);
        Assert.False(small.Celebrate);
        Assert.Equal("200 MB liberados", small.Title);

        Assert.Equal("Nada para liberar", CleanOutcome.Describe(0).Title);
    }
}

public class DiagnosticLogTests
{
    [Fact]
    public void Tem_todas_as_secoes_e_diz_quando_esta_vazio()
    {
        string log = DiagnosticLog.Build("1.2.0", "Windows 11", new DateTime(2026, 10, 21, 9, 0, 0), [], "", [], [], []);
        Assert.StartsWith("Notebook Doctor 1.2.0 · log de diagnóstico · 21/10/2026 09:00\r\n", log);
        foreach (var s in new[] { "Diagnóstico agora", "Relatórios de falha", "Últimas ações", "Últimos avisos", "Erros do app" })
            Assert.Contains($"== {s} ==", log);
        Assert.Contains("Nenhum problema encontrado.", log);
    }

    [Fact]
    public void Erros_longos_ficam_so_com_o_final()
    {
        string errors = new string('a', DiagnosticLog.MaxErrorChars) + "ULTIMO ERRO";
        string log = DiagnosticLog.Build("1.2.0", "W", DateTime.Now, [new Finding("PROBLEMA", "Disco quase cheio", "5% livre", "")], errors, ["falha-1.txt"], ["acao"], ["aviso"]);
        Assert.Contains("ULTIMO ERRO", log);
        Assert.DoesNotContain(new string('a', DiagnosticLog.MaxErrorChars), log);
        Assert.Contains("[PROBLEMA] Disco quase cheio — 5% livre", log);
        Assert.Contains("falha-1.txt", log);
    }
}

public class CleanPreviewTests
{
    private static readonly string[] Roots = [@"C:\Users\ana\AppData\Local\Temp"];

    [Fact]
    public void Caminho_relativo_a_pasta_do_local()
    {
        Assert.Equal(@"Temp\sub\a.tmp", CleanPreview.Relative(@"C:\Users\ana\AppData\Local\Temp\sub\a.tmp", Roots));
        Assert.Equal(@"Temp\a.tmp", CleanPreview.Relative(@"c:\users\ana\appdata\local\temp\a.tmp", [@"C:\Users\ana\AppData\Local\Temp\"]));
        Assert.Equal(@"D:\outro\b.tmp", CleanPreview.Relative(@"D:\outro\b.tmp", Roots));
        Assert.Equal(@"C:\Users\ana\AppData\Local\Temp2\c.tmp", CleanPreview.Relative(@"C:\Users\ana\AppData\Local\Temp2\c.tmp", Roots)); // não confunde Temp com Temp2
    }

    [Fact]
    public void Lista_os_maiores_primeiro_com_total()
    {
        var d = new DateTime(2026, 10, 1);
        var files = new List<PreviewFile>
        {
            new(@"C:\Users\ana\AppData\Local\Temp\pequeno.tmp", 2048, d),
            new(@"C:\Users\ana\AppData\Local\Temp\grande.tmp", 50L << 20, d),
        };
        string text = CleanPreview.Format("Temporários", Roots, files);
        Assert.StartsWith("Temporários: 2 arquivos, 50 MB.", text);
        Assert.True(text.IndexOf("grande.tmp") < text.IndexOf("pequeno.tmp"));
        Assert.Contains("01/10/2026", text);
    }

    [Fact]
    public void Lista_longa_diz_quantos_ficaram_de_fora()
    {
        var files = Enumerable.Range(0, CleanPreview.MaxListed + 3).Select(i => new PreviewFile($@"C:\t\{i}.tmp", 1000 + i, DateTime.Now)).ToList();
        Assert.EndsWith("… e mais 3 arquivos menores.", CleanPreview.Format("X", [@"C:\t"], files));
        Assert.EndsWith("Nada a apagar aqui agora.", CleanPreview.Format("X", [@"C:\t"], []));
    }
}
