using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>Drivers do fabricante: endereços oficiais, número de série e de onde cada driver deve vir.</summary>
public class OemDriversTests
{
    private static ChassisInfo Chassis(string mf, string model, string serial = "", string product = "", bool? portable = true) =>
        new(mf, model, serial, "", product, portable);

    private static HardwareInventory Inventory(ChassisInfo chassis, GpuInfo[]? gpus = null, NetworkAdapterInfo[]? nets = null, string[]? audio = null) =>
        new(chassis, new BiosInfo("Dell Inc.", "1.12.0", new DateTime(2024, 3, 12)), gpus ?? [], nets ?? [], audio ?? []);

    private static GpuInfo Gpu(string name, string pnp)
    {
        var id = OemRules.ParseDeviceId(pnp);
        var vendor = OemRules.VendorFromPci(id.VendorId);
        return new GpuInfo(name, vendor, "1.0", id, OemRules.IsDedicated(vendor, name));
    }

    // ---------- fabricante e identificadores ----------

    [Theory]
    [InlineData("Dell Inc.", SupportVendor.Dell)]
    [InlineData("Alienware", SupportVendor.Dell)]
    [InlineData("LENOVO", SupportVendor.Lenovo)]
    [InlineData("HP", SupportVendor.Hp)]
    [InlineData("Hewlett-Packard", SupportVendor.Hp)]
    [InlineData("ASUSTeK COMPUTER INC.", SupportVendor.Generic)]
    [InlineData("HPE Something", SupportVendor.Generic)]
    [InlineData("", SupportVendor.Generic)]
    public void Reconhece_o_fabricante(string manufacturer, SupportVendor expected) =>
        Assert.Equal(expected, OemRules.VendorFromManufacturer(manufacturer));

    [Fact]
    public void Le_o_identificador_pci()
    {
        var id = OemRules.ParseDeviceId(@"PCI\VEN_10DE&DEV_25A2&SUBSYS_0B291028&REV_A1\4&2B8260C3&0&0008");
        Assert.Equal("10DE", id.VendorId);
        Assert.Equal("25A2", id.DeviceId);
        Assert.Equal("0B291028", id.SubsystemId);
        Assert.Equal(SupportVendor.Nvidia, OemRules.VendorFromPci(id.VendorId));
        Assert.Equal("", OemRules.ParseDeviceId(@"USB\VID_0BDA&PID_8153\000001").VendorId);
    }

    [Theory]
    [InlineData(SupportVendor.Nvidia, "NVIDIA GeForce RTX 4060 Laptop GPU", true)]
    [InlineData(SupportVendor.Amd, "AMD Radeon RX 7600S", true)]
    [InlineData(SupportVendor.Amd, "AMD Radeon(TM) Graphics", false)]
    [InlineData(SupportVendor.Amd, "AMD Radeon 780M", false)]
    [InlineData(SupportVendor.Intel, "Intel(R) Arc(TM) A370M Graphics", true)]
    [InlineData(SupportVendor.Intel, "Intel(R) Arc(TM) Graphics", false)]
    [InlineData(SupportVendor.Intel, "Intel(R) Iris(R) Xe Graphics", false)]
    public void Separa_video_dedicado_do_integrado(SupportVendor vendor, string name, bool dedicated) =>
        Assert.Equal(dedicated, OemRules.IsDedicated(vendor, name));

    // ---------- número de série ----------

    [Theory]
    [InlineData("System Serial Number")]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("Default string")]
    [InlineData("0")]
    [InlineData("00000000")]
    [InlineData("XXXXXXXX")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Numero_de_serie_de_exemplo_nao_conta(string? serial)
    {
        Assert.True(OemRules.IsPlaceholderSerial(serial));
        Assert.Equal("", OemRules.SafeSerial(serial));
    }

    [Theory]
    [InlineData("abc1234", "ABC1234")]
    [InlineData(" PF2ABCDE \t", "PF2ABCDE")]
    [InlineData("5CD1234ABC", "5CD1234ABC")]
    public void Numero_de_serie_valido_fica_limpo(string serial, string expected) =>
        Assert.Equal(expected, OemRules.SafeSerial(serial));

    [Theory]
    [InlineData("ABC1234/../x")]
    [InlineData("ABC 1234")]
    [InlineData("ABC1234?a=1")]
    [InlineData("ABC1234&b")]
    [InlineData("ABC1234'; rm")]
    public void Numero_de_serie_com_caractere_estranho_e_descartado(string serial) =>
        Assert.Equal("", OemRules.SafeSerial(serial));

    // ---------- endereços ----------

    [Fact]
    public void Dell_abre_pela_service_tag_so_quando_pedido()
    {
        var c = Chassis("Dell Inc.", "Latitude 5440", "ABC1234");
        var com = OemLinks.ModelPageFor(c, withSerial: true)!;
        Assert.Equal("https://www.dell.com/support/home/pt-br/product-support/servicetag/ABC1234/drivers", com.Url);
        Assert.True(com.SendsSerial);

        var sem = OemLinks.ModelPageFor(c, withSerial: false)!;
        Assert.Equal("https://www.dell.com/support/home/pt-br?app=drivers", sem.Url);
        Assert.False(sem.SendsSerial);
        Assert.DoesNotContain("ABC1234", sem.Url);
    }

    [Fact]
    public void Dell_sem_numero_de_serie_abre_o_portal()
    {
        var page = OemLinks.ModelPageFor(Chassis("Dell Inc.", "Inspiron 15", "To be filled by O.E.M."), withSerial: true)!;
        Assert.Equal("https://www.dell.com/support/home/pt-br?app=drivers", page.Url);
        Assert.False(page.SendsSerial);
    }

    [Fact]
    public void Lenovo_usa_a_busca_pelo_numero_ou_pelo_tipo_de_maquina()
    {
        var c = Chassis("LENOVO", "ThinkPad T14 Gen 3", "PF2ABCDE", "21AH00BBBR");
        Assert.Equal("https://pcsupport.lenovo.com/br/pt/search?query=PF2ABCDE", OemLinks.ModelPageFor(c, true)!.Url);
        // Sem o número, o tipo de máquina (4 primeiros caracteres) leva ao modelo sem identificar o aparelho
        var sem = OemLinks.ModelPageFor(c, false)!;
        Assert.Equal("https://pcsupport.lenovo.com/br/pt/search?query=21AH", sem.Url);
        Assert.False(sem.SendsSerial);
    }

    [Fact]
    public void HP_nunca_leva_o_numero_no_endereco()
    {
        var page = OemLinks.ModelPageFor(Chassis("HP", "HP Pavilion Laptop 15", "5CD1234ABC"), withSerial: true)!;
        Assert.Equal("https://support.hp.com/br-pt/drivers", page.Url);
        Assert.False(page.SendsSerial);
    }

    [Fact]
    public void Fabricante_desconhecido_nao_tem_pagina_de_modelo() =>
        Assert.Null(OemLinks.ModelPageFor(Chassis("ASUSTeK COMPUTER INC.", "VivoBook", "K1N0CV12345678A"), true));

    [Theory]
    [InlineData("ThinkPad T14 Gen 3", "lsu")]
    [InlineData("IdeaPad 3 15ITL6", "vantage")]
    public void Lenovo_indica_o_aplicativo_da_linha(string model, string key) =>
        Assert.Equal(key, OemLinks.Route(SupportVendor.Lenovo, model).Tool!.Key);

    [Theory]
    [InlineData("HP EliteBook 840 G9", "hpia")]
    [InlineData("HP ProBook 450 G8", "hpia")]
    [InlineData("HP Pavilion Laptop 15-eg0xxx", "hpsa")]
    public void HP_indica_o_aplicativo_da_linha(string model, string key) =>
        Assert.Equal(key, OemLinks.Route(SupportVendor.Hp, model).Tool!.Key);

    [Fact]
    public void Todo_endereco_montado_e_oficial()
    {
        foreach (var v in Enum.GetValues<SupportVendor>())
        {
            var route = OemLinks.Route(v, "ThinkPad");
            Assert.True(OemLinks.IsOfficial(route.PortalUrl), route.PortalUrl);
        }
        foreach (var t in OemLinks.AllTools) Assert.True(OemLinks.IsOfficial(t.PageUrl), t.PageUrl);
        Assert.False(OemLinks.IsOfficial("http://www.dell.com/support"));
        Assert.False(OemLinks.IsOfficial("https://dell.com.example.org/drivers"));
        Assert.False(OemLinks.IsOfficial("https://evil-dell.com/"));
        Assert.False(OemLinks.IsOfficial("file:///C:/Windows/System32/cmd.exe"));
    }

    [Fact]
    public void Ids_do_winget_passam_na_validacao()
    {
        foreach (var t in OemLinks.AllTools.Where(t => t.WingetId.Length > 0))
            Assert.Contains("install", WingetCommands.Install(t.WingetId, true, true));
    }

    [Fact]
    public void Instalar_pelo_winget_so_aceita_termos_quando_a_pessoa_aceitou()
    {
        var sem = WingetCommands.Install("Dell.CommandUpdate", false, false);
        Assert.DoesNotContain("--accept-source-agreements", sem);
        Assert.DoesNotContain("--accept-package-agreements", sem);
        Assert.Equal(["install", "--id", "Dell.CommandUpdate", "--exact", "--source", "winget", "--silent", "--disable-interactivity",
            "--accept-source-agreements", "--accept-package-agreements"], WingetCommands.Install("Dell.CommandUpdate", true, true));
        Assert.Throws<ArgumentException>(() => WingetCommands.Install("x; calc", true, true));
    }

    // ---------- regras ----------

    [Fact]
    public void Firmware_chipset_e_touchpad_so_do_fabricante_do_notebook()
    {
        var recs = OemAdvisor.Recommend(Inventory(Chassis("Dell Inc.", "Latitude 5440")));
        foreach (var cat in new[] { DriverCategory.BiosFirmware, DriverCategory.ChipsetAcpi, DriverCategory.Peripherals })
        {
            var r = Assert.Single(recs, r => r.Category == cat);
            Assert.Equal(RecommendationAuthority.OemMandatory, r.Authority);
            Assert.Equal(SupportVendor.Dell, r.Source);
            Assert.StartsWith("https://www.dell.com/", r.DirectUrl);
            Assert.True(r.RequiresRestorePoint);
        }
        Assert.Equal(RiskLevel.Critical, recs[0].Risk); // a BIOS vem primeiro
        Assert.Equal(DriverCategory.BiosFirmware, recs[0].Category);
        Assert.Contains("ventoinhas", recs.Single(r => r.Category == DriverCategory.ChipsetAcpi).Notes);
    }

    [Fact]
    public void Endereco_das_regras_nunca_leva_o_numero_de_serie()
    {
        var recs = OemAdvisor.Recommend(Inventory(Chassis("Dell Inc.", "Latitude 5440", "ABC1234")));
        Assert.All(recs, r => Assert.DoesNotContain("ABC1234", r.DirectUrl));
    }

    [Fact]
    public void Desktop_nao_recebe_touchpad()
    {
        var recs = OemAdvisor.Recommend(Inventory(Chassis("Dell Inc.", "OptiPlex 7010", portable: false)));
        Assert.DoesNotContain(recs, r => r.Category == DriverCategory.Peripherals);
    }

    [Fact]
    public void Video_dedicado_nvidia_e_amd_vem_do_fabricante_da_placa()
    {
        var inv = Inventory(Chassis("LENOVO", "Legion 5"), [
            Gpu("NVIDIA GeForce RTX 4060 Laptop GPU", @"PCI\VEN_10DE&DEV_28E0&SUBSYS_3C7A17AA"),
            Gpu("AMD Radeon RX 7600S", @"PCI\VEN_1002&DEV_7480&SUBSYS_3C7A17AA"),
        ]);
        var recs = OemAdvisor.Recommend(inv).Where(r => r.Category == DriverCategory.GpuDedicated).ToList();
        Assert.Equal(2, recs.Count);
        Assert.All(recs, r => Assert.Equal(RecommendationAuthority.ComponentPreferred, r.Authority));
        Assert.Equal("https://www.nvidia.com/pt-br/drivers/", recs.Single(r => r.Source == SupportVendor.Nvidia).DirectUrl);
        Assert.Equal("https://www.amd.com/pt/support/download/drivers.html", recs.Single(r => r.Source == SupportVendor.Amd).DirectUrl);
        Assert.All(recs, r => Assert.False(r.RequiresRestorePoint));
    }

    [Fact]
    public void Video_integrado_pode_vir_de_qualquer_um()
    {
        var inv = Inventory(Chassis("HP", "HP ProBook 450 G8"), [Gpu("Intel(R) Iris(R) Xe Graphics", @"PCI\VEN_8086&DEV_9A49&SUBSYS_8A9D103C")]);
        var r = Assert.Single(OemAdvisor.Recommend(inv), r => r.Category == DriverCategory.GpuIntegrated);
        Assert.Equal(RecommendationAuthority.DualChoice, r.Authority);
        Assert.Equal(SupportVendor.Intel, r.Source);
    }

    [Fact]
    public void Video_virtual_e_generico_ficam_de_fora()
    {
        var inv = Inventory(Chassis("Dell Inc.", "Latitude"), [
            Gpu("Microsoft Basic Display Adapter", @"PCI\VEN_1234&DEV_1111"),
            Gpu("VMware SVGA 3D", @"PCI\VEN_15AD&DEV_0405"),
        ]);
        Assert.DoesNotContain(OemAdvisor.Recommend(inv), r => r.Category is DriverCategory.GpuDedicated or DriverCategory.GpuIntegrated);
    }

    [Fact]
    public void Rede_intel_e_dupla_escolha_e_as_outras_vem_do_notebook()
    {
        var inv = Inventory(Chassis("Dell Inc.", "Latitude"), nets: [
            new NetworkAdapterInfo("Intel(R) Wi-Fi 6E AX211", SupportVendor.Intel, "23.30", OemRules.ParseDeviceId(@"PCI\VEN_8086&DEV_51F1")),
            new NetworkAdapterInfo("Realtek PCIe GbE Family Controller", SupportVendor.Generic, "10.6", OemRules.ParseDeviceId(@"PCI\VEN_10EC&DEV_8168")),
        ]);
        var nets = OemAdvisor.Recommend(inv).Where(r => r.Category == DriverCategory.Network).ToList();
        Assert.Equal(RecommendationAuthority.DualChoice, nets.Single(r => r.ComponentName.StartsWith("Intel")).Authority);
        Assert.Equal(RecommendationAuthority.OemMandatory, nets.Single(r => r.ComponentName.StartsWith("Realtek")).Authority);
    }

    [Fact]
    public void Audio_do_video_nao_conta_como_audio_do_notebook()
    {
        var inv = Inventory(Chassis("Dell Inc.", "Latitude"), audio: ["Realtek Audio", "Intel(R) Display Audio", "NVIDIA High Definition Audio"]);
        var r = Assert.Single(OemAdvisor.Recommend(inv), r => r.Category == DriverCategory.Audio);
        Assert.Equal("Realtek Audio", r.ComponentName);
        Assert.Equal(RecommendationAuthority.OemMandatory, r.Authority);
    }

    [Fact]
    public void Fabricante_desconhecido_manda_ao_windows_update()
    {
        var recs = OemAdvisor.Recommend(Inventory(Chassis("ASUSTeK COMPUTER INC.", "VivoBook")));
        var bios = recs.Single(r => r.Category == DriverCategory.BiosFirmware);
        Assert.Equal(SupportVendor.Generic, bios.Source);
        Assert.Equal(OemLinks.WindowsOptionalUpdates, bios.DirectUrl);
    }

    [Fact]
    public void Risco_alto_ou_critico_exige_ponto_de_restauracao()
    {
        var inv = Inventory(Chassis("Dell Inc.", "Latitude"), [Gpu("NVIDIA GeForce MX550", @"PCI\VEN_10DE&DEV_1F9F")], audio: ["Realtek Audio"]);
        Assert.All(OemAdvisor.Recommend(inv), r => Assert.Equal(r.Risk >= RiskLevel.High, r.RequiresRestorePoint));
    }

    [Theory]
    [InlineData(true, 2, RestoreReadiness.Ready)]
    [InlineData(true, 30, RestoreReadiness.CreateFirst)]
    [InlineData(false, 2, RestoreReadiness.ProtectionOff)]
    [InlineData(null, 2, RestoreReadiness.Unknown)]
    public void Estado_do_ponto_de_restauracao(bool? on, int hoursAgo, RestoreReadiness expected)
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0);
        Assert.Equal(expected, OemRules.Readiness(on, now.AddHours(-hoursAgo), now));
        Assert.Equal(RestoreReadiness.CreateFirst, OemRules.Readiness(true, DateTime.MinValue, now));
    }

    // ---------- inventário ----------

    [Fact]
    public void Le_o_json_do_script()
    {
        const string json = """
            {"cs":{"mf":"LENOVO","model":"21AH00BBBR","sku":"LENOVO_MT_21AH","serial":"PF2ABCDE","product":"21AH00BBBR","version":"ThinkPad T14 Gen 3","chassis":[10]},
             "bios":{"vendor":"LENOVO","ver":"R1XET52W (1.34 )","date":"2024-02-01","serial":"PF2ABCDE"},
             "gpu":{"name":"Intel(R) Iris(R) Xe Graphics","mf":"Intel Corporation","drv":"31.0.101.4502","pnp":"PCI\\VEN_8086&DEV_46A6&SUBSYS_22E617AA&REV_0C\\3&11583659&0&10"},
             "net":[{"name":"Intel(R) Wi-Fi 6E AX211 160MHz","mf":"Intel Corporation","drv":"23.30.0.6","pnp":"PCI\\VEN_8086&DEV_51F0&SUBSYS_00948086"}],
             "audio":"Realtek(R) Audio"}
            """;
        var inv = OemRules.ParseInventory(json);
        Assert.Equal(SupportVendor.Lenovo, inv.Oem);
        Assert.Equal("ThinkPad T14 Gen 3", inv.Chassis.Model); // nome comercial no lugar do tipo de máquina
        Assert.Equal("PF2ABCDE", inv.Chassis.SerialNumber);
        Assert.True(inv.Chassis.Portable);
        Assert.Equal(new DateTime(2024, 2, 1), inv.Bios.ReleaseDate);
        var gpu = Assert.Single(inv.Gpus); // um item sozinho vem como objeto, não lista
        Assert.Equal(SupportVendor.Intel, gpu.Vendor);
        Assert.False(gpu.IsDedicated);
        Assert.Single(inv.NetworkAdapters);
        Assert.Equal(["Realtek(R) Audio"], inv.Audio);
    }

    [Fact]
    public void Inventario_com_numero_de_serie_de_exemplo_e_texto_sujo()
    {
        const string json = """
            {"cs":{"mf":"To be filled by O.E.M.","model":"System\u0000 Product\r\nName","serial":"System Serial Number","chassis":[3]},
             "bios":{"serial":"Default string"}}
            """;
        var inv = OemRules.ParseInventory(json);
        Assert.Equal("", inv.Chassis.SerialNumber);
        Assert.Equal("System Product Name", inv.Chassis.Model);
        Assert.False(inv.Chassis.Portable);
        Assert.Equal(SupportVendor.Generic, inv.Oem);
        Assert.Empty(inv.Gpus);
    }

    [Fact]
    public void Inventario_vazio_nao_quebra() => Assert.Equal("", OemRules.ParseInventory("").Chassis.Manufacturer);

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Procura_os_aplicativos_nas_duas_pastas_de_programas()
    {
        var all = OemProbe.Candidates(@"C:\Program Files", @"C:\Program Files (x86)").ToList();
        Assert.Contains(all, c => c.Key == "dcu" && c.Path.EndsWith("dcu-cli.exe") && c.Path.Contains("(x86)"));
        Assert.Contains(all, c => c.Key == "dcu" && !c.Path.Contains("(x86)"));
        Assert.All(all, c => Assert.Contains(OemLinks.AllTools, t => t.Key == c.Key));
    }

    [Fact]
    public void Demonstracao_traz_um_notebook_completo()
    {
        var recs = OemAdvisor.Recommend(DemoData.OemInventory());
        Assert.Contains(recs, r => r.Category == DriverCategory.GpuDedicated);
        Assert.Contains(recs, r => r.Category == DriverCategory.GpuIntegrated);
        Assert.Contains(recs, r => r.Category == DriverCategory.Audio && r.ComponentName == "Realtek Audio");
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class OemProbeIntegrationTests
{
    [WindowsFact]
    public async Task O_script_do_inventario_roda_e_traz_o_fabricante()
    {
        var inv = await OemProbe.InventoryAsync();
        Assert.NotNull(inv); // erro de sintaxe no script devolveria null
        Assert.NotEqual("", inv.Chassis.Manufacturer);
        OemProbe.DetectTools();       // não pode lançar exceção
        OemProbe.SystemRestoreEnabled();
    }
}
