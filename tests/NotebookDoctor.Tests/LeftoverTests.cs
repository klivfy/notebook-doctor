using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NotebookDoctor.Tests;

public class ProgramNamesTests
{
    [Theory]
    [InlineData("7-Zip 24.08 (x64)", "7-Zip")]
    [InlineData("Mozilla Firefox (x64 pt-BR)", "Mozilla Firefox")]
    [InlineData("Python 3.12.1 (64-bit)", "Python")]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33130", "Microsoft Visual C++ 2015-2022 Redistributable")]
    [InlineData("VLC media player", "VLC media player")]
    [InlineData("Foo Editor v2.1", "Foo Editor")]
    public void Produto_sem_versao_e_arquitetura(string name, string expected) => Assert.Equal(expected, ProgramNames.Product(name));

    [Theory]
    [InlineData("Spotify AB", "Spotify")]
    [InlineData("Google LLC", "Google")]
    [InlineData("Adobe Inc.", "Adobe")]
    [InlineData("Acme Software, Inc.", "Acme Software")]
    [InlineData("Valve", "Valve")]
    public void Editor_sem_sufixo_de_empresa(string publisher, string expected) => Assert.Equal(expected, ProgramNames.Publisher(publisher));

    [Fact]
    public void Apelidos_do_editor()
    {
        Assert.Equal(["Adobe Systems", "Adobe"], ProgramNames.PublisherAliases("Adobe Systems Incorporated"));
        Assert.Equal(["Valve"], ProgramNames.PublisherAliases("Valve Corporation"));
        Assert.Empty(ProgramNames.PublisherAliases("Microsoft Corporation")); // genérico: nunca guia a varredura
        Assert.Empty(ProgramNames.PublisherAliases(""));
    }

    [Theory]
    [InlineData("7-Zip", "7zip", true)]
    [InlineData("Foo Editor", "foo_editor", true)]
    [InlineData("Foo", "Food", false)]
    [InlineData("ab", "ab", false)] // curto demais
    public void Comparacao_de_nomes(string a, string b, bool same) => Assert.Equal(same, ProgramNames.Same(a, b));

    [Theory]
    [InlineData("Setup", false)]
    [InlineData("Microsoft", false)]
    [InlineData("Common Files", true)]
    [InlineData("Foo Editor", true)]
    [InlineData("VLC", true)]
    public void Nome_utilizavel(string name, bool usable) => Assert.Equal(usable, ProgramNames.Usable(name));

    [Theory]
    [InlineData("VLC media player", "VLC")]
    [InlineData("Microsoft Edge", null)]
    [InlineData("Spotify", null)] // uma palavra só: já é o produto
    public void Primeira_palavra(string product, string? expected) => Assert.Equal(expected, ProgramNames.FirstWord(product));
}

public class UninstallerTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App\unins000.exe", "", "", InstallerEngine.InnoSetup)]
    [InlineData(@"C:\Program Files\App\uninstall.exe", "", "This installation was built with Inno Setup.", InstallerEngine.InnoSetup)]
    [InlineData(@"C:\Program Files\App\Uninstall.exe", "", "Nullsoft Install System", InstallerEngine.Nsis)]
    [InlineData("msiexec.exe", "/x {GUID}", "", InstallerEngine.Msi)]
    [InlineData(@"C:\Users\ana\AppData\Local\Discord\Update.exe", "--uninstall", "", InstallerEngine.Squirrel)]
    [InlineData(@"C:\ProgramData\Package Cache\{X}\setup.exe", "/uninstall", "", InstallerEngine.WixBurn)]
    [InlineData(@"C:\Program Files\InstallShield Installation Information\{X}\setup.exe", "-runfromtemp -l0x0409", "", InstallerEngine.InstallShield)]
    [InlineData(@"C:\Program Files\App\uninstall.exe", "", "", InstallerEngine.Unknown)]
    public void Tipo_do_desinstalador(string file, string args, string version, InstallerEngine expected) =>
        Assert.Equal(expected, InstallerDetection.Detect(file, args, version));

    [Fact]
    public void Modo_silencioso_so_quando_confiavel()
    {
        Assert.Equal("/S", InstallerDetection.SilentArguments(InstallerEngine.Nsis));
        Assert.Contains("/VERYSILENT", InstallerDetection.SilentArguments(InstallerEngine.InnoSetup));
        Assert.Null(InstallerDetection.SilentArguments(InstallerEngine.InstallShield));
        Assert.Null(InstallerDetection.SilentArguments(InstallerEngine.Unknown));
    }

    [Theory]
    [InlineData(0, UninstallResult.Ok)]
    [InlineData(3010, UninstallResult.RebootNeeded)]
    [InlineData(1641, UninstallResult.RebootNeeded)]
    [InlineData(1605, UninstallResult.NotInstalled)]
    [InlineData(1602, UninstallResult.Canceled)]
    [InlineData(1618, UninstallResult.Busy)]
    [InlineData(1603, UninstallResult.Failed)]
    public void Codigos_do_msiexec(int code, UninstallResult expected) => Assert.Equal(expected, MsiExit.Result(code));

    [Fact]
    public void Arvore_adota_a_copia_temporaria_antes_de_o_original_fechar()
    {
        var tracked = new HashSet<int> { 100 };
        // O original (100) abriu a cópia (200) e fechou antes da próxima leitura
        Assert.Equal(1, ProcessTree.Step(tracked, [new ProcInfo(200, 100), new ProcInfo(4, 0)]));
        Assert.Equal([200], tracked);
        // Um processo novo que reaproveita o PID 100 não entra
        Assert.Equal(1, ProcessTree.Step(tracked, [new ProcInfo(200, 100), new ProcInfo(100, 4), new ProcInfo(300, 100)]));
        Assert.Equal([200], tracked);
        // Netos entram; quando todos fecham, sobra zero
        Assert.Equal(2, ProcessTree.Step(tracked, [new ProcInfo(200, 100), new ProcInfo(201, 200)]));
        Assert.Equal(0, ProcessTree.Step(tracked, [new ProcInfo(4, 0)]));
    }

    [Fact]
    public void Espera_alguns_segundos_sem_processos_antes_de_dar_por_terminado()
    {
        var t0 = Make.T0;
        var watch = new UninstallWatch(t0, TimeSpan.FromSeconds(5), TimeSpan.FromHours(2));
        Assert.Equal(WaitState.Running, watch.Observe(t0.AddSeconds(1), 2));
        Assert.Equal(WaitState.Settling, watch.Observe(t0.AddSeconds(2), 0));
        Assert.Equal(WaitState.Running, watch.Observe(t0.AddSeconds(4), 1)); // um filho apareceu: volta a esperar
        Assert.Equal(WaitState.Settling, watch.Observe(t0.AddSeconds(5), 0));
        Assert.Equal(WaitState.Done, watch.Observe(t0.AddSeconds(10), 0));
        Assert.Equal(WaitState.TimedOut, watch.Observe(t0.AddHours(3), 1));
    }

    [Fact]
    public void Retrato_guarda_as_pastas_antes_de_desinstalar()
    {
        var p = new InstalledProgram
        {
            Id = "k", Name = "Foo Editor 2.1 (x64)", Publisher = "Acme Software Inc.", InstallLocation = @"C:\Program Files\Acme\Foo Editor\",
            DisplayIcon = @"C:\Program Files\Acme\Foo Editor\bin\foo.exe,0", Kind = ProgramKind.Program, RegistryPath = "HKEY_X",
        };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Program Files\Acme\Foo Editor", @"C:\Program Files\Acme\Foo Editor\bin" };
        var fp = ProgramFingerprint.Of(p, dirs.Contains, _ => false);
        Assert.Equal("Foo Editor", fp.Product);
        Assert.Equal(["Acme Software", "Acme"], fp.Publishers);
        Assert.Equal([@"C:\Program Files\Acme\Foo Editor"], fp.Dirs); // a do ícone está dentro: a mesma pasta via InstallLocation
    }
}

public class UninstallQueueTests
{
    private const string Guid = "{23170F69-40C1-2702-2408-000001000000}";

    [Fact]
    public void Msi_vai_com_qn()
    {
        var p = new InstalledProgram { Kind = ProgramKind.Msi, ProductCode = Guid, UninstallString = "MsiExec.exe /I" + Guid };
        Assert.Equal((new UninstallCommand("msiexec.exe", $"/x {Guid} /qn /norestart"), true), UninstallQueue.Plan(p, _ => false, ""));
    }

    [Fact]
    public void QuietUninstallString_tem_preferencia()
    {
        var p = new InstalledProgram { UninstallString = @"""C:\App\unins000.exe""", QuietUninstallString = @"""C:\App\unins000.exe"" /SILENT" };
        Assert.Equal((new UninstallCommand(@"C:\App\unins000.exe", "/SILENT"), true), UninstallQueue.Plan(p, _ => false, ""));
    }

    [Theory]
    [InlineData(@"""C:\App\unins000.exe""", "", @"C:\App\unins000.exe", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", true)]
    [InlineData(@"""C:\App\uninst.exe""", "Nullsoft", @"C:\App\uninst.exe", "/S", true)]
    [InlineData(@"""C:\App\uninst.exe"" /S", "Nullsoft", @"C:\App\uninst.exe", "/S", true)] // não repete o /S
    [InlineData(@"""C:\Users\ana\AppData\Local\Discord\Update.exe"" --uninstall", "", @"C:\Users\ana\AppData\Local\Discord\Update.exe", "--uninstall -s", true)]
    [InlineData(@"""C:\App\uninstall.exe""", "", @"C:\App\uninstall.exe", "", false)] // desconhecido: abre normal
    public void Argumentos_silenciosos_por_tipo(string uninstall, string version, string file, string args, bool silent)
    {
        var p = new InstalledProgram { UninstallString = uninstall };
        Assert.Equal((new UninstallCommand(file, args), silent), UninstallQueue.Plan(p, _ => false, version));
    }

    [Fact]
    public void Sem_comando_nao_entra_na_fila() => Assert.Null(UninstallQueue.Plan(new InstalledProgram(), _ => false, ""));

    [Fact]
    public void Dono_do_executavel_e_a_pasta_mais_funda()
    {
        var places = LeftoverScannerTests.Places;
        var suite = new InstalledProgram { Id = "suite", Name = "Acme Suite", InstallLocation = @"C:\Program Files\Acme" };
        var editor = new InstalledProgram { Id = "editor", Name = "Acme Editor", InstallLocation = @"C:\Program Files\Acme\Editor" };
        var bad = new InstalledProgram { Id = "bad", Name = "Instalador ruim", InstallLocation = @"C:\Program Files" };
        var kb = new InstalledProgram { Id = "kb", Name = "KB1", Kind = ProgramKind.Update };
        var all = new[] { suite, editor, bad, kb };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Program Files\Acme", @"C:\Program Files\Acme\Editor", @"C:\Program Files" };
        Assert.Same(editor, UninstallQueue.OwnerOf(@"C:\Program Files\Acme\Editor\editor.exe", all, places, dirs.Contains, _ => false));
        Assert.Same(suite, UninstallQueue.OwnerOf(@"C:\Program Files\Acme\tool.exe", all, places, dirs.Contains, _ => false));
        Assert.Null(UninstallQueue.OwnerOf(@"C:\Program Files\Outro\o.exe", all, places, dirs.Contains, _ => false)); // "C:\Program Files" não identifica
        Assert.Null(UninstallQueue.OwnerOf(@"C:\Windows\notepad.exe", all, places, dirs.Contains, _ => false));
    }

    [Fact]
    public void Busca_manual_usa_o_nome_e_a_pasta_escolhida()
    {
        var fp = ProgramFingerprint.Manual("  Foo Editor 2.1 ", @"C:\Program Files\Acme\Foo Editor\");
        Assert.Equal("Foo Editor", fp.Product);
        Assert.Equal([@"C:\Program Files\Acme\Foo Editor"], fp.Dirs);
        Assert.Empty(fp.Publishers);
        Assert.Equal("", fp.RegistryPath);
        Assert.Empty(ProgramFingerprint.Manual("Foo", null).Dirs);
    }
}

public class PathsTests
{
    [Theory]
    [InlineData(@"C:\App\x.dll", @"C:\App", true)]
    [InlineData(@"C:\App", @"C:\app\", true)]
    [InlineData(@"C:\Apple\x", @"C:\App", false)]
    [InlineData(@"C:\App", @"C:\App\x", false)]
    [InlineData("", @"C:\App", false)]
    public void Dentro_de(string path, string dir, bool under) => Assert.Equal(under, Paths.IsUnder(path, dir));

    [Fact]
    public void Folha_e_pai()
    {
        Assert.Equal("Foo", Paths.Leaf(@"C:\App\Foo\"));
        Assert.Equal(@"C:\App", Paths.Parent(@"C:\App\Foo"));
        Assert.Equal(@"HKEY_CURRENT_USER\Software", Paths.Parent(@"HKEY_CURRENT_USER\Software\Acme"));
    }
}

/// <summary>Registro e disco de mentira para testar a varredura em qualquer sistema.</summary>
internal sealed class FakeSystem : ILeftoverSystem
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeSystem Key(string path, params (string Name, string Value)[] values)
    {
        for (string k = path; k.Contains('\\'); k = Paths.Parent(k))
            if (!_keys.ContainsKey(k)) _keys[k] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values) _keys[path][name] = value;
        return this;
    }

    public FakeSystem Dir(string path)
    {
        for (string d = Paths.Clean(path); d.Length > 2; d = Paths.Parent(d)) _dirs.Add(d);
        return this;
    }

    public FakeSystem File(string path, long size = 100)
    {
        Dir(Paths.Parent(path));
        _files[path] = size;
        return this;
    }

    public IEnumerable<string> SubKeys(string key) => _keys.Keys.Where(k => Paths.Same(Paths.Parent(k), key)).Select(Paths.Leaf).ToList();
    public bool KeyExists(string key) => _keys.ContainsKey(key);
    public string? StringValue(string key, string? name) => _keys.TryGetValue(key, out var v) && v.TryGetValue(name ?? "", out var s) ? s : null;
    public IEnumerable<string> ValueNames(string key) => _keys.TryGetValue(key, out var v) ? v.Keys.Where(n => n.Length > 0).ToList() : [];
    public IEnumerable<string> Directories(string dir) => _dirs.Where(d => Paths.Same(Paths.Parent(d), dir)).ToList();
    public bool DirectoryExists(string dir) => _dirs.Contains(Paths.Clean(dir));
    public bool FileExists(string path) => _files.ContainsKey(path);
    public IEnumerable<string> Files(string dir) => _files.Keys.Where(f => Paths.IsUnder(f, dir)).ToList();
    public long Size(string path) => _files.TryGetValue(path, out long s) ? s : _files.Where(f => Paths.IsUnder(f.Key, path)).Sum(f => f.Value);
}

public class LeftoverScannerTests
{
    internal static readonly KnownPlaces Places = new(
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Program Files\Common Files", @"C:\Program Files (x86)\Common Files",
        @"C:\ProgramData", @"C:\Users\ana", @"C:\Users\ana\AppData\Roaming", @"C:\Users\ana\AppData\Local", @"C:\Users\ana\Desktop",
        @"C:\Users\Public\Desktop", @"C:\Users\ana\AppData\Roaming\Microsoft\Windows\Start Menu", @"C:\ProgramData\Microsoft\Windows\Start Menu",
        [@"C:\Users\ana\Documents", @"C:\Users\ana\Pictures", @"C:\Users\ana\OneDrive"]);

    private const string Install = @"C:\Program Files\Acme\Foo Editor";
    private const string HkcuRun = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppPaths = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\foo.exe";
    private const string Service = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FooUpdate";
    private const string Clsid = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{11111111-2222-3333-4444-555555555555}";
    private const string ProgId = @"HKEY_CURRENT_USER\Software\Classes\FooEditor.Document";
    private const string Uninstall = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FooEditor";

    private static ProgramFingerprint Foo(string install = Install, string name = "Foo Editor 2.1") =>
        new("foo", name, ProgramNames.Product(name), ProgramNames.PublisherAliases("Acme Software Inc."), "2.1", [install], null, Uninstall, ProgramKind.Program);

    private static FakeSystem Pc() => new FakeSystem()
        // Seguro: pasta de instalação e o que aponta para ela
        .File($@"{Install}\plugins\x.dll").File($@"{Install}\foo.exe", 5000)
        .Key(HkcuRun, ("FooTray", $@"""{Install}\footray.exe"" /min"), ("OutroApp", @"""C:\Program Files\Outro\outro.exe"""))
        .Key(AppPaths, ("", $@"{Install}\foo.exe"))
        .Key(Service, ("ImagePath", $@"""{Install}\fooupd.exe"""))
        .Key(Clsid + @"\InprocServer32", ("", $@"{Install}\shellext.dll"))
        .Key(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{99999999-0000-0000-0000-000000000000}\InprocServer32", ("", @"C:\Windows\System32\shell32.dll"))
        .Key(ProgId + @"\shell\open\command", ("", $@"""{Install}\foo.exe"" ""%1"""))
        .Key(@"HKEY_CURRENT_USER\Software\Classes\.foo", ("", "FooEditor.Document")) // a extensão em si não sai
        .Key(Uninstall, ("DisplayName", "Foo Editor 2.1"))
        // Moderado e avançado: nomes
        .File(@"C:\Users\ana\AppData\Roaming\Foo Editor\settings.json")
        .File(@"C:\Users\ana\AppData\Local\Acme\Foo Editor\cache.bin")
        .File(@"C:\ProgramData\Foo\log.txt")
        .Key(@"HKEY_CURRENT_USER\Software\Acme\Foo Editor", ("Tema", "escuro"))
        .Key(@"HKEY_LOCAL_MACHINE\SOFTWARE\Foo Editor", ("Versao", "2.1"))
        // Nunca: Microsoft, Documentos, pasta com nome só parecido
        .Dir(@"C:\Users\ana\AppData\Roaming\Microsoft\Foo Editor")
        .File(@"C:\Users\ana\Documents\Foo Editor\meu texto.docx")
        .Dir(@"C:\Users\ana\AppData\Roaming\Foo Editor Backup")
        .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Foo Editor");

    private static LeftoverContext Ctx(params InstalledProgram[] others) => new(Places, others,
        [new ScheduledTaskInfo(@"\Acme\Foo Update", "Foo Update", "Acme", $@"{Install}\fooupd.exe", true, false, false, false),
         new ScheduledTaskInfo(@"\Outra", "Outra", "X", @"C:\Program Files\Outro\o.exe", true, false, false, false)],
        [new ShortcutInfo(@"C:\Users\ana\Desktop\Foo Editor.lnk", $@"{Install}\foo.exe"),
         new ShortcutInfo(@"C:\Users\ana\Desktop\Outro.lnk", @"C:\Program Files\Outro\outro.exe")]);

    private static (LeftoverKind, string, LeftoverLevel) Row(Leftover l) => (l.Kind, l.Kind == LeftoverKind.RegistryValue ? $"{l.Path}|{l.ValueName}" : l.Path, l.Level);

    [Fact]
    public void Acha_cada_tipo_de_sobra_no_nivel_certo_e_nada_mais()
    {
        var found = LeftoverScanner.Scan(Foo(), Ctx(), Pc()).Select(Row).ToHashSet();
        var expected = new HashSet<(LeftoverKind, string, LeftoverLevel)>
        {
            (LeftoverKind.Folder, Install, LeftoverLevel.Safe),
            (LeftoverKind.RegistryValue, $"{HkcuRun}|FooTray", LeftoverLevel.Safe),
            (LeftoverKind.RegistryKey, AppPaths, LeftoverLevel.Safe),
            (LeftoverKind.Service, Service, LeftoverLevel.Safe),
            (LeftoverKind.RegistryKey, Clsid, LeftoverLevel.Safe),
            (LeftoverKind.RegistryKey, ProgId, LeftoverLevel.Safe),
            (LeftoverKind.RegistryKey, Uninstall, LeftoverLevel.Safe),
            (LeftoverKind.Task, @"\Acme\Foo Update", LeftoverLevel.Safe),
            (LeftoverKind.File, @"C:\Users\ana\Desktop\Foo Editor.lnk", LeftoverLevel.Safe),
            (LeftoverKind.Folder, @"C:\Users\ana\AppData\Roaming\Foo Editor", LeftoverLevel.Moderate),
            (LeftoverKind.Folder, @"C:\Users\ana\AppData\Local\Acme\Foo Editor", LeftoverLevel.Moderate),
            (LeftoverKind.RegistryKey, @"HKEY_CURRENT_USER\Software\Acme\Foo Editor", LeftoverLevel.Moderate),
            (LeftoverKind.RegistryKey, @"HKEY_LOCAL_MACHINE\SOFTWARE\Foo Editor", LeftoverLevel.Moderate),
            (LeftoverKind.Folder, @"C:\Program Files\Acme", LeftoverLevel.Advanced), // a pasta do editor que contém a de instalação
            (LeftoverKind.Folder, @"C:\Users\ana\AppData\Local\Acme", LeftoverLevel.Advanced),
            (LeftoverKind.Folder, @"C:\ProgramData\Foo", LeftoverLevel.Advanced),
            (LeftoverKind.RegistryKey, @"HKEY_CURRENT_USER\Software\Acme", LeftoverLevel.Advanced),
        };
        Assert.Equal(expected.OrderBy(x => x.ToString()), found.OrderBy(x => x.ToString()));
    }

    [Fact]
    public void Tipos_do_Windows_e_protocolos_tomados_pelo_programa_nao_saem_inteiros()
    {
        var pc = Pc()
            .Key(@"HKEY_CURRENT_USER\Software\Classes\http\shell\open\command", ("", $@"""{Install}\foo.exe"" ""%1"""))
            .Key(@"HKEY_CURRENT_USER\Software\Classes\txtfile\shell\open\command", ("", $@"""{Install}\foo.exe"" ""%1"""))
            .Key(@"HKEY_CURRENT_USER\Software\Classes\foo-link", ("URL Protocol", ""))
            .Key(@"HKEY_CURRENT_USER\Software\Classes\foo-link\shell\open\command", ("", $@"""{Install}\foo.exe"" ""%1"""));
        var paths = LeftoverScanner.Scan(Foo(), Ctx(), pc).Select(l => l.Path).ToList();
        Assert.Contains(ProgId, paths); // o tipo do próprio programa continua saindo
        Assert.DoesNotContain(paths, p => p.EndsWith(@"\Classes\http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.EndsWith(@"\Classes\txtfile", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.EndsWith(@"\Classes\foo-link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Marca_seguro_e_moderado_e_deixa_avancado_desmarcado()
    {
        var found = LeftoverScanner.Scan(Foo(), Ctx(), Pc());
        Assert.All(found, l => Assert.Equal(l.Level != LeftoverLevel.Advanced, l.Selected));
        Assert.True(found.Single(l => l.Path == Install).NeedsAdmin);
        Assert.False(found.Single(l => l.Path == @"C:\Users\ana\AppData\Roaming\Foo Editor").NeedsAdmin);
        Assert.True(found.Single(l => l.Kind == LeftoverKind.Service).NeedsAdmin);
        Assert.Equal(5100, found.Single(l => l.Path == Install).Bytes);
    }

    [Fact]
    public void Outro_programa_do_mesmo_editor_protege_a_pasta_e_a_chave_do_editor()
    {
        var viewer = new InstalledProgram { Id = "viewer", Name = "Acme Viewer", Publisher = "Acme Software Inc.", InstallLocation = @"C:\Program Files\Acme\Viewer" };
        var found = LeftoverScanner.Scan(Foo(), Ctx(viewer), Pc());
        Assert.DoesNotContain(found, l => l.Path == @"C:\Users\ana\AppData\Local\Acme" || l.Path == @"HKEY_CURRENT_USER\Software\Acme");
        Assert.Contains(found, l => l.Path == @"HKEY_CURRENT_USER\Software\Acme\Foo Editor"); // a do programa continua
    }

    [Fact]
    public void Pasta_de_instalacao_generica_nao_guia_a_varredura()
    {
        // Instalador que grava InstallLocation = "C:\Program Files": acharia tudo que está lá
        var found = LeftoverScanner.Scan(Foo(install: @"C:\Program Files\"), Ctx(), Pc());
        Assert.DoesNotContain(found, l => l.Level == LeftoverLevel.Safe && l.Path != Uninstall);
        Assert.DoesNotContain(found, l => l.Path == @"C:\Program Files");
    }

    [Fact]
    public void Pasta_dentro_de_outro_programa_instalado_fica()
    {
        var host = new InstalledProgram { Id = "host", Name = "Acme Suite", Publisher = "Outra", InstallLocation = @"C:\Program Files\Acme" };
        var found = LeftoverScanner.Scan(Foo(), Ctx(host), Pc());
        Assert.DoesNotContain(found, l => Paths.IsUnder(l.Path, @"C:\Program Files\Acme"));
        Assert.DoesNotContain(found, l => l.Kind == LeftoverKind.Service); // o caminho guia só quando a pasta é do programa
    }

    [Fact]
    public void Outra_versao_instalada_desliga_a_busca_pelo_nome()
    {
        var other = new InstalledProgram { Id = "foo3", Name = "Foo Editor 3.0", Publisher = "Acme Software Inc.", InstallLocation = @"C:\Program Files\Acme\Foo Editor 3" };
        var found = LeftoverScanner.Scan(Foo(), Ctx(other), Pc());
        Assert.DoesNotContain(found, l => l.Level != LeftoverLevel.Safe);
        Assert.Contains(found, l => l.Path == Install);
    }

    [Fact]
    public void Nome_generico_so_usa_o_caminho()
    {
        var found = LeftoverScanner.Scan(Foo(name: "Setup"), Ctx(), Pc().Dir(@"C:\ProgramData\Setup"));
        Assert.DoesNotContain(found, l => l.Path == @"C:\ProgramData\Setup");
        Assert.Contains(found, l => l.Path == Install);
    }

    [Fact]
    public void Pasta_com_documentos_comeca_desmarcada()
    {
        var pc = Pc().File(@"C:\Users\ana\AppData\Roaming\Foo Editor\exportados\relatorio.pdf");
        var data = LeftoverScanner.Scan(Foo(), Ctx(), pc).Single(l => l.Path == @"C:\Users\ana\AppData\Roaming\Foo Editor");
        Assert.True(data.MayHoldUserData);
        Assert.False(data.Selected);
        Assert.Contains("Pode ter arquivos seus", data.Notes);
    }

    [Fact]
    public void Niveis_e_ordem_de_remocao()
    {
        var found = LeftoverScanner.Scan(Foo(), Ctx(), Pc());
        Assert.All(LeftoverScanner.UpTo(found, LeftoverLevel.Safe), l => Assert.Equal(LeftoverLevel.Safe, l.Level));
        Assert.Equal(found.Count, LeftoverScanner.UpTo(found, LeftoverLevel.Advanced).Count());
        var order = LeftoverScanner.RemovalOrder(found);
        Assert.Equal(LeftoverKind.Service, order[0].Kind);
        Assert.Equal(LeftoverKind.Folder, order[^1].Kind);
        int key = order.FindIndex(l => l.Path == @"HKEY_CURRENT_USER\Software\Acme\Foo Editor");
        int parent = order.FindIndex(l => l.Path == @"HKEY_CURRENT_USER\Software\Acme");
        Assert.True(key < parent); // a mais funda antes
    }

    [Theory]
    [InlineData(@"C:\Program Files", false, true)]
    [InlineData(@"C:\Users", false, true)]                          // contém pastas conhecidas
    [InlineData(@"C:\Windows\Temp\x", false, true)]
    [InlineData(@"C:\Users\ana\Documents\Foo", false, true)]
    [InlineData(@"C:\Users\ana\Desktop\Foo", false, true)]          // pasta na Área de Trabalho
    [InlineData(@"C:\Users\ana\Desktop\Foo.lnk", true, false)]      // atalho pode sair
    [InlineData(@"C:\Users\ana\AppData\Local\Packages", false, true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Foo", false, false)]
    public void Pastas_protegidas(string path, bool shortcut, bool isProtected) =>
        Assert.Equal(isProtected, LeftoverRules.IsProtectedPath(path, Places, shortcut));

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software", true)]
    [InlineData(@"HKEY_CURRENT_USER", true)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft", true)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node", true)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID", true)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion", true)] // contém Run e Uninstall
    [InlineData(@"HKEY_CURRENT_USER\Software\Acme", false)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{1}", false)]
    public void Chaves_protegidas(string key, bool isProtected) =>
        Assert.Equal(isProtected, LeftoverRules.IsProtectedKey(key, LeftoverScanner.Containers, LeftoverScanner.SoftwareRoots));
}

public class BackupRulesTests
{
    [Fact]
    public void Id_ordena_pela_data_e_diz_o_programa() =>
        Assert.Equal("20261022-041500-foo-editor-2-1-a", BackupRules.NewId(new DateTime(2026, 10, 22, 4, 15, 0), "Foo Editor 2.1 (á)"));

    [Theory]
    [InlineData(@"C:\Program Files\App\a.dll", "C/Program Files/App/a.dll")]
    [InlineData(@"D:\Jogos\x\", "D/Jogos/x")]
    public void Nome_no_zip_e_volta(string path, string entry)
    {
        Assert.Equal(entry, BackupRules.EntryName(path));
        Assert.Equal(Paths.Clean(path), BackupRules.PathOfEntry(entry));
    }

    /// <summary>S3: o Desfazer como administrador não escreve fora da pasta restaurada nem lê .reg fora da sessão.</summary>
    [Theory]
    [InlineData("C/Apps/Velho/a.dll", @"C:\Apps\Velho\a.dll")]
    [InlineData("C/Apps/Velho/sub/b.txt", @"C:\Apps\Velho\sub\b.txt")]
    [InlineData("C/Apps/Velho/../../Windows/System32/x.dll", null)]
    [InlineData("C/Apps/Velho2/a.dll", null)]
    [InlineData("C/Windows/System32/x.dll", null)]
    [InlineData("D/Apps/Velho/a.dll", null)]
    public void Entrada_do_zip_so_volta_para_dentro_da_pasta(string entry, string? expected) =>
        Assert.Equal(expected, BackupRules.FolderTarget(entry, @"C:\Apps\Velho"));

    [Theory]
    [InlineData("001.reg", "001.reg")]
    [InlineData("tarefa.xml", "tarefa.xml")]
    [InlineData(@"..\..\outro.reg", null)]
    [InlineData(@"C:\Temp\malicioso.reg", null)]
    [InlineData("sub/x.reg", null)]
    [InlineData("..", null)]
    [InlineData("", null)]
    public void Arquivo_do_backup_e_so_um_nome_solto(string archive, string? expected) =>
        Assert.Equal(expected, BackupRules.ArchiveFile(archive));

    [Fact]
    public void Vencidos_por_idade_e_tamanho_mas_o_mais_novo_fica()
    {
        var now = Make.T0;
        var sessions = new List<(string, DateTime, long)>
        {
            ("novo", now.AddDays(-40), 3L << 30), // velho e grande, mas é o mais novo: fica
            ("b", now.AddDays(-41), 1),
            ("c", now.AddDays(-50), 1),
        };
        Assert.Equal(["b", "c"], BackupRules.Expired(sessions, now, BackupRules.MaxAge, BackupRules.MaxBytes));

        var recent = new List<(string, DateTime, long)> { ("a", now, 1L << 30), ("b", now.AddDays(-1), 1L << 30), ("c", now.AddDays(-2), 1L << 30) };
        Assert.Equal(["c"], BackupRules.Expired(recent, now, BackupRules.MaxAge, BackupRules.MaxBytes)); // passa de 2 GB
    }
}

/// <summary>Testes que mexem no registro e em arquivos de verdade: só rodam no Windows (job "windows" da CI).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Só roda no Windows (job windows da CI).";
    }
}

[SupportedOSPlatform("windows")]
public class UninstallIntegrationTests
{
    /// <summary>
    /// Cria um programa falso de verdade (pastas no perfil, chaves em HKCU, item de inicialização, App Paths, tipo de
    /// arquivo), apaga só a entrada de desinstalação, como um desinstalador que esquece o resto, e confere que a
    /// varredura acha exatamente o que é dele, que a remoção apaga com backup e que desfazer devolve tudo.
    /// </summary>
    [WindowsFact]
    public void Acha_apaga_e_restaura_as_sobras_de_um_programa_falso()
    {
        string tag = "NDTeste" + Guid.NewGuid().ToString("N")[..8];
        string product = tag + " App", publisher = tag + "Fabricante";
        var places = WindowsPlaces.Current();
        string install = Path.Combine(places.LocalPrograms, product);
        string roaming = Path.Combine(places.Roaming, product);
        string localPub = Path.Combine(places.Local, publisher);
        string localProd = Path.Combine(localPub, product);
        const string run = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
        string appPaths = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\{tag}.exe";
        string progId = $@"HKEY_CURRENT_USER\Software\Classes\{tag}.Document";
        string pubKey = $@"HKEY_CURRENT_USER\Software\{publisher}";
        string prodKey = $@"{pubKey}\{product}";
        string uninstall = $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{tag}";
        var store = new BackupStore(Path.Combine(Path.GetTempPath(), tag + "-backups"));
        string exe = Path.Combine(install, "app.exe");
        try
        {
            Directory.CreateDirectory(Path.Combine(install, "sub"));
            File.WriteAllText(exe, "MZ");
            File.WriteAllText(Path.Combine(install, "sub", "a.dat"), "dados");
            Directory.CreateDirectory(roaming);
            File.WriteAllText(Path.Combine(roaming, "config.ini"), "[a]");
            Directory.CreateDirectory(localProd);
            File.WriteAllText(Path.Combine(localProd, "cache.bin"), "c");
            using (var k = RegistryPaths.Create(prodKey)) k.SetValue("Opcao", 1, RegistryValueKind.DWord);
            using (var k = RegistryPaths.Create(run)) k.SetValue(tag, $"\"{exe}\" --bg");
            using (var k = RegistryPaths.Create(appPaths)) k.SetValue("", exe);
            using (var k = RegistryPaths.Create(progId + @"\shell\open\command")) k.SetValue("", $"\"{exe}\" \"%1\"");
            var key = new UninstallKey
            {
                KeyName = tag, RegistryPath = uninstall, DisplayName = product, Publisher = publisher, InstallLocation = install,
                UninstallString = $"\"{install}\\unins000.exe\"",
            };
            using (var k = RegistryPaths.Create(uninstall)) k.SetValue("DisplayName", product);
            var fp = ProgramFingerprint.Of(ProgramRules.FromKey(key, includeSystem: false)!, Directory.Exists, File.Exists);
            RegistryPaths.DeleteTree(uninstall); // o "desinstalador" só tirou a entrada

            var found = LeftoverScanner.Scan(fp, new LeftoverContext(places, [], [], []), new WindowsLeftoverSystem());

            Assert.All(found, l => Assert.Contains(tag, l.Display, StringComparison.OrdinalIgnoreCase)); // nada de outros programas
            var rows = found.Select(l => (l.Kind, l.Kind == LeftoverKind.RegistryValue ? l.ValueName : l.Path, l.Level)).ToHashSet();
            Assert.Contains((LeftoverKind.Folder, install, LeftoverLevel.Safe), rows);
            Assert.Contains((LeftoverKind.RegistryValue, tag, LeftoverLevel.Safe), rows);
            Assert.Contains((LeftoverKind.RegistryKey, appPaths, LeftoverLevel.Safe), rows);
            Assert.Contains((LeftoverKind.RegistryKey, progId, LeftoverLevel.Safe), rows);
            Assert.Contains((LeftoverKind.Folder, roaming, LeftoverLevel.Moderate), rows);
            Assert.Contains((LeftoverKind.Folder, localProd, LeftoverLevel.Moderate), rows);
            Assert.Contains((LeftoverKind.RegistryKey, prodKey, LeftoverLevel.Moderate), rows);
            Assert.Contains((LeftoverKind.Folder, localPub, LeftoverLevel.Advanced), rows);
            Assert.Contains((LeftoverKind.RegistryKey, pubKey, LeftoverLevel.Advanced), rows);
            Assert.Equal(9, found.Count);

            var chosen = found.Where(l => l.Selected).ToList();
            Assert.Equal(7, chosen.Count); // o avançado começa desmarcado
            string id;
            List<RemovalResult> results;
            using (var session = store.Begin(product))
            {
                id = session.Id;
                results = LeftoverRemover.Remove(chosen, session, isAdmin: false);
            }
            Assert.All(results, r => Assert.True(r.Ok, $"{r.Item.Display}: {r.Error}"));
            Assert.False(Directory.Exists(install));
            Assert.False(Directory.Exists(roaming));
            Assert.False(Directory.Exists(localProd));
            Assert.True(Directory.Exists(localPub)); // avançado não marcado: ficou
            Assert.Null(RegistryPaths.Open(prodKey));
            Assert.Null(RegistryPaths.Open(appPaths));
            Assert.Null(RegistryPaths.Open(progId));
            Assert.NotNull(RegistryPaths.Open(pubKey));
            using (var k = RegistryPaths.Open(run)) Assert.Null(k!.GetValue(tag));

            string msg = store.Restore(id);
            Assert.Contains("7", msg);
            Assert.Equal("dados", File.ReadAllText(Path.Combine(install, "sub", "a.dat")));
            Assert.Equal("[a]", File.ReadAllText(Path.Combine(roaming, "config.ini")));
            Assert.True(File.Exists(Path.Combine(localProd, "cache.bin")));
            using (var k = RegistryPaths.Open(prodKey)) Assert.Equal(1, k!.GetValue("Opcao"));
            using (var k = RegistryPaths.Open(run)) Assert.Equal($"\"{exe}\" --bg", k!.GetValue(tag));
            using (var k = RegistryPaths.Open(progId + @"\shell\open\command")) Assert.Equal($"\"{exe}\" \"%1\"", k!.GetValue(""));
            Assert.True(store.List().Single().Restored);
        }
        finally
        {
            foreach (var dir in new[] { install, roaming, localPub, store.Root })
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            foreach (var k in new[] { pubKey, appPaths, progId, uninstall }) RegistryPaths.DeleteTree(k);
            using (var k = RegistryPaths.Open(run, writable: true)) k?.DeleteValue(tag, throwOnMissingValue: false);
        }
    }
}
