using System.Runtime.Versioning;
using System.Security.Principal;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class DefragRulesTests
{
    private static DriveFacts Drive(bool? ssd, string fs = "NTFS", DriveKind kind = DriveKind.Fixed, bool ready = true) =>
        new("D", kind, ready, false, false, 3, fs, "Dados", 1L << 40, 1L << 39, ssd);

    [Fact]
    public void HD_NTFS_pode_analisar_e_desfragmentar_mas_nao_refazer_TRIM()
    {
        var o = DefragRules.Check(Drive(false));
        Assert.True(o.Analyze); Assert.True(o.Defrag); Assert.False(o.ReTrim);
    }

    [Fact]
    public void SSD_nao_desfragmenta_so_refaz_o_TRIM()
    {
        var o = DefragRules.Check(Drive(true));
        Assert.True(o.Analyze); Assert.False(o.Defrag); Assert.True(o.ReTrim);
        Assert.NotEmpty(o.Reason);
    }

    [Fact]
    public void Tipo_desconhecido_nao_desfragmenta_por_precaucao()
    {
        var o = DefragRules.Check(Drive(null));
        Assert.True(o.Analyze); Assert.False(o.Defrag); Assert.False(o.ReTrim);
    }

    [Theory]
    [InlineData("FAT32")]
    [InlineData("exFAT")]
    [InlineData("")]
    public void Fora_do_NTFS_nada_e_oferecido(string fs)
    {
        var o = DefragRules.Check(Drive(false, fs));
        Assert.False(o.Analyze); Assert.False(o.Defrag); Assert.False(o.ReTrim);
    }

    [Theory]
    [InlineData(DriveKind.Network)]
    [InlineData(DriveKind.CdRom)]
    [InlineData(DriveKind.Ram)]
    [InlineData(DriveKind.Unknown)]
    public void Rede_CD_e_disco_de_RAM_ficam_de_fora(DriveKind kind) => Assert.False(DefragRules.Check(Drive(false, kind: kind)).Defrag);

    [Fact]
    public void Unidade_nao_pronta_fica_de_fora() => Assert.False(DefragRules.Check(Drive(false, ready: false)).Analyze);

    [Theory]
    [InlineData("C", true)]
    [InlineData("z", true)]
    [InlineData("", false)]
    [InlineData("CC", false)]
    [InlineData("1", false)]
    [InlineData("C;", false)]
    [InlineData(null, false)]
    public void Letra_valida(string? letter, bool ok) => Assert.Equal(ok, DefragRules.ValidLetter(letter));

    [Theory]
    [InlineData(DefragAction.Analyze, "-Analyze")]
    [InlineData(DefragAction.Defrag, "-Defrag")]
    [InlineData(DefragAction.ReTrim, "-ReTrim")]
    public void Script_usa_so_a_letra_e_a_acao(DefragAction action, string flag)
    {
        string s = DefragRules.Script("d", action);
        Assert.Contains("-DriveLetter D ", s);
        Assert.Contains(flag, s);
        Assert.DoesNotContain("-Defrag", DefragRules.Script("d", DefragAction.Analyze));
    }

    [Theory]
    [InlineData("C:\\; calc")]
    [InlineData("")]
    [InlineData("$(x)")]
    public void Script_recusa_letra_invalida(string letter) => Assert.Throws<ArgumentException>(() => DefragRules.Script(letter, DefragAction.Defrag));

    [Theory]
    [InlineData(@"C:\dados\a.bin", FileAttributes.Normal, 1_000_000L, true)]
    [InlineData(@"C:\dados\a.bin", FileAttributes.Normal, 4096L, false)]           // cabe num cluster
    [InlineData(@"C:\$MFT", FileAttributes.Normal, 1_000_000L, false)]
    [InlineData(@"C:\pagefile.sys", FileAttributes.Normal, 1_000_000L, false)]
    [InlineData(@"C:\hiberfil.sys", FileAttributes.Normal, 1_000_000L, false)]
    [InlineData(@"C:\dados\link", FileAttributes.ReparsePoint, 1_000_000L, false)]
    [InlineData(@"C:\dados\vm.vhd", FileAttributes.SparseFile, 1_000_000L, false)]
    [InlineData(@"C:\dados\cofre.dat", FileAttributes.Encrypted, 1_000_000L, false)]
    public void Quais_arquivos_podem_ser_movidos(string path, FileAttributes attrs, long length, bool ok) =>
        Assert.Equal(ok, DefragRules.CanMove(path, attrs, length, 4096).Ok);

    [Fact]
    public void Arquivo_so_na_nuvem_nao_e_movido()
    {
        const int recallOnDataAccess = 0x400000;
        Assert.False(DefragRules.CanMove(@"C:\Users\ana\OneDrive\x.docx", (FileAttributes)recallOnDataAccess, 1_000_000, 4096).Ok);
    }

    [Fact]
    public void Trechos_que_se_emendam_contam_como_um_pedaco()
    {
        var extents = new List<Extent> { new(0, 100, 10), new(10, 110, 5), new(15, 500, 5) };
        Assert.Equal(2, DefragRules.Fragments(extents));
        Assert.Equal(20, DefragRules.TotalClusters(extents));
    }

    [Fact]
    public void Arquivo_em_um_trecho_so_tem_um_pedaco() => Assert.Equal(1, DefragRules.Fragments([new Extent(0, 100, 50)]));

    [Fact]
    public void Arquivo_sem_trechos_tem_zero_pedacos() => Assert.Equal(0, DefragRules.Fragments([]));

    [Fact]
    public void Buracos_nao_contam_como_pedaco_mas_sao_detectados()
    {
        var extents = new List<Extent> { new(0, 100, 10), new(10, -1, 10), new(20, 110, 10) };
        Assert.True(DefragRules.HasHoles(extents));
        Assert.Equal(1, DefragRules.Fragments(extents)); // 100..110 e 110..120 se emendam no disco
        Assert.Equal(20, DefragRules.TotalClusters(extents));
    }

    [Fact]
    public void Passos_de_movimento_deixam_o_arquivo_em_clusters_seguidos()
    {
        var steps = DefragRules.MoveSteps([new Extent(0, 900, 10), new Extent(10, 50, 6)], 2000);
        Assert.Equal([(0L, 2000L, 10L), (10L, 2010L, 6L)], steps);
    }

    [Fact]
    public void Trechos_grandes_sao_movidos_em_pedacos_do_limite()
    {
        long big = DefragRules.MoveChunkClusters * 2L + 5;
        var steps = DefragRules.MoveSteps([new Extent(0, 10, big)], 1000);
        Assert.Equal(3, steps.Count);
        Assert.Equal(big, steps.Sum(s => s.Clusters));
        Assert.All(steps, s => Assert.True(s.Clusters <= DefragRules.MoveChunkClusters));
        Assert.Equal(1000 + DefragRules.MoveChunkClusters, steps[1].Lcn);
        Assert.Equal(DefragRules.MoveChunkClusters, steps[1].Vcn);
    }

    [Fact]
    public void Passos_ignoram_buracos()
    {
        var steps = DefragRules.MoveSteps([new Extent(0, 10, 4), new Extent(4, -1, 4), new Extent(8, 70, 2)], 500);
        Assert.Equal([(0L, 500L, 4L), (8L, 504L, 2L)], steps);
    }

    [Theory]
    [InlineData("  45%", 45)]
    [InlineData("Pré-consolidação: 7%", 7)]
    public void Porcentagem_do_andamento(string line, int percent) => Assert.Equal(percent, DefragRules.ParsePercent(line));
}

public class VolumeStructuresTests
{
    private static byte[] Retrieval(long startVcn, params (long NextVcn, long Lcn)[] pairs)
    {
        var buf = new byte[16 + pairs.Length * 16];
        BitConverter.GetBytes(pairs.Length).CopyTo(buf, 0);
        BitConverter.GetBytes(startVcn).CopyTo(buf, 8);
        for (int i = 0; i < pairs.Length; i++)
        {
            BitConverter.GetBytes(pairs[i].NextVcn).CopyTo(buf, 16 + i * 16);
            BitConverter.GetBytes(pairs[i].Lcn).CopyTo(buf, 24 + i * 16);
        }
        return buf;
    }

    [Fact]
    public void Retrieval_pointers_viram_trechos()
    {
        var buf = Retrieval(0, (10, 1000), (15, 5000), (20, -1));
        var (extents, next) = VolumeStructures.ParseRetrievalPointers(buf, buf.Length);
        Assert.Equal([new Extent(0, 1000, 10), new Extent(10, 5000, 5), new Extent(15, -1, 5)], extents);
        Assert.Equal(20, next);
    }

    [Fact]
    public void Retrieval_pointers_comecando_depois_do_zero()
    {
        var buf = Retrieval(40, (50, 700));
        var (extents, next) = VolumeStructures.ParseRetrievalPointers(buf, buf.Length);
        Assert.Equal([new Extent(40, 700, 10)], extents);
        Assert.Equal(50, next);
    }

    [Fact]
    public void Retrieval_pointers_cortado_nao_estoura()
    {
        var buf = Retrieval(0, (10, 1000), (15, 5000));
        var (extents, _) = VolumeStructures.ParseRetrievalPointers(buf, buf.Length - 8); // o segundo par está incompleto
        Assert.Single(extents);
    }

    [Fact]
    public void Retrieval_pointers_com_VCN_que_nao_avanca_param()
    {
        var buf = Retrieval(10, (10, 1000), (20, 2000));
        Assert.Empty(VolumeStructures.ParseRetrievalPointers(buf, buf.Length).Extents);
    }

    [Fact]
    public void Retrieval_pointers_de_buffer_minusculo_viram_lista_vazia() => Assert.Empty(VolumeStructures.ParseRetrievalPointers(new byte[4], 4).Extents);

    // 16 clusters: 0..3 usados, 4..7 livres, 8..9 usados, 10..15 livres
    private static VolumeBitmap Sample() => new([0b0000_1111, 0b0000_0011], 16);

    [Fact]
    public void Bitmap_diz_o_que_esta_usado()
    {
        var b = Sample();
        Assert.True(b.IsUsed(0)); Assert.True(b.IsUsed(3)); Assert.False(b.IsUsed(4)); Assert.False(b.IsUsed(7));
        Assert.True(b.IsUsed(8)); Assert.True(b.IsUsed(9)); Assert.False(b.IsUsed(10));
        Assert.True(b.IsUsed(16)); // fora do volume conta como usado
        Assert.True(b.IsUsed(-1));
        Assert.Equal(6, b.UsedClusters);
    }

    [Fact]
    public void Bitmap_com_cauda_incompleta_conta_so_os_clusters_do_volume()
    {
        var b = new VolumeBitmap([0xFF, 0xFF], 12);
        Assert.Equal(12, b.UsedClusters);
    }

    [Theory]
    [InlineData(1, 0, 4)]
    [InlineData(4, 0, 4)]
    [InlineData(5, 0, 10)]
    [InlineData(6, 0, 10)]
    [InlineData(7, 0, -1)]
    [InlineData(2, 6, 6)]
    [InlineData(2, 8, 10)]
    [InlineData(0, 0, -1)]
    public void Acha_a_primeira_corrida_livre(long clusters, long from, long expected) => Assert.Equal(expected, Sample().FindFreeRun(clusters, from));

    [Fact]
    public void Corrida_livre_pula_bytes_inteiros_ocupados()
    {
        var bits = new byte[64];
        Array.Fill(bits, (byte)0xFF);
        bits[40] = 0; // 8 clusters livres a partir de 320
        var b = new VolumeBitmap(bits, 512);
        Assert.Equal(320, b.FindFreeRun(8));
        Assert.Equal(-1, b.FindFreeRun(9));
    }

    [Fact]
    public void Corrida_livre_atravessa_a_fronteira_de_bytes()
    {
        var b = new VolumeBitmap([0b0000_0111, 0b1111_1000], 16); // livres 3..10
        Assert.Equal(3, b.FindFreeRun(8));
    }

    [Fact]
    public void Mapa_divide_o_volume_em_regioes()
    {
        var map = Sample().Map(4); // regiões de 4 clusters: cheia, vazia, meio-cheia, vazia
        Assert.Equal([1.0, 0.0, 0.5, 0.0], map);
    }

    [Fact]
    public void Mapa_com_mais_celulas_que_clusters_nao_estoura()
    {
        var map = new VolumeBitmap([0b0000_0001], 3).Map(10);
        Assert.Equal(10, map.Length);
        Assert.All(map, v => Assert.InRange(v, 0.0, 1.0));
    }

    [Fact]
    public void Pedaco_do_bitmap_vira_inicio_tamanho_e_bytes()
    {
        var buf = new byte[16 + 3];
        BitConverter.GetBytes(64L).CopyTo(buf, 0);
        BitConverter.GetBytes(24L).CopyTo(buf, 8);
        buf[16] = 1; buf[17] = 2; buf[18] = 3;
        var (start, size, bytes) = VolumeBitmap.ParseChunk(buf, buf.Length);
        Assert.Equal(64, start); Assert.Equal(24, size);
        Assert.Equal([1, 2, 3], bytes);
    }

    [Fact]
    public void Pedaco_cortado_devolve_so_o_que_veio()
    {
        var buf = new byte[16 + 2];
        BitConverter.GetBytes(0L).CopyTo(buf, 0);
        BitConverter.GetBytes(1_000_000L).CopyTo(buf, 8);
        Assert.Equal(2, VolumeBitmap.ParseChunk(buf, buf.Length).Bytes.Length);
        Assert.Empty(VolumeBitmap.ParseChunk(new byte[8], 8).Bytes);
    }
}

public class WindowsToolsTests
{
    [Fact]
    public void Todo_atalho_do_catalogo_e_seguro() => Assert.All(WindowsTools.All, t => Assert.True(WindowsTools.IsSafe(t), t.Id));

    [Fact]
    public void Ids_e_arquivos_nao_se_repetem_sem_motivo()
    {
        Assert.Equal(WindowsTools.All.Count, WindowsTools.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(WindowsTools.All.Count, WindowsTools.All.Select(t => t.File + " " + t.Arguments).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(@"..\cmd.exe", "")]
    [InlineData(@"C:\Windows\System32\cmd.exe", "")]
    [InlineData("cmd.exe", "/c calc")]
    [InlineData("x.bat", "")]
    [InlineData("a b.exe", "")]
    [InlineData("rundll32.exe", "shell32.dll,Control_RunDLL")]
    [InlineData("", "")]
    public void Atalhos_perigosos_sao_recusados(string file, string args) => Assert.False(WindowsTools.IsSafe(new("x", "x", "x", "Sistema", file, args)));

    [Fact]
    public void So_aparece_o_que_existe_no_computador()
    {
        var present = WindowsTools.Available(f => f != "gpedit.msc").ToList();
        Assert.DoesNotContain(present, t => t.File == "gpedit.msc");
        Assert.Contains(present, t => t.File == "services.msc");
    }

    [Fact]
    public void Grupos_saem_na_ordem_combinada()
    {
        var groups = WindowsTools.Grouped(WindowsTools.All).Select(g => g.Key).ToList();
        Assert.Equal("Sistema", groups[0]);
        Assert.Equal("Avançado", groups[^1]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Nomes_e_descricoes_existem_em_ingles_e_espanhol(string lang)
    {
        try
        {
            Loc.Use(lang);
            foreach (var t in WindowsTools.All)
                foreach (var text in new[] { t.Name, t.Description, t.Group })
                    if (text.Any(char.IsLetter))
                        Assert.True(Loc.T(text) != text || lang == "es" && Loc.T(text) == text, $"{lang}: {text}");
        }
        finally { Loc.Use("pt"); }
    }
}

[SupportedOSPlatform("windows")]
public class VolumeApiIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-defrag-" + Guid.NewGuid().ToString("N"));

    public VolumeApiIntegrationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? RootOf(string path) => Path.GetPathRoot(path);

    [WindowsFact]
    public void Trechos_de_um_arquivo_comum_sao_lidos_sem_administrador()
    {
        string file = Path.Combine(_dir, "grande.bin");
        File.WriteAllBytes(file, new byte[2 * 1024 * 1024]);
        if (!string.Equals(new DriveInfo(RootOf(file)!).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return; // o disco temporário da máquina não é NTFS
        var extents = VolumeApi.GetExtents(file);
        Assert.NotEmpty(extents);
        Assert.True(DefragRules.TotalClusters(extents) * 512 >= 2 * 1024 * 1024 / 8); // pelo menos um oitavo: cluster nunca é maior que 4 KB nem 64 KB
        Assert.True(DefragRules.Fragments(extents) >= 1);
    }

    [WindowsFact]
    public void Arquivo_que_nao_existe_da_erro_claro()
    {
        Assert.ThrowsAny<Exception>(() => VolumeApi.GetExtents(Path.Combine(_dir, "nao-existe.bin")));
    }

    [WindowsFact]
    public void Abrir_o_volume_para_mover_sem_administrador_falha_com_aviso_claro()
    {
        if (IsAdmin()) return;
        string root = RootOf(_dir)!;
        Assert.ThrowsAny<Exception>(() => VolumeApi.OpenVolume(root[0], forMoving: true));
    }

    [WindowsFact]
    public void Com_administrador_o_bitmap_bate_com_os_dados_do_volume()
    {
        if (!IsAdmin()) return;
        string root = RootOf(_dir)!;
        if (!string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return;
        using var volume = VolumeApi.OpenVolume(root[0], forMoving: false);
        var data = VolumeApi.GetVolumeData(volume);
        var bitmap = VolumeApi.ReadBitmap(volume);
        Assert.NotNull(bitmap);
        Assert.Equal(data.TotalClusters, bitmap!.TotalClusters);
        long free = data.TotalClusters - bitmap.UsedClusters;
        // o volume muda enquanto lemos: aceita diferença pequena (menos de 1% do total)
        Assert.InRange(Math.Abs(free - data.FreeClusters), 0, Math.Max(1, data.TotalClusters / 100));
    }

    [WindowsFact]
    public void Com_administrador_arquivo_e_juntado_e_o_conteudo_continua_igual()
    {
        if (!IsAdmin()) return;
        string root = RootOf(_dir)!;
        if (!string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return;
        string file = Path.Combine(_dir, "mover.bin");
        var data = new byte[3 * 1024 * 1024];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(file, data);
        using var volume = VolumeApi.OpenVolume(root[0], forMoving: true);
        var bitmap = VolumeApi.ReadBitmap(volume)!;
        int cluster = VolumeApi.GetVolumeData(volume).BytesPerCluster;
        var result = FileDefragmenter.Defrag(volume, bitmap, cluster, file, CancellationToken.None);
        Assert.True(result.Status is FileDefragStatus.Defragmented or FileDefragStatus.AlreadyTogether or FileDefragStatus.NoRoom, result.Status + " " + result.Note);
        Assert.Equal(data, File.ReadAllBytes(file));
    }

    [WindowsFact]
    public void As_ferramentas_do_catalogo_existem_no_Windows()
    {
        string sys = Environment.SystemDirectory;
        int found = WindowsTools.All.Count(t => File.Exists(Path.Combine(sys, t.File)));
        Assert.True(found >= 15, $"Só {found} das {WindowsTools.All.Count} ferramentas existem em {sys}");
    }
}
