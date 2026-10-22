using System.Runtime.Versioning;
using Microsoft.Win32;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

internal sealed class FakeRegSystem : IRegistryCheckSystem
{
    public FakeSystem Inner { get; } = new();
    private readonly Dictionary<string, int> _ints = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Drives { get; } = new(StringComparer.OrdinalIgnoreCase) { @"C:\" };

    public FakeRegSystem Key(string path, params (string Name, string Value)[] values) { Inner.Key(path, values); return this; }
    public FakeRegSystem File(string path) { Inner.File(path); return this; }
    public FakeRegSystem Dir(string path) { Inner.Dir(path); return this; }
    public FakeRegSystem Int(string key, string name, int value) { _ints[key + "|" + name] = value; return this; }

    public int? IntValue(string key, string name) => _ints.TryGetValue(key + "|" + name, out int v) ? v : null;
    public bool DriveReady(string root) => Drives.Contains(root);
    public IEnumerable<string> SubKeys(string key) => Inner.SubKeys(key);
    public bool KeyExists(string key) => Inner.KeyExists(key);
    public string? StringValue(string key, string? name) => Inner.StringValue(key, name);
    public IEnumerable<string> ValueNames(string key) => Inner.ValueNames(key);
    public IEnumerable<string> Directories(string dir) => Inner.Directories(dir);
    public bool DirectoryExists(string dir) => Inner.DirectoryExists(dir);
    public bool FileExists(string path) => Inner.FileExists(path);
    public IEnumerable<string> Files(string dir) => Inner.Files(dir);
    public long Size(string path) => Inner.Size(path);
}

public class RegistryCheckTests
{
    private const string Win = @"C:\Windows";
    private const string Fonts = @"C:\Windows\Fonts";
    private const string CV = @"Software\Microsoft\Windows\CurrentVersion";
    private const string Un = $@"HKEY_LOCAL_MACHINE\{CV}\Uninstall";
    private const string UnUser = $@"HKEY_CURRENT_USER\{CV}\Uninstall";
    private const string AppPaths = $@"HKEY_LOCAL_MACHINE\{CV}\App Paths";
    private const string RunUser = $@"HKEY_CURRENT_USER\{CV}\Run";
    private const string Shared = $@"HKEY_LOCAL_MACHINE\{CV}\SharedDLLs";
    private const string FontsKey = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    private const string ClassesUser = @"HKEY_CURRENT_USER\Software\Classes";

    private static List<RegIssue> Scan(FakeRegSystem sys) => RegistryChecker.Scan(sys, Win, Fonts);

    // ---------- 1. desinstalação ----------

    private static FakeRegSystem Program(string key, string uninstallExe = @"C:\Program Files\X\unins000.exe", string? location = @"C:\Program Files\X")
    {
        var sys = new FakeRegSystem()
            .Key(key, ("DisplayName", "Programa X"), ("DisplayVersion", "1.2"), ("UninstallString", $"\"{uninstallExe}\" /SILENT"));
        if (location is not null) sys.Key(key, ("InstallLocation", location));
        return sys;
    }

    [Fact]
    public void Desinstalacao_com_desinstalador_e_pasta_sumidos_e_apontada()
    {
        var issue = Assert.Single(Scan(Program($@"{Un}\X")));
        Assert.Equal(RegIssueKind.Uninstall, issue.Kind);
        Assert.Equal("Programa X 1.2", issue.Label);
        Assert.Contains(@"C:\Program Files\X\unins000.exe", issue.Evidence);
        Assert.Null(issue.Value);
        Assert.True(issue.DefaultOn);
    }

    [Fact]
    public void Desinstalacao_com_qualquer_sinal_de_vida_nao_e_apontada()
    {
        Assert.Empty(Scan(Program($@"{Un}\X").File(@"C:\Program Files\X\unins000.exe")));               // desinstalador existe
        Assert.Empty(Scan(Program($@"{Un}\X").Dir(@"C:\Program Files\X")));                              // a pasta ainda existe
        Assert.Empty(Scan(Program($@"{Un}\X").Key($@"{Un}\X", ("DisplayIcon", @"C:\Program Files\X\x.exe,0")).File(@"C:\Program Files\X\x.exe"))); // o ícone existe
        Assert.Empty(Scan(Program($@"{Un}\X").Key($@"{Un}\X", ("QuietUninstallString", @"""C:\Program Files\X\quiet.exe""")).File(@"C:\Program Files\X\quiet.exe")));
    }

    [Fact]
    public void Desinstalacao_que_nao_da_para_provar_fica_de_fora()
    {
        Assert.Empty(Scan(Program($@"{Un}\X").Int($@"{Un}\X", "WindowsInstaller", 1)));                  // MSI
        Assert.Empty(Scan(Program($@"{Un}\X").Int($@"{Un}\X", "SystemComponent", 1)));
        Assert.Empty(Scan(Program($@"{Un}\X").Key($@"{Un}\X", ("ParentKeyName", "Outro"))));             // atualização de outro programa
        Assert.Empty(Scan(new FakeRegSystem().Key($@"{Un}\X", ("DisplayName", "Msi"), ("UninstallString", "MsiExec.exe /X{AAAA}"))));
        Assert.Empty(Scan(new FakeRegSystem().Key($@"{Un}\X", ("DisplayName", "Sem desinstalador"))));  // sem UninstallString
        Assert.Empty(Scan(new FakeRegSystem().Key($@"{Un}\X", ("UninstallString", @"C:\a\b.exe"))));    // sem nome: nem aparece em Programas
        Assert.Empty(Scan(new FakeRegSystem().Key($@"{Un}\X", ("DisplayName", "Via cmd"), ("UninstallString", "cmd /c del x"))));
        Assert.Empty(Scan(Program($@"{Un}\X", @"C:\Program Files\X\unins000.exe", location: @"relativo\X")));
    }

    [Fact]
    public void Unidade_ausente_nunca_e_prova()
    {
        var sys = Program($@"{Un}\X", uninstallExe: @"E:\Programas\X\unins000.exe", location: @"E:\Programas\X");
        Assert.Empty(Scan(sys));                       // E: não está presente (disco externo)
        sys.Drives.Add(@"E:\");
        Assert.Single(Scan(sys));                      // com o disco presente e o arquivo sumido, sim
    }

    [Fact]
    public void Desinstalacao_do_usuario_tambem_e_lida_e_vale_so_para_ele()
    {
        var issue = Assert.Single(Scan(Program($@"{UnUser}\X")));
        Assert.False(issue.Machine);
    }

    // ---------- 2 a 5 ----------

    [Fact]
    public void App_Paths_so_quando_o_programa_sumiu()
    {
        var sys = new FakeRegSystem().Key($@"{AppPaths}\velho.exe", ("", @"C:\Velho\velho.exe")).Key($@"{AppPaths}\vivo.exe", ("", @"C:\Vivo\vivo.exe")).File(@"C:\Vivo\vivo.exe")
            .Key($@"{AppPaths}\sem.exe");
        var issue = Assert.Single(Scan(sys));
        Assert.Equal(RegIssueKind.AppPath, issue.Kind);
        Assert.Equal("velho.exe", issue.Label);
        Assert.Equal($@"{AppPaths}\velho.exe", issue.Key);
    }

    [Fact]
    public void Run_apaga_o_valor_e_nunca_a_chave_e_so_com_caminho_completo()
    {
        var sys = new FakeRegSystem().Key(RunUser,
            ("Velho", @"""C:\Velho\upd.exe"" --boot"), ("Vivo", @"C:\Vivo\vivo.exe /bg"), ("Solto", "notepad.exe"), ("Variavel", @"%APPDATA%\x\y.exe"),
            ("Script", @"C:\Velho\ligar.vbs"), ("Remoto", @"\\servidor\pasta\x.exe")).File(@"C:\Vivo\vivo.exe");
        var issue = Assert.Single(Scan(sys));
        Assert.Equal(RegIssueKind.Run, issue.Kind);
        Assert.Equal("Velho", issue.Value);
        Assert.Equal(RunUser, issue.Key);                   // a chave Run fica; só o valor sai
    }

    [Fact]
    public void SharedDLLs_pelo_nome_do_valor()
    {
        var sys = new FakeRegSystem().Key(Shared, (@"C:\Velho\lib.dll", "1"), (@"C:\Vivo\lib.dll", "2"), (@"D:\Fora\lib.dll", "1")).File(@"C:\Vivo\lib.dll");
        var issue = Assert.Single(Scan(sys));               // D: não está presente
        Assert.Equal(RegIssueKind.SharedDll, issue.Kind);
        Assert.Equal(@"C:\Velho\lib.dll", issue.Value);
    }

    [Fact]
    public void Fontes_com_nome_de_arquivo_na_pasta_Fonts_ou_caminho_completo()
    {
        var sys = new FakeRegSystem().Key(FontsKey,
            ("Arial (TrueType)", "arial.ttf"), ("Sumida (TrueType)", "sumida.ttf"), ("Usuario (TrueType)", @"C:\Users\a\Fonts\u.ttf"), ("Variavel (TrueType)", @"%windir%\f.ttf"))
            .File(@"C:\Windows\Fonts\arial.ttf");
        var found = Scan(sys).Select(i => i.Value).OrderBy(v => v).ToList();
        Assert.Equal(["Sumida (TrueType)", "Usuario (TrueType)"], found);
    }

    // ---------- 6. ProgID ----------

    private static FakeRegSystem ProgId(string name = "Velho.Doc", string exe = @"C:\Program Files\Velho\velho.exe") =>
        new FakeRegSystem().Key($@"{ClassesUser}\{name}\shell\open\command", ("", $"\"{exe}\" \"%1\""));

    [Fact]
    public void ProgID_sem_programa_e_sem_uso_e_apontado_mas_desmarcado()
    {
        var issue = Assert.Single(Scan(ProgId()));
        Assert.Equal(RegIssueKind.ProgId, issue.Kind);
        Assert.Equal($@"{ClassesUser}\Velho.Doc", issue.Key);
        Assert.False(issue.DefaultOn);                     // a categoria que pede mais cuidado nunca vem marcada
    }

    [Fact]
    public void ProgID_em_uso_por_alguma_extensao_nunca_e_apontado()
    {
        Assert.Empty(Scan(ProgId().Key($@"{ClassesUser}\.velho", ("", "Velho.Doc"))));                                       // extensão aponta
        Assert.Empty(Scan(ProgId().Key($@"{ClassesUser}\.velho\OpenWithProgids", ("Velho.Doc", "")))); // lista "abrir com"
        Assert.Empty(Scan(ProgId().Key($@"HKEY_CURRENT_USER\{CV}\Explorer\FileExts\.velho\UserChoice", ("ProgId", "Velho.Doc")))); // escolha do usuário
        Assert.Empty(Scan(ProgId()
            .Key(@"HKEY_LOCAL_MACHINE\Software\RegisteredApplications", ("Velho", @"Software\Velho\Capabilities"))
            .Key(@"HKEY_LOCAL_MACHINE\Software\Velho\Capabilities\FileAssociations", (".velho", "Velho.Doc"))));              // aplicativo registrado
    }

    [Fact]
    public void ProgID_que_e_objeto_COM_protocolo_ou_do_windows_nunca_e_apontado()
    {
        Assert.Empty(Scan(ProgId().Key($@"{ClassesUser}\Velho.Doc\CLSID", ("", "{AAAAAAAA-0000-0000-0000-000000000001}"))));
        Assert.Empty(Scan(ProgId().Key($@"{ClassesUser}\Velho.Doc", ("URL Protocol", ""))));
        Assert.Empty(Scan(ProgId(exe: @"C:\Windows\System32\velho.exe")));
        Assert.Empty(Scan(ProgId(name: "Applications", exe: @"C:\Velho\x.exe")));       // chave de estrutura, não é ProgID
        Assert.Empty(Scan(ProgId(name: ".ext")));                                      // extensão
        Assert.Empty(Scan(new FakeRegSystem().Key($@"{ClassesUser}\Store.App\shell\open\command", ("DelegateExecute", "{GUID}"))));  // sem comando
    }

    // ---------- trava ----------

    [Fact]
    public void Ordem_por_categoria()
    {
        var sys = Program($@"{Un}\X").Key(RunUser, ("Velho", @"C:\Velho\upd.exe")).Key(Shared, (@"C:\Velho\lib.dll", "1"));
        Assert.Equal([RegIssueKind.Uninstall, RegIssueKind.Run, RegIssueKind.SharedDll], Scan(sys).Select(i => i.Kind));
    }

    [Fact]
    public void Conferencia_final_so_aprova_o_que_ainda_tem_prova()
    {
        var sys = Program($@"{Un}\X");
        var issue = Assert.Single(Scan(sys));
        Assert.True(RegistryChecker.StillBroken(sys, issue, Win, Fonts));
        sys.File(@"C:\Program Files\X\unins000.exe");                       // o arquivo apareceu depois da verificação
        Assert.False(RegistryChecker.StillBroken(sys, issue, Win, Fonts));

        var run = new FakeRegSystem().Key(RunUser, ("Velho", @"C:\Velho\upd.exe"));
        var runIssue = Assert.Single(Scan(run));
        Assert.True(RegistryChecker.StillBroken(run, runIssue, Win, Fonts));
        run.Key(RunUser, ("Velho", @"C:\Velho\outro.exe")).File(@"C:\Velho\outro.exe");          // o valor mudou
        Assert.False(RegistryChecker.StillBroken(run, runIssue, Win, Fonts));

        var pid = ProgId();
        var pidIssue = Assert.Single(Scan(pid));
        pid.Key($@"{ClassesUser}\.velho", ("", "Velho.Doc"));                                    // uma extensão passou a usar
        Assert.False(RegistryChecker.StillBroken(pid, pidIssue, Win, Fonts));
    }

    [Theory]
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\X", null, true)]
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall", null, false)]        // a raiz inteira
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\X\Sub", null, false)]  // fundo demais
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\..\Run\X", null, false)]
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\X", null, false)]                        // lugar que o verificador não conhece
    [InlineData(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\X", "valor", false)]   // categoria de chave com valor
    [InlineData(RegIssueKind.Run, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Velho", true)]
    [InlineData(RegIssueKind.Run, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", null, false)]                     // Run só por valor: nunca a chave
    [InlineData(RegIssueKind.Run, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "Velho", false)]
    [InlineData(RegIssueKind.ProgId, @"HKEY_CURRENT_USER\Software\Classes\Velho.Doc", null, true)]
    [InlineData(RegIssueKind.ProgId, @"HKEY_CURRENT_USER\Software\Classes\.txt", null, false)]
    [InlineData(RegIssueKind.ProgId, @"HKEY_CURRENT_USER\Software\Classes\CLSID", null, false)]
    [InlineData(RegIssueKind.ProgId, @"HKEY_CURRENT_USER\Software\Classes\Applications", null, false)]
    [InlineData(RegIssueKind.ProgId, @"HKEY_CLASSES_ROOT\Velho.Doc", null, false)]
    [InlineData(RegIssueKind.Font, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Fonts", "Sumida (TrueType)", true)]
    public void So_chaves_conhecidas_podem_ser_apagadas(RegIssueKind kind, string key, string? value, bool ok) =>
        Assert.Equal(ok, RegistryChecker.Allowed(new RegIssue(kind, key, value, "x", "y", true)));

    [Fact]
    public void Item_adulterado_nunca_passa_pela_conferencia()
    {
        var sys = new FakeRegSystem().Key(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Algo", ("DisplayName", "x"), ("UninstallString", @"C:\nao\existe.exe"));
        var evil = new RegIssue(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Algo", null, "x", "y", true);
        Assert.False(RegistryChecker.StillBroken(sys, evil, Win, Fonts));
    }
}

[SupportedOSPlatform("windows")]
public class RegistryFixerIntegrationTests : IDisposable
{
    private const string SubKey = @"Software\NotebookDoctorTest7x4";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-regfix-" + Guid.NewGuid().ToString("N"));

    public RegistryFixerIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            run?.DeleteValue("NDTestRun7x4", throwOnMissingValue: false);
            run?.DeleteValue("NDTestRunAlive7x4", throwOnMissingValue: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\NDTest.Doc7x4", throwOnMissingSubKey: false);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [WindowsFact]
    public void Valor_de_Run_quebrado_e_guardado_apagado_e_restaurado_de_verdade_sem_tocar_nos_outros()
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        string alive = Path.Combine(_dir, "vivo.exe");
        File.WriteAllText(alive, "x");
        using (var run = Registry.CurrentUser.CreateSubKey(RunPath))
        {
            run.SetValue("NDTestRun7x4", @"""C:\NDTest7x4\nao-existe\x.exe"" --boot");
            run.SetValue("NDTestRunAlive7x4", $"\"{alive}\"");
        }
        var sys = new WindowsRegistryCheckSystem();
        var issues = RegistryChecker.Scan(sys, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts"));
        var mine = issues.Where(i => i.Value is "NDTestRun7x4" or "NDTestRunAlive7x4").ToList();
        var issue = Assert.Single(mine);                                  // só o quebrado
        Assert.Equal("NDTestRun7x4", issue.Value);

        var store = new BackupStore(Path.Combine(_dir, "backups"));
        var result = RegistryFixer.Fix([issue], sys, store, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts"));
        Assert.Equal(1, result.Removed);
        Assert.NotNull(result.SessionId);
        using (var run = Registry.CurrentUser.OpenSubKey(RunPath))
        {
            Assert.Null(run!.GetValue("NDTestRun7x4"));
            Assert.NotNull(run.GetValue("NDTestRunAlive7x4"));            // o outro valor não foi tocado
        }

        store.Restore(result.SessionId!);
        using (var run = Registry.CurrentUser.OpenSubKey(RunPath))
            Assert.Equal(@"""C:\NDTest7x4\nao-existe\x.exe"" --boot", run!.GetValue("NDTestRun7x4"));
    }

    [WindowsFact]
    public void ProgID_quebrado_e_guardado_com_a_arvore_toda_e_volta()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\NDTest.Doc7x4\shell\open\command"))
            k.SetValue("", @"""C:\NDTest7x4\nao-existe\velho.exe"" ""%1""");
        var sys = new WindowsRegistryCheckSystem();
        var issue = RegistryChecker.Scan(sys, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts")).Single(i => i.Label == "NDTest.Doc7x4");
        Assert.False(issue.DefaultOn);

        var store = new BackupStore(Path.Combine(_dir, "backups"));
        var result = RegistryFixer.Fix([issue], sys, store, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts"));
        Assert.Equal(1, result.Removed);
        using (var gone = Registry.CurrentUser.OpenSubKey(@"Software\Classes\NDTest.Doc7x4")) Assert.Null(gone);

        store.Restore(result.SessionId!);
        using var back = Registry.CurrentUser.OpenSubKey(@"Software\Classes\NDTest.Doc7x4\shell\open\command");
        Assert.Equal(@"""C:\NDTest7x4\nao-existe\velho.exe"" ""%1""", back?.GetValue("") as string);
    }

    [WindowsFact]
    public void Item_que_ganhou_vida_depois_da_verificacao_e_pulado_e_item_estranho_recusado()
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        string exe = Path.Combine(_dir, "voltou.exe");
        using (var run = Registry.CurrentUser.CreateSubKey(RunPath)) run.SetValue("NDTestRun7x4", $"\"{exe}\"");
        var sys = new WindowsRegistryCheckSystem();
        var issue = RegistryChecker.Scan(sys, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts")).Single(i => i.Value == "NDTestRun7x4");
        File.WriteAllText(exe, "x");                                       // o programa voltou

        var store = new BackupStore(Path.Combine(_dir, "backups"));
        var result = RegistryFixer.Fix([issue, new RegIssue(RegIssueKind.Run, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies", "Algo", "x", "y", true)],
            sys, store, ServiceManagerRoot, Path.Combine(ServiceManagerRoot, "Fonts"));
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, result.Skipped);
        Assert.Null(result.SessionId);
        Assert.Empty(store.List());                                        // nada apagado, nada de backup à toa
        using var after = Registry.CurrentUser.OpenSubKey(RunPath);
        Assert.NotNull(after!.GetValue("NDTestRun7x4"));
    }

    private static string ServiceManagerRoot => Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
}
