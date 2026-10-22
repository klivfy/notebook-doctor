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

public class SettingsLockTests
{
    private const int Fast = 1_000; // o padrão (600 mil) só no teste que confere o padrão

    [Fact]
    public void Senha_certa_confere_e_errada_nao()
    {
        string stored = PasswordHash.Create("segredo1", Fast);
        Assert.True(PasswordHash.Verify("segredo1", stored));
        Assert.False(PasswordHash.Verify("segredo2", stored));
        Assert.False(PasswordHash.Verify("Segredo1", stored));
    }

    [Fact]
    public void Padrao_usa_600_mil_iteracoes_e_sal_aleatorio()
    {
        string a = PasswordHash.Create("segredo1"), b = PasswordHash.Create("segredo1");
        Assert.StartsWith("pbkdf2-sha256$600000$", a);
        Assert.NotEqual(a, b); // sais diferentes
        Assert.True(PasswordHash.Verify("segredo1", b));
        Assert.DoesNotContain("segredo1", a);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("segredo1")]
    [InlineData("md5$1000$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2-sha256$abc$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2-sha256$999999999$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")] // travaria o app
    [InlineData("pbkdf2-sha256$1000$não-é-base64$AAAAAAAAAAAAAAAAAAAAAA==")]
    public void Codigo_invalido_nao_vale_como_senha(string? stored)
    {
        Assert.False(PasswordHash.IsValidFormat(stored));
        Assert.False(PasswordHash.Verify("qualquer", stored));
    }

    [Fact]
    public void Codigo_com_espacos_em_volta_ainda_vale()
    {
        string stored = PasswordHash.Create("segredo1", Fast);
        Assert.True(PasswordHash.Verify("segredo1", "  " + stored + "\r\n"));
    }

    [Theory]
    [InlineData("12345", "12345", "Use pelo menos 6 caracteres.")]
    [InlineData("      ", "      ", "A senha não pode ser só espaços.")]
    [InlineData("segredo1", "segredo2", "As duas senhas não são iguais.")]
    [InlineData("segredo1", "segredo1", null)]
    public void Senha_nova_e_conferida(string pw, string confirm, string? expected) =>
        Assert.Equal(expected, PasswordHash.CheckNew(pw, confirm));

    [Fact]
    public void Libera_por_10_minutos()
    {
        var s = new LockSession();
        var t = new DateTime(2026, 10, 22, 9, 0, 0);
        string stored = PasswordHash.Create("segredo1", Fast);
        Assert.False(s.IsUnlocked(t));
        Assert.Equal(UnlockResult.Ok, s.TryUnlock("segredo1", stored, t));
        Assert.True(s.IsUnlocked(t.AddMinutes(9)));
        Assert.False(s.IsUnlocked(t.AddMinutes(10)));
        s.Grant(t);
        s.Lock();
        Assert.False(s.IsUnlocked(t));
    }

    [Fact]
    public void Cinco_erros_seguidos_esperam_30_segundos()
    {
        var s = new LockSession();
        var t = new DateTime(2026, 10, 22, 9, 0, 0);
        string stored = PasswordHash.Create("segredo1", Fast);
        for (int i = 0; i < LockSession.MaxTries - 1; i++) Assert.Equal(UnlockResult.Wrong, s.TryUnlock("errada", stored, t));
        Assert.Equal(UnlockResult.Blocked, s.TryUnlock("errada", stored, t));
        // Nem a senha certa entra durante a espera
        Assert.Equal(UnlockResult.Blocked, s.TryUnlock("segredo1", stored, t.AddSeconds(29)));
        Assert.Equal(UnlockResult.Ok, s.TryUnlock("segredo1", stored, t.AddSeconds(30)));
    }

    [Fact]
    public void Acerto_zera_a_contagem_de_erros()
    {
        var s = new LockSession();
        var t = new DateTime(2026, 10, 22, 9, 0, 0);
        string stored = PasswordHash.Create("segredo1", Fast);
        for (int i = 0; i < LockSession.MaxTries - 1; i++) s.TryUnlock("errada", stored, t);
        Assert.Equal(UnlockResult.Ok, s.TryUnlock("segredo1", stored, t));
        s.Lock();
        Assert.Equal(UnlockResult.Wrong, s.TryUnlock("errada", stored, t));
    }

    [Fact]
    public void Texto_do_cartao()
    {
        Assert.StartsWith("Sem senha", LockSession.Describe(false, false, TimeSpan.Zero));
        Assert.Equal("Configurações protegidas por senha. Bloqueado.", LockSession.Describe(true, false, TimeSpan.Zero));
        Assert.Equal("Configurações protegidas por senha. Liberado por mais 10 minutos.", LockSession.Describe(true, false, TimeSpan.FromMinutes(9.5)));
        Assert.Contains("1 minuto.", LockSession.Describe(true, false, TimeSpan.FromSeconds(20)));
        Assert.StartsWith("Senha definida pela política", LockSession.Describe(true, true, TimeSpan.Zero));
    }

    [Fact]
    public void Senha_nao_sai_na_exportacao_e_sobrevive_a_importar_e_redefinir()
    {
        string current = """{"SettingsLock":"pbkdf2-sha256$1000$x$y","TrayCpu":false}""";
        Assert.DoesNotContain("SettingsLock", SettingsTransfer.Export(current, "1.2.0"));
        string imported = SettingsTransfer.Export("""{"SettingsLock":null,"TrayCpu":true}""", "1.2.0");
        Assert.Contains("pbkdf2-sha256$1000$x$y", SettingsTransfer.Merge(current, imported));
        Assert.Contains("pbkdf2-sha256$1000$x$y", SettingsTransfer.Reset(current, """{"SettingsLock":null,"TrayCpu":false}"""));
    }
}
