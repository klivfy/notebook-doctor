using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class FileRecoveryRulesTests
{
    private static RecoveryRequest Req(string source = "E", string dest = @"D:\Recuperados", RecoveryMode mode = RecoveryMode.Extensive,
                                       RecoveryType[]? types = null, string[]? filters = null) =>
        new(source, dest, mode, types ?? [RecoveryType.Photos], filters ?? []);

    [Fact]
    public void Modo_profundo_filtra_por_nome_com_um_n_por_filtro()
    {
        var args = RecoveryRules.Args(Req(types: [RecoveryType.Photos], filters: [@"\Users\ana\Documents\"]), "exFAT");
        Assert.Equal("E:", args[0]);
        Assert.Equal(@"D:\Recuperados", args[1]);
        Assert.Equal("/extensive", args[2]);
        // pares "/n filtro", sem repetir
        var pairs = args.Skip(3).Chunk(2).ToList();
        Assert.All(pairs, p => Assert.Equal("/n", p[0]));
        Assert.Contains(pairs, p => p[1] == "*.jpg");
        Assert.Contains(pairs, p => p[1] == "*.heic");
        Assert.Contains(pairs, p => p[1] == @"\Users\ana\Documents\");
        Assert.Equal(pairs.Count, pairs.Select(p => p[1].ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void Modo_rapido_so_em_ntfs()
    {
        Assert.Equal("/regular", RecoveryRules.Args(Req(mode: RecoveryMode.Regular), "NTFS")[2]);
        foreach (var fs in new[] { "FAT32", "exFAT", "FAT", "ReFS" })
        {
            var errors = RecoveryRules.Validate(Req(mode: RecoveryMode.Regular), fs);
            Assert.Contains(errors, e => e.StartsWith("O modo rápido só funciona em NTFS"));
            Assert.Throws<InvalidOperationException>(() => RecoveryRules.Args(Req(mode: RecoveryMode.Regular), fs));
        }
    }

    [Fact]
    public void Modo_por_assinatura_usa_x_e_os_grupos_sem_repetir()
    {
        var args = RecoveryRules.Args(Req(mode: RecoveryMode.Signature, types: [RecoveryType.Documents, RecoveryType.Photos, RecoveryType.Archives]), "exFAT");
        Assert.Equal(["E:", @"D:\Recuperados", "/x", "/y:JPEG,PDF,PNG,ZIP"], args);                    // ZIP vem de documentos e de compactados, uma vez só
        Assert.Contains(RecoveryRules.Validate(Req(mode: RecoveryMode.Signature, types: []), "exFAT"), e => e.Contains("pelo menos um tipo"));
    }

    [Theory]
    [InlineData("E", @"E:\Recuperados")]       // mesma unidade da origem
    [InlineData("e", @"E:\")]
    [InlineData("E", @"e:\x\y")]
    public void Destino_na_mesma_unidade_e_recusado(string source, string dest) =>
        Assert.Contains(RecoveryRules.Validate(Req(source, dest), "NTFS"), e => e.Contains("OUTRA unidade"));

    [Theory]
    [InlineData("")]
    [InlineData("recuperados")]
    [InlineData(@"\\servidor\pasta")]
    [InlineData(@"D:\a\..\..\Windows")]
    [InlineData("D:\\a\"b")]
    [InlineData("D:\\a|b")]
    public void Destino_invalido_e_recusado(string dest) =>
        Assert.Contains(RecoveryRules.Validate(Req(dest: dest), "NTFS"), e => e.Contains("destino válida"));

    [Theory]
    [InlineData("1")]
    [InlineData("")]
    [InlineData("EE")]
    public void Origem_invalida(string source) => Assert.NotEmpty(RecoveryRules.Validate(Req(source), "NTFS"));

    [Theory]
    [InlineData("*.docx", true)]
    [InlineData("relatorio 2024*.pdf", true)]
    [InlineData(@"\Users\ana\Documents\", true)]
    [InlineData("foto (1).jpg", true)]
    [InlineData("a & b", true)]                 // sem shell, o & não é comando
    [InlineData("nome\"com aspas", false)]
    [InlineData("nome|pipe", false)]
    [InlineData("<redir>", false)]
    [InlineData(@"..\..\Windows", false)]
    [InlineData("linha\nnova", false)]
    [InlineData("", false)]
    public void Filtros(string filter, bool ok)
    {
        var errors = RecoveryRules.Validate(Req(types: [], filters: [filter]), "NTFS");
        if (ok) Assert.Empty(errors); else Assert.NotEmpty(errors);
    }

    [Fact]
    public void Sem_tipo_nem_filtro_nao_ha_o_que_procurar()
    {
        Assert.Contains(RecoveryRules.Validate(Req(types: [], filters: []), "NTFS"), e => e.Contains("pelo menos um tipo"));
        Assert.Empty(RecoveryRules.Validate(Req(types: [], filters: ["*.pdf"]), "NTFS"));
    }

    [Fact]
    public void Argumentos_nunca_levam_o_que_veio_de_fora_sem_validar()
    {
        var rng = new Random(5);
        for (int i = 0; i < 300; i++)
        {
            string junk = new(Enumerable.Range(0, rng.Next(1, 40)).Select(_ => (char)rng.Next(32, 300)).ToArray());
            var req = Req(types: [], filters: [junk]);
            if (RecoveryRules.Validate(req, "NTFS").Count == 0)
                foreach (var a in RecoveryRules.Args(req, "NTFS").Skip(3).Chunk(2).Select(c => c[1]))
                    Assert.DoesNotMatch("[\"|<>\\n\\r]", a);
        }
    }

    [Theory]
    [InlineData("NTFS", RecoveryMode.Regular)]
    [InlineData("ntfs", RecoveryMode.Regular)]
    [InlineData("exFAT", RecoveryMode.Extensive)]
    [InlineData("FAT32", RecoveryMode.Extensive)]
    [InlineData("ReFS", RecoveryMode.Extensive)]
    public void Modo_sugerido_pelo_sistema_de_arquivos(string fs, RecoveryMode expected) => Assert.Equal(expected, RecoveryRules.Suggest(fs));

    [Theory]
    [InlineData("Pass 1: Scanning disk  12%", 12)]
    [InlineData("Passagem 2: recuperando arquivos 100 %", 100)]
    [InlineData("0%", 0)]
    [InlineData("101%", null)]
    [InlineData("Scanning disk", null)]
    [InlineData("", null)]
    public void Progresso_em_qualquer_idioma(string line, int? expected) => Assert.Equal(expected, RecoveryRules.ParsePercent(line));

    [Fact]
    public void Acha_a_pasta_desta_rodada_e_ignora_as_antigas()
    {
        var started = new DateTime(2026, 10, 22, 14, 30, 0);
        var names = new[] { "Recovery_20261020_090000", "Recovery_20261022_143105", "Recovery_20261022_143001", "Outra", "Recovery_20261022_1431", "recovery_20261022_150000x" };
        Assert.Equal("Recovery_20261022_143105", RecoveryRules.FindOutput(names, started));         // a mais nova da rodada
        Assert.Null(RecoveryRules.FindOutput(["Recovery_20261020_090000"], started));                // só de uma rodada antiga
        Assert.Equal("Recovery_20261022_142900", RecoveryRules.FindOutput(["Recovery_20261022_142900"], started));   // 1 min antes: dentro da folga do relógio
        Assert.Null(RecoveryRules.FindOutput([], started));
    }

    [Fact]
    public void Avisos_conforme_a_unidade_e_o_modo()
    {
        Assert.Contains(RecoveryRules.Warnings(false, false, RecoveryMode.Regular), w => w.StartsWith("Pare de usar"));
        Assert.Contains(RecoveryRules.Warnings(true, false, RecoveryMode.Regular), w => w.Contains("unidade do Windows"));
        Assert.DoesNotContain(RecoveryRules.Warnings(false, false, RecoveryMode.Regular), w => w.Contains("unidade do Windows"));
        Assert.Contains(RecoveryRules.Warnings(false, true, RecoveryMode.Regular), w => w.Contains("TRIM"));
        Assert.Contains(RecoveryRules.Warnings(false, false, RecoveryMode.Extensive), w => w.Contains("muitas horas"));
        Assert.Contains(RecoveryRules.Warnings(false, false, RecoveryMode.Signature), w => w.Contains("nomes genéricos"));
    }
}

[SupportedOSPlatform("windows")]
public class FileRecoveryRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-rec-" + Guid.NewGuid().ToString("N"));

    public FileRecoveryRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Script(params string[] lines)
    {
        string path = Path.Combine(_dir, "fake-winfr.cmd");
        File.WriteAllText(path, "@echo off\r\n" + string.Join("\r\n", lines) + "\r\n");
        return path;
    }

    [WindowsFact]
    public async Task Le_o_progresso_e_responde_y_a_pergunta()
    {
        string script = Script("echo Pass 1: Scanning disk 12%%", "echo Pass 1: Scanning disk 55%%", "set /p ans=", "echo resposta=%ans%", "echo Pass 2: Recovering 100%%");
        var percents = new List<int>();
        var lines = new List<string>();
        var run = await FileRecoveryRunner.RunAsync("cmd.exe", ["/c", script], new Progress<int>(percents.Add), new Progress<string>(lines.Add), CancellationToken.None, "y");
        await Task.Delay(100);
        Assert.Equal(0, run.ExitCode);
        Assert.False(run.Cancelled);
        Assert.Contains("resposta=y", run.LastLines);                      // o app respondeu só o que a pessoa já tinha confirmado
        Assert.Equal([12, 55, 100], percents.ToArray());
    }

    [WindowsFact]
    public async Task Sem_resposta_o_stdin_e_fechado_e_nao_trava()
    {
        string script = Script("set /p ans=", "echo fim");
        var run = await FileRecoveryRunner.RunAsync("cmd.exe", ["/c", script], null, null, CancellationToken.None, answer: null, limit: TimeSpan.FromSeconds(20));
        Assert.False(run.Cancelled);
        Assert.Contains("fim", run.LastLines);
    }

    [WindowsFact]
    public async Task Cancelar_encerra_o_processo()
    {
        string script = Script("ping -n 30 127.0.0.1 > nul", "echo nunca");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var run = await FileRecoveryRunner.RunAsync("cmd.exe", ["/c", script], null, null, cts.Token);
        Assert.True(run.Cancelled);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.DoesNotContain("nunca", run.LastLines);
    }

    [WindowsFact]
    public async Task Programa_que_nao_existe_lanca_win32exception_e_conta_como_indisponivel()
    {
        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() =>
            FileRecoveryRunner.RunAsync("nd-nao-existe-7x7.exe", [], null, null, CancellationToken.None));
        Assert.False(await FileRecoveryRunner.AvailableAsync("nd-nao-existe-7x7.exe"));
    }
}
