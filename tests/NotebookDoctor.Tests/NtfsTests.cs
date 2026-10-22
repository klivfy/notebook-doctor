using System.Buffers.Binary;
using System.Text;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class NtfsBootTests
{
    [Fact]
    public void Setor_de_boot_vira_medidas_do_volume()
    {
        var g = new NtfsImage().Geometry;
        Assert.Equal(512, g.BytesPerSector);
        Assert.Equal(4096, g.BytesPerCluster);
        Assert.Equal(256, g.TotalClusters);
        Assert.Equal(4, g.MftLcn);
        Assert.Equal(1024, g.RecordSize);
        Assert.Equal(4 * 4096, g.MftOffset);
    }

    [Fact]
    public void Registro_medido_em_clusters_quando_o_byte_e_positivo()
    {
        var img = new NtfsImage();
        img.Bytes[0x40] = 1; // 1 cluster por registro = 4096 bytes
        Assert.Equal(4096, NtfsBoot.Parse(img.Bytes)!.RecordSize);
    }

    [Fact]
    public void Cluster_grande_codificado_como_expoente()
    {
        var img = new NtfsImage();
        img.Bytes[0x0D] = 0xF8; // 256 - 248 = 8 -> 2^8 = 256 setores por cluster
        Assert.Equal(512 * 256, NtfsBoot.Parse(img.Bytes)!.BytesPerCluster);
    }

    [Fact]
    public void Nao_NTFS_e_medidas_impossiveis_sao_recusadas()
    {
        Assert.Null(NtfsBoot.Parse(new byte[512]));
        Assert.Null(NtfsBoot.Parse(new byte[10]));
        var fat = new NtfsImage();
        "MSDOS5.0"u8.CopyTo(fat.Bytes.AsSpan(3));
        Assert.Null(NtfsBoot.Parse(fat.Bytes));
        foreach (var edit in new Action<byte[]>[]
        {
            b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0B), 300),            // setor que não é potência de 2
            b => b[0x0D] = 0,                                                              // zero setores por cluster
            b => b[0x0D] = 3,                                                              // 3 não é potência de 2
            b => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x30), 0),              // MFT no cluster 0
            b => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x30), 9999),           // MFT fora do volume
            b => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0x28), 0),              // volume sem setores
            b => b[0x40] = unchecked((byte)-3),                                           // registro de 8 bytes
            b => b[0x40] = 0,                                                              // registro de tamanho zero
        })
        {
            var img = new NtfsImage();
            edit(img.Bytes);
            Assert.Null(NtfsBoot.Parse(img.Bytes));
        }
    }
}

public class NtfsRunlistTests
{
    [Fact]
    public void Um_trecho()
    {
        var (runs, ok) = NtfsRunlist.Parse([0x11, 0x08, 0x64, 0x00], 0);
        Assert.True(ok);
        Assert.Equal([new Extent(0, 100, 8)], runs);
    }

    [Fact]
    public void Distancia_negativa_volta_no_disco()
    {
        // 8 clusters em 1000; depois 4 clusters 200 clusters ANTES (delta -200 = 0x38 0xFF em 2 bytes)
        var (runs, ok) = NtfsRunlist.Parse(Rec.EncodeRuns((1000, 8), (800, 4)), 0);
        Assert.True(ok);
        Assert.Equal([new Extent(0, 1000, 8), new Extent(8, 800, 4)], runs);
    }

    [Fact]
    public void Buraco_de_arquivo_esparso_fica_sem_cluster()
    {
        var (runs, ok) = NtfsRunlist.Parse(Rec.EncodeRuns((50, 2), (-1, 3), (60, 1)), 0);
        Assert.True(ok);
        Assert.Equal([new Extent(0, 50, 2), new Extent(2, -1, 3), new Extent(5, 60, 1)], runs);
        Assert.False(runs[1].Allocated);
    }

    [Fact]
    public void VCN_inicial_vale_para_a_primeira_posicao()
    {
        var (runs, _) = NtfsRunlist.Parse([0x11, 0x04, 0x10, 0x00], 20);
        Assert.Equal(20, runs[0].Vcn);
    }

    [Theory]
    [InlineData(new byte[] { 0x10, 0x08, 0x64, 0x00 })]       // campo de tamanho com 0 bytes
    [InlineData(new byte[] { 0x91, 0x08, 0x64, 0x00 })]       // campo de posição com 9 bytes
    [InlineData(new byte[] { 0x21, 0x08, 0x64 })]             // cortado no meio da posição
    [InlineData(new byte[] { 0x11, 0x00, 0x64, 0x00 })]       // trecho de zero clusters
    [InlineData(new byte[] { 0x11, 0x08, 0x80, 0x00 })]       // posição -128 a partir de 0: LCN negativo
    public void Dado_estragado_e_apontado(byte[] data) => Assert.False(NtfsRunlist.Parse(data, 0).Ok);

    [Fact]
    public void Lista_vazia_ou_so_o_zero_e_valida()
    {
        Assert.True(NtfsRunlist.Parse([0x00], 0).Ok);
        Assert.Empty(NtfsRunlist.Parse([], 0).Extents);
    }
}

public class MftRecordTests
{
    [Fact]
    public void Marcadores_de_integridade_voltam_ao_valor_verdadeiro()
    {
        var content = new byte[600];
        content[510 - 0x38 - 0x18 - 8] = 0xAB; // bytes que caem nos 2 últimos de cada setor
        var raw = Rec.Build(1, 1, 0, Rec.Resident(0x80, "", Enumerable.Range(0, 900).Select(i => (byte)(i % 251)).ToArray()));
        var record = MftRecord.Parse((byte[])raw.Clone(), 512, 30)!;
        var data = record.MainData!;
        Assert.Equal(900, data.Resident.Length);
        for (int i = 0; i < 900; i++) Assert.Equal((byte)(i % 251), data.Resident[i]);
    }

    [Fact]
    public void Marcador_que_nao_confere_e_gravacao_interrompida()
    {
        var torn = Rec.Build(1, 1, 0, corruptFixup: true, Rec.FileName(5, 5, "a.txt"));
        Assert.Null(MftRecord.Parse(torn, 512, 30));
    }

    [Fact]
    public void Nao_e_registro_sem_a_palavra_FILE()
    {
        var raw = Rec.Build(1, 1, 0, Rec.FileName(5, 5, "a.txt"));
        raw[0] = (byte)'B';
        Assert.Null(MftRecord.Parse(raw, 512, 30));
        Assert.Null(MftRecord.Parse(new byte[1024], 512, 30));
        Assert.Null(MftRecord.Parse(new byte[10], 512, 30));
    }

    [Fact]
    public void Campos_do_cabecalho()
    {
        var r = MftRecord.Parse(Rec.Build(7, 3, 0, Rec.FileName(5, 5, "pasta")), 512, 44)!;
        Assert.Equal(44, r.Number); Assert.Equal(7, r.Sequence);
        Assert.True(r.InUse); Assert.True(r.IsDirectory);
        Assert.Equal(0, r.BaseRecord);
        var apagado = MftRecord.Parse(Rec.Build(8, 0, 26, Rec.FileName(5, 5, "x")), 512, 45)!;
        Assert.False(apagado.InUse); Assert.False(apagado.IsDirectory);
        Assert.Equal(26, apagado.BaseRecord);
    }

    [Fact]
    public void Nome_longo_ganha_do_nome_curto()
    {
        var r = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(5, 5, "RELATO~1.DOC", ns: 2), Rec.FileName(5, 5, "Relatório anual.doc", ns: 1)), 512, 30)!;
        Assert.Equal("Relatório anual.doc", r.BestName()!.Name);
        var soCurto = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(5, 5, "RELATO~1.DOC", ns: 2)), 512, 31)!;
        Assert.Equal("RELATO~1.DOC", soCurto.BestName()!.Name);
        Assert.True(soCurto.BestName()!.IsDosOnly);
    }

    [Fact]
    public void Pasta_pai_e_sequencia_saem_da_referencia()
    {
        var r = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(123456, 9, "a.txt")), 512, 30)!;
        var n = r.BestName()!;
        Assert.Equal(123456, n.Parent);
        Assert.Equal(9, n.ParentSequence);
    }

    [Fact]
    public void Data_de_modificacao()
    {
        var when = new DateTime(2024, 5, 1, 12, 30, 0, DateTimeKind.Utc);
        var r = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.StdInfo(when), Rec.FileName(5, 5, "a")), 512, 30)!;
        Assert.Equal(when, r.Modified);
        Assert.Null(MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(5, 5, "a")), 512, 31)!.Modified);
    }

    [Fact]
    public void Dados_com_nome_sao_outro_fluxo_e_nao_o_conteudo()
    {
        var r = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(5, 5, "a"), Rec.Resident(0x80, "Zone.Identifier", new byte[] { 1, 2, 3 })), 512, 30)!;
        Assert.Null(r.MainData);
    }

    [Fact]
    public void Atributo_nao_residente_traz_tamanhos_e_trechos()
    {
        var r = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.FileName(5, 5, "a"), Rec.NonResident(0x80, "", 10000, 9000, 0, (100, 3))), 512, 30)!;
        var d = r.MainData!;
        Assert.True(d.NonResident); Assert.True(d.RunsOk);
        Assert.Equal(10000, d.RealSize); Assert.Equal(9000, d.InitializedSize);
        Assert.Equal([new Extent(0, 100, 3)], d.Runs);
    }

    [Fact]
    public void Flags_de_compressao_e_criptografia()
    {
        var c = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.NonResident(0x80, "", 100, 100, 0x0001, (50, 1))), 512, 30)!.MainData!;
        Assert.True(c.Compressed); Assert.False(c.Encrypted);
        var e = MftRecord.Parse(Rec.Build(1, 1, 0, Rec.NonResident(0x80, "", 100, 100, 0x4000, (50, 1))), 512, 30)!.MainData!;
        Assert.True(e.Encrypted);
    }

    [Fact]
    public void Atributo_com_tamanho_absurdo_para_a_leitura_sem_estourar()
    {
        var raw = Rec.Build(1, 1, 0, Rec.FileName(5, 5, "a"));
        // adultera o tamanho do primeiro atributo para além do registro
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x38 + 4), 0x7FFFFFF8);
        var r = MftRecord.Parse(raw, 512, 30);
        Assert.NotNull(r);
        Assert.Empty(r!.Attributes);
    }
}

public class NtfsScannerTests
{
    private static readonly DateTime Modified = new(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] Pattern(int length, byte seed)
    {
        var b = new byte[length];
        for (int i = 0; i < length; i++) b[i] = (byte)(seed + i * 7);
        return b;
    }

    /// <summary>Um volume com pastas, arquivos vivos, apagados inteiros, sobrescritos, comprimidos e outros casos de borda.</summary>
    private static NtfsImage Build(out byte[] jpeg)
    {
        var img = new NtfsImage();
        jpeg = Pattern(10_000, 3);
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF;
        img.PutClusters(100, jpeg);

        img.PutRecord(24, Rec.Build(1, 3, 0, Rec.FileName(5, 5, "Fotos")));                                     // pasta em uso
        img.PutRecord(25, Rec.Build(2, 2, 0, Rec.FileName(24, 1, "Antigas")));                                  // pasta apagada (sequência 1 -> 2)
        img.PutRecord(26, Rec.Build(1, 0, 0, Rec.StdInfo(Modified), Rec.FileName(25, 1, "foto1.jpg", real: 10_000),
            Rec.NonResident(0x80, "", 10_000, 10_000, 0, (100, 3))));
        img.PutRecord(27, Rec.Build(1, 0, 0, Rec.FileName(24, 1, "nota.txt"), Rec.Resident(0x80, "", Encoding.UTF8.GetBytes("olá mundo"))));
        img.PutRecord(28, Rec.Build(1, 1, 0, Rec.FileName(24, 1, "vivo.txt"), Rec.Resident(0x80, "", [1, 2, 3])));   // em uso: não entra
        img.PutRecord(29, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "sobrescrito.bin"), Rec.NonResident(0x80, "", 8192, 8192, 0, (110, 2))));
        img.PutRecord(30, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "parcial.bin"), Rec.NonResident(0x80, "", 16_000, 16_000, 0, (120, 4))));
        img.PutRecord(31, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "comprimido.dat"), Rec.NonResident(0x80, "", 5000, 5000, 0x0001, (130, 2))));
        img.PutRecord(32, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "espalhado.dat"), Rec.AttributeList(), Rec.NonResident(0x80, "", 5000, 5000, 0, (132, 2))));
        img.PutRecord(33, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "vazio.txt"), Rec.NonResident(0x80, "", 0, 0, 0)));
        img.PutRecord(34, Rec.Build(1, 0, 26, Rec.Resident(0x80, "extra", [9])));                                // registro de extensão: ignorado
        img.PutRecord(35, Rec.Build(1, 0, 0, Rec.FileName(25, 7, "orfao.txt"), Rec.Resident(0x80, "", [65, 66])));  // pai com outra sequência: reaproveitado
        img.PutRecord(36, Rec.Build(1, 0, 0, corruptFixup: true, Rec.FileName(5, 5, "rasgado.txt"), Rec.Resident(0x80, "", [65])));
        img.PutRecord(37, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "cripto.dat"), Rec.NonResident(0x80, "", 5000, 5000, 0x4000, (134, 2))));
        img.PutRecord(38, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "fora.dat"), Rec.NonResident(0x80, "", 4096 * 4, 4096 * 4, 0, (254, 4)))); // passa do fim do volume
        img.PutRecord(39, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "curto.dat"), Rec.NonResident(0x80, "", 50_000, 50_000, 0, (140, 2)))); // trechos não cobrem o tamanho
        return img;
    }

    private static NtfsScan Scan(NtfsImage img) => NtfsScanner.Scan(new MemoryVolume(img.Bytes), null, CancellationToken.None);

    [Fact]
    public void Lista_so_os_apagados_com_conteudo()
    {
        var scan = Scan(Build(out _));
        var names = scan.Files.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["comprimido.dat", "cripto.dat", "curto.dat", "espalhado.dat", "fora.dat", "foto1.jpg", "nota.txt", "orfao.txt", "parcial.bin", "sobrescrito.bin"], names);
        Assert.DoesNotContain("vivo.txt", names);       // em uso
        Assert.DoesNotContain("vazio.txt", names);      // sem conteúdo
        Assert.DoesNotContain("rasgado.txt", names);    // gravação interrompida
        Assert.Equal(1, scan.SkippedEmpty);
        Assert.False(scan.Truncated);
    }

    [Fact]
    public void Caminhos_vem_das_pastas_mesmo_apagadas()
    {
        var scan = Scan(Build(out _));
        string Folder(string name) { var f = scan.Files.Single(x => x.Name == name); return scan.Folders.PathOf(f.Parent, f.ParentSequence); }
        Assert.Equal(@"Fotos\Antigas", Folder("foto1.jpg"));
        Assert.Equal("Fotos", Folder("nota.txt"));
        Assert.Equal("", Folder("sobrescrito.bin"));
        Assert.Equal(NtfsFolders.Lost, Folder("orfao.txt"));   // a pasta foi reaproveitada por outra
    }

    [Fact]
    public void Dados_do_arquivo_apagado()
    {
        var scan = Scan(Build(out _));
        var f = scan.Files.Single(x => x.Name == "foto1.jpg");
        Assert.Equal(10_000, f.Size);
        Assert.Equal(Modified, f.Modified);
        Assert.False(f.Resident);
        Assert.Equal([new Extent(0, 100, 3)], f.Extents);
        Assert.Equal(NtfsLimit.None, f.Limit);
        Assert.Equal(".jpg", f.Extension);
        var n = scan.Files.Single(x => x.Name == "nota.txt");
        Assert.True(n.Resident);
        Assert.Equal("olá mundo", Encoding.UTF8.GetString(n.ResidentData!));
    }

    [Fact]
    public void Limites_do_que_nao_da_para_recuperar()
    {
        var scan = Scan(Build(out _));
        NtfsLimit Of(string n) => scan.Files.Single(f => f.Name == n).Limit;
        Assert.True(Of("comprimido.dat").HasFlag(NtfsLimit.Compressed));
        Assert.True(Of("cripto.dat").HasFlag(NtfsLimit.Encrypted));
        Assert.True(Of("espalhado.dat").HasFlag(NtfsLimit.SpreadOut));
        Assert.True(Of("fora.dat").HasFlag(NtfsLimit.BadRuns));
        Assert.True(Of("curto.dat").HasFlag(NtfsLimit.BadRuns));
    }

    [Fact]
    public void Chance_de_recuperar_segue_o_bitmap()
    {
        var scan = Scan(Build(out _));
        var bitmap = NtfsImage.Bitmap(110, 111, 122);
        RecoveryChance Chance(string n) => NtfsChance.Assess(scan.Files.Single(f => f.Name == n), bitmap).Chance;
        Assert.Equal(RecoveryChance.Excellent, Chance("foto1.jpg"));       // clusters livres
        Assert.Equal(RecoveryChance.Excellent, Chance("nota.txt"));        // dentro da MFT
        Assert.Equal(RecoveryChance.Poor, Chance("sobrescrito.bin"));      // tudo reaproveitado
        Assert.Equal(RecoveryChance.Partial, Chance("parcial.bin"));       // 1 de 4 reaproveitado
        Assert.Equal(RecoveryChance.Unsupported, Chance("comprimido.dat"));
        Assert.Equal(RecoveryChance.Unsupported, Chance("cripto.dat"));
        var parcial = NtfsChance.Assess(scan.Files.Single(f => f.Name == "parcial.bin"), bitmap);
        Assert.Equal(1, parcial.UsedClusters); Assert.Equal(4, parcial.TotalClusters);
    }

    [Fact]
    public void Sem_bitmap_a_chance_e_desconhecida_menos_dentro_da_MFT()
    {
        var scan = Scan(Build(out _));
        Assert.Equal(RecoveryChance.Unknown, NtfsChance.Assess(scan.Files.Single(f => f.Name == "foto1.jpg"), null).Chance);
        Assert.Equal(RecoveryChance.Excellent, NtfsChance.Assess(scan.Files.Single(f => f.Name == "nota.txt"), null).Chance);
    }

    [Fact]
    public void Progresso_chega_ao_fim_e_cancelar_para()
    {
        var img = Build(out _);
        var seen = new List<ScanProgress>();
        NtfsScanner.Scan(new MemoryVolume(img.Bytes), new Progress<ScanProgress>(seen.Add), CancellationToken.None);
        Thread.Sleep(50);
        Assert.Contains(seen, p => p.RecordsDone == p.RecordsTotal);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => NtfsScanner.Scan(new MemoryVolume(img.Bytes), null, cts.Token));
    }

    [Fact]
    public void Volume_que_nao_e_NTFS_ou_sem_MFT_da_erro_claro()
    {
        Assert.Throws<NtfsException>(() => NtfsScanner.Scan(new MemoryVolume(new byte[8192]), null, CancellationToken.None));
        var img = new NtfsImage();
        img.PutRecord(0, new byte[1024]); // o registro do $MFT sumiu
        Assert.Throws<NtfsException>(() => NtfsScanner.Scan(new MemoryVolume(img.Bytes), null, CancellationToken.None));
        Assert.Throws<NtfsException>(() => NtfsScanner.Scan(new MemoryVolume(new byte[100]), null, CancellationToken.None));
    }

    [Fact]
    public void Pastas_em_laco_nao_prendem_a_busca()
    {
        var img = new NtfsImage();
        img.PutRecord(24, Rec.Build(1, 3, 0, Rec.FileName(25, 1, "A")));
        img.PutRecord(25, Rec.Build(1, 3, 0, Rec.FileName(24, 1, "B")));
        img.PutRecord(26, Rec.Build(1, 0, 0, Rec.FileName(24, 1, "x.txt"), Rec.Resident(0x80, "", [65])));
        var scan = Scan(img);
        var f = scan.Files.Single();
        Assert.StartsWith(NtfsFolders.Lost, scan.Folders.PathOf(f.Parent, f.ParentSequence)); // o "perdido" fica no topo do caminho
    }

    [Fact]
    public void Parte_da_MFT_em_buraco_nao_quebra()
    {
        // MFT em dois pedaços: 8 clusters no começo, buraco de 2, e mais 6 (só para provar que o varredor pula buracos)
        var img = new NtfsImage();
        img.PutRecord(0, Rec.Build(1, 1, 0, Rec.FileName(5, 5, "$MFT"), Rec.NonResident(0x80, "", 16 * 4096, 16 * 4096, 0, (4, 8), (-1, 2), (14, 6))));
        img.PutRecord(26, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "a.txt"), Rec.Resident(0x80, "", [65])));
        var scan = Scan(img);
        Assert.Equal("a.txt", scan.Files.Single().Name);
    }
}

public class NtfsRecovererTests
{
    private static readonly NtfsGeometry Geo = new NtfsImage().Geometry;

    private static DeletedFile File(long size, long initialized, params Extent[] extents) =>
        new(30, 1, "x.bin", 5, 5, size, initialized, null, null, extents, NtfsLimit.None);

    [Fact]
    public void Copia_o_arquivo_inteiro_ate_o_tamanho_real()
    {
        var img = new NtfsImage();
        var data = Enumerable.Range(0, 10_000).Select(i => (byte)(i * 13)).ToArray();
        img.PutClusters(100, data);
        using var ms = new MemoryStream();
        var (written, complete) = NtfsRecoverer.CopyTo(new MemoryVolume(img.Bytes), Geo, File(10_000, 10_000, new Extent(0, 100, 3)), ms, CancellationToken.None);
        Assert.Equal(10_000, written);
        Assert.True(complete);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public void Arquivo_dentro_da_MFT_sai_do_proprio_registro()
    {
        var f = new DeletedFile(27, 1, "n.txt", 5, 5, 4, 4, null, [1, 2, 3, 4], [], NtfsLimit.None);
        using var ms = new MemoryStream();
        var (written, complete) = NtfsRecoverer.CopyTo(new MemoryVolume(new byte[10]), Geo, f, ms, CancellationToken.None);
        Assert.Equal(4, written); Assert.True(complete);
        Assert.Equal([1, 2, 3, 4], ms.ToArray());
    }

    [Fact]
    public void Buraco_de_arquivo_esparso_sai_como_zeros()
    {
        var img = new NtfsImage();
        img.Fill(130, 1, 0xAA);
        img.Fill(131, 1, 0xBB);
        var f = File(3 * 4096, 3 * 4096, new Extent(0, 130, 1), new Extent(1, -1, 1), new Extent(2, 131, 1));
        using var ms = new MemoryStream();
        NtfsRecoverer.CopyTo(new MemoryVolume(img.Bytes), Geo, f, ms, CancellationToken.None);
        var o = ms.ToArray();
        Assert.Equal(3 * 4096, o.Length);
        Assert.All(o.Take(4096), b => Assert.Equal(0xAA, b));
        Assert.All(o.Skip(4096).Take(4096), b => Assert.Equal(0, b));
        Assert.All(o.Skip(8192), b => Assert.Equal(0xBB, b));
    }

    [Fact]
    public void Depois_do_tamanho_inicializado_sai_zero_sem_ler_o_disco()
    {
        var img = new NtfsImage();
        img.Fill(140, 2, 0xCC);
        using var ms = new MemoryStream();
        NtfsRecoverer.CopyTo(new MemoryVolume(img.Bytes), Geo, File(8000, 5000, new Extent(0, 140, 2)), ms, CancellationToken.None);
        var o = ms.ToArray();
        Assert.Equal(8000, o.Length);
        Assert.All(o.Take(5000), b => Assert.Equal(0xCC, b));
        Assert.All(o.Skip(5000), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Limite_pede_so_o_comeco()
    {
        var img = new NtfsImage();
        img.Fill(100, 3, 0x11);
        using var ms = new MemoryStream();
        var (written, complete) = NtfsRecoverer.CopyTo(new MemoryVolume(img.Bytes), Geo, File(10_000, 10_000, new Extent(0, 100, 3)), ms, CancellationToken.None, limit: 300);
        Assert.Equal(300, written);
        Assert.False(complete);
    }

    [Fact]
    public void Leitura_curta_devolve_o_que_veio_e_diz_que_ficou_incompleto()
    {
        var img = new NtfsImage();
        img.Fill(100, 3, 0x22);
        var truncated = img.Bytes[..(100 * 4096 + 5000)];
        using var ms = new MemoryStream();
        var (written, complete) = NtfsRecoverer.CopyTo(new MemoryVolume(truncated), Geo, File(10_000, 10_000, new Extent(0, 100, 3)), ms, CancellationToken.None);
        Assert.Equal(5000, written);
        Assert.False(complete);
    }

    [Fact]
    public void Recusa_o_que_tem_limite_e_trecho_fora_do_volume()
    {
        var comprimido = new DeletedFile(1, 1, "c", 5, 5, 100, 100, null, null, [new Extent(0, 100, 1)], NtfsLimit.Compressed);
        Assert.Throws<NtfsException>(() => NtfsRecoverer.CopyTo(new MemoryVolume(new byte[1 << 20]), Geo, comprimido, new MemoryStream(), CancellationToken.None));
        Assert.Throws<NtfsException>(() => NtfsRecoverer.CopyTo(new MemoryVolume(new byte[1 << 20]), Geo, File(4096 * 4, 4096 * 4, new Extent(0, 254, 4)), new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public void Cancelar_interrompe_a_copia()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            NtfsRecoverer.CopyTo(new MemoryVolume(new NtfsImage().Bytes), Geo, File(10_000, 10_000, new Extent(0, 100, 3)), new MemoryStream(), cts.Token));
    }

    [Fact]
    public void Arquivo_grande_passa_por_varios_blocos()
    {
        // 300 clusters = 1,2 MB: mais de um bloco de 1 MiB
        var img = new NtfsImage();
        var big = new NtfsImage();
        var bytes = new byte[300 * 4096 + 4096 * 0];
        new Random(1).NextBytes(bytes);
        var geo = new NtfsGeometry(512, 4096, 400, 4, 1024);
        var disk = new byte[400 * 4096];
        bytes.CopyTo(disk, 50 * 4096);
        using var ms = new MemoryStream();
        var (written, complete) = NtfsRecoverer.CopyTo(new MemoryVolume(disk), geo, File(bytes.Length, bytes.Length, new Extent(0, 50, 300)), ms, CancellationToken.None);
        Assert.True(complete);
        Assert.Equal(bytes, ms.ToArray());
    }
}

public class NtfsRecoveryJobTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-ntfs-" + Guid.NewGuid().ToString("N"));

    public NtfsRecoveryJobTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static (NtfsImage Img, NtfsScan Scan) Scanned()
    {
        var img = new NtfsImage();
        var jpeg = new byte[10_000];
        new Random(5).NextBytes(jpeg);
        img.PutClusters(100, jpeg);
        img.PutRecord(24, Rec.Build(1, 3, 0, Rec.FileName(5, 5, "Fotos")));
        img.PutRecord(26, Rec.Build(1, 0, 0, Rec.StdInfo(new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc)), Rec.FileName(24, 1, "foto1.jpg"), Rec.NonResident(0x80, "", 10_000, 10_000, 0, (100, 3))));
        img.PutRecord(27, Rec.Build(1, 0, 0, Rec.FileName(24, 1, "foto1.jpg"), Rec.Resident(0x80, "", [1, 2, 3])));           // mesmo nome: vira "foto1 (2).jpg"
        img.PutRecord(28, Rec.Build(1, 0, 0, Rec.FileName(5, 5, "CON"), Rec.Resident(0x80, "", [4, 5])));                      // nome reservado do Windows
        return (img, NtfsScanner.Scan(new MemoryVolume(img.Bytes), null, CancellationToken.None));
    }

    [Fact]
    public void Recupera_para_as_pastas_de_origem_sem_sobrescrever_nada()
    {
        var (img, scan) = Scanned();
        var results = NtfsRecoveryJob.Run(new MemoryVolume(img.Bytes), scan, scan.Files, _dir, null, CancellationToken.None);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Complete, r.Note));
        string fotos = Path.Combine(_dir, "Fotos");
        var jpg = Directory.GetFiles(fotos).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(["foto1 (2).jpg", "foto1.jpg"], jpg);
        Assert.Equal(img.Bytes[(100 * 4096)..(100 * 4096 + 10_000)], File.ReadAllBytes(Path.Combine(fotos, "foto1.jpg")));
        Assert.True(File.Exists(Path.Combine(_dir, "_CON")));
        Assert.Equal(new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(fotos, "foto1.jpg")));
    }

    [Fact]
    public void Arquivo_que_ja_existe_no_destino_nao_e_tocado()
    {
        var (img, scan) = Scanned();
        string fotos = Directory.CreateDirectory(Path.Combine(_dir, "Fotos")).FullName;
        File.WriteAllText(Path.Combine(fotos, "foto1.jpg"), "meu arquivo");
        NtfsRecoveryJob.Run(new MemoryVolume(img.Bytes), scan, scan.Files, _dir, null, CancellationToken.None);
        Assert.Equal("meu arquivo", File.ReadAllText(Path.Combine(fotos, "foto1.jpg")));
        Assert.Equal(3, Directory.GetFiles(fotos).Length);
    }

    [Fact]
    public void Falha_num_arquivo_nao_derruba_os_outros()
    {
        var (img, scan) = Scanned();
        File.WriteAllText(Path.Combine(_dir, "Fotos"), "isto é um arquivo, não uma pasta"); // a pasta Fotos não pode ser criada
        var results = NtfsRecoveryJob.Run(new MemoryVolume(img.Bytes), scan, scan.Files, _dir, null, CancellationToken.None);
        Assert.Equal(3, results.Count);
        Assert.Equal(2, results.Count(r => !r.Complete && r.Note is not null));
        Assert.Single(results, r => r.Complete);
    }

    [Fact]
    public void Leitura_incompleta_e_marcada_e_o_arquivo_fica()
    {
        var (img, scan) = Scanned();
        var truncated = new MemoryVolume(img.Bytes[..(100 * 4096 + 5000)]);
        var results = NtfsRecoveryJob.Run(truncated, scan, scan.Files.Where(f => f.Name == "foto1.jpg" && !f.Resident).ToList(), _dir, null, CancellationToken.None);
        var r = Assert.Single(results);
        Assert.False(r.Complete);
        Assert.Equal(5000, new FileInfo(r.Path).Length);
    }

    [Fact]
    public void Cancelar_apaga_o_arquivo_pela_metade_e_relanca()
    {
        var (img, scan) = Scanned();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<(int Done, int Total, string Name)>(p => { if (p.Done == 1) cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => NtfsRecoveryJob.Run(new MemoryVolume(img.Bytes), scan, scan.Files, _dir, progress, cts.Token));
        Assert.Single(Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Progresso_conta_arquivo_a_arquivo()
    {
        var (img, scan) = Scanned();
        var seen = new List<(int, int, string)>();
        NtfsRecoveryJob.Run(new MemoryVolume(img.Bytes), scan, scan.Files, _dir, new SyncProgress<(int Done, int Total, string Name)>(seen.Add), CancellationToken.None);
        Assert.Equal(4, seen.Count);
        Assert.Equal((3, 3, ""), seen[^1]);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

public class NtfsRecoveryRulesTests
{
    [Theory]
    [InlineData("E", @"D:\Recuperados", null, false)]
    [InlineData("E", @"d:\x\y", null, false)]
    [InlineData("E", @"E:\Recuperados", null, true)]
    [InlineData("e", @"E:\", null, true)]
    [InlineData("E", @"\\servidor\pasta", null, true)]
    [InlineData("E", @"D:\a\..\E:\x", null, true)]
    [InlineData("E", @"Recuperados", null, true)]
    [InlineData("E", @"", null, true)]
    [InlineData("E", @"D:\Link", @"E:\Real", true)]            // junção que volta para a origem
    [InlineData("E", @"D:\Link", @"D:\Real", false)]
    [InlineData("", @"D:\x", null, true)]
    [InlineData("EE", @"D:\x", null, true)]
    public void Destino_tem_de_ser_outra_unidade(string source, string dest, string? resolved, bool blocked) =>
        Assert.Equal(blocked, NtfsRecoveryRules.CheckDestination(source, dest, resolved) is not null);

    [Theory]
    [InlineData("a.txt", "a.txt")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("a<b>|c\".txt", "a_b__c_.txt")]
    [InlineData("fim com ponto.", "fim com ponto")]
    [InlineData("fim com espaço   ", "fim com espaço")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("com1", "_com1")]
    [InlineData("", "arquivo")]
    [InlineData("...", "arquivo")]
    [InlineData("dois\ttabs\n.txt", "dois_tabs_.txt")]
    public void Nome_seguro(string name, string expected) => Assert.Equal(expected, NtfsRecoveryRules.SafeName(name));

    [Fact]
    public void Nome_gigante_e_encurtado_mantendo_a_extensao()
    {
        string safe = NtfsRecoveryRules.SafeName(new string('a', 400) + ".docx");
        Assert.True(safe.Length <= 150);
        Assert.EndsWith(".docx", safe);
    }

    [Theory]
    [InlineData(@"Fotos\Antigas", @"Fotos\Antigas")]
    [InlineData(@"..\..\Windows", "Windows")]
    [InlineData(@"a\.\b", @"a\b")]
    [InlineData(@"a/b", @"a\b")]
    [InlineData(@"a\CON\b", @"a\_CON\b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Pasta_relativa_segura(string? folder, string expected) => Assert.Equal(expected, NtfsRecoveryRules.SafeFolder(folder));

    [Fact]
    public void Pasta_funda_demais_e_cortada() =>
        Assert.Equal(32, NtfsRecoveryRules.SafeFolder(string.Join('\\', Enumerable.Repeat("p", 100))).Split('\\').Length);

    [Fact]
    public void Nome_unico_nunca_devolve_um_que_existe()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string first = NtfsRecoveryRules.UniquePath(Path.Combine("d", "a.txt"), taken.Contains);
        Assert.Equal(Path.Combine("d", "a.txt"), first);
        taken.Add(first);
        taken.Add(Path.Combine("d", "a (2).txt"));
        Assert.Equal(Path.Combine("d", "a (3).txt"), NtfsRecoveryRules.UniquePath(first, taken.Contains));
    }

    [Fact]
    public void Espaco_precisa_de_folga()
    {
        Assert.True(NtfsRecoveryRules.SpaceNeeded(1_000_000) > 1_000_000);
        Assert.Equal(1000L + 20 + (64L << 20), NtfsRecoveryRules.SpaceNeeded(1000));
    }

    [Fact]
    public void Avisos_sempre_dizem_que_a_origem_so_e_lida()
    {
        var d = new DriveFacts("E", DriveKind.Removable, true, false, false, 7, "NTFS", "PEN", 1L << 33, 1L << 30, null);
        Assert.Contains(NtfsRecoveryRules.Warnings(d), w => w.Contains("só lê"));
    }
}

public class FileSignatureTests
{
    [Theory]
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 }, SignatureCheck.Match)]
    [InlineData(".JPG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 }, SignatureCheck.Match)]
    [InlineData(".jpg", new byte[] { 1, 2, 3, 4, 5, 6 }, SignatureCheck.Mismatch)]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, SignatureCheck.Match)]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, SignatureCheck.Match)]
    [InlineData(".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0 }, SignatureCheck.Match)]
    [InlineData(".docx", new byte[] { 0x25, 0x50, 0x44, 0x46 }, SignatureCheck.Mismatch)]
    [InlineData(".xyz", new byte[] { 1, 2, 3 }, SignatureCheck.Unknown)]
    [InlineData(".jpg", new byte[0], SignatureCheck.Unknown)]
    public void Assinatura_combina_com_a_extensao(string ext, byte[] head, SignatureCheck expected) => Assert.Equal(expected, FileSignatures.Check(ext, head));

    [Fact]
    public void MP4_tem_ftyp_no_quarto_byte()
    {
        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 1, 2 };
        Assert.Equal(SignatureCheck.Match, FileSignatures.Check(".mp4", mp4));
        Assert.Equal(SignatureCheck.Mismatch, FileSignatures.Check(".mp4", new byte[10]));
    }

    [Fact]
    public void WAV_e_AVI_dividem_o_RIFF_mas_nao_o_tipo()
    {
        var wav = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");
        Assert.Equal(SignatureCheck.Match, FileSignatures.Check(".wav", wav));
        Assert.Equal(SignatureCheck.Mismatch, FileSignatures.Check(".avi", wav));
    }

    [Fact]
    public void Texto_confere_se_parece_texto()
    {
        Assert.Equal(SignatureCheck.Match, FileSignatures.Check(".txt", Encoding.UTF8.GetBytes("Olá, isto é um texto.\r\nSegunda linha.")));
        Assert.Equal(SignatureCheck.Mismatch, FileSignatures.Check(".txt", new byte[] { 0x50, 0x4B, 0, 0, 1, 2, 3 }));
        Assert.Equal(SignatureCheck.Match, FileSignatures.Check(".csv", [0xFF, 0xFE, 0x61, 0x00]));
    }

    [Fact]
    public void Tudo_zero_e_detectado()
    {
        Assert.True(FileSignatures.AllZero(new byte[100]));
        Assert.False(FileSignatures.AllZero([0, 0, 1]));
        Assert.False(FileSignatures.AllZero([]));
    }

    [Theory]
    [InlineData(".jpg", PreviewKind.Image)]
    [InlineData(".PNG", PreviewKind.Image)]
    [InlineData(".txt", PreviewKind.Text)]
    [InlineData(".json", PreviewKind.Text)]
    [InlineData(".rtf", PreviewKind.None)]
    [InlineData(".exe", PreviewKind.None)]
    [InlineData("", PreviewKind.None)]
    public void Tipo_de_previa(string ext, PreviewKind kind) => Assert.Equal(kind, FileSignatures.PreviewOf(ext));

    [Theory]
    [InlineData(RecoveryChance.Excellent, SignatureCheck.Match, false, RecoveryChance.Excellent)]
    [InlineData(RecoveryChance.Excellent, SignatureCheck.Unknown, false, RecoveryChance.Excellent)]
    [InlineData(RecoveryChance.Excellent, SignatureCheck.Mismatch, false, RecoveryChance.Partial)]
    [InlineData(RecoveryChance.Partial, SignatureCheck.Mismatch, false, RecoveryChance.Poor)]
    [InlineData(RecoveryChance.Excellent, SignatureCheck.Match, true, RecoveryChance.Poor)]
    [InlineData(RecoveryChance.Unknown, SignatureCheck.Match, false, RecoveryChance.Unknown)]
    [InlineData(RecoveryChance.Unsupported, SignatureCheck.Match, true, RecoveryChance.Unsupported)]
    [InlineData(RecoveryChance.Poor, SignatureCheck.Match, false, RecoveryChance.Poor)]
    public void Chance_afinada_pelo_conteudo(RecoveryChance chance, SignatureCheck sig, bool zero, RecoveryChance expected) =>
        Assert.Equal(expected, NtfsChance.Refine(chance, sig, zero));

    [Fact]
    public void Ordem_das_chances_vai_da_melhor_para_a_pior()
    {
        var order = new[] { RecoveryChance.Excellent, RecoveryChance.Partial, RecoveryChance.Unknown, RecoveryChance.Poor, RecoveryChance.Unsupported };
        Assert.Equal(order, order.OrderBy(NtfsChance.Order).ToArray());
    }
}
