using System.ComponentModel;
using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class ProgramUpdatesTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.10", "1.9", 1)]                 // número, não texto
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("v2.0", "2.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]        // pré-lançamento vem antes
    [InlineData("1.0.0", "1.0.0-rc1", 1)]
    [InlineData("1.0.0-beta2", "1.0.0-beta10", -1)]
    [InlineData("2024.10.1", "2024.9.30", 1)]
    [InlineData("20240101123456", "20240101123457", -1)] // grande demais para int
    [InlineData("139.0", "140.0.2", -1)]
    [InlineData("1.2.3.4", "1.2.3", 1)]
    [InlineData("", "1.0", -1)]
    public void Comparacao_de_versoes(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(VersionOrder.Compare(a, b)));
        Assert.Equal(-expected, Math.Sign(VersionOrder.Compare(b, a))); // simétrica
    }

    [Fact]
    public void Comparacao_e_transitiva_numa_lista_embaralhada()
    {
        string[] sorted = ["0.9", "1.0.0-alpha", "1.0.0-beta", "1.0.0-rc1", "1.0.0", "1.0.1", "1.1", "1.10", "2.0"];
        var rnd = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            var shuffled = sorted.OrderBy(_ => rnd.Next()).ToList();
            shuffled.Sort(VersionOrder.Compare);
            Assert.Equal(sorted, shuffled);
        }
    }

    private static UpdateCandidate C(string name, string id, string installed, string available) => new(name, id, installed, available, "winget");

    [Fact]
    public void Pular_vale_so_para_a_versao_dispensada_e_as_anteriores()
    {
        var skipped = new List<string> { ProgramUpdateRules.SkipKey("Mozilla.Firefox", "140.0.2") };
        Assert.Equal(UpdateState.Skipped, ProgramUpdateRules.StateOf(C("Firefox", "Mozilla.Firefox", "139", "140.0.2"), skipped, []));
        Assert.Equal(UpdateState.Available, ProgramUpdateRules.StateOf(C("Firefox", "Mozilla.Firefox", "139", "141.0"), skipped, [])); // versão mais nova volta
        Assert.Equal(UpdateState.Skipped, ProgramUpdateRules.StateOf(C("Firefox", "Mozilla.Firefox", "139", "140.0.1"), skipped, []));
        Assert.Equal(UpdateState.Available, ProgramUpdateRules.StateOf(C("Outro", "Outro.App", "1", "2"), skipped, [])); // outro programa não é afetado
        Assert.Equal(UpdateState.Skipped, ProgramUpdateRules.StateOf(C("Firefox", "mozilla.firefox", "139", "140.0.2"), skipped, [])); // id sem diferenciar maiúsculas
    }

    [Fact]
    public void Pausar_ganha_de_pular_e_vale_para_qualquer_versao()
    {
        var skipped = new List<string> { ProgramUpdateRules.SkipKey("A.B", "2.0") };
        var paused = new List<string> { "a.b" };
        Assert.Equal(UpdateState.Paused, ProgramUpdateRules.StateOf(C("AB", "A.B", "1", "2.0"), skipped, paused));
        Assert.Equal(UpdateState.Paused, ProgramUpdateRules.StateOf(C("AB", "A.B", "1", "9.0"), [], paused));
        Assert.Equal(UpdateState.Available, ProgramUpdateRules.StateOf(C("AB", "A.B", "1", "9.0"), [], ProgramUpdateRules.WithPause(paused, "A.B", pause: false)));
    }

    [Fact]
    public void Pular_de_novo_troca_a_entrada_antiga_do_mesmo_programa()
    {
        var list = ProgramUpdateRules.WithSkip([], "X.Y", "2.0");
        list = ProgramUpdateRules.WithSkip(list, "x.y", "3.0");
        list = ProgramUpdateRules.WithSkip(list, "Z.W", "1.0");
        Assert.Equal(2, list.Count);
        Assert.Equal("3.0", ProgramUpdateRules.SkippedVersion(list, "X.Y"));
        Assert.Equal(2, ProgramUpdateRules.WithPause(ProgramUpdateRules.WithPause([], "A.B", true), "C.D", true).Count);
        Assert.Single(ProgramUpdateRules.WithPause(ProgramUpdateRules.WithPause([], "A.B", true), "A.B", true)); // sem repetir
    }

    [Fact]
    public void Ordem_disponiveis_primeiro_e_por_nome()
    {
        var items = new[]
        {
            C("Zeta", "Z.Z", "1", "2"), C("alfa", "A.A", "1", "2"), C("Meio", "M.M", "1", "2"),
            C("Pausado", "P.P", "1", "2"), C("Pulado", "S.S", "1", "2"),
        };
        var ordered = ProgramUpdateRules.Order(items, [ProgramUpdateRules.SkipKey("S.S", "2")], ["P.P"]);
        Assert.Equal(["alfa", "Meio", "Zeta", "Pulado", "Pausado"], ordered.Select(t => t.Item.Name));
        Assert.Equal([UpdateState.Available, UpdateState.Available, UpdateState.Available, UpdateState.Skipped, UpdateState.Paused], ordered.Select(t => t.State));
    }

    [Theory]
    [InlineData("Mozilla.Firefox", true)]
    [InlineData("7zip.7zip", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("Microsoft.VisualStudio.2022.Community", true)]
    [InlineData(@"ARP\Machine\X64\Algo", false)]    // programa fora do catálogo
    [InlineData(@"MSIX\Foo_1.0_x64__abc", false)]
    [InlineData("a; calc", false)]
    [InlineData("--source", false)]                 // não pode virar opção
    [InlineData("a b", false)]
    [InlineData("a&b", false)]
    [InlineData("a|b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Id_do_catalogo(string? id, bool ok) => Assert.Equal(ok, ProgramUpdateRules.IdOk(id));

    [Fact]
    public void Argumentos_do_upgrade_so_aceitam_os_termos_quando_a_pessoa_aceitou()
    {
        var sem = WingetCommands.Upgrade("Mozilla.Firefox", sourceTermsAccepted: false, packageTermsAccepted: false);
        Assert.DoesNotContain("--accept-package-agreements", sem);
        Assert.DoesNotContain("--accept-source-agreements", sem);
        var com = WingetCommands.Upgrade("Mozilla.Firefox", sourceTermsAccepted: true, packageTermsAccepted: true);
        Assert.Contains("--accept-package-agreements", com);
        Assert.Contains("--accept-source-agreements", com);
        foreach (var args in new[] { sem, com })
        {
            Assert.Equal("Mozilla.Firefox", args[Array.IndexOf(args, "--id") + 1]);
            Assert.Contains("--exact", args);
            Assert.Contains("--silent", args);
            Assert.Equal(WingetCommands.Source, args[Array.IndexOf(args, "--source") + 1]); // nunca a fonte da Store
        }
        // Nenhuma consulta aceita termos de pacote
        Assert.DoesNotContain("--accept-package-agreements", WingetCommands.ListUpgrades(true));
        Assert.DoesNotContain("--accept-package-agreements", WingetCommands.Show("A.B", true));
        Assert.DoesNotContain("--accept-source-agreements", WingetCommands.ListUpgrades(false));
    }

    [Theory]
    [InlineData("a; calc")]
    [InlineData(@"ARP\Machine\X64\Algo")]
    [InlineData("--id")]
    [InlineData("")]
    public void Id_invalido_nunca_chega_a_linha_de_comando(string id)
    {
        Assert.Throws<ArgumentException>(() => WingetCommands.Upgrade(id, true, true));
        Assert.Throws<ArgumentException>(() => WingetCommands.Show(id, true));
    }

    private const string TabelaEn = """
          -
          \
          |
        Name                       Id                         Version     Available   Source
        -----------------------------------------------------------------------------------
        Mozilla Firefox (x64 en-US)  Mozilla.Firefox           139.0       140.0.2     winget
        7-Zip                      7zip.7zip                  24.07       24.09       winget
        Programa Desconhecido      ARP\Machine\X64\Desconhecido 1.0       2.0         winget
        Sem versão nova            Foo.Bar                    2.0         2.0         winget
        Cortado                    Muito.Longo.Identific…     1.0         2.0         winget
        3 upgrades available.

        The following packages have an upgrade available, but require explicit targeting for upgrade:
        Name   Id        Version   Available  Source
        ---------------------------------------------
        Fora   Fora.App  1.0       9.0        winget
        """;

    [Fact]
    public void Le_a_tabela_do_winget()
    {
        var list = WingetCommands.ParseUpgrades(TabelaEn);
        Assert.Equal(["Mozilla.Firefox", "7zip.7zip"], list.Items.Select(i => i.Id));
        Assert.Equal("Mozilla Firefox (x64 en-US)", list.Items[0].Name);
        Assert.Equal("139.0", list.Items[0].Installed);
        Assert.Equal("140.0.2", list.Items[0].Available);
        Assert.Equal(1, list.Truncated);
    }

    [Fact]
    public void Le_a_tabela_em_portugues_com_progresso_e_versao_aproximada()
    {
        // Cabeçalhos e rodapé em outro idioma; "< 1.5" quando a versão instalada é aproximada; o indicador de progresso reescreve a linha com \r
        string output = "  - \r  \\ \r  | \r\n"
            + "Nome              ID               Versão   Disponível  Fonte\r\n"
            + "-----------------------------------------------------------\r\n"
            + "VLC               VideoLAN.VLC     < 3.0.20 3.0.21      winget\r\n".Replace("< 3.0.20 3.0.21", "< 3.0.20  3.0.21")
            + "Notepad++         Notepad++.Notepad++ 8.6.9  8.7.1      winget\r\n".Replace("Notepad++.Notepad++ 8.6.9  8.7.1", "Notepad++.Notepad++  8.6.9  8.7.1")
            + "2 atualizações disponíveis.\r\n";
        var list = WingetCommands.ParseUpgrades(output);
        Assert.Equal(["VideoLAN.VLC", "Notepad++.Notepad++"], list.Items.Select(i => i.Id));
        Assert.Equal("3.0.20", list.Items[0].Installed); // sem o "<"
    }

    [Theory]
    [InlineData("")]
    [InlineData("No installed package found matching input criteria.")]
    [InlineData("lixo qualquer\n\n---\nsem tabela")]
    public void Sem_tabela_nao_ha_atualizacoes(string output)
    {
        var list = WingetCommands.ParseUpgrades(output);
        Assert.Empty(list.Items);
        Assert.Equal(0, list.Truncated);
    }

    [Fact]
    public void Codigos_de_saida()
    {
        Assert.Equal(ExitKind.Done, WingetCommands.Classify(0));
        Assert.Equal(ExitKind.NotApplicable, WingetCommands.Classify(unchecked((int)0x8A15002B)));
        Assert.Equal(ExitKind.Cancelled, WingetCommands.Classify(1223));
        Assert.Equal(ExitKind.Other, WingetCommands.Classify(1));
        Assert.True(WingetCommands.Succeeded(0));
        Assert.False(WingetCommands.Succeeded(1));
        Assert.False(WingetCommands.Succeeded(1223));
    }

    [Fact]
    public void Limpa_o_progresso_do_texto_dos_termos()
    {
        string text = WingetShowText.Clean("  - \r  \\ \r\n  |\r\nFound Foo [Foo.Foo]\r\nLicense: MIT\r\n");
        Assert.Equal("Found Foo [Foo.Foo]\nLicense: MIT", text);
    }

    [Fact]
    public void Configuracoes_de_atualizacao_ficam_na_maquina_so_o_aceite_dos_termos()
    {
        const string json = """{"WingetSourceTerms": true, "UpdatePaused": ["A.B"], "UpdateSkipped": ["A.B|2.0"]}""";
        string exported = SettingsTransfer.Export(json, "1");
        Assert.DoesNotContain("WingetSourceTerms", exported); // o aceite é de quem aceitou, neste PC
        Assert.Contains("UpdatePaused", exported);            // pausar e pular viajam com as preferências
        Assert.Contains("UpdateSkipped", exported);
    }
}

[SupportedOSPlatform("windows")]
public class WingetIntegrationTests
{
    [WindowsFact]
    public async Task Winget_instalado_responde_e_ausente_nao_quebra()
    {
        try
        {
            var run = await WingetRunner.RunAsync(["--version"], TimeSpan.FromSeconds(30));
            Assert.False(run.TimedOut);
            Assert.Equal(0, run.ExitCode);
            Assert.Matches(@"\d+\.\d+", run.Output);
        }
        catch (Win32Exception) { /* sem o Instalador de Aplicativo: o app mostra como instalar */ }
        Assert.IsType<bool>(await WingetRunner.AvailableAsync());
    }
}
