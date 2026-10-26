using System.Text;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class SmbiosTests
{
    /// <summary>Uma estrutura SMBIOS: cabeçalho de 4 bytes, área formatada (já com o cabeçalho, no tamanho dado) e textos.</summary>
    private static byte[] Struct(byte type, int length, Action<byte[]> fill, params string[] strings)
    {
        var f = new byte[length];
        f[0] = type; f[1] = (byte)length;
        fill(f);
        using var ms = new MemoryStream();
        ms.Write(f);
        foreach (var s in strings) { ms.Write(Encoding.UTF8.GetBytes(s)); ms.WriteByte(0); }
        if (strings.Length == 0) ms.WriteByte(0);
        ms.WriteByte(0);
        return ms.ToArray();
    }

    private static byte[] Raw(params byte[][] structs)
    {
        var body = structs.SelectMany(s => s).ToArray();
        var raw = new byte[8 + body.Length];
        raw[1] = 3; raw[2] = 4;
        BitConverter.GetBytes((uint)body.Length).CopyTo(raw, 4);
        body.CopyTo(raw, 8);
        return raw;
    }

    private static byte[] Array16(int kb, int devices, byte ecc = 3) => Struct(16, 0x17, f =>
    {
        f[0x06] = ecc;
        BitConverter.GetBytes(kb).CopyTo(f, 0x07);
        BitConverter.GetBytes((ushort)devices).CopyTo(f, 0x0D);
    });

    private static byte[] Dimm(ushort size, byte type, ushort speed, ushort configured, uint extended, string slot, string bank, string maker, string part, byte form = 0x0D) =>
        Struct(17, 0x28, f =>
        {
            BitConverter.GetBytes(size).CopyTo(f, 0x0C);
            f[0x0E] = form; f[0x10] = 1; f[0x11] = 2; f[0x12] = type;
            BitConverter.GetBytes(speed).CopyTo(f, 0x15);
            f[0x17] = size == 0 ? (byte)0 : (byte)3; f[0x1A] = size == 0 ? (byte)0 : (byte)4;
            BitConverter.GetBytes(extended).CopyTo(f, 0x1C);
            BitConverter.GetBytes(configured).CopyTo(f, 0x20);
        }, size == 0 ? [slot, bank] : [slot, bank, maker, part]);

    private static readonly byte[] End = Struct(127, 4, _ => { });

    [Fact]
    public void Le_slots_pentes_e_slots_vazios()
    {
        var raw = Raw(
            Array16(64 * 1024 * 1024, 4),
            Dimm(16384, 0x1A, 3200, 2933, 0, "DIMM A1", "BANK 0", "Samsung", "M471A2K43DB1"),
            Dimm(0, 0x1A, 0, 0, 0, "DIMM A2", "BANK 0", "", ""),
            Dimm(0x7FFF, 0x22, 5600, 5600, 65536, "DIMM B1", "BANK 1", "Kingston", "KF556S40"),
            End);
        var data = SmbiosParser.Parse(raw);
        Assert.Equal("3.4", data.Version);
        var array = Assert.Single(data.Arrays);
        Assert.Equal(4, array.Slots);
        Assert.Equal(64L << 30, array.MaxCapacityBytes);
        Assert.Equal("Sem", array.Ecc);

        Assert.Equal(3, data.Devices.Count);
        var a = data.Devices[0];
        Assert.Equal(16L << 30, a.SizeBytes);
        Assert.Equal("DDR4", a.Type);
        Assert.Equal("SODIMM", a.FormFactor);
        Assert.Equal(3200, a.SpeedMHz);
        Assert.Equal(2933, a.ConfiguredMHz);
        Assert.Equal("DIMM A1", a.Slot);
        Assert.Equal("BANK 0", a.Bank);
        Assert.Equal("Samsung", a.Manufacturer);
        Assert.Equal("M471A2K43DB1", a.PartNumber);

        var empty = data.Devices[1];
        Assert.Equal(0, empty.SizeBytes);
        Assert.Equal("DIMM A2", empty.Slot);
        Assert.Equal("", empty.Manufacturer);               // slot vazio não tem fabricante nem peça

        var big = data.Devices[2];
        Assert.Equal(64L << 30, big.SizeBytes);             // 0x7FFF: vale o tamanho estendido
        Assert.Equal("DDR5", big.Type);
    }

    [Theory]
    [InlineData(0x18, "DDR3")]
    [InlineData(0x1A, "DDR4")]
    [InlineData(0x22, "DDR5")]
    [InlineData(0x1E, "LPDDR4")]
    [InlineData(0x23, "LPDDR5")]
    [InlineData(0xFE, "")]
    public void Tipos_de_memoria(int code, string name) => Assert.Equal(name, SmbiosParser.TypeName(code));

    [Fact]
    public void Tamanho_em_KB_quando_o_bit_alto_esta_ligado()
    {
        var raw = Raw(Dimm(0x8000 | 512, 0x03, 100, 0, 0, "Chip", "B", "X", "Y", form: 0x0B), End);
        Assert.Equal(512L * 1024, SmbiosParser.Parse(raw).Devices.Single().SizeBytes);
    }

    [Fact]
    public void Entrada_vazia_estragada_ou_truncada_nunca_lanca_erro()
    {
        Assert.Empty(SmbiosParser.Parse([]).Devices);
        Assert.Empty(SmbiosParser.Parse(new byte[5]).Devices);
        var good = Raw(Array16(8 << 20, 2), Dimm(8192, 0x1A, 2666, 2666, 0, "A", "B", "M", "P"), End);
        for (int cut = 0; cut < good.Length; cut++) _ = SmbiosParser.Parse(good[..cut]);          // todo corte possível
        var rnd = new Random(42);
        for (int i = 0; i < 300; i++)
        {
            var junk = new byte[rnd.Next(0, 400)];
            rnd.NextBytes(junk);
            if (junk.Length >= 8) BitConverter.GetBytes((uint)rnd.Next(0, 500)).CopyTo(junk, 4);
            _ = SmbiosParser.Parse(junk);
        }
        // Tamanho declarado maior que o buffer: lê só o que existe
        var lie = (byte[])good.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(lie, 4);
        Assert.Single(SmbiosParser.Parse(lie).Devices);
    }

    [Fact]
    public void Estrutura_com_tamanho_menor_que_o_cabecalho_encerra_a_leitura()
    {
        var raw = Raw(Array16(1 << 20, 1), new byte[] { 99, 2, 0, 0, 0, 0 }, Dimm(4096, 0x1A, 2400, 2400, 0, "A", "B", "M", "P"), End);
        Assert.Empty(SmbiosParser.Parse(raw).Devices);   // não adianta andar a partir de uma tabela quebrada
    }
}

public class SystemInfoTests
{
    private static RawSystem Sample() => SystemInfoParser.Parse("""
    {
      "os": {"caption":"Microsoft Windows 11 Pro","version":"23H2","build":"22631.3737","arch":"64 bits","install":"2024-03-12","boot":"2026-10-22 08:00","lic":1,"secureboot":true},
      "tpm": {"present":true,"version":"2.0","enabled":true},
      "cpu": {"name":"Intel(R) Core(TM) i5-1235U","cores":10,"threads":12,"mhz":4400,"cur":1300,"l2kb":6656,"l3kb":12288,"virt":true},
      "mem": {"slots":2,"maxkb":67108864,"modules":[{"loc":"DIMM A","bank":"B0","cap":8589934592,"speed":3200,"cfg":3200,"mf":"Samsung","pn":"M471"}]},
      "board": {"mf":"Acme","product":"Notebook 15","ver":"Rev 1"},
      "bios": {"vendor":"Acme","ver":"F.20","date":"2024-01-15"},
      "gpu": [{"name":"NVIDIA GeForce RTX 3060","drv":"31.0.15.5222","drvdate":"2024-05-01","vram":6442450944,"res":"1920x1080 @ 60 Hz"}],
      "disk": [{"name":"NVMe 512GB","media":"SSD","bus":"NVMe","size":512110190592,"health":"Healthy","temp":41,"wear":4}],
      "predict": false,
      "audio": ["Realtek Audio"], "optical": [], "usb": ["Mouse", "Webcam"], "bt": "Fone",
      "thermal": [{"src":"ACPI","n":"TZ00_0","c":48},{"src":"ACPI","n":"TZ01_0","c":0},{"src":"ACPI","n":"TZ02_0","c":-273.1}]
    }
    """);

    [Fact]
    public void Le_o_json_do_powershell_com_objeto_unico_no_lugar_de_lista()
    {
        var r = Sample();
        Assert.Equal("Microsoft Windows 11 Pro", r.OsName);
        Assert.Equal(1, r.Activation);
        Assert.True(r.SecureBoot);
        Assert.True(r.Tpm!.Present);
        Assert.Equal("Intel(R) Core(TM) i5-1235U", Assert.Single(r.Cpus).Name);   // PowerShell 5 manda objeto, não lista, quando só há um
        Assert.Equal(67108864L * 1024, r.MemMaxBytes);
        Assert.Equal(8L << 30, Assert.Single(r.Modules).Bytes);
        Assert.Equal(6442450944, Assert.Single(r.Gpus).VramBytes);
        Assert.Equal(["Fone"], r.Bluetooth);                                        // texto solto vira lista de um
        Assert.False(r.PredictFailure);
        Assert.Equal(3, r.Temps.Count);
    }

    [Fact]
    public void Json_vazio_ou_com_campos_faltando_nao_quebra()
    {
        var r = SystemInfoParser.Parse("{}");
        Assert.Equal("", r.OsName);
        Assert.Empty(r.Cpus);
        Assert.Null(r.Tpm);
        Assert.Null(r.Activation);
        Assert.Null(r.PredictFailure);
        Assert.Empty(SystemInfoParser.Parse("""{"cpu":"lixo","gpu":42,"disk":[1,2,"x"],"mem":null}""").Disks);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SystemInfoParser.Parse("isto não é json"));
    }

    private static List<InfoSection> Build(SmbiosData? smbios = null, RawSystem? raw = null, IReadOnlyList<RawTemp>? extra = null, WifiInfo? wifi = null) =>
        SystemInfoBuilder.Build(raw ?? Sample(), smbios, "UEFI", [new NetAdapter("Wi-Fi", "Intel AX201", "Wireless80211", "", 866, true, ["192.168.0.12"], "192.168.0.1", ["1.1.1.1"])],
            wifi, extra ?? [], "PC-TESTE");

    private static string Value(List<InfoSection> s, string key, string label) =>
        s.Single(x => x.Key == key).Lines.FirstOrDefault(l => l.Label == label)?.Value ?? "";

    [Fact]
    public void Secoes_na_ordem_certa_e_so_as_que_tem_conteudo()
    {
        var keys = Build().Select(s => s.Key).ToList();
        Assert.Equal(["os", "cpu", "mem", "board", "gpu", "disk", "devices", "net", "temp"], keys);
        var minimal = SystemInfoBuilder.Build(new RawSystem(), null, "", [], null, [], "");
        Assert.DoesNotContain(minimal, s => s.Lines.Count == 0);
    }

    [Fact]
    public void Memoria_pela_SMBIOS_mostra_slots_livres_e_vazios()
    {
        var smbios = new SmbiosData("3.4", [new SmbiosArray(4, 64L << 30, "Sem")],
        [
            new SmbiosMemory("DIMM A1", "BANK 0", 16L << 30, "SODIMM", "DDR4", 3200, 2933, "Samsung", "M471"),
            new SmbiosMemory("DIMM A2", "BANK 0", 0, "SODIMM", "DDR4", 0, 0, "", ""),
        ]);
        var s = Build(smbios);
        Assert.Equal("4 no total · 1 ocupados · 3 livres", Value(s, "mem", "Slots"));
        Assert.Equal("Vazio", Value(s, "mem", "DIMM A2 (BANK 0)"));
        Assert.StartsWith("16", Value(s, "mem", "DIMM A1 (BANK 0)"));
        Assert.Contains("DDR4 SODIMM", Value(s, "mem", "DIMM A1 (BANK 0)"));
        Assert.Contains("3200 MHz (em uso 2933)", Value(s, "mem", "DIMM A1 (BANK 0)"));
        Assert.Contains("Samsung M471", Value(s, "mem", "DIMM A1 (BANK 0)"));
    }

    [Fact]
    public void Memoria_sem_SMBIOS_usa_o_WMI()
    {
        var s = Build();
        Assert.Equal("2 no total · 1 ocupados · 1 livres", Value(s, "mem", "Slots"));
        Assert.Contains("3200 MHz", Value(s, "mem", "DIMM A"));
    }

    [Fact]
    public void Slot_nunca_negativo_quando_o_wmi_conta_mais_pentes_que_slots()
    {
        var raw = SystemInfoParser.Parse("""{"mem":{"slots":1,"modules":[{"cap":1073741824},{"cap":1073741824}]}}""");
        Assert.Equal("1 no total · 2 ocupados · 0 livres", Value(SystemInfoBuilder.Build(raw, null, "", [], null, [], ""), "mem", "Slots"));
    }

    [Fact]
    public void Sistema_operacional_com_tpm_inicializacao_segura_e_ativacao()
    {
        var s = Build();
        Assert.Equal("Ativado", Value(s, "os", "Ativação"));
        Assert.Equal("Ativada", Value(s, "os", "Inicialização segura"));
        Assert.Equal("2.0", Value(s, "os", "TPM"));
        Assert.Equal("23H2 (compilação 22631.3737)", Value(s, "os", "Versão"));
        Assert.Equal("UEFI", Value(s, "os", "Firmware"));
        var noTpm = SystemInfoParser.Parse("""{"os":{"caption":"Windows"}}""");
        Assert.Equal("Não informado (exige administrador)", Value(SystemInfoBuilder.Build(noTpm, null, "", [], null, [], ""), "os", "TPM"));
        var absent = SystemInfoParser.Parse("""{"os":{"caption":"Windows","secureboot":false,"lic":0},"tpm":{"present":false}}""");
        var s2 = SystemInfoBuilder.Build(absent, null, "", [], null, [], "");
        Assert.Equal("Não encontrado", Value(s2, "os", "TPM"));
        Assert.Equal("Desativada", Value(s2, "os", "Inicialização segura"));
        Assert.Equal("Não ativado", Value(s2, "os", "Ativação"));
    }

    [Fact]
    public void Video_mostra_a_memoria_de_verdade_e_o_driver()
    {
        var s = Build();
        Assert.Equal("NVIDIA GeForce RTX 3060", Value(s, "gpu", "Placa de vídeo"));
        Assert.StartsWith("6", Value(s, "gpu", "Memória de vídeo"));
        Assert.Equal("31.0.15.5222 · 2024-05-01", Value(s, "gpu", "Driver"));
    }

    [Fact]
    public void Disco_com_saude_desgaste_temperatura_e_previsao_de_falha()
    {
        var s = Build();
        var disk = Value(s, "disk", "NVMe 512GB");
        Assert.Contains("SSD NVMe", disk);
        Assert.Contains("desgaste 4%", disk);
        Assert.Contains("41 °C", disk);
        Assert.Equal("Nenhum disco prevê falha", Value(s, "disk", "Previsão de falha (SMART)"));
        var failing = SystemInfoParser.Parse("""{"disk":[{"name":"D","size":1}],"predict":true}""");
        Assert.StartsWith("Algum disco prevê falha", Value(SystemInfoBuilder.Build(failing, null, "", [], null, [], ""), "disk", "Previsão de falha (SMART)"));
        var unknown = SystemInfoParser.Parse("""{"disk":[{"name":"D","size":1}]}""");
        Assert.StartsWith("Não informada", Value(SystemInfoBuilder.Build(unknown, null, "", [], null, [], ""), "disk", "Previsão de falha (SMART)"));
    }

    [Theory]
    [InlineData(48.5, true)]
    [InlineData(0, false)]          // sensor sem ligação
    [InlineData(-273.1, false)]     // kelvin zerado
    [InlineData(125, false)]
    [InlineData(124.9, true)]
    [InlineData(5000, false)]       // décimos de kelvin sem converter
    public void Temperatura_plausivel(double celsius, bool ok) => Assert.Equal(ok, SystemInfoBuilder.PlausibleTemp(celsius));

    [Fact]
    public void Temperaturas_juntam_zona_termica_disco_e_nvidia_sem_leituras_absurdas()
    {
        var s = Build(extra: [new RawTemp("NVIDIA", "GeForce RTX 3060", 57)]);
        var lines = s.Single(x => x.Key == "temp").Lines;
        Assert.Contains(lines, l => l.Label == "TZ00_0 (ACPI)" && l.Value == "48 °C");
        Assert.Contains(lines, l => l.Label.StartsWith("GeForce RTX 3060") && l.Value == "57 °C");
        Assert.Contains(lines, l => l.Label == "NVMe 512GB" && l.Value == "41 °C");
        Assert.DoesNotContain(lines, l => l.Label.Contains("TZ01_0") || l.Label.Contains("TZ02_0"));
        Assert.Equal(3, lines.Count);
    }

    [Theory]
    [InlineData(1, "2,4 GHz")]
    [InlineData(11, "2,4 GHz")]
    [InlineData(36, "5 GHz")]
    [InlineData(149, "5 GHz")]
    [InlineData(0, "")]
    [InlineData(200, "")]           // 6 GHz repete números: não afirma
    public void Faixa_do_wifi_pelo_canal(int channel, string band) => Assert.Equal(band, SystemInfoBuilder.BandOfChannel(channel));

    [Fact]
    public void Rede_com_wifi_e_sem_endereco_mac()
    {
        var s = Build(wifi: new WifiInfo("MinhaCasa", 82, 44, "5 GHz", "802.11ax (Wi-Fi 6)", 866, 866));
        var net = s.Single(x => x.Key == "net").Lines;
        Assert.Contains(net, l => l.Label == "Wi-Fi" && l.Value.Contains("conectado") && l.Value.Contains("866 Mbps"));
        Assert.Contains(net, l => l.Label.Trim() == "IP" && l.Value == "192.168.0.12");
        Assert.Contains(net, l => l.Label.Trim() == "Gateway" && l.Value == "192.168.0.1");
        Assert.Equal("MinhaCasa", Value(s, "net", "Wi-Fi: rede"));
        Assert.Equal("82%", Value(s, "net", "Wi-Fi: sinal"));
        Assert.Equal("5 GHz · canal 44", Value(s, "net", "Wi-Fi: faixa"));
        Assert.DoesNotContain(s.SelectMany(x => x.Lines), l => System.Text.RegularExpressions.Regex.IsMatch(l.Value, "([0-9A-F]{2}[-:]){5}[0-9A-F]{2}"));
    }

    [Fact]
    public void Lista_longa_de_dispositivos_e_cortada_com_a_contagem()
    {
        var many = Enumerable.Range(1, 30).Select(i => $"Dispositivo {i}").ToList();
        string text = SystemInfoBuilder.List(many);
        Assert.StartsWith("30 itens:", text);
        Assert.EndsWith("…", text);
        Assert.DoesNotContain("Dispositivo 13", text);
        Assert.Equal("a; b", SystemInfoBuilder.List(["a", "b"]));
        Assert.Equal("", SystemInfoBuilder.List([]));
    }

    // ---------- exportação ----------

    [Fact]
    public void Texto_html_e_pdf_trazem_as_mesmas_secoes()
    {
        var s = Build();
        var when = new DateTime(2026, 10, 22, 9, 30, 0);
        string txt = SystemInfoExport.Text(s, "PC-TESTE", when, "rodapé");
        Assert.Contains("Memória", txt);
        Assert.Contains("Processador: Intel(R) Core(TM) i5-1235U", txt);
        Assert.Contains("2026-10-22 09:30", txt);
        string html = SystemInfoExport.Html(s, "PC-TESTE", when, "rodapé");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<title>Informações do sistema · Notebook Doctor</title>", html);
        Assert.Contains("<h2>Processador</h2>", html);
        Assert.DoesNotContain("<script", html);
        var pdf = SimplePdf.Build(SystemInfoExport.PdfLines(s, "PC-TESTE", when, "rodapé"), "t");
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.Equal(s.Count, SystemInfoExport.PdfLines(s, "PC", when, "r").Count(l => l.Style == PdfStyle.Heading));
    }

    [Fact]
    public void Html_escapa_o_que_vem_do_computador()
    {
        var evil = new List<InfoSection> { new("os", "Sistema <b>", [new InfoLine("Nome", "<script>alert(1)</script> & \"aspas\"")]) };
        string html = SystemInfoExport.Html(evil, "PC<x>", DateTime.Now, "r");
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<b>", html.Replace("<body", "").Split("<main>")[1].Split("<h2>")[1].Split("</h2>")[0]);
    }

    [Fact]
    public void Resumo_curto_tem_o_essencial_e_nenhum_identificador()
    {
        string summary = SystemInfoExport.Summary(Build(), "PC-TESTE");
        Assert.Contains("PC-TESTE", summary);
        Assert.Contains("Intel(R) Core(TM) i5-1235U", summary);
        Assert.Contains("Memória:", summary);
        Assert.Contains("NVIDIA GeForce RTX 3060", summary);
        Assert.Contains("Disco: NVMe 512GB", summary);
        Assert.DoesNotContain("192.168", summary);                 // IP não vai no resumo
        Assert.True(summary.Split('\n').Length < 12);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SystemProbeIntegrationTests
{
    [WindowsFact]
    public void Leituras_de_baixo_nivel_nunca_lancam_erro_e_a_smbios_real_e_lida()
    {
        var raw = SystemProbe.ReadSmbios();
        if (raw is not null)
        {
            var data = SmbiosParser.Parse(raw);
            Assert.Matches(@"^\d+\.\d+$", data.Version);                  // o cabeçalho de verdade tem a versão da SMBIOS
            Assert.All(data.Devices, d => Assert.True(d.SizeBytes >= 0));
        }
        Assert.Contains(SystemProbe.FirmwareKind(), new[] { "UEFI", "BIOS legado", "" });
        Assert.NotNull(SystemProbe.Adapters());
        var wifi = SystemProbe.Wifi();                                    // null no runner, que não tem Wi-Fi
        if (wifi is not null) Assert.InRange(wifi.SignalPct, 0, 100);
        Assert.All(SystemProbe.NvidiaTemperatures(), t => Assert.True(SystemInfoBuilder.PlausibleTemp(t.Celsius)));
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SystemInfoScriptIntegrationTests
{
    [WindowsFact]
    public void O_script_do_powershell_roda_e_traz_o_basico_do_computador()
    {
        var raw = SystemInfoScript.Run();
        Assert.NotEqual("", raw.OsName);                 // se o script tivesse erro de sintaxe, tudo viria vazio
        Assert.NotEmpty(raw.Cpus);
        Assert.NotEqual("", raw.Cpus[0].Name);
        var sections = SystemInfoBuilder.Build(raw, null, "", [], null, [], "CI");
        Assert.Contains(sections, s => s.Key == "os");
        Assert.Contains(sections, s => s.Key == "cpu");
    }
}
