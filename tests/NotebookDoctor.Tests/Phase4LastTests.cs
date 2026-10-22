using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class DensityTests
{
    [Fact]
    public void Modo_compacto_sempre_mostra_so_icones()
    {
        Assert.True(Density.NavIconsOnly(1600, 1, compact: true));
        Assert.False(Density.NavIconsOnly(1600, 1, compact: false));
    }

    [Fact]
    public void Janela_estreita_mostra_so_icones_e_o_limite_cresce_com_o_texto()
    {
        Assert.True(Density.NavIconsOnly(900, 1, compact: false));
        Assert.False(Density.NavIconsOnly(1000, 1, compact: false));
        // Texto a 150%: o limite passa a 960 × 1,2 = 1152
        Assert.True(Density.NavIconsOnly(1100, 1.5, compact: false));
    }

    [Fact]
    public void Compacto_e_mais_denso_mas_linhas_continuam_clicaveis()
    {
        Assert.True(Density.RowHeight(true) < Density.RowHeight(false));
        Assert.True(Density.RowHeight(true) >= 32);
        Assert.True(Density.CardPadding(true) < Density.CardPadding(false));
        Assert.True(Density.CardMargin(true) < Density.CardMargin(false));
    }
}

public class ColumnChoiceTests
{
    private static readonly string[] Proc = ["Programa", "Processos", "CPU %", "Memória (MB)", "Conexões"];

    [Fact]
    public void Esconde_e_mostra_de_novo()
    {
        var hidden = ColumnChoice.Toggle(Proc, [], "Conexões");
        Assert.Equal(["Conexões"], hidden);
        Assert.Empty(ColumnChoice.Toggle(Proc, hidden, "Conexões"));
    }

    [Fact]
    public void Coluna_fixa_nunca_some()
    {
        Assert.False(ColumnChoice.CanHide(Proc, [], "Programa"));
        Assert.Empty(ColumnChoice.Toggle(Proc, [], "Programa"));
        // Limpeza: a caixa de marcar e o local são fixos
        string[] clean = ["Limpar", "Local", "Arquivos", "Tamanho"];
        Assert.False(ColumnChoice.CanHide(clean, [], "Local", fixedCount: 2));
        Assert.True(ColumnChoice.CanHide(clean, [], "Arquivos", fixedCount: 2));
    }

    [Fact]
    public void Sempre_sobra_uma_coluna_visivel()
    {
        string[] two = ["Nome", "Estado"];
        Assert.False(ColumnChoice.CanHide(two, ["Estado"], "Nome", fixedCount: 0));
        var hidden = ColumnChoice.Toggle(two, [], "Estado", fixedCount: 0);
        Assert.Equal(["Estado"], ColumnChoice.Toggle(two, hidden, "Nome", fixedCount: 0));
    }

    [Fact]
    public void Preferencias_antigas_sao_limpas()
    {
        // Coluna que não existe mais, coluna fixa e repetição saem
        var s = ColumnChoice.Sanitize(Proc, ["Disco", "Programa", "CPU %", "CPU %"]);
        Assert.Equal(["CPU %"], s);
        // Tudo escondido (arquivo editado à mão): mostra tudo
        Assert.Empty(ColumnChoice.Sanitize(["A", "B"], ["A", "B"], fixedCount: 0));
        Assert.Empty(ColumnChoice.Sanitize(Proc, null));
    }

    [Fact]
    public void Coluna_desconhecida_nao_muda_nada() =>
        Assert.Equal(["CPU %"], ColumnChoice.Toggle(Proc, ["CPU %"], "Não existe"));
}

public class FriendlyTests
{
    [Fact]
    public void Mesmo_texto_o_dia_todo_e_varia_entre_dias()
    {
        var morning = new DateTime(2026, 10, 22, 8, 0, 0);
        Assert.Equal(Friendly.Pick(Friendly.AllGood, morning), Friendly.Pick(Friendly.AllGood, morning.AddHours(14)));
        var week = Enumerable.Range(0, 7).Select(d => Friendly.Pick(Friendly.AllGood, morning.AddDays(d))).Distinct().Count();
        Assert.Equal(Friendly.AllGood.Length, week);
    }

    [Fact]
    public void Lista_vazia_nao_quebra() => Assert.Equal("", Friendly.Pick([], DateTime.Today));

    [Fact]
    public void Diagnostico_ok_usa_uma_das_frases()
    {
        var f = Assert.Single(Diagnosis.Evaluate(Make.Minute(Make.Snap()), Make.Health(), null, Make.FreshBoot));
        Assert.Equal("Tudo certo", f.Title);
        Assert.Contains(f.Detail, Friendly.AllGood);
    }

    [Fact]
    public void Limpeza_mantem_o_titulo_e_varia_o_apoio()
    {
        var day = new DateTime(2026, 1, 2);
        var small = CleanOutcome.Describe(200L << 20, day);
        Assert.Equal("200 MB liberados", small.Title);
        Assert.Contains(small.Text, Friendly.Freed);
        Assert.Contains(CleanOutcome.Describe(0, day).Text, Friendly.NothingFreed);
        Assert.Contains("fotos de celular. " + Friendly.Pick(Friendly.Freed, day), CleanOutcome.Describe(3L << 30, day).Text);
    }
}

public class StartMenuLinkTests
{
    private const string Exe = @"C:\Apps\NotebookDoctor\NotebookDoctor.exe";

    [Fact]
    public void Caminho_na_pasta_Programas() =>
        Assert.Equal(@"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Notebook Doctor.lnk",
                     StartMenuLink.PathIn(@"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\"));

    [Fact]
    public void Estados_do_atalho()
    {
        Assert.Equal(StartMenuState.Missing, StartMenuLink.State(false, null, Exe));
        Assert.Equal(StartMenuState.Ok, StartMenuLink.State(true, @"c:\apps\notebookdoctor\NotebookDoctor.exe", Exe));
        Assert.Equal(StartMenuState.Ok, StartMenuLink.State(true, "\"" + Exe + "\"", Exe));
        Assert.Equal(StartMenuState.Stale, StartMenuLink.State(true, @"D:\Pendrive\NotebookDoctor.exe", Exe));
        Assert.Equal(StartMenuState.Stale, StartMenuLink.State(true, "", Exe));
    }

    [Fact]
    public void Botao_diz_o_que_vai_acontecer()
    {
        Assert.Equal("Adicionar ao Menu Iniciar", StartMenuLink.ButtonText(StartMenuState.Missing));
        Assert.Equal("Remover do Menu Iniciar", StartMenuLink.ButtonText(StartMenuState.Ok));
        Assert.Equal("Atualizar atalho do Menu Iniciar", StartMenuLink.ButtonText(StartMenuState.Stale));
    }
}
