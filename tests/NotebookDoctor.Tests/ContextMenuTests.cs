using System.Runtime.Versioning;
using Microsoft.Win32;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class ContextMenuRulesTests
{
    private const string Win = @"C:\Windows";
    private const string Hklm = @"HKEY_LOCAL_MACHINE\Software\Classes";
    private const string Hkcu = @"HKEY_CURRENT_USER\Software\Classes";

    private static FakeSystem Sys() => new FakeSystem()
        // Manipulador de terceiros: o CLSID está no valor padrão da chave
        .Key($@"{Hklm}\*\shellex\ContextMenuHandlers\7-Zip", ("", "{23170F69-40C1-278A-1000-000100020000}"))
        .Key($@"{Hklm}\CLSID\{{23170F69-40C1-278A-1000-000100020000}}", ("", "7-Zip Shell Extension"))
        .Key($@"{Hklm}\CLSID\{{23170F69-40C1-278A-1000-000100020000}}\InprocServer32", ("", @"C:\Program Files\7-Zip\7-zip.dll"))
        .File(@"C:\Program Files\7-Zip\7-zip.dll")
        // Manipulador do Windows: o nome da chave é o próprio CLSID, sem valor padrão
        .Key($@"{Hklm}\AllFilesystemObjects\shellex\ContextMenuHandlers\{{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}}")
        .Key($@"{Hklm}\CLSID\{{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}}\InprocServer32", ("", @"%SystemRoot%\system32\ntshrui.dll"))
        .File(@"C:\Windows\system32\ntshrui.dll")
        // Manipulador cuja DLL sumiu
        .Key($@"{Hklm}\Directory\shellex\ContextMenuHandlers\Velho", ("", "{AAAAAAAA-0000-0000-0000-000000000001}"))
        .Key($@"{Hklm}\CLSID\{{AAAAAAAA-0000-0000-0000-000000000001}}\InprocServer32", ("", @"C:\Program Files\Velho\velho.dll"))
        // Verbo de terceiros do usuário, e um verbo básico do Windows
        .Key($@"{Hkcu}\Directory\Background\shell\git_shell", ("", "Git Ba&sh Here"))
        .Key($@"{Hkcu}\Directory\Background\shell\git_shell\command", ("", "\"C:\\Program Files\\Git\\git-bash.exe\" \"--cd=%v\""))
        .File(@"C:\Program Files\Git\git-bash.exe")
        .Key($@"{Hklm}\Directory\shell\open", ("", "&Abrir"))
        .Key($@"{Hklm}\Directory\shell\open\command", ("", @"%SystemRoot%\explorer.exe /idlist,%I,%L"))
        // Verbo já escondido
        .Key($@"{Hklm}\*\shell\Escondido", ("MUIVerb", "@shell32.dll,-1234"), ("ProgrammaticAccessOnly", ""))
        .Key($@"{Hklm}\*\shell\Escondido\command", ("", @"C:\Program Files\X\x.exe ""%1"""));

    private static List<ContextMenuItem> Scan() => ContextMenuRules.Scan(Sys(), Win);

    [Fact]
    public void Le_manipuladores_e_verbos_dos_dois_ramos()
    {
        var items = Scan();
        Assert.Equal(6, items.Count);
        var zip = items.Single(i => i.Name == "7-Zip");
        Assert.Equal(MenuItemKind.Handler, zip.Kind);
        Assert.Equal("{23170F69-40C1-278A-1000-000100020000}", zip.Clsid);
        Assert.Equal(@"C:\Program Files\7-Zip\7-zip.dll", zip.File);
        Assert.True(zip.FileExists);
        Assert.Equal("7-Zip (7-Zip Shell Extension)", zip.Display);
        Assert.True(zip.Machine);

        var git = items.Single(i => i.Name == "git_shell");
        Assert.Equal(MenuItemKind.Verb, git.Kind);
        Assert.False(git.Machine);
        Assert.Equal(@"Directory\Background", git.Location);
        Assert.Equal("Git Bash Here (git_shell)", git.Display); // o "&" do atalho sai
        Assert.Equal(@"C:\Program Files\Git\git-bash.exe", git.File);
    }

    [Fact]
    public void Chave_com_nome_de_guid_vale_como_clsid_e_expande_a_pasta_do_windows()
    {
        var sharing = Scan().Single(i => i.Name.StartsWith("{f81e9010"));
        Assert.Equal("{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", sharing.Clsid);
        Assert.Equal(@"C:\Windows\system32\ntshrui.dll", sharing.File);
        Assert.True(sharing.FileExists);
    }

    [Fact]
    public void Verbo_escondido_e_reconhecido_e_texto_de_recurso_nao_vira_nome()
    {
        var hidden = Scan().Single(i => i.Name == "Escondido");
        Assert.True(hidden.Hidden);
        Assert.Equal("Escondido", hidden.Display); // "@shell32.dll,-1234" não dá para ler: vale o nome da chave
        Assert.Equal(@"C:\Program Files\X\x.exe", hidden.File);
        Assert.False(hidden.FileExists);
    }

    [Fact]
    public void Protecao_so_para_o_que_e_do_windows()
    {
        var items = Scan();
        Assert.True(ContextMenuRules.Protection(items.Single(i => i.Name.StartsWith("{f81e9010")), Win).Protected);   // DLL do Windows
        Assert.True(ContextMenuRules.Protection(items.Single(i => i.Name == "open"), Win).Protected);                  // verbo básico
        Assert.False(ContextMenuRules.Protection(items.Single(i => i.Name == "7-Zip"), Win).Protected);
        Assert.False(ContextMenuRules.Protection(items.Single(i => i.Name == "git_shell"), Win).Protected);
        Assert.False(ContextMenuRules.Protection(items.Single(i => i.Name == "Velho"), Win).Protected);               // DLL sumiu: lixo, pode sair
    }

    [Fact]
    public void Ordem_primeiro_os_quebrados_depois_terceiros_e_por_ultimo_o_windows()
    {
        var order = ContextMenuRules.Order(Scan(), Win).Select(i => i.Name).ToList();
        Assert.Equal(["Escondido", "Velho"], order.Take(2).OrderBy(n => n)); // os dois com arquivo que sumiu vêm antes de tudo
        Assert.True(order.IndexOf("7-Zip") < order.IndexOf("open"));
        Assert.True(order.IndexOf("7-Zip") < order.IndexOf("{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}"));
    }

    [Fact]
    public void Chave_inteira_do_item_segue_o_ramo()
    {
        var zip = Scan().Single(i => i.Name == "7-Zip");
        Assert.Equal($@"{Hklm}\*\shellex\ContextMenuHandlers\7-Zip", zip.Key);
        var git = Scan().Single(i => i.Name == "git_shell");
        Assert.Equal($@"{Hkcu}\Directory\Background\shell\git_shell", git.Key);
    }

    [Theory]
    [InlineData("HKEY_LOCAL_MACHINE", "*", "7-Zip", true)]
    [InlineData("HKEY_CURRENT_USER", @"Directory\Background", "x", true)]
    [InlineData("HKEY_CLASSES_ROOT", "*", "x", false)]               // só os dois ramos conhecidos
    [InlineData("HKEY_LOCAL_MACHINE", @"Software\Run", "x", false)]    // só os lugares do menu
    [InlineData("HKEY_LOCAL_MACHINE", "*", @"..\..\Run", false)]
    [InlineData("HKEY_LOCAL_MACHINE", "*", "a\\b", false)]
    [InlineData("HKEY_LOCAL_MACHINE", "*", "..", false)]
    [InlineData("HKEY_LOCAL_MACHINE", "*", "", false)]
    public void So_chaves_do_menu_de_contexto_sao_aceitas(string hive, string location, string name, bool ok) =>
        Assert.Equal(ok, ContextMenuRules.Valid(hive, location, name));

    [Theory]
    [InlineData("{23170F69-40C1-278A-1000-000100020000}", true)]
    [InlineData("23170F69-40C1-278A-1000-000100020000", false)]
    [InlineData("{não é guid}", false)]
    [InlineData("", false)]
    public void Clsid(string text, bool ok) => Assert.Equal(ok, ContextMenuRules.IsClsid(text));
}

[SupportedOSPlatform("windows")]
public class ContextMenuEditorIntegrationTests : IDisposable
{
    // Chaves de teste no ramo do usuário (sem administrador), com nome próprio
    private const string Name = "NDTestHandler7x3";
    private const string VerbName = "NDTestVerb7x3";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-ctx-" + Guid.NewGuid().ToString("N"));

    public ContextMenuEditorIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shellex\ContextMenuHandlers", writable: true);
        root?.DeleteSubKeyTree(Name, throwOnMissingSubKey: false);
        using var shell = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell", writable: true);
        shell?.DeleteSubKeyTree(VerbName, throwOnMissingSubKey: false);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [WindowsFact]
    public void Verbo_some_do_menu_e_volta_sem_apagar_nada()
    {
        using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\shell\{VerbName}"))
        {
            k.SetValue("", "Teste");
            using var c = k.CreateSubKey("command");
            c.SetValue("", @"C:\Windows\notepad.exe ""%1""");
        }
        var item = ContextMenuEditor.Scan().Single(i => i.Name == VerbName);
        Assert.False(item.Hidden);

        ContextMenuEditor.SetVerbHidden(item, true);
        Assert.True(ContextMenuEditor.Scan().Single(i => i.Name == VerbName).Hidden);
        using (var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{VerbName}\command"))
            Assert.NotNull(k); // o comando continua lá

        ContextMenuEditor.SetVerbHidden(item, false);
        Assert.False(ContextMenuEditor.Scan().Single(i => i.Name == VerbName).Hidden);
    }

    [WindowsFact]
    public void Manipulador_e_guardado_apagado_e_restaurado_de_verdade()
    {
        string clsid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Directory\shellex\ContextMenuHandlers\{Name}"))
            k.SetValue("", clsid);
        var item = ContextMenuEditor.Scan().Single(i => i.Name == Name);
        Assert.Equal(clsid, item.Clsid);

        var rec = ContextMenuEditor.DisableHandler(item, _dir);
        using (var gone = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shellex\ContextMenuHandlers\{Name}"))
            Assert.Null(gone);
        Assert.DoesNotContain(ContextMenuEditor.Scan(), i => i.Name == Name);
        Assert.Contains(ContextMenuEditor.Disabled(_dir), r => r.SessionId == rec.SessionId);

        string msg = ContextMenuEditor.EnableHandler(rec, _dir, out bool restored);
        Assert.True(restored, msg);
        using (var back = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shellex\ContextMenuHandlers\{Name}"))
            Assert.Equal(clsid, back?.GetValue("") as string);
        Assert.Empty(ContextMenuEditor.Disabled(_dir));
    }

    [WindowsFact]
    public void Manipulador_do_windows_e_item_invalido_sao_recusados()
    {
        var windows = new ContextMenuItem(MenuItemKind.Handler, "HKEY_CURRENT_USER", "*", "Sharing", "Sharing", "", ContextMenuEditor.SystemRoot + @"\System32\ntshrui.dll", true, "", false);
        Assert.Throws<InvalidOperationException>(() => ContextMenuEditor.DisableHandler(windows, _dir));
        var evil = new ContextMenuItem(MenuItemKind.Handler, "HKEY_CURRENT_USER", "*", @"..\..\Run", "x", "", "", false, "", false);
        Assert.Throws<ArgumentException>(() => ContextMenuEditor.DisableHandler(evil, _dir));
        var wrongKind = new ContextMenuItem(MenuItemKind.Verb, "HKEY_CURRENT_USER", "*", "x", "x", "", "", false, "", false);
        Assert.Throws<ArgumentException>(() => ContextMenuEditor.DisableHandler(wrongKind, _dir));
    }
}
