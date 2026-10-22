using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace NotebookDoctor.Tests;

public class UsnJournalTests
{
    internal static byte[] Record(ulong file, ulong parent, long usn, uint reason, string name, bool dir = false)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        int length = (60 + nameBytes.Length + 7) & ~7;
        var b = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), file);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16), parent);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(24), usn);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40), reason);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52), dir ? 0x10u : 0x20u);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(56), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(58), 60);
        nameBytes.CopyTo(b, 60);
        return b;
    }

    private static byte[] Buffer(long next, params byte[][] records) =>
        BitConverter.GetBytes(next).Concat(records.SelectMany(r => r)).ToArray();

    [Fact]
    public void Le_os_registros_do_diario()
    {
        var (next, records) = UsnJournal.Parse(Buffer(900, Record(1, 5, 100, UsnJournal.FileCreate, "app.exe"), Record(2, 5, 120, UsnJournal.FileCreate, "Pasta", dir: true)));
        Assert.Equal(900, next);
        Assert.Equal(2, records.Count);
        Assert.Equal("app.exe", records[0].Name);
        Assert.Equal(5ul, records[1].ParentRef);
        Assert.Equal(0x10u, records[1].Attributes);
    }

    [Fact]
    public void Buffer_cortado_ou_vazio_nao_quebra()
    {
        var full = Buffer(1, Record(1, 5, 100, UsnJournal.FileCreate, "app.exe"));
        Assert.Empty(UsnJournal.Parse(full.AsSpan(0, full.Length - 4)).Records);
        Assert.Empty(UsnJournal.Parse([]).Records);
    }

    [Fact]
    public void Resumo_tira_temporarios_e_separa_criado_de_alterado()
    {
        var records = new[]
        {
            new UsnRecord(1, 5, 1, UsnJournal.FileCreate, 0x20, "app.exe"),
            new UsnRecord(1, 5, 2, UsnJournal.DataExtend | UsnJournal.Close, 0x20, "app.exe"),
            new UsnRecord(2, 5, 3, UsnJournal.FileCreate, 0x20, "setup.tmp"),
            new UsnRecord(2, 5, 4, UsnJournal.FileDelete | UsnJournal.Close, 0x20, "setup.tmp"),      // criado e apagado: sai
            new UsnRecord(3, 5, 5, UsnJournal.DataOverwrite, 0x20, "hosts"),                         // só alterado
            new UsnRecord(4, 5, 6, UsnJournal.Close, 0x20, "lido.txt"),                              // só lido: sai
            new UsnRecord(5, 9, 7, UsnJournal.FileCreate, 0x10, "App"),
            new UsnRecord(6, 5, 8, UsnJournal.FileCreate, 0x20, "a.tmp"),
            new UsnRecord(6, 5, 9, UsnJournal.RenameOldName, 0x20, "a.tmp"),
            new UsnRecord(6, 5, 10, UsnJournal.RenameNewName, 0x20, "config.ini"),                   // renomeado: vale o nome novo
        };
        var changes = UsnJournal.Summarize(records).OrderBy(c => c.FileRef).ToList();
        Assert.Equal(
            [new UsnChange(1, "app.exe", false, true), new UsnChange(3, "hosts", false, false), new UsnChange(5, "App", true, true), new UsnChange(6, "config.ini", false, true)],
            changes);
    }
}

public class TraceRulesTests
{
    private static readonly KnownPlaces Places = LeftoverScannerTests.Places;
    private const string Soft = @"HKEY_LOCAL_MACHINE\SOFTWARE";
    private const string Uninstall = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FooEditor";

    private static RegistrySnapshot Snap(params (string Key, (string, string)[] Values)[] keys)
    {
        var s = new RegistrySnapshot();
        foreach (var (k, v) in keys) s.Add(k, v);
        return s;
    }

    [Fact]
    public void Diferenca_acha_chaves_e_valores_novos_e_alterados()
    {
        var path = @"HKEY_CURRENT_USER\Environment";
        var before = Snap(
            (Soft, []), ($@"{Soft}\Acme", [("A", "String:1")]), (path, [("Path", @"ExpandString:C:\x")]),
            ($@"{Soft}\Velha", []), ($@"{Soft}\Microsoft\Windows\CurrentVersion\Explorer\Bags", []));
        var after = Snap(
            (Soft, []), ($@"{Soft}\Acme", [("A", "String:2"), ("B", "DWord:1")]), (path, [("Path", @"ExpandString:C:\x;C:\Foo")]),
            ($@"{Soft}\Foo", [("X", "String:x")]), ($@"{Soft}\Foo\Sub", []), (Uninstall, [("DisplayName", "String:Foo Editor")]),
            ($@"{Soft}\Microsoft\Windows\CurrentVersion\Explorer\Bags\1", []));
        var names = new Dictionary<string, string[]> { [$@"{Soft}\Acme"] = ["A", "B"], [path] = ["Path"] };

        var c = TraceRules.Diff(before, after, k => names.TryGetValue(k, out var n) ? n : []);

        Assert.Equal([$@"{Soft}\Foo", Uninstall], c.CreatedKeys); // a subchave sai junto com a de cima; o ruído do Explorer some
        Assert.Equal([new TraceValue($@"{Soft}\Acme", "B")], c.CreatedValues);
        Assert.Contains(new TraceValue($@"{Soft}\Acme", "A"), c.ModifiedValues);
        Assert.Contains(new TraceValue(path, "Path", @"ExpandString:C:\x"), c.ModifiedValues); // o PATH guarda o valor antigo
        Assert.Equal(1, c.DeletedKeys);
    }

    [Fact]
    public void Monta_o_trace_sem_ruido_e_ligado_ao_programa()
    {
        var registry = new RegistryChanges([$@"{Soft}\Foo", Uninstall], [], [], 0);
        var files = new[]
        {
            (@"C:\Program Files\Acme\Foo Editor", true, true),
            (@"C:\Program Files\Acme\Foo Editor\foo.exe", false, true),
            (@"C:\Users\ana\AppData\Roaming\Foo Editor", true, true),
            (@"C:\Users\ana\Desktop\Foo Editor.lnk", false, true),
            (@"C:\Users\ana\AppData\Local\Temp\setup123\x.dll", false, true),     // temporário
            (@"C:\Windows\Prefetch\SETUP.EXE-1.pf", false, true),                  // ruído do Windows
            (@"C:\Windows\System32\drivers\etc\hosts", false, false),              // alterado
            (@"C:\Users\ana\AppData\Local\NotebookDoctor\history.jsonl", false, false), // o próprio app
        };
        var t = TraceRules.Build(@"C:\Downloads\FooSetup.exe", Make.T0, Make.T0.AddMinutes(2), registry, files, Places,
                                 @"C:\Users\ana\AppData\Local\NotebookDoctor", k => k == Uninstall ? "Foo Editor 2.1" : null);
        Assert.Equal("Foo Editor 2.1", t.Name);
        Assert.Equal(Uninstall, t.UninstallKey);
        Assert.Equal([@"C:\Program Files\Acme\Foo Editor", @"C:\Users\ana\AppData\Roaming\Foo Editor"], t.CreatedFolders);
        Assert.Equal([@"C:\Users\ana\Desktop\Foo Editor.lnk"], t.CreatedFiles);
        Assert.Equal([@"C:\Windows\System32\drivers\etc\hosts"], t.ModifiedFiles);
        Assert.StartsWith("20261001-090000-foo-editor", t.Id);
        Assert.Equal(5, t.Count);
    }

    [Fact]
    public void Sem_entrada_de_desinstalacao_o_nome_vem_do_instalador()
    {
        var t = TraceRules.Build(@"C:\Downloads\FooSetup.exe", Make.T0, Make.T0, new RegistryChanges([], [], [], 0), [], Places, @"C:\x", _ => null);
        Assert.Equal("FooSetup", t.Name);
        Assert.Equal("", t.UninstallKey);
    }

    private static InstallTrace Foo(bool imported = false) => new()
    {
        Name = "Foo Editor", UninstallKey = Uninstall, Imported = imported,
        CreatedFolders = [@"C:\Program Files\Acme\Foo Editor", @"C:\Users\ana\Documents\Foo", @"C:\Program Files\Acme\Compartilhada"],
        CreatedFiles = [@"C:\Users\ana\Desktop\Foo Editor.lnk", @"C:\Windows\System32\foo.dll", @"C:\Users\ana\AppData\Roaming\sumiu.txt"],
        CreatedKeys = [$@"{Soft}\Foo", @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FooSvc", $@"{Soft}\Microsoft", $@"{Soft}\NaoExisteMais"],
        CreatedValues = [new TraceValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "FooTray")],
        ModifiedValues = [new TraceValue(@"HKEY_CURRENT_USER\Environment", "Path", "x")],
    };

    private static FakeSystem Pc() => new FakeSystem()
        .File(@"C:\Program Files\Acme\Foo Editor\foo.exe", 300)
        .File(@"C:\Users\ana\Documents\Foo\meu.docx")
        .Dir(@"C:\Program Files\Acme\Compartilhada")
        .File(@"C:\Users\ana\Desktop\Foo Editor.lnk")
        .File(@"C:\Windows\System32\foo.dll")
        .Key($@"{Soft}\Foo").Key(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FooSvc").Key($@"{Soft}\Microsoft")
        .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", ("FooTray", "x"), ("Outro", "y"));

    [Fact]
    public void Do_trace_so_sai_o_que_ainda_existe_e_nao_e_protegido()
    {
        var other = new InstalledProgram { Id = "o", Name = "Acme Outro", InstallLocation = @"C:\Program Files\Acme\Compartilhada" };
        var items = TraceRules.ToLeftovers(Foo(), Places, [other], Pc());
        var rows = items.Select(l => (l.Kind, l.Kind == LeftoverKind.RegistryValue ? l.ValueName : l.Path)).ToHashSet();
        Assert.Equal(new HashSet<(LeftoverKind, string)>
        {
            (LeftoverKind.Folder, @"C:\Program Files\Acme\Foo Editor"),
            (LeftoverKind.File, @"C:\Users\ana\Desktop\Foo Editor.lnk"),
            (LeftoverKind.RegistryKey, $@"{Soft}\Foo"),
            (LeftoverKind.Service, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\FooSvc"),
            (LeftoverKind.RegistryValue, "FooTray"),
        }, rows); // fora: Documentos, pasta de outro programa, System32, o que sumiu, a chave Microsoft
        Assert.All(items, l => Assert.Equal(LeftoverLevel.Safe, l.Level));
        Assert.Equal(300, items.Single(l => l.Kind == LeftoverKind.Folder).Bytes);
        Assert.True(items.Single(l => l.Kind == LeftoverKind.Folder).NeedsAdmin);
    }

    [Fact]
    public void Trace_importado_entra_como_moderado() =>
        Assert.All(TraceRules.ToLeftovers(Foo(imported: true), Places, [], Pc()), l => Assert.Equal(LeftoverLevel.Moderate, l.Level));

    [Fact]
    public void Juntar_com_a_varredura_nao_repete()
    {
        var fromTrace = TraceRules.ToLeftovers(Foo(), Places, [], Pc());
        var fromScan = new List<Leftover>
        {
            new() { Kind = LeftoverKind.Folder, Path = @"C:\Program Files\Acme\Foo Editor", Level = LeftoverLevel.Safe },
            new() { Kind = LeftoverKind.File, Path = @"C:\Program Files\Acme\Foo Editor\foo.exe", Level = LeftoverLevel.Moderate },
            new() { Kind = LeftoverKind.Folder, Path = @"C:\Users\ana\AppData\Roaming\Foo Editor", Level = LeftoverLevel.Moderate },
        };
        var merged = TraceRules.Merge(fromTrace, fromScan);
        Assert.Equal(fromTrace.Count + 1, merged.Count);
        Assert.Single(merged, l => l.Path == @"C:\Program Files\Acme\Foo Editor");
        Assert.All(merged, l => Assert.True(l.Selected));
    }

    [Fact]
    public void Relatorio_lista_cada_secao()
    {
        string r = TraceRules.Report(Foo());
        Assert.Contains("Pastas criadas (3)", r);
        Assert.Contains("FooTray", r);
        Assert.Contains("Valores do registro alterados (não são desfeitos sozinhos) (1)", r);
        Assert.Contains("&lt;", TraceRules.Html(new InstallTrace { Name = "<x>" }));
    }
}

[SupportedOSPlatform("windows")]
public class InstallMonitorIntegrationTests
{
    /// <summary>Fotografa uma chave de teste em HKCU, cria subchaves e valores, fotografa de novo e confere a diferença.</summary>
    [WindowsFact]
    public void Fotografia_do_registro_acha_o_que_foi_criado()
    {
        string root = $@"HKEY_CURRENT_USER\Software\NDTesteTrace{Guid.NewGuid():N}"[..60];
        try
        {
            using (var k = RegistryPaths.Create(root)) k.SetValue("Antes", "1");
            var before = RegistrySnapshotter.Take([root]);
            using (var k = RegistryPaths.Create(root)) { k.SetValue("Antes", "2"); k.SetValue("Novo", 5, RegistryValueKind.DWord); }
            using (var k = RegistryPaths.Create(root + @"\App\Config")) k.SetValue("X", "y");
            var after = RegistrySnapshotter.Take([root]);

            var c = TraceRules.Diff(before, after, RegistrySnapshotter.ValueNames);
            Assert.Equal([root + @"\App"], c.CreatedKeys);
            Assert.Equal([new TraceValue(root, "Novo")], c.CreatedValues);
            Assert.Equal([new TraceValue(root, "Antes")], c.ModifiedValues);
        }
        finally { RegistryPaths.DeleteTree(root); }
    }

    /// <summary>Com administrador (a CI do Windows roda assim), o diário do NTFS acha o arquivo criado na pasta temporária.</summary>
    [WindowsFact]
    public void Diario_do_disco_acha_arquivo_criado()
    {
        if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) return; // sem administrador o diário não abre
        string dir = Path.Combine(Path.GetTempPath(), "NDTesteUsn" + Guid.NewGuid().ToString("N")[..8]);
        var watcher = UsnWatcher.Start();
        Assert.True(watcher.Volumes > 0);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "app.exe"), "MZ");
            var changes = watcher.Changes();
            // A pasta temporária pode vir com nome curto (RUNNER~1); o diário devolve o nome longo: compara pelo fim
            string leaf = Path.GetFileName(dir);
            Assert.Contains(changes, c => c.Created && c.Directory && c.Path.EndsWith(@"\" + leaf, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(changes, c => c.Created && !c.Directory && c.Path.EndsWith($@"\{leaf}\app.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
