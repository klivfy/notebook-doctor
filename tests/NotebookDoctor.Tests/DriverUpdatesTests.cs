using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class DriverUpdatesTests
{
    private const string Gid = "11111111-2222-3333-4444-555555555555";
    private const string Hw = @"PCI\VEN_8086&DEV_46A6";

    private static OfferedDriver Offer(string version, DateTime? date, string hw = Hw, string id = Gid) =>
        new(id, $"Intel - Display - {version}", "Intel", "UHD", "Display", hw, date, version, 1000, false, true, "");

    private static InstalledDriver Dev(string version, DateTime? date, string inf = "oem12.inf", params string[] hw) =>
        new("UHD Graphics", @"PCI\VEN_8086&DEV_46A6\3&1", hw.Length > 0 ? hw : [Hw], version, date, inf, "Intel");

    [Theory]
    [InlineData("Intel Corporation - Net - 22.250.1.2", "22.250.1.2")]
    [InlineData("NVIDIA - Display - 31.0.15.5222", "31.0.15.5222")]
    [InlineData("Realtek Semiconductor Corp. - MEDIA - 6.0.9500.1  ", "6.0.9500.1")]
    [InlineData("Driver sem versão", "")]
    [InlineData("Driver 7", "")]                    // um número só não é versão
    public void Versao_no_fim_do_titulo(string title, string expected) => Assert.Equal(expected, DriverUpdateRules.VersionFromTitle(title));

    [Fact]
    public void Mais_novo_segue_a_ordem_do_windows_primeiro_a_data_depois_a_versao()
    {
        var old = new DateTime(2023, 1, 1);
        var recent = new DateTime(2024, 5, 1);
        Assert.True(DriverUpdateRules.IsNewer(recent, "1.0.0.1", old, "9.9.9.9"));      // data manda, mesmo com versão numericamente menor
        Assert.False(DriverUpdateRules.IsNewer(old, "9.9.9.9", recent, "1.0.0.1"));
        Assert.True(DriverUpdateRules.IsNewer(recent, "2.0.0.0", recent, "1.9.9.9"));   // mesma data: versão
        Assert.False(DriverUpdateRules.IsNewer(recent, "2.0.0.0", recent, "2.0.0.0"));  // igual não é novo
        Assert.False(DriverUpdateRules.IsNewer(recent, "1.0.0.0", recent, "2.0.0.0"));
        Assert.True(DriverUpdateRules.IsNewer(null, "2.0.0.0", recent, "1.0.0.0"));     // sem data de um lado: versão
        Assert.False(DriverUpdateRules.IsNewer(null, "", null, ""));                    // sem dado nenhum, não recomenda
        Assert.False(DriverUpdateRules.IsNewer(recent, "texto", recent, "1.0.0.0"));    // versão ilegível, mesma data: não recomenda no escuro
    }

    [Fact]
    public void Compara_o_dia_e_ignora_a_hora()
    {
        Assert.False(DriverUpdateRules.IsNewer(new DateTime(2024, 5, 1, 23, 0, 0), "1.0.0.0", new DateTime(2024, 5, 1, 1, 0, 0), "1.0.0.0"));
    }

    [Fact]
    public void Acha_o_dispositivo_pelo_id_de_hardware_sem_diferenciar_maiusculas()
    {
        var offered = Offer("2.0.0.0", new DateTime(2024, 1, 1), hw: Hw.ToLowerInvariant());
        Assert.NotNull(DriverUpdateRules.Match(offered, [Dev("1.0.0.0", null)]));
        Assert.Null(DriverUpdateRules.Match(offered, [Dev("1.0.0.0", null, "oem1.inf", @"PCI\VEN_10DE&DEV_0001")]));
        Assert.Null(DriverUpdateRules.Match(Offer("2.0.0.0", null, hw: ""), [Dev("1.0.0.0", null)])); // oferta sem id de hardware não casa com nada
    }

    [Fact]
    public void Avaliacao_recomenda_so_o_que_e_mais_novo_e_tem_dispositivo()
    {
        var installed = new List<InstalledDriver> { Dev("31.0.101.4502", new DateTime(2023, 6, 1)) };
        var newer = DriverUpdateRules.Evaluate(Offer("31.0.101.5333", new DateTime(2024, 5, 1)), installed);
        Assert.Equal(DriverVerdict.Newer, newer.Verdict);
        Assert.True(newer.CanInstall);
        Assert.True(newer.CanBackup);

        var same = DriverUpdateRules.Evaluate(Offer("31.0.101.4502", new DateTime(2023, 6, 1)), installed);
        Assert.Equal(DriverVerdict.NotNewer, same.Verdict);
        Assert.False(same.CanInstall);

        var other = DriverUpdateRules.Evaluate(Offer("9.0", new DateTime(2025, 1, 1), hw: @"USB\VID_0000&PID_0000"), installed);
        Assert.Equal(DriverVerdict.NoDevice, other.Verdict);
        Assert.False(other.CanInstall); // sem dispositivo, sem como guardar o atual: não instala
    }

    [Theory]
    [InlineData("oem12.inf", true)]
    [InlineData("OEM3.INF", true)]
    [InlineData("display.inf", false)]               // do próprio Windows
    [InlineData("c:\\windows\\inf\\x.inf", false)]
    [InlineData("oem12.inf & calc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void So_pacote_de_terceiros_pode_ser_exportado(string? inf, bool expected) => Assert.Equal(expected, DriverUpdateRules.IsExportable(inf));

    [Fact]
    public void Driver_do_windows_instala_mas_sem_copia()
    {
        var offer = DriverUpdateRules.Evaluate(Offer("2.0.0.0", new DateTime(2024, 1, 1)), [Dev("1.0.0.0", new DateTime(2020, 1, 1), inf: "display.inf")]);
        Assert.True(offer.CanInstall);
        Assert.False(offer.CanBackup);
    }

    [Fact]
    public void Ordem_recomendados_primeiro()
    {
        var installed = new List<InstalledDriver> { Dev("1.0.0.0", new DateTime(2023, 1, 1)) };
        var list = DriverUpdateRules.Order(
        [
            Offer("0.5.0.0", new DateTime(2020, 1, 1), id: "aaaaaaaa-0000-0000-0000-000000000001"),                    // mais antigo
            Offer("3.0.0.0", new DateTime(2025, 1, 1), hw: "ACPI\\FOO", id: "aaaaaaaa-0000-0000-0000-000000000002"),   // sem dispositivo
            Offer("2.0.0.0", new DateTime(2024, 1, 1), id: "aaaaaaaa-0000-0000-0000-000000000003"),                    // recomendado
        ], installed);
        Assert.Equal([DriverVerdict.Newer, DriverVerdict.NotNewer, DriverVerdict.NoDevice], list.Select(o => o.Verdict));
    }

    [Fact]
    public void Le_a_busca_com_uma_oferta_so_como_objeto_e_ignora_itens_invalidos()
    {
        string json = $$"""
        {"offered":{"id":"{{Gid}}","t":"Intel - Display - 31.0.1.2","mf":"Intel","model":"UHD","cls":"Display","hw":"PCI\\VEN_8086&DEV_46A6","date":"2024-05-01","size":123456,"opt":true,"eula":false,"eulaText":"Termos"},
         "installed":[{"name":"UHD","dev":"PCI\\X","hw":["PCI\\VEN_8086&DEV_46A6","PCI\\VEN_8086"],"v":"1.0.0.0","d":"2023-06-01","inf":"oem12.inf","prov":"Intel"},
                      {"name":"sem hardware","dev":"PCI\\Y","hw":[],"v":"1","d":"","inf":"","prov":""},
                      {"name":"","dev":"","hw":["A"],"v":"1","d":"","inf":"","prov":""}]}
        """;
        var scan = DriverUpdateRules.ParseScan(json);
        Assert.Single(scan.Offered);
        Assert.Equal("31.0.1.2", scan.Offered[0].Version);          // vem do título
        Assert.Equal(new DateTime(2024, 5, 1), scan.Offered[0].Date);
        Assert.True(scan.Offered[0].Optional);
        Assert.False(scan.Offered[0].EulaAccepted);
        Assert.Equal("Termos", scan.Offered[0].EulaText);
        Assert.Single(scan.Installed);                               // sem id de hardware ou sem dispositivo, descartado
        Assert.Equal(2, scan.Installed[0].HardwareIds.Count);
        Assert.Equal("", scan.Error);
    }

    [Fact]
    public void Oferta_com_id_que_nao_e_guid_e_descartada_e_o_erro_chega()
    {
        var scan = DriverUpdateRules.ParseScan("""{"offered":[{"id":"'; calc","t":"X - 1.0"}],"installed":[],"error":"0x8024402C"}""");
        Assert.Empty(scan.Offered);
        Assert.Equal("0x8024402C", scan.Error);
    }

    [Theory]
    [InlineData("""{"ok":true,"stage":"install","code":2,"hr":0,"reboot":false}""", true, false)]
    [InlineData("""{"ok":true,"stage":"install","code":2,"hr":0,"reboot":true}""", true, true)]
    [InlineData("""{"ok":true,"stage":"install","code":3,"hr":0,"reboot":false}""", false, false)]   // "concluído com erros" não é sucesso
    [InlineData("""{"ok":false,"stage":"download","code":4,"hr":-2145124322,"reboot":false}""", false, false)]
    [InlineData("""{"ok":true,"stage":"install","code":0,"hr":0,"reboot":false}""", false, false)]
    [InlineData("""[1,2]""", false, false)]
    public void Resultado_da_instalacao_so_vale_com_o_codigo_de_concluido(string json, bool ok, bool reboot)
    {
        var r = DriverUpdateRules.ParseInstall(json);
        Assert.Equal(ok, r.Ok);
        Assert.Equal(reboot, r.RebootRequired);
    }

    [Fact]
    public void Argumentos_do_pnputil_so_aceitam_pacote_e_pasta_validos()
    {
        Assert.Equal(["/export-driver", "oem12.inf", @"C:\dados\copia"], DriverUpdateRules.ExportArgs("oem12.inf", @"C:\dados\copia"));
        Assert.Equal(["/add-driver", @"C:\dados\copia\*.inf", "/install"], DriverUpdateRules.AddArgs(@"C:\dados\copia"));
        Assert.Equal(["/delete-driver", "oem12.inf", "/uninstall"], DriverUpdateRules.DeleteArgs("oem12.inf"));
        Assert.DoesNotContain("/force", DriverUpdateRules.DeleteArgs("oem12.inf"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.ExportArgs("display.inf", @"C:\x"));           // driver do Windows
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.ExportArgs("oem1.inf; calc", @"C:\x"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.ExportArgs("oem1.inf", @"relativa\pasta"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.ExportArgs("oem1.inf", @"C:\x\..\y"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.AddArgs(@"C:\x" + "\"" + @" /y"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.AddArgs(@"C:\x\*"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.DeleteArgs("../oem1.inf"));
        Assert.Throws<ArgumentException>(() => DriverUpdateRules.DeleteArgs("display.inf"));
    }

    [Fact]
    public void Nome_da_pasta_da_copia_e_seguro()
    {
        var now = new DateTime(2026, 10, 22, 13, 5, 9);
        Assert.Equal("20261022-130509-Intel-R-UHD-Graphics", DriverUpdateRules.BackupId(now, "Intel(R) UHD Graphics"));
        Assert.Equal("20261022-130509-driver", DriverUpdateRules.BackupId(now, "\\..\\//"));
        Assert.DoesNotContain("..", DriverUpdateRules.BackupId(now, "../../Windows"));
        Assert.True(DriverUpdateRules.BackupId(now, new string('a', 300)).Length < 70);
    }

    [Fact]
    public void Manifesto_da_copia_ida_e_volta_e_recusa_lixo()
    {
        var b = new DriverBackup { Id = "x", Folder = @"C:\a", DeviceName = "UHD", Version = "1.0", Created = new DateTime(2026, 1, 1) };
        var back = DriverBackupIndex.Read(DriverBackupIndex.Serialize(b));
        Assert.NotNull(back);
        Assert.Equal("UHD", back!.DeviceName);
        Assert.Null(DriverBackupIndex.Read("não é json"));
        Assert.Null(DriverBackupIndex.Read("""{"Id":"","Folder":""}"""));
    }
}

[SupportedOSPlatform("windows")]
public class DriverUpdaterFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-drv-" + Guid.NewGuid().ToString("N"));

    public DriverUpdaterFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string MakeCopy(string id, string inf = "x.inf", bool manifest = true)
    {
        string folder = Path.Combine(DriverUpdater.BackupsRoot(_dir), id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, inf), "; driver");
        if (manifest)
            File.WriteAllText(Path.Combine(folder, DriverBackupIndex.FileName), DriverBackupIndex.Serialize(new DriverBackup
            { Id = id, Folder = @"C:\Windows\System32", DeviceName = "UHD", Version = "1.0", Created = DateTime.Now }));
        return folder;
    }

    [Fact]
    public void Lista_as_copias_e_usa_a_pasta_onde_o_manifesto_esta_nao_a_que_ele_diz()
    {
        string folder = MakeCopy("20260101-000000-uhd");
        MakeCopy("sem-manifesto", manifest: false);
        Directory.CreateDirectory(Path.Combine(DriverUpdater.BackupsRoot(_dir), "vazia"));
        File.WriteAllText(Path.Combine(DriverUpdater.BackupsRoot(_dir), "vazia", DriverBackupIndex.FileName), "lixo");
        var list = DriverUpdater.ListBackups(_dir);
        Assert.Single(list);
        Assert.Equal(folder, list[0].Folder); // o manifesto dizia C:\Windows\System32
        Assert.NotNull(DriverUpdater.Find(_dir, "20260101-000000-uhd"));
        Assert.Null(DriverUpdater.Find(_dir, "outra"));
    }

    [Fact]
    public void Apagar_copia_nunca_sai_da_pasta_das_copias()
    {
        string outside = Path.Combine(_dir, "importante");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "a.txt"), "x");
        DriverUpdater.DeleteBackup(_dir, new DriverBackup { Id = "x", Folder = outside });
        Assert.True(File.Exists(Path.Combine(outside, "a.txt")));
        string folder = MakeCopy("copia");
        DriverUpdater.DeleteBackup(_dir, DriverUpdater.Find(_dir, "copia")!);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Voltar_sem_copia_ou_com_pasta_de_fora_nao_chama_nada()
    {
        Assert.Equal(RestoreOutcome.CopyMissing, await DriverUpdater.RestoreAsync(new DriverBackup { Id = "x", Folder = Path.Combine(_dir, "nao-existe") }, _dir));
        string outside = Path.Combine(_dir, "fora");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "evil.inf"), "x");
        Assert.Equal(RestoreOutcome.CopyMissing, await DriverUpdater.RestoreAsync(new DriverBackup { Id = "x", Folder = outside }, _dir));
    }

    [Fact]
    public async Task Driver_do_windows_nao_gera_copia()
    {
        var current = new InstalledDriver("Tela", @"X\1", [@"X\HW"], "1.0", null, "display.inf", "Microsoft");
        Assert.Null(await DriverUpdater.BackupAsync(current, "Oferta", _dir, DateTime.Now));
        Assert.False(Directory.Exists(DriverUpdater.BackupsRoot(_dir)));
    }
}
