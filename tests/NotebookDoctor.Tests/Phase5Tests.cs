using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class AppTabsTests
{
    [Theory]
    [InlineData("programas", AppTabs.Programs)]
    [InlineData("Configurações", AppTabs.Settings)]
    [InlineData(" ESPAÇO ", AppTabs.Space)]
    [InlineData("historico", AppTabs.History)]
    [InlineData("0", AppTabs.Panel)]
    [InlineData("9", AppTabs.System)]
    [InlineData("10", AppTabs.Settings)]
    [InlineData("sistema", AppTabs.System)]
    public void Nome_ou_numero_viram_a_aba(string text, int expected) => Assert.Equal(expected, AppTabs.Parse(text));

    [Theory]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("downloads")]
    [InlineData("")]
    [InlineData(null)]
    public void Aba_desconhecida_e_nula(string? text) => Assert.Null(AppTabs.Parse(text));

    [Fact]
    public void Cada_nome_volta_para_a_propria_aba()
    {
        for (int i = 0; i < AppTabs.Names.Length; i++) Assert.Equal(i, AppTabs.Parse(AppTabs.Name(i)));
    }

    [Fact]
    public void Diagnostico_aponta_para_as_abas_certas_depois_de_Programas()
    {
        Assert.Equal(AppTabs.Startup, Diagnosis.TabStartup);
        Assert.Equal(AppTabs.Settings, Diagnosis.TabSettings);
        Assert.Equal(5, AppTabs.Startup);
    }
}

public class ProgramListTests
{
    private const string Hklm = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    private static UninstallKey Key(string name = "App", string key = "App", string? uninstall = @"""C:\Program Files\App\unins000.exe""",
                                    bool system = false, string? parent = null, bool msi = false, int? sizeKb = null, bool wow = false,
                                    bool machine = true, string? release = null, bool noRemove = false, string? version = "1.0") => new()
    {
        KeyName = key, RegistryPath = Hklm + key, MachineWide = machine, Wow64 = wow, DisplayName = name, DisplayVersion = version,
        Publisher = "Fabricante", UninstallString = uninstall, ParentKeyName = parent, SystemComponent = system, WindowsInstaller = msi,
        EstimatedSizeKb = sizeKb, ReleaseType = release, NoRemove = noRemove, InstallDate = "20260930",
    };

    [Fact]
    public void Programa_comum_aparece_com_os_dados_do_registro()
    {
        var p = ProgramRules.FromKey(Key(sizeKb: 2048, wow: true), includeSystem: false)!;
        Assert.Equal("App", p.Name);
        Assert.Equal(ProgramKind.Program, p.Kind);
        Assert.Equal(32, p.Bits);
        Assert.True(p.MachineWide);
        Assert.Equal(2048L * 1024, p.Bytes);
        Assert.Equal(SizeSource.Installer, p.SizeSource);
        Assert.Equal(new DateTime(2026, 9, 30), p.InstallDate);
        Assert.True(p.CanUninstall);
        Assert.Equal(Hklm + "App", p.Id);
    }

    [Fact]
    public void Do_usuario_nao_diz_a_arquitetura()
    {
        var p = ProgramRules.FromKey(Key(machine: false), false)!;
        Assert.False(p.MachineWide);
        Assert.Equal(0, p.Bits);
        Assert.Equal("—", p.BitsText);
    }

    [Theory]
    [InlineData(true, null, null, true)]            // SystemComponent=1
    [InlineData(false, "Office16", null, true)]     // atualização de outro programa
    [InlineData(false, null, "Security Update", true)]
    [InlineData(false, null, null, false)]
    public void Componentes_do_sistema_so_aparecem_quando_pedido(bool system, string? parent, string? release, bool hidden)
    {
        var key = Key(system: system, parent: parent, release: release);
        Assert.Equal(hidden, ProgramRules.FromKey(key, includeSystem: false) is null);
        var shown = ProgramRules.FromKey(key, includeSystem: true)!;
        Assert.Equal(hidden, shown.IsSystem);
    }

    [Fact]
    public void Sem_nome_ou_sem_como_desinstalar_fica_de_fora()
    {
        Assert.Null(ProgramRules.FromKey(Key(name: "  "), true));
        Assert.Null(ProgramRules.FromKey(Key(uninstall: null), false));
        Assert.True(ProgramRules.FromKey(Key(uninstall: null), true)!.IsSystem);
    }

    [Fact]
    public void NoRemove_mostra_mas_nao_desinstala() => Assert.False(ProgramRules.FromKey(Key(noRemove: true), false)!.CanUninstall);

    [Fact]
    public void Pacote_MSI_desinstala_com_msiexec_x()
    {
        const string guid = "{23170F69-40C1-2702-2408-000001000000}";
        var p = ProgramRules.FromKey(Key(key: guid, uninstall: "MsiExec.exe /I" + guid, msi: true), false)!;
        Assert.Equal(ProgramKind.Msi, p.Kind);
        Assert.Equal(guid, p.ProductCode);
        Assert.Equal(new UninstallCommand("msiexec.exe", "/x " + guid), ProgramRules.CommandFor(p, _ => false));
    }

    [Theory]
    [InlineData("{ABCDEF01-2345-6789-ABCD-EF0123456789}", "MsiExec.exe /X{abcdef01-2345-6789-abcd-ef0123456789}", false, "{ABCDEF01-2345-6789-ABCD-EF0123456789}")]
    [InlineData("App", "MsiExec.exe /X{abcdef01-2345-6789-abcd-ef0123456789}", false, "{ABCDEF01-2345-6789-ABCD-EF0123456789}")]
    [InlineData("{ABCDEF01-2345-6789-ABCD-EF0123456789}", @"C:\App\uninstall.exe", false, null)] // GUID no nome não basta: não é MSI
    [InlineData("{ABCDEF01-2345-6789-ABCD-EF0123456789}", null, true, "{ABCDEF01-2345-6789-ABCD-EF0123456789}")]
    public void Codigo_do_produto(string key, string? uninstall, bool windowsInstaller, string? expected) =>
        Assert.Equal(expected, ProgramRules.ProductCodeOf(key, uninstall, windowsInstaller));

    [Fact]
    public void Repetidos_viram_uma_linha_com_a_mais_completa()
    {
        var a = ProgramRules.FromKey(Key(key: "a", wow: true), false)!;
        var b = ProgramRules.FromKey(Key(key: "b", sizeKb: 10), false)!;
        var other = ProgramRules.FromKey(Key(key: "c", version: "2.0"), false)!;
        var list = ProgramRules.Dedupe([a, b, other]);
        Assert.Equal(2, list.Count);
        Assert.Contains(b, list);
        Assert.Contains(other, list);
    }

    [Theory]
    [InlineData("programs", ProgramKind.Msi, true)]
    [InlineData("programs", ProgramKind.Store, false)]
    [InlineData("store", ProgramKind.Store, true)]
    [InlineData("updates", ProgramKind.Update, true)]
    [InlineData("updates", ProgramKind.Program, false)]
    [InlineData("all", ProgramKind.Update, true)]
    [InlineData(null, ProgramKind.Program, true)]
    public void Filtro_por_tipo(string? tag, ProgramKind kind, bool shown) =>
        Assert.Equal(shown, ProgramRules.Matches(new InstalledProgram { Kind = kind }, ProgramRules.ParseFilter(tag)));
}

public class UninstallCommandTests
{
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\App Pro\uninstall.exe",
        @"C:\Program Files\Tool\remove tool.exe",
    };

    [Theory]
    [InlineData(@"""C:\Program Files\App\unins000.exe""", @"C:\Program Files\App\unins000.exe", "")]
    [InlineData(@"""C:\Program Files\App\unins000.exe"" /SILENT", @"C:\Program Files\App\unins000.exe", "/SILENT")]
    [InlineData(@"C:\Program Files\App Pro\uninstall.exe /x /y", @"C:\Program Files\App Pro\uninstall.exe", "/x /y")] // sem aspas: o caminho que existe
    [InlineData(@"C:\Program Files\Tool\remove tool.exe --all", @"C:\Program Files\Tool\remove tool.exe", "--all")]
    [InlineData(@"C:\Program Files\Outro\setup.exe /uninstall", @"C:\Program Files\Outro\setup.exe", "/uninstall")] // não existe: até o .exe
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2408-000001000000}", "MsiExec.exe", "/I{23170F69-40C1-2702-2408-000001000000}")]
    [InlineData(@"""C:\App\sem fim.exe /S", @"C:\App\sem fim.exe /S", "")] // aspas sem fechar
    [InlineData("rundll32.exe dfshim.dll,ShArpMaintain App.application", "rundll32.exe", "dfshim.dll,ShArpMaintain App.application")]
    public void Separa_programa_e_argumentos(string command, string file, string args) =>
        Assert.Equal(new UninstallCommand(file, args), ProgramRules.ParseCommand(command, Files.Contains));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sem_comando(string? command) => Assert.Null(ProgramRules.ParseCommand(command, _ => true));
}

public class ProgramDetailsTests
{
    [Theory]
    [InlineData(@"C:\App\app.exe,0", @"C:\App\app.exe", 0)]
    [InlineData(@"""C:\App\app.exe"",-101", @"C:\App\app.exe", -101)]
    [InlineData(@"C:\App\app.ico", @"C:\App\app.ico", 0)]
    [InlineData(@"C:\App, Inc\app.exe", @"C:\App, Inc\app.exe", 0)] // vírgula no caminho, sem índice
    public void Icone_e_indice(string spec, string path, int index) => Assert.Equal((path, index), ProgramRules.IconSource(spec));

    [Fact]
    public void Icone_vazio() => Assert.Null(ProgramRules.IconSource(" "));

    [Fact]
    public void Pasta_vem_da_instalacao_ou_do_icone_mas_nunca_do_Windows()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Program Files\App", @"C:\Windows\Installer", @"C:\Tools\Viewer" };
        bool Dir(string d) => dirs.Contains(d);

        var withLocation = new InstalledProgram { InstallLocation = @"C:\Program Files\App\" };
        Assert.Equal(@"C:\Program Files\App", ProgramRules.FolderOf(withLocation, Dir, _ => false));

        var fromIcon = new InstalledProgram { DisplayIcon = @"C:\Tools\Viewer\viewer.exe,0", UninstallString = @"C:\Windows\Installer\x.exe" };
        Assert.Equal(@"C:\Tools\Viewer", ProgramRules.FolderOf(fromIcon, Dir, _ => false));

        var msiOnly = new InstalledProgram { DisplayIcon = @"C:\Windows\Installer\{GUID}\icon.ico", UninstallString = "MsiExec.exe /X{GUID}" };
        Assert.Null(ProgramRules.FolderOf(msiOnly, Dir, _ => false));

        // Ícone numa pasta que vários programas do editor usam: não é a pasta deste programa
        dirs.Add(@"C:\Program Files\Common Files\Acme");
        var shared = new InstalledProgram { DisplayIcon = @"C:\Program Files\Common Files\Acme\icons.dll,3" };
        Assert.Null(ProgramRules.FolderOf(shared, Dir, _ => false));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App", true)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Windows\System32\Tool", false)]
    [InlineData(@"C:\Users\ana", false)]
    [InlineData(@"C:\Users\ana\AppData\Local\Programs\App", true)]
    public void So_mede_pasta_do_proprio_programa(string folder, bool ok) =>
        Assert.Equal(ok, ProgramRules.CanMeasure(folder, [@"C:\Program Files", @"C:\Users\ana", @"C:\Users\ana\AppData\Local\Programs"], @"C:\Windows"));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator", "Windows Calculator")]
    [InlineData("SpotifyAB.SpotifyMusic", "Spotify Music")]
    [InlineData("Clipchamp.Clipchamp", "Clipchamp")]
    [InlineData("Microsoft.ZuneMusic", "Zune Music")]
    public void Nome_do_pacote_legivel(string name, string expected) => Assert.Equal(expected, ProgramRules.FriendlyPackageName(name));

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft Corporation")]
    [InlineData("CN=Spotify AB, O=Spotify AB", "Spotify AB")]
    [InlineData("CN=24803D75-212C-471A-BC57-9EF86AB91435", "")]
    public void Editor_do_certificado(string subject, string expected) => Assert.Equal(expected, ProgramRules.PublisherOfCertificate(subject));

    [Theory]
    [InlineData(@"Computador\HKEY_CURRENT_USER\Software", @"Computador\HKEY_LOCAL_MACHINE\Software\X")]
    [InlineData(@"HKEY_CURRENT_USER\Software", @"HKEY_LOCAL_MACHINE\Software\X")]
    [InlineData(null, @"HKEY_LOCAL_MACHINE\Software\X")]
    public void Editor_do_registro_mantem_o_no_raiz(string? current, string expected) =>
        Assert.Equal(expected, ProgramRules.RegeditLastKey(current, @"HKEY_LOCAL_MACHINE\Software\X"));

    [Fact]
    public void Store_esconde_bibliotecas_e_apps_do_sistema()
    {
        StorePackage Pkg(string sig = "Store", bool fw = false, bool nr = false, string? display = "Spotify") =>
            new("SpotifyAB.SpotifyMusic", "SpotifyAB.SpotifyMusic_1.2_x64__zpdnekdrzrea0", "CN=Spotify AB, O=Spotify AB", "1.2", @"C:\Program Files\WindowsApps\Spotify",
                sig, fw, nr, "X64", display);
        var app = ProgramRules.FromStore(Pkg(), false)!;
        Assert.Equal("Spotify", app.Name);
        Assert.Equal("Spotify AB", app.Publisher);
        Assert.Equal(ProgramKind.Store, app.Kind);
        Assert.Equal(64, app.Bits);
        Assert.True(app.CanUninstall);
        Assert.Null(ProgramRules.FromStore(Pkg(fw: true), true));
        Assert.Null(ProgramRules.FromStore(Pkg(sig: "System"), false));
        Assert.False(ProgramRules.FromStore(Pkg(nr: true), true)!.CanUninstall);
        Assert.Equal("Spotify Music", ProgramRules.FromStore(Pkg(display: null), false)!.Name);
    }

    [Fact]
    public void Inventario_do_PowerShell()
    {
        const string json = """
            {"store":[{"n":"Microsoft.WindowsCalculator","f":"Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe","p":"CN=Microsoft Corporation, O=Microsoft Corporation","v":"11.2","l":"C:\\Program Files\\WindowsApps\\Calc","s":"Store","fw":false,"nr":false,"a":"X64","d":"Calculadora"},
                      {"n":"","f":"x"}],
             "kb":{"id":"KB5031455","d":"Security Update","t":"2026-10-14"}}
            """;
        var (store, updates) = ProgramRules.ParseInventory(json);
        var calc = Assert.Single(store);
        Assert.Equal("Calculadora", calc.DisplayName);
        Assert.Equal("Store", calc.SignatureKind);
        var kb = Assert.Single(updates); // um item só vem como objeto, não lista
        Assert.Equal(new DateTime(2026, 10, 14), kb.InstalledOn);
        var p = ProgramRules.FromHotfix(kb);
        Assert.Equal("KB5031455 — Security Update", p.Name);
        Assert.Equal(ProgramKind.Update, p.Kind);
        Assert.Null(p.MachineWide);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("[]")]
    public void Inventario_invalido_fica_vazio(string json)
    {
        var (store, updates) = ProgramRules.ParseInventory(json);
        Assert.Empty(store);
        Assert.Empty(updates);
    }

    [Fact]
    public void Propriedades_so_com_o_que_o_programa_informa()
    {
        var p = new InstalledProgram { Name = "App", Publisher = "", Version = "1.0", Kind = ProgramKind.Program, MachineWide = true, Bits = 64 };
        string text = ProgramRules.Describe(p);
        Assert.Contains("Nome: App", text);
        Assert.Contains("Versão: 1.0", text);
        Assert.Contains("Arquitetura: 64 bits", text);
        Assert.DoesNotContain("Editor:", text);
        Assert.DoesNotContain("Tamanho:", text); // sem tamanho
    }
}

public class ProgramExportTests
{
    private static readonly InstalledProgram App = new()
    {
        Name = "App; \"Pro\"", Publisher = "=cmd|'/c calc'!A1", Version = "1.0", Kind = ProgramKind.Program, MachineWide = true, Bits = 64,
        Bytes = 1024, SizeSource = SizeSource.Installer, InstallDate = new DateTime(2026, 9, 30), RegistryPath = @"HKEY_LOCAL_MACHINE\X",
    };

    [Fact]
    public void Csv_com_separador_do_idioma_aspas_e_sem_formulas()
    {
        string csv = ProgramExport.Csv([App], ";");
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Nome;Editor;Versão;Tamanho;Bytes;Instalado em", lines[0]);
        Assert.StartsWith("\"App; \"\"Pro\"\"\";'=cmd|'/c calc'!A1;1.0;", lines[1]);
        Assert.Contains(";1024;2026-09-30;", lines[1]);
        Assert.EndsWith(@";HKEY_LOCAL_MACHINE\X", lines[1]);
    }

    [Fact]
    public void Html_escapa_o_texto_e_nao_mostra_a_coluna_bytes()
    {
        string html = ProgramExport.Html([WithName("<script>")], "PC-ANA", new DateTime(2026, 10, 22, 9, 0, 0));
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<th>Bytes</th>", html);
        Assert.Contains("PC-ANA", html);
        Assert.Contains("1 item", html);
    }

    private static InstalledProgram WithName(string name) => new()
    {
        Name = name, Kind = App.Kind, Version = App.Version, Bytes = App.Bytes, SizeSource = App.SizeSource, RegistryPath = App.RegistryPath,
    };
}

public class ProgramTotalsTests
{
    [Fact]
    public void Soma_so_os_tamanhos_conhecidos()
    {
        string text = TableTotals.Programs([1L << 30, null, 1L << 30], 5);
        Assert.StartsWith("3 de 5 itens", text);
        Assert.Contains(Units.Bytes(2L << 30), text);
        Assert.Contains("(1 sem tamanho)", text);
    }

    [Fact]
    public void Sem_busca_e_todos_com_tamanho() => Assert.Equal($"2 itens · {Units.Bytes(3)}", TableTotals.Programs([1, 2], 2));
}
