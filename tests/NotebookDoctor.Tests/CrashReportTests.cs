using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class CrashReportTests
{
    [Fact]
    public void Troca_pasta_pessoal_usuario_computador_e_dominio()
    {
        string text = @"em C:\Users\maria.silva\AppData\x.dll; caminho JSON C:\\Users\\maria.silva\\a; usuário MARIA.SILVA no NOTE-MARIA (CASA)";
        string r = Redaction.Apply(text, @"C:\Users\maria.silva", "maria.silva", "NOTE-MARIA", "CASA");
        Assert.Equal(@"em <pasta-pessoal>\AppData\x.dll; caminho JSON <pasta-pessoal>\\a; usuário <usuario> no <computador> (<dominio>)", r);
    }

    [Fact]
    public void Nao_troca_valores_curtos_demais()
    {
        // "ab" trocaria pedaços de palavras comuns; fica como está
        Assert.Equal("tabela ab", Redaction.Apply("tabela ab", "C:", "ab", "", ""));
    }

    [Fact]
    public void Relatorio_tem_cabecalho_e_quebras_do_Windows()
    {
        string r = CrashReport.Build("System.Exception: boom\n   at X.Y()", "tela", "1.2.0", "Windows 11", new DateTime(2026, 10, 21, 9, 5, 3));
        Assert.StartsWith("Notebook Doctor 1.2.0\r\nWindows: Windows 11\r\nQuando: 2026-10-21 09:05:03\r\nOnde: tela\r\n\r\n", r);
        Assert.Contains("boom\r\n   at X.Y()", r);
    }

    [Fact]
    public void Resumo_usa_a_primeira_linha_e_corta_as_longas()
    {
        Assert.Equal("System.IO.IOException: disco cheio", CrashReport.Headline("System.IO.IOException: disco cheio\r\n   at A.B()"));
        string longLine = new('x', 300);
        Assert.Equal(160, CrashReport.Headline(longLine).Length);
    }

    [Fact]
    public void Guarda_so_os_mais_recentes()
    {
        var files = Enumerable.Range(1, 13).Select(i => $@"C:\d\falhas\{CrashReport.FileName(new DateTime(2026, 10, i, 12, 0, 0))}").ToList();
        var excess = CrashReport.Excess(files).ToList();
        Assert.Equal(3, excess.Count);
        Assert.All(excess, f => Assert.Matches(@"falha-2026-10-0[123]_", f));
    }

    [Fact]
    public void Conta_falhas_novas_pela_data_do_nome()
    {
        var files = new[]
        {
            CrashReport.FileName(new DateTime(2026, 10, 20, 8, 0, 0)),
            CrashReport.FileName(new DateTime(2026, 10, 21, 8, 0, 0)),
            "falha-lixo.txt",
        };
        Assert.Equal(1, CrashReport.CountSince(files, new DateTime(2026, 10, 20, 23, 0, 0)));
        Assert.Equal(2, CrashReport.CountSince(files, DateTime.MinValue));
    }

    [Fact]
    public void Data_da_ultima_falha_vista_nao_e_exportada()
    {
        string exported = SettingsTransfer.Export("""{"CrashReports":true,"LastCrashSeen":"2026-10-21T08:00:00"}""", "1.2.0");
        Assert.Contains("CrashReports", exported);
        Assert.DoesNotContain("LastCrashSeen", exported);
    }
}
