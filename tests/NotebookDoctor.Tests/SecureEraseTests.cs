using System.Runtime.Versioning;
using System.Text;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class EraseRulesTests
{
    private static readonly KnownPlaces Places = LeftoverScannerTests.Places;
    private static string? Blocked(string path, string[]? cloud = null, string[]? app = null) =>
        EraseRules.Blocked(path, Places, cloud ?? [@"C:\Users\ana\OneDrive"], app ?? [@"C:\Users\ana\AppData\Local\NotebookDoctor", @"C:\Tools\NotebookDoctor"]);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Program Files\App\dados.db")]
    [InlineData(@"C:\Program Files (x86)\App")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\x.dat")]
    [InlineData(@"C:\System Volume Information")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5\x")]
    [InlineData(@"\\servidor\pasta\arquivo.txt")]
    [InlineData(@"C:\Users\ana")]                                  // o perfil inteiro
    [InlineData(@"C:\Users")]                                      // contém o perfil
    [InlineData(@"C:\Users\ana\Documents")]                        // a pasta de documentos inteira
    [InlineData(@"C:\Users\ana\AppData")]
    [InlineData(@"C:\Users\ana\Desktop")]
    [InlineData(@"C:\Users\ana\OneDrive")]
    [InlineData(@"C:\Users\ana\OneDrive\Fotos\ferias.jpg")]        // a nuvem tem outra cópia
    [InlineData(@"C:\Users\ana\AppData\Local\NotebookDoctor\settings.json")]
    [InlineData(@"C:\Tools")]                                      // contém o app
    [InlineData(@"relativo\arquivo.txt")]
    [InlineData("")]
    public void Caminhos_que_nunca_sao_apagados_de_vez(string path) => Assert.NotNull(Blocked(path));

    [Theory]
    [InlineData(@"C:\Users\ana\Documents\segredo.docx")]           // o conteúdo de Documentos pode sair; a pasta não
    [InlineData(@"C:\Users\ana\Documents\Projeto antigo")]
    [InlineData(@"C:\Users\ana\Downloads\instalador.exe")]
    [InlineData(@"D:\Backup antigo")]
    [InlineData(@"E:\pendrive\arquivo.txt")]
    [InlineData(@"C:\Dados\clientes.xlsx")]
    public void Caminhos_que_podem_ser_apagados_de_vez(string path) => Assert.Null(Blocked(path));

    [Fact]
    public void Pasta_que_contem_uma_biblioteca_da_nuvem_fica_de_fora()
    {
        string[] cloud = [@"C:\Users\ana\OneDrive", @"D:\Dados\Empresa\Site - Documentos"];
        Assert.NotNull(Blocked(@"D:\Dados", cloud));                         // C5: levaria a biblioteca junto
        Assert.NotNull(Blocked(@"D:\Dados\Empresa", cloud));
        Assert.NotNull(Blocked(@"D:\Dados\Empresa\Site - Documentos\a.docx", cloud));
        Assert.Null(Blocked(@"D:\Dados\Outros", cloud));
        Assert.Null(Blocked(@"D:\Dados\Empresa\Site - Documentos 2", cloud)); // nome parecido não é a biblioteca
    }

    [Fact]
    public void Plano_junta_arquivos_pastas_e_o_que_ficou_de_fora()
    {
        var sys = new FakeSystem()
            .File(@"D:\Antigo\a.txt", 10).File(@"D:\Antigo\sub\b.txt", 20).File(@"D:\Antigo\sub\fundo\c.txt", 30)
            .File(@"D:\solto.txt", 5).File(@"C:\Windows\x.dll", 1);
        var plan = EraseRules.Plan([@"D:\Antigo", @"D:\solto.txt", @"C:\Windows\x.dll", @"D:\nao-existe", @"D:\solto.txt"], sys, Places, [], []);
        Assert.Equal(4, plan.Files.Count);                   // o repetido não conta duas vezes
        Assert.Equal(65, plan.Bytes);
        Assert.Contains(@"D:\Antigo\sub\fundo\c.txt", plan.Files);
        Assert.Equal(2, plan.Blocked.Count);
        Assert.Contains(plan.Blocked, b => b.Path == @"C:\Windows\x.dll");
        Assert.Contains(plan.Blocked, b => b.Path == @"D:\nao-existe" && b.Reason == "Não existe mais");
        // Pastas da mais funda para a mais rasa
        Assert.Equal([@"D:\Antigo\sub\fundo", @"D:\Antigo\sub", @"D:\Antigo"], plan.Dirs);
        Assert.False(plan.Truncated);
    }

    [Fact]
    public void Plano_nunca_lista_o_que_esta_fora_da_escolha()
    {
        var sys = new FakeSystem().File(@"D:\Antigo\a.txt").File(@"D:\Antigo2\b.txt").File(@"D:\Outro\c.txt");
        var plan = EraseRules.Plan([@"D:\Antigo"], sys, Places, [], []);
        Assert.Equal([@"D:\Antigo\a.txt"], plan.Files);                // "D:\Antigo2" não é "dentro de" D:\Antigo
    }

    [Fact]
    public void Nome_aleatorio_nunca_repete_a_origem_e_tem_o_formato_certo()
    {
        var rng = new Random(1);
        var names = Enumerable.Range(0, 200).Select(_ => EraseRules.RandomName(rng)).ToList();
        Assert.All(names, n => Assert.Matches("^[a-z0-9]{8,12}$", n));
        Assert.True(names.Distinct().Count() > 190);
    }

    [Fact]
    public void Avisos_de_ssd_e_arquivos_especiais()
    {
        Assert.Contains(EraseRules.Warnings(true, false), w => w.Contains("SSD"));      // SSD
        Assert.Contains(EraseRules.Warnings(null, false), w => w.Contains("SSD"));      // não sabe: avisa também
        Assert.DoesNotContain(EraseRules.Warnings(false, false), w => w.Contains("SSD")); // disco giratório
        Assert.Contains(EraseRules.Warnings(false, true), w => w.Contains("comprimidos"));
        Assert.Contains(EraseRules.Warnings(false, false), w => w.Contains("backup"));
    }
}

public class WipeRulesTests
{
    [Fact]
    public void Reserva_maior_no_sistema_e_nunca_zero()
    {
        long total = 500L << 30;
        Assert.True(WipeRules.Reserve(true, total) >= 512L << 20);
        Assert.True(WipeRules.Reserve(false, total) >= 8L << 20);
        Assert.True(WipeRules.Reserve(true, total) > WipeRules.Reserve(false, total));
        Assert.True(WipeRules.Reserve(false, 1L << 30) >= 8L << 20);
    }

    [Fact]
    public void Alvo_e_blocos_respeitam_a_reserva_e_o_limite()
    {
        Assert.Equal(900, WipeRules.Target(1000, 100));
        Assert.Equal(0, WipeRules.Target(50, 100));                      // menos que a reserva: nada a fazer
        Assert.Equal(300, WipeRules.Target(1000, 100, max: 300));
        Assert.Equal(WipeRules.BlockBytes, WipeRules.NextBlock(10L << 30, 100, 0));
        Assert.Equal(5000, WipeRules.NextBlock(5100, 100, 0));          // falta menos que um bloco
        Assert.Equal(0, WipeRules.NextBlock(100, 100, 0));
        Assert.Equal(200, WipeRules.NextBlock(10L << 30, 100, writtenSoFar: 800, max: 1000));
        Assert.Equal(0, WipeRules.NextBlock(10L << 30, 100, writtenSoFar: 1000, max: 1000));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(150, 100, 100)]
    [InlineData(10, 0, 100)]
    public void Porcentagem(long done, long target, int expected) => Assert.Equal(expected, WipeRules.Percent(done, target));

    [Fact]
    public void Pasta_temporaria_fica_na_unidade_certa()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal(@"C:\Users\ana\AppData\Local\Temp\NotebookDoctor-limpeza-11111111222233334444555555555555", WipeRules.TempDir(@"C:\", @"C:\Users\ana\AppData\Local\Temp", id));
        Assert.Equal(@"E:\NotebookDoctor-limpeza-11111111222233334444555555555555", WipeRules.TempDir(@"E:\", @"C:\Users\ana\AppData\Local\Temp", id));
    }

    [Fact]
    public void Avisos_da_limpeza_do_espaco_livre()
    {
        Assert.Contains(WipeRules.Warnings(true, false), w => w.Contains("quase cheio"));
        Assert.DoesNotContain(WipeRules.Warnings(false, false), w => w.Contains("quase cheio"));
        Assert.Contains(WipeRules.Warnings(false, true), w => w.Contains("SSD"));
        Assert.Contains(WipeRules.Warnings(false, false), w => w.Contains("MFT"));
    }
}

public class DriveWipeRulesTests
{
    private static DriveFacts Usb(string letter = "E", string fs = "exFAT", string label = "FOTOS", bool system = false, bool app = false, int bus = 7,
                                  DriveKind kind = DriveKind.Removable, bool ready = true, long total = 64L << 30) =>
        new(letter, kind, ready, system, app, bus, fs, label, total, total / 2, true);

    [Fact]
    public void Pendrive_ou_hd_externo_pode()
    {
        Assert.True(DriveWipeRules.Check(Usb()).Allowed);
        Assert.True(DriveWipeRules.Check(Usb(kind: DriveKind.Fixed, fs: "NTFS", label: "HD Externo")).Allowed);   // HD USB aparece como "fixo"
        Assert.True(DriveWipeRules.Check(Usb(bus: 12, fs: "FAT32")).Allowed);                                      // cartão SD
    }

    [Theory]
    [InlineData("C", "NTFS", true, false, 11, DriveKind.Fixed, true, "É a unidade do Windows")]
    [InlineData("D", "NTFS", false, true, 7, DriveKind.Fixed, true, "É a unidade onde o Notebook Doctor está")]
    [InlineData("D", "NTFS", false, false, 11, DriveKind.Fixed, true, "Não é uma unidade externa (USB, cartão): discos internos não entram")]   // SATA/NVMe
    [InlineData("E", "exFAT", false, false, 7, DriveKind.Removable, false, "A unidade não está pronta")]
    [InlineData("Z", "NTFS", false, false, 7, DriveKind.Network, true, "Não é um disco local: rede e CD não entram")]
    [InlineData("F", "NTFS", false, false, 7, DriveKind.CdRom, true, "Não é um disco local: rede e CD não entram")]
    [InlineData("E", "RAW", false, false, 7, DriveKind.Removable, true, "Sistema de arquivos que o app não reconhece")]
    [InlineData("E", "ReFS", false, false, 7, DriveKind.Removable, true, "Sistema de arquivos que o app não reconhece")]
    [InlineData("E", "NTFS", false, false, 0, DriveKind.Removable, true, "Não é uma unidade externa (USB, cartão): discos internos não entram")]   // barramento não respondeu
    [InlineData("1", "NTFS", false, false, 7, DriveKind.Removable, true, "Letra de unidade inválida")]
    [InlineData("", "NTFS", false, false, 7, DriveKind.Removable, true, "Letra de unidade inválida")]
    public void Unidade_que_nunca_pode(string letter, string fs, bool system, bool app, int bus, DriveKind kind, bool ready, string reason)
    {
        var (ok, why) = DriveWipeRules.Check(Usb(letter, fs, "X", system, app, bus, kind, ready));
        Assert.False(ok);
        Assert.Equal(reason, why);
        Assert.Throws<InvalidOperationException>(() => DriveWipeRules.FormatArgs(Usb(letter, fs, "X", system, app, bus, kind, ready)));
    }

    [Fact]
    public void Argumentos_do_format_completo_com_uma_passada_e_o_mesmo_sistema_de_arquivos()
    {
        Assert.Equal(["E:", "/FS:exFAT", "/P:1", "/Y", "/V:FOTOS"], DriveWipeRules.FormatArgs(Usb()));
        Assert.Equal(["F:", "/FS:NTFS", "/P:1", "/Y", "/V:Meu HD"], DriveWipeRules.FormatArgs(Usb("f", "ntfs", "Meu HD")));
        var args = DriveWipeRules.FormatArgs(Usb());
        Assert.DoesNotContain("/Q", args);                          // formatação rápida não zera nada
        Assert.Equal(1, args.Count(a => a.StartsWith("/P:")));
    }

    [Theory]
    [InlineData("Fotos & Vídeos; calc", "NTFS", "Fotos  Vdeos calc")]
    [InlineData("meu pendrive de trabalho muito comprido", "FAT32", "MEU PENDRIV")]  // FAT: 11 caracteres em maiúsculas
    [InlineData("", "NTFS", "DISCO")]
    [InlineData("!!!", "exFAT", "DISCO")]
    [InlineData("a/b\\c|d", "NTFS", "abcd")]
    public void Rotulo_seguro_para_a_linha_de_comando(string label, string fs, string expected) => Assert.Equal(expected, DriveWipeRules.SafeLabel(label, fs));

    [Fact]
    public void Rotulo_nunca_tem_caractere_de_comando()
    {
        var rng = new Random(9);
        for (int i = 0; i < 300; i++)
        {
            string junk = new(Enumerable.Range(0, rng.Next(0, 60)).Select(_ => (char)rng.Next(32, 400)).ToArray());
            string label = DriveWipeRules.SafeLabel(junk, i % 2 == 0 ? "NTFS" : "FAT32");
            Assert.Matches(@"^[A-Za-z0-9 _\-]{1,32}$", label);
        }
    }

    [Theory]
    [InlineData("12 percent completed.", 12)]
    [InlineData(" 100 por cento concluído.", 100)]
    [InlineData("0 por cento concluído.", 0)]
    [InlineData("101 percent", null)]
    [InlineData("Formatando", null)]
    [InlineData("Format complete.", null)]
    [InlineData("", null)]
    public void Progresso_do_format_em_qualquer_idioma(string line, int? expected) => Assert.Equal(expected, DriveWipeRules.ParseProgress(line));

    [Fact]
    public void Confirmacao_exige_a_letra_e_o_nome_quando_existe()
    {
        var d = Usb(label: "FOTOS");
        Assert.True(DriveWipeRules.Confirmed(d, "e", "fotos"));
        Assert.True(DriveWipeRules.Confirmed(d, "E:", "FOTOS"));
        Assert.False(DriveWipeRules.Confirmed(d, "F", "FOTOS"));      // letra errada
        Assert.False(DriveWipeRules.Confirmed(d, "E", "OUTRO"));      // nome errado
        Assert.False(DriveWipeRules.Confirmed(d, "E", ""));
        var noName = Usb(label: "");
        Assert.True(DriveWipeRules.Confirmed(noName, "E", ""));       // sem nome, basta a letra
        Assert.False(DriveWipeRules.Confirmed(noName, "", ""));
    }

    [Fact]
    public void Avisos_da_unidade()
    {
        Assert.Contains(DriveWipeRules.Warnings(Usb()), w => w.Contains("não dá para voltar"));
        Assert.Contains(DriveWipeRules.Warnings(Usb()), w => w.Contains("cancelar"));
        Assert.DoesNotContain(DriveWipeRules.Warnings(Usb(total: 8L << 30)), w => w.Contains("muitas horas"));
        Assert.Contains(DriveWipeRules.Warnings(Usb(total: 2L << 40)), w => w.Contains("muitas horas"));
    }
}

[SupportedOSPlatform("windows")]
public class SecureEraserIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-erase-" + Guid.NewGuid().ToString("N"));

    public SecureEraserIntegrationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private string Make(string name, int size, byte fill = 0xAB)
    {
        string path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, size).ToArray());
        return path;
    }

    [WindowsFact]
    public void Sobrescrever_troca_o_conteudo_e_mantem_o_tamanho()
    {
        string path = Make("a.bin", 3 * 1024 * 1024 + 123);
        SecureEraser.Overwrite(path);
        var after = File.ReadAllBytes(path);
        Assert.Equal(3 * 1024 * 1024 + 123, after.Length);
        Assert.False(after.All(b => b == 0xAB));
        Assert.True(after.Count(b => b == 0xAB) < after.Length / 100);           // aleatório: ~1/256 por acaso
    }

    [WindowsFact]
    public void Sobrescrever_arquivo_somente_leitura_e_vazio()
    {
        string ro = Make("ro.txt", 100);
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        SecureEraser.Overwrite(ro);
        Assert.False(File.ReadAllBytes(ro).All(b => b == 0xAB));
        string empty = Make("vazio.txt", 0);
        SecureEraser.Overwrite(empty);
        Assert.Empty(File.ReadAllBytes(empty));
    }

    [WindowsFact]
    public void Fluxo_alternativo_tambem_e_sobrescrito()
    {
        string path = Make("ads.txt", 50);
        try { File.WriteAllText(path + ":segredo", new string('X', 4000)); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { return; }   // volume sem fluxos
        Assert.Contains("segredo", SecureEraser.AlternateStreams(path));
        SecureEraser.Overwrite(path);
        var bytes = File.ReadAllBytes(path + ":segredo");
        Assert.Equal(4000, bytes.Length);                                   // mesmo tamanho
        Assert.True(bytes.Count(b => b == (byte)'X') < 100);                // e o conteúdo trocado
    }

    [WindowsFact]
    public void Apagar_remove_arquivos_e_pastas_e_nao_sobra_nada()
    {
        Make("tudo/a.txt", 10); Make("tudo/sub/b.txt", 2000); Make("tudo/sub/fundo/c.bin", 5_000_000);
        Make("fica/d.txt", 10);
        var sys = new WindowsLeftoverSystem();
        var plan = EraseRules.Plan([Path.Combine(_dir, "tudo")], sys, WindowsPlaces.Current(), [], []);
        Assert.Equal(3, plan.Files.Count);
        var seen = new List<EraseProgress>();
        var result = SecureEraser.Erase(plan, new Progress<EraseProgress>(seen.Add), CancellationToken.None);
        Assert.Equal(3, result.Erased);
        Assert.Equal(0, result.Failed);
        Assert.False(result.Cancelled);
        Assert.False(Directory.Exists(Path.Combine(_dir, "tudo")));
        Assert.True(File.Exists(Path.Combine(_dir, "fica", "d.txt")));            // o que não foi escolhido não é tocado
        Assert.Equal(["fica"], Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));   // nenhum nome aleatório sobrou
    }

    [WindowsFact]
    public void Arquivo_em_uso_fica_e_conta_como_falha_e_a_pasta_nao_e_apagada()
    {
        string path = Make("pasta/preso.txt", 100);
        Make("pasta/livre.txt", 100);
        var plan = EraseRules.Plan([Path.Combine(_dir, "pasta")], new WindowsLeftoverSystem(), WindowsPlaces.Current(), [], []);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = SecureEraser.Erase(plan, null, CancellationToken.None);
            Assert.Equal(1, result.Erased);
            Assert.Equal(1, result.Failed);
        }
        Assert.True(File.Exists(path));
        Assert.True(Directory.Exists(Path.Combine(_dir, "pasta")));               // pasta com sobra não é apagada
        Assert.Equal(100, new FileInfo(path).Length);
    }

    [WindowsFact]
    public void Arquivo_comum_tem_um_nome_so()
    {
        string a = Make("um-nome.txt", 10);
        Assert.Equal(1, SecureEraser.LinkCount(a));                               // um número absurdo aqui faria o apagador pular tudo
    }

    [WindowsFact]
    public void Link_fisico_nao_e_sobrescrito()
    {
        string a = Make("orig.txt", 100);
        string b = Path.Combine(_dir, "outro-nome.txt");
        if (!CreateHardLink(b, a, IntPtr.Zero)) return;                           // volume sem links físicos
        Assert.Equal(2, SecureEraser.LinkCount(a));
        var result = SecureEraser.Erase(new ErasePlan { Files = [a], Bytes = 100 }, null, CancellationToken.None);
        Assert.Equal(0, result.Erased);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.ReadAllBytes(b).All(x => x == 0xAB));                    // o outro nome continua intacto
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);

    [WindowsFact]
    public void Cancelar_antes_de_comecar_nao_apaga_nada()
    {
        string path = Make("c.txt", 100);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = SecureEraser.Erase(new ErasePlan { Files = [path], Bytes = 100 }, null, cts.Token);
        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Erased);
        Assert.True(File.ReadAllBytes(path).All(x => x == 0xAB));                 // intacto
    }

    [WindowsFact]
    public void Limpar_espaco_livre_escreve_ate_o_limite_e_apaga_tudo()
    {
        string tempDir = Path.Combine(_dir, "limpeza");
        string root = Path.GetPathRoot(_dir)!;
        var seen = new List<WipeProgress>();
        var result = FreeSpaceWiper.Wipe(root, tempDir, reserveBytes: 0, maxBytes: 40L << 20, new Progress<WipeProgress>(seen.Add), CancellationToken.None);
        Assert.Equal("", result.Error);
        Assert.Equal(40L << 20, result.Written);                                   // parou no limite, sem encher o disco do runner
        Assert.True(result.Completed);
        Assert.False(Directory.Exists(tempDir));                                   // nada sobrou
        Assert.NotEmpty(seen);
    }

    [WindowsFact]
    public void Cancelar_a_limpeza_apaga_o_que_escreveu()
    {
        string tempDir = Path.Combine(_dir, "limpeza2");
        using var cts = new CancellationTokenSource();
        var progress = new Progress<WipeProgress>(p => { if (p.Written > 8L << 20) cts.Cancel(); });
        var result = FreeSpaceWiper.Wipe(Path.GetPathRoot(_dir)!, tempDir, 0, 200L << 20, progress, cts.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
        Assert.True(result.Written < 200L << 20);
        Assert.False(Directory.Exists(tempDir));
    }

    [WindowsFact]
    public void Sondas_de_unidade_nao_lancam_erro()
    {
        string letter = Path.GetPathRoot(_dir)![..1];
        _ = DriveProbe.IsSolidState(letter[0]);
        Assert.True(DriveProbe.BusType(letter[0]) >= 0);
        var all = DriveProbe.List([_dir]);
        var mine = all.Single(d => d.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));
        Assert.True(mine.HostsApp);                                                // a pasta de teste é "do app" nesta chamada
        Assert.False(DriveWipeRules.Check(mine).Allowed);                          // e a unidade do runner nunca é formatável
    }
}

[SupportedOSPlatform("windows")]
public class ErasePathLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-link-" + Guid.NewGuid().ToString("N"));

    public ErasePathLinkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private bool Junction(string link, string target)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        p!.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    [WindowsFact]
    public void Caminho_real_segue_a_juncao_e_o_plano_recusa()
    {
        string real = Path.Combine(_dir, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "dados.txt"), "importante");
        string link = Path.Combine(_dir, "atalho");
        if (!Junction(link, real)) return;                                          // sem permissão para criar junção

        Assert.Equal(Path.Combine(real, "dados.txt"), RealPath.Of(Path.Combine(link, "dados.txt")), ignoreCase: true);
        Assert.Equal(real, RealPath.Of(real), ignoreCase: true);
        Assert.Null(RealPath.Of(Path.Combine(_dir, "nao-existe")));

        var plan = EraseRules.Plan([link, Path.Combine(link, "dados.txt"), real], new WindowsLeftoverSystem(), WindowsPlaces.Current(), [], [], RealPath.Of);
        Assert.Equal(2, plan.Blocked.Count);
        Assert.All(plan.Blocked, b => Assert.Equal("O caminho passa por um link ou atalho de pasta", b.Reason));
        Assert.Equal([Path.Combine(real, "dados.txt")], plan.Files.Select(f => f).ToList());      // só pelo caminho de verdade

        // Mesmo que um plano adulterado traga o caminho pelo atalho, o executor pula
        var result = SecureEraser.Erase(new ErasePlan { Files = [Path.Combine(link, "dados.txt")], Bytes = 10 }, null, CancellationToken.None);
        Assert.Equal(0, result.Erased);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("importante", File.ReadAllText(Path.Combine(real, "dados.txt")));
    }

    [WindowsFact]
    public void Caminho_normal_e_igual_ao_real()
    {
        string file = Path.Combine(_dir, "normal.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(file, RealPath.Of(file), ignoreCase: true);
        var plan = EraseRules.Plan([file], new WindowsLeftoverSystem(), WindowsPlaces.Current(), [], [], RealPath.Of);
        Assert.Empty(plan.Blocked);
        Assert.Single(plan.Files);
    }
}
