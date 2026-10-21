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
