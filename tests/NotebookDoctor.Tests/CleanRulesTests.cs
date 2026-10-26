using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>O computador de mentira da leva 6.1: o de sobras, mais arquivos só de uma pasta e datas.</summary>
internal sealed class FakeCleanSystem : ICleanSystem
{
    public FakeSystem Inner { get; } = new();
    private readonly Dictionary<string, DateTime> _dates = new(StringComparer.OrdinalIgnoreCase);

    public FakeCleanSystem File(string path, long size = 100, DateTime? written = null)
    {
        Inner.File(path, size);
        if (written is { } w) _dates[path] = w;
        return this;
    }

    public FakeCleanSystem Dir(string path) { Inner.Dir(path); return this; }
    public FakeCleanSystem Key(string path, params (string Name, string Value)[] values) { Inner.Key(path, values); return this; }

    public IEnumerable<string> SubKeys(string key) => Inner.SubKeys(key);
    public bool KeyExists(string key) => Inner.KeyExists(key);
    public string? StringValue(string key, string? name) => Inner.StringValue(key, name);
    public IEnumerable<string> ValueNames(string key) => Inner.ValueNames(key);
    public IEnumerable<string> Directories(string dir) => Inner.Directories(dir);
    public bool DirectoryExists(string dir) => Inner.DirectoryExists(dir);
    public bool FileExists(string path) => Inner.FileExists(path);
    public IEnumerable<string> Files(string dir) => Inner.Files(dir);
    public long Size(string path) => Inner.Size(path);
    public IEnumerable<string> DirectFiles(string dir) => Inner.Files(dir).Where(f => Paths.Same(Paths.Parent(f), dir)).ToList();
    public DateTime LastWrite(string path) => _dates.TryGetValue(path, out var d) ? d : new DateTime(2020, 1, 1);
}

public class GlobAndParserTests
{
    [Theory]
    [InlineData("*.tmp", "a.TMP", true)]
    [InlineData("*.tmp", "a.tmpx", false)]
    [InlineData("~$*", "~$Relatorio.docx", true)]
    [InlineData("*.*", "semextensao", true)]
    [InlineData("thumbcache_*.db", "thumbcache_256.db", true)]
    [InlineData("thumbcache_*.db", "iconcache_256.db", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("*a*b*", "xxaxxbxx", true)]
    [InlineData("*a*b*", "xxbxxaxx", false)]
    public void Curingas_do_Windows(string pattern, string text, bool expected) => Assert.Equal(expected, Glob.IsMatch(pattern, text));

    private const string Text = """
        ; comentário
        [Acme Editor *]
        LangSecRef=3025
        Detect=HKCU\Software\Acme
        Detect2=HKLM\SOFTWARE\Acme
        DetectFile=%ProgramFiles%\Acme\acme.exe
        SpecialDetect=DET_CHROME
        DetectOS=6.1|10.0
        Default=True
        Warning=Fecha o Acme antes.
        FileKey1=%AppData%\Acme\Cache|*.*|RECURSE
        FileKey2=%AppData%\Acme|*.log;*.tmp
        FileKey3=%AppData%\Acme\Old|REMOVESELF|RECURSE
        RegKey1=HKCU\Software\Acme\Recent
        RegKey2=HKCU\Software\Acme|LastFile*
        ExcludeKey1=FILE|%AppData%\Acme\Cache|keep.dat;keep2.dat
        ExcludeKey2=PATH|%AppData%\Acme\Cache\Pinned
        ExcludeKey3=REG|HKCU\Software\Acme\Recent|Pinned

        [Sem Chaves *]
        Detect=HKCU\Software\Nada
        """;

    [Fact]
    public void Le_uma_regra_completa()
    {
        var r = Assert.Single(Winapp2Parser.Parse(Text));
        Assert.Equal("Acme Editor", r.Name);
        Assert.Equal(CleanCategory.Windows, r.Category);
        Assert.Equal(RuleOrigin.Community, r.Origin);
        Assert.Equal([@"HKEY_CURRENT_USER\Software\Acme", @"HKEY_LOCAL_MACHINE\SOFTWARE\Acme"], r.Detect);
        Assert.Equal([@"%ProgramFiles%\Acme\acme.exe"], r.DetectFiles);
        Assert.Equal("DET_CHROME", r.SpecialDetect);
        Assert.Equal(new Version(6, 1), r.MinOs);
        Assert.Equal(new Version(10, 0), r.MaxOs);
        Assert.Equal("Fecha o Acme antes.", r.Warning);

        Assert.Equal(3, r.FileKeys.Count);
        Assert.True(r.FileKeys[0].Recurse);
        Assert.False(r.FileKeys[0].RemoveSelf);
        Assert.Equal(["*.log", "*.tmp"], r.FileKeys[1].Patterns);
        Assert.False(r.FileKeys[1].Recurse);
        Assert.True(r.FileKeys[2].Recurse && r.FileKeys[2].RemoveSelf);
        Assert.Equal(["*.*"], r.FileKeys[2].Patterns);

        Assert.Equal(new RegKey(@"HKEY_CURRENT_USER\Software\Acme\Recent", null), r.RegKeys[0]);
        Assert.Equal(new RegKey(@"HKEY_CURRENT_USER\Software\Acme", "LastFile*"), r.RegKeys[1]);

        Assert.Equal(3, r.Excludes.Count);
        Assert.Equal(ExcludeKind.File, r.Excludes[0].Kind);
        Assert.Equal(["keep.dat", "keep2.dat"], r.Excludes[0].Names);
        Assert.Equal(ExcludeKind.Path, r.Excludes[1].Kind);
        Assert.Equal(ExcludeKind.Registry, r.Excludes[2].Kind);
    }

    [Fact]
    public void Regra_da_comunidade_nunca_vem_marcada_mesmo_com_Default_True()
    {
        Assert.False(Winapp2Parser.Parse(Text)[0].DefaultOn);
        Assert.True(Winapp2Parser.Parse(Text, RuleOrigin.Builtin)[0].DefaultOn);
    }

    [Fact]
    public void Aceita_BOM_CRLF_e_ignora_o_que_nao_entende()
    {
        string text = "\uFEFF[Foo *]\r\nDetect=HKCU\\Software\\Foo\r\nFileKey1=%AppData%\\Foo|*.tmp\r\nChaveEstranha=1\r\nSemIgual\r\n";
        var r = Assert.Single(Winapp2Parser.Parse(text));
        Assert.Equal("Foo", r.Name);
        Assert.Single(r.FileKeys);
    }

    [Fact]
    public void Idade_minima_so_deixa_mais_restrito()
    {
        var fk = Winapp2Parser.ParseFileKey(@"%SystemRoot%\Minidump|*.dmp|AGE7");
        Assert.Equal(7, fk!.MinAgeDays);
    }

    [Theory]
    [InlineData(@"HKCU\Software\X", @"HKEY_CURRENT_USER\Software\X")]
    [InlineData(@"hklm\software\x", @"HKEY_LOCAL_MACHINE\software\x")]
    [InlineData(@"HKEY_USERS\S-1\x", @"HKEY_USERS\S-1\x")]
    [InlineData(@"HKXX\Software", "")]
    public void Abreviacoes_do_registro(string input, string expected) => Assert.Equal(expected, RegPath.Normalize(input));

    [Fact]
    public void Variaveis_de_ambiente()
    {
        var env = EnvExpander.FromPlaces(LeftoverScannerTests.Places);
        Assert.Equal(@"C:\Users\ana\AppData\Roaming\Acme", env.Expand(@"%appdata%\Acme"));
        Assert.Equal(@"C:\Windows\Temp", env.Expand(@"%SystemRoot%\Temp"));
        Assert.Null(env.Expand(@"%NaoExiste%\x"));
    }
}

public class CleanSafetyTests
{
    private static readonly KnownPlaces P = LeftoverScannerTests.Places;

    [Theory]
    [InlineData(@"C:\Users\ana\AppData\Local\Google\Chrome\User Data\Default\Login Data", true)]
    [InlineData(@"C:\Users\ana\AppData\Local\Google\Chrome\User Data\Default\Cookies", true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Mozilla\Firefox\Profiles\x\key4.db", true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Mozilla\Firefox\Profiles\x\places.sqlite", true)]
    [InlineData(@"C:\Users\ana\Saved Games\Jogo", true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Jogo\saves", true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Acme\Cache", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Acme\Savings", false)]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp", false)]
    public void Lista_negra_de_nomes(string path, bool blocked) => Assert.Equal(blocked, CleanSafety.HasBlockedWord(path));

    [Theory]
    [InlineData(@"C:\Windows\Temp", true, false, "*.*", true)]
    [InlineData(@"C:\Windows\Logs\CBS", false, false, "*.log", true)]
    [InlineData(@"C:\Windows\System32", true, false, "*.*", false)]
    [InlineData(@"C:\Windows\System32\config", false, false, "*.log", false)]
    [InlineData(@"C:\Windows", false, false, "MEMORY.DMP", true)]
    [InlineData(@"C:\Windows", false, false, "*.dll", false)]
    [InlineData(@"C:\Windows", true, false, "MEMORY.DMP", false)]
    [InlineData(@"C:\Windows\Temp", true, true, "*.*", false)]
    [InlineData(@"C:\", true, false, "*.tmp", false)]
    [InlineData(@"C:\Users", true, false, "*.tmp", false)]
    [InlineData(@"C:\Users\ana", true, false, "*.*", false)]
    [InlineData(@"C:\Users\ana\Documents\Projeto", true, false, "*.tmp", false)]
    [InlineData(@"C:\Users\ana\OneDrive\Fotos", false, false, "*.jpg", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming", true, false, "*.tmp", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming", false, false, "*.*", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming", false, false, "*.tmp", true)]
    [InlineData(@"C:\Users\ana\AppData\Roaming", false, true, "*.tmp", false)]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp", true, false, "*.*", true)]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp", true, true, "*.*", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\Acme\Cache", true, true, "*.*", true)]
    [InlineData(@"C:\Program Files\Acme\logs", true, false, "*.log", true)]
    [InlineData(@"C:\Program Files", true, false, "*.*", false)]
    [InlineData(@"\\servidor\pasta", true, false, "*.*", false)]
    [InlineData(@"C:\Users\ana\AppData\Roaming\..\..\Documents", true, false, "*.*", false)]
    public void Pastas_que_uma_regra_pode_limpar(string dir, bool recurse, bool self, string pattern, bool allowed) =>
        Assert.Equal(allowed, CleanSafety.DirAllowed(dir, recurse, self, [pattern], P));

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\Acme\Recent", true)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", true)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Word\File MRU", true)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Acme\Logs", true)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Acme", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\Algo", false)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Foo", false)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", false)]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Foo", false)]
    [InlineData(@"HKEY_CLASSES_ROOT\CLSID\{x}", false)]
    [InlineData(@"HKEY_USERS\S-1-5\Software\Acme\Recent", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Classes\Acme.Doc", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Acme\Passwords", false)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Acme\..\Microsoft\Windows\CurrentVersion\Run", false)]
    public void Chaves_do_registro_que_uma_regra_pode_apagar(string key, bool allowed) => Assert.Equal(allowed, CleanSafety.RegKeyAllowed(key));

    [Fact]
    public void Regra_da_comunidade_nao_apaga_executaveis()
    {
        Assert.Equal(["*.log", "*.*"], CleanSafety.UsablePatterns(["*.exe", "*.log", "setup.msi", "*.*", "a\\b"], RuleOrigin.Community));
        Assert.Contains("*.exe", CleanSafety.UsablePatterns(["*.exe"], RuleOrigin.Builtin));
    }
}

public class CleanEngineTests
{
    private static readonly KnownPlaces P = LeftoverScannerTests.Places;
    private const string Roaming = @"C:\Users\ana\AppData\Roaming";
    private const string Local = @"C:\Users\ana\AppData\Local";

    private static CleanEngine Engine(FakeCleanSystem sys, int osMajor = 10) =>
        new(sys, new CleanContext(P, EnvExpander.FromPlaces(P), new Version(osMajor, 0)));

    private static CleanRule Rule(string text) => Assert.Single(Winapp2Parser.Parse(text));

    [Fact]
    public void Regra_so_aparece_quando_o_programa_existe()
    {
        var rule = Rule($"[Acme *]\nDetect=HKCU\\Software\\Acme\nFileKey1=%AppData%\\Acme|*.log\n");
        var sys = new FakeCleanSystem().File($@"{Roaming}\Acme\a.log");
        Assert.Null(Engine(sys).Plan(rule));
        sys.Key(@"HKEY_CURRENT_USER\Software\Acme");
        Assert.NotNull(Engine(sys).Plan(rule));
    }

    [Fact]
    public void Detecta_por_arquivo_com_curinga_e_pelo_navegador()
    {
        var byFile = Rule("[A *]\nDetectFile=%ProgramFiles%\\Acme*\\acme.exe\nFileKey1=%AppData%\\A|*.log\n");
        var sys = new FakeCleanSystem().File($@"{Roaming}\A\x.log").File(@"C:\Program Files\Acme 2\acme.exe");
        Assert.True(Engine(sys).Detected(byFile));
        Assert.False(Engine(new FakeCleanSystem()).Detected(byFile));

        var special = Rule("[B *]\nSpecialDetect=DET_CHROME\nFileKey1=%AppData%\\B|*.log\n");
        Assert.False(Engine(new FakeCleanSystem()).Detected(special));
        Assert.True(Engine(new FakeCleanSystem().Dir($@"{Local}\Google\Chrome\User Data")).Detected(special));
        var unknown = Rule("[C *]\nSpecialDetect=DET_INVENTADO\nFileKey1=%AppData%\\C|*.log\n");
        Assert.False(Engine(new FakeCleanSystem().Dir($@"{Local}\Google\Chrome\User Data")).Detected(unknown));
    }

    [Fact]
    public void Respeita_a_versao_do_Windows()
    {
        var rule = Rule("[Antigo *]\nDetectOS=6.1|6.3\nDetectFile=%AppData%\nFileKey1=%AppData%\\Antigo|*.log\n");
        var sys = new FakeCleanSystem().File($@"{Roaming}\Antigo\a.log");
        Assert.False(Engine(sys, osMajor: 10).Detected(rule));
    }

    [Fact]
    public void Sem_RECURSE_so_os_arquivos_da_propria_pasta()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Roaming}\Acme\Logs\a.log", 10).File($@"{Roaming}\Acme\Logs\sub\b.log", 20).File($@"{Roaming}\Acme\Logs\c.txt", 40);
        var flat = Engine(sys).Plan(Rule("[Acme *]\nFileKey1=%AppData%\\Acme\\Logs|*.log\n"))!;
        Assert.Equal(1, flat.FileCount);
        Assert.Equal(10, flat.Bytes);
        var deep = Engine(sys).Plan(Rule("[Acme *]\nFileKey1=%AppData%\\Acme\\Logs|*.log|RECURSE\n"))!;
        Assert.Equal(2, deep.FileCount);
        Assert.Equal(30, deep.Bytes);
    }

    [Fact]
    public void Nao_toca_em_pasta_vizinha_de_nome_parecido()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Roaming}\Acme\Cache\a.bin", 10)
            .File($@"{Roaming}\Acme\Cache2\b.bin", 99)
            .File($@"{Roaming}\AcmePro\Cache\c.bin", 99)
            .File($@"{Roaming}\Acme\d.bin", 99);
        var e = Engine(sys).Plan(Rule("[Acme *]\nFileKey1=%AppData%\\Acme\\Cache|*.*|RECURSE\n"))!;
        Assert.Equal(1, e.FileCount);
        var target = Assert.Single(e.Files);
        Assert.True(CleanEngine.Matches(target, $@"{Roaming}\Acme\Cache\a.bin"));
        Assert.False(CleanEngine.Matches(target, $@"{Roaming}\Acme\Cache2\b.bin"));
        Assert.False(CleanEngine.Matches(target, $@"{Roaming}\AcmePro\Cache\c.bin"));
        Assert.False(CleanEngine.Matches(target, $@"{Roaming}\Acme\d.bin"));
    }

    [Fact]
    public void Recurse_com_curinga_nao_leva_cookies_senhas_nem_favoritos_das_subpastas()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Local}\Vivaldi\User Data\Default\Cache\a.bin", 10)
            .File($@"{Local}\Vivaldi\User Data\Default\Network\Cookies", 99)
            .File($@"{Local}\Vivaldi\User Data\Default\Login Data", 99)
            .File($@"{Local}\Vivaldi\User Data\Default\Bookmarks", 99)
            .File($@"{Local}\Vivaldi\User Data\Default\Local Storage\leveldb\000003.log", 99);
        var e = Engine(sys).Plan(Rule("[Vivaldi *]\nFileKey1=%LocalAppData%\\Vivaldi\\User Data\\Default|*.*|RECURSE\n"))!;
        Assert.Equal(1, e.FileCount);
        Assert.Equal(10, e.Bytes);
        var target = Assert.Single(e.Files);
        Assert.False(CleanEngine.Matches(target, $@"{Local}\Vivaldi\User Data\Default\Network\Cookies"));
        Assert.False(CleanEngine.Matches(target, $@"{Local}\Vivaldi\User Data\Default\Local Storage\leveldb\000003.log"));
    }

    [Fact]
    public void Pasta_com_curinga_vale_para_cada_perfil()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Local}\Navegador\User Data\Default\Cache\a", 1).File($@"{Local}\Navegador\User Data\Profile 1\Cache\b", 2)
            .File($@"{Local}\Navegador\User Data\Default\Other\c", 4);
        var e = Engine(sys).Plan(Rule("[Nav *]\nFileKey1=%LocalAppData%\\Navegador\\User Data\\*\\Cache|*.*|RECURSE\n"))!;
        Assert.Equal(2, e.Files.Count);
        Assert.Equal(3, e.Bytes);
    }

    [Fact]
    public void Excecoes_poupam_arquivos_e_pastas()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Roaming}\Acme\Cache\a.bin", 1).File($@"{Roaming}\Acme\Cache\keep.dat", 2)
            .File($@"{Roaming}\Acme\Cache\Pinned\p.bin", 4).File($@"{Roaming}\Acme\Cache\Sub\keep.dat", 8).File($@"{Roaming}\Acme\Cache\Sub\z.bin", 16);
        var rule = Rule("[Acme *]\nFileKey1=%AppData%\\Acme\\Cache|*.*|RECURSE\nExcludeKey1=FILE|%AppData%\\Acme\\Cache|keep.dat\nExcludeKey2=PATH|%AppData%\\Acme\\Cache\\Pinned\n");
        var e = Engine(sys).Plan(rule)!;
        Assert.Equal(2, e.FileCount); // a.bin e Sub\z.bin
        Assert.Equal(17, e.Bytes);
    }

    [Fact]
    public void Ignora_desktop_ini()
    {
        var sys = new FakeCleanSystem().File($@"{Roaming}\Acme\desktop.ini").File($@"{Roaming}\Acme\a.log");
        Assert.Equal(1, Engine(sys).Plan(Rule("[Acme *]\nFileKey1=%AppData%\\Acme|*.*\n"))!.FileCount);
    }

    [Fact]
    public void Idade_minima()
    {
        var now = new DateTime(2026, 10, 22);
        var sys = new FakeCleanSystem()
            .File(@"C:\Windows\Minidump\velho.dmp", 10, now.AddDays(-30)).File(@"C:\Windows\Minidump\novo.dmp", 10, DateTime.Now.AddDays(-1));
        var e = Engine(sys).Plan(Rule("[Dumps *]\nFileKey1=%SystemRoot%\\Minidump|*.dmp|AGE7\n"))!;
        Assert.Equal(1, e.FileCount);
    }

    [Theory]
    [InlineData(@"%AppData%\..\..\Documents|*.*|RECURSE")]
    [InlineData(@"%UserProfile%|*.*|RECURSE")]
    [InlineData(@"%UserProfile%\Documents|*.*|RECURSE")]
    [InlineData(@"%UserProfile%\Documents\Acme|*.tmp")]
    [InlineData(@"%SystemRoot%\System32|*.log")]
    [InlineData(@"%SystemDrive%\|*.*|RECURSE")]
    [InlineData(@"%AppData%|*.*|RECURSE")]
    [InlineData(@"%AppData%\Acme|*.*|REMOVESELF|RECURSE")]
    public void Regra_fora_dos_limites_nao_apaga_nada(string fileKey)
    {
        var sys = new FakeCleanSystem()
            .File($@"{Roaming}\Acme\a.log").File(@"C:\Users\ana\Documents\x.docx").File(@"C:\Users\ana\Documents\Acme\y.tmp")
            .File(@"C:\Windows\System32\z.log").File(@"C:\Users\ana\n.txt").File($@"{Roaming}\solto.tmp").File(@"C:\a.txt");
        var e = Engine(sys).Plan(Rule($"[Ruim *]\nFileKey1={fileKey}\n"));
        Assert.True(e is null || e.FileCount == 0 || e.Files.All(t => t.Dir.StartsWith($@"{Roaming}\Acme", StringComparison.OrdinalIgnoreCase)));
        if (fileKey.Contains("REMOVESELF")) Assert.True(e is null || e.Files.All(t => t.RemoveSelf)); // a pasta do app, sim
    }

    [Fact]
    public void Nomes_de_senhas_e_cookies_sao_bloqueados()
    {
        var sys = new FakeCleanSystem()
            .File($@"{Local}\Google\Chrome\User Data\Default\Login Data").File($@"{Local}\Google\Chrome\User Data\Default\Cookies")
            .File($@"{Local}\Google\Chrome\User Data\Default\History");
        var e = Engine(sys).Plan(Rule("[Chrome *]\nFileKey1=%LocalAppData%\\Google\\Chrome\\User Data\\Default|Login Data;Cookies\nFileKey2=%LocalAppData%\\Google\\Chrome\\User Data\\Default|History\n"))!;
        Assert.Equal(1, e.FileCount);
        Assert.Equal(1, e.Blocked);
    }

    [Fact]
    public void Registro_chave_inteira_e_valores()
    {
        const string Key = @"HKEY_CURRENT_USER\Software\Acme\Recent";
        var sys = new FakeCleanSystem().Key(Key, ("a", "1"), ("b", "2")).Key(@"HKEY_CURRENT_USER\Software\Acme\Opts", ("LastFile1", "x"), ("LastFile2", "y"), ("Theme", "dark"));
        var rule = Rule("[Acme *]\nRegKey1=HKCU\\Software\\Acme\\Recent\nRegKey2=HKCU\\Software\\Acme\\Opts|LastFile*\n");
        var e = Engine(sys).Plan(rule)!;
        Assert.Equal(3, e.RegCount);
        Assert.Contains(new RegTarget(Key, null), e.Registry);
        Assert.Contains(new RegTarget(@"HKEY_CURRENT_USER\Software\Acme\Opts", "LastFile2"), e.Registry);
        Assert.DoesNotContain(e.Registry, t => t.ValueName == "Theme");
        Assert.False(e.NeedsAdmin);
    }

    [Fact]
    public void Registro_excecao_poupa_valor_e_chave()
    {
        const string Opts = @"HKEY_CURRENT_USER\Software\Acme\Opts";
        var sys = new FakeCleanSystem().Key(Opts, ("Last1", "x"), ("Pinned", "y")).Key(@"HKEY_CURRENT_USER\Software\Acme\Recent", ("a", "1"));
        var values = Engine(sys).Plan(Rule("[A *]\nRegKey1=HKCU\\Software\\Acme\\Opts|*\nExcludeKey1=REG|HKCU\\Software\\Acme\\Opts|Pinned\n"))!;
        Assert.Equal([new RegTarget(Opts, "Last1")], values.Registry);
        var wholeKey = Engine(sys).Plan(Rule("[B *]\nRegKey1=HKCU\\Software\\Acme\\Opts\nFileKey1=%AppData%\\Nada|*.*\nExcludeKey1=REG|HKCU\\Software\\Acme\\Opts|Pinned\n"));
        Assert.True(wholeKey is null || wholeKey.RegCount == 0);
    }

    [Fact]
    public void Registro_com_curinga_no_meio_do_caminho()
    {
        var sys = new FakeCleanSystem()
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Word\User MRU\ADAL_123\File MRU", ("Item 1", "x"))
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Word\User MRU\LiveId_9\File MRU", ("Item 1", "y"))
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Word\User MRU\LiveId_9\Outra", ("Item 1", "y"));
        var e = Engine(sys).Plan(Rule("[Office *]\nRegKey1=HKCU\\Software\\Microsoft\\Office\\16.0\\Word\\User MRU\\*\\File MRU\n"))!;
        Assert.Equal(2, e.RegCount);
    }

    [Theory]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Foo")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Foo")]
    [InlineData(@"HKCU\Software\Acme\Passwords")]
    [InlineData(@"HKCU\Software")]
    public void Registro_protegido_nao_entra_no_plano(string key)
    {
        string full = RegPath.Normalize(key);
        var sys = new FakeCleanSystem().Key(full, ("v", "1"));
        var e = Engine(sys).Plan(Rule($"[Ruim *]\nRegKey1={key}\n"));
        Assert.True(e is null || e.RegCount == 0);
    }

    [Fact]
    public void Exige_administrador_fora_da_pasta_do_usuario_ou_em_HKLM()
    {
        var files = Engine(new FakeCleanSystem().File(@"C:\Windows\Logs\CBS\a.log")).Plan(Rule("[W *]\nFileKey1=%SystemRoot%\\Logs\\CBS|*.log\n"))!;
        Assert.True(files.NeedsAdmin);
        var hklm = Engine(new FakeCleanSystem().Key(@"HKEY_LOCAL_MACHINE\SOFTWARE\Acme\Logs", ("a", "1"))).Plan(Rule("[H *]\nRegKey1=HKLM\\SOFTWARE\\Acme\\Logs\n"))!;
        Assert.True(hklm.NeedsAdmin);
    }

    [Fact]
    public void PlanAll_ordena_e_so_traz_o_que_tem_o_que_limpar()
    {
        var sys = new FakeCleanSystem().File($@"{Roaming}\Acme\a.log");
        var rules = Winapp2Parser.Parse("[Zeta *]\nFileKey1=%AppData%\\Zeta|*.log\n[Acme *]\nFileKey1=%AppData%\\Acme|*.log\n");
        var all = Engine(sys).PlanAll(rules);
        Assert.Equal(["Acme"], all.Select(a => a.Name));
        Assert.All(all, a => Assert.False(a.Selected));
    }
}

public class BuiltInRulesTests
{
    private static readonly KnownPlaces P = LeftoverScannerTests.Places;
    private static readonly EnvExpander Env = EnvExpander.FromPlaces(P);

    [Fact]
    public void Nomes_unicos_e_sem_palavras_proibidas()
    {
        var rules = BuiltInRules.All();
        Assert.Equal(rules.Count, rules.Select(r => r.Name).Distinct().Count());
        Assert.All(rules, r => Assert.False(CleanSafety.HasBlockedWord(r.Name)));
        Assert.All(rules, r => Assert.Equal(RuleOrigin.Builtin, r.Origin));
    }

    [Fact]
    public void Toda_pasta_das_regras_proprias_e_permitida()
    {
        foreach (var r in BuiltInRules.All())
            foreach (var fk in r.FileKeys)
            {
                string? dir = Env.Expand(fk.Dir);
                Assert.NotNull(dir);
                Assert.True(CleanSafety.DirAllowed(dir!, fk.Recurse, fk.RemoveSelf, fk.Patterns, P), $"{r.Name}: {fk.Dir}");
                Assert.False(CleanSafety.HasBlockedWord(dir!), $"{r.Name}: {fk.Dir}");
            }
    }

    [Fact]
    public void Toda_chave_das_regras_proprias_e_permitida()
    {
        foreach (var r in BuiltInRules.All())
            foreach (var rk in r.RegKeys)
                Assert.True(CleanSafety.RegKeyAllowed(rk.Key.Replace("*", "ADAL_x")), $"{r.Name}: {rk.Key}");
    }

    [Fact]
    public void Regras_do_Windows_com_acao_nao_mexem_em_arquivos()
    {
        var actions = BuiltInRules.All().Where(r => r.Action != CleanAction.None).ToList();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, r => Assert.Empty(r.FileKeys.Concat<object>(r.RegKeys)));
    }

    [Fact]
    public void Acha_rastros_do_Windows_e_do_Office()
    {
        var sys = new FakeCleanSystem()
            .File($@"{P.Roaming}\Microsoft\Windows\Recent\relatorio.lnk", 1000).File($@"{P.Roaming}\Microsoft\Windows\Recent\desktop.ini")
            .File($@"{P.Roaming}\Microsoft\Windows\Recent\AutomaticDestinations\x.automaticDestinations-ms", 2000)
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", ("a", "cmd"))
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Excel\File MRU", ("Item 1", "x"))
            .Key(@"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Excel\User MRU\LiveId_1\File MRU", ("Item 1", "x"));
        var engine = new CleanEngine(sys, new CleanContext(P, Env, new Version(10, 0)));
        var plan = engine.PlanAll(BuiltInRules.All());
        var recent = Assert.Single(plan, e => e.Name == "Documentos recentes");
        Assert.Equal(2, recent.FileCount);
        Assert.Equal(3000, recent.Bytes);
        Assert.Equal(1, Assert.Single(plan, e => e.Name.StartsWith("Executar")).RegCount);
        Assert.Equal(2, Assert.Single(plan, e => e.Category == CleanCategory.Office).RegCount);
        Assert.Contains(plan, e => e.Rule.Action == CleanAction.FlushDns); // ações aparecem sempre
        Assert.DoesNotContain(plan, e => e.Name.StartsWith("Despejos")); // sem arquivos, sem linha
    }
}

public class JunkAndDiskCleanupTests
{
    private static readonly KnownPlaces P = LeftoverScannerTests.Places;
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0);

    [Theory]
    [InlineData("a.tmp", 5, false, JunkKind.Temporary)]
    [InlineData("a.TEMP", 5, false, JunkKind.Temporary)]
    [InlineData("FILE0001.CHK", 5, false, JunkKind.CheckDisk)]
    [InlineData("velho.dmp", 40, false, JunkKind.OldDump)]
    [InlineData("recente.dmp", 5, false, null)]
    [InlineData("config.bak", 5, false, JunkKind.Backup)]
    [InlineData("vida.old", 5, false, JunkKind.OldFile)]
    [InlineData("~$Relatorio.docx", 5, false, JunkKind.OfficeLock)]
    [InlineData("~$Relatorio.docx", 0, false, null)]
    [InlineData("~$Relatorio.docx", 5, true, JunkKind.OfficeLock)]
    [InlineData("a.tmp", 5, true, null)]
    [InlineData("config.bak", 5, true, null)]
    [InlineData("desktop.ini", 5, false, null)]
    [InlineData("foto.jpg", 5, false, null)]
    public void Classifica_pelo_nome(string name, int ageDays, bool userContent, JunkKind? expected) =>
        Assert.Equal(expected, JunkRules.Classify(name, Now.AddDays(-ageDays).AddHours(-1), Now, userContent));

    [Fact]
    public void Busca_respeita_as_pastas_protegidas_e_as_indicadas()
    {
        var sys = new FakeCleanSystem()
            .File(@"C:\Dados\a.tmp", 10).File(@"C:\Dados\b.bak", 20).File(@"C:\Dados\FOUND.000\FILE0000.CHK", 30)
            .File(@"C:\Windows\Temp\w.tmp").File(@"C:\Program Files\App\p.tmp").File(@"C:\Users\ana\Documents\d.tmp").File(@"C:\Users\ana\Documents\~$d.docx", 5, Now.AddDays(-3))
            .File(@"C:\Dados\Ignorar\i.tmp").File(@"C:\Dados\node_modules\n.tmp").File(@"C:\Dados\.git\g.tmp");
        var items = JunkRules.Scan(sys, P, [@"C:\"], [@"C:\Dados\Ignorar"], Now);
        var paths = items.Select(i => i.Path).ToList();
        Assert.Contains(@"C:\Dados\a.tmp", paths);
        Assert.Contains(@"C:\Dados\b.bak", paths);
        Assert.Contains(@"C:\Dados\FOUND.000", paths);
        Assert.Contains(@"C:\Users\ana\Documents\~$d.docx", paths);
        Assert.DoesNotContain(paths, p => p.Contains("Windows") || p.Contains("Program Files") || p.EndsWith("d.tmp") || p.Contains("Ignorar") || p.Contains("node_modules") || p.Contains(".git"));
        Assert.True(items.Single(i => i.Path.EndsWith("a.tmp")).Selected);
        Assert.False(items.Single(i => i.Path.EndsWith("b.bak")).Selected);
        Assert.False(items.Single(i => i.Kind == JunkKind.LostFolder).Selected);
        Assert.DoesNotContain(paths, p => p.Contains("FOUND.000\\")); // a pasta é um item só
    }

    [Theory]
    [InlineData("FOUND.000", true)]
    [InlineData("found.012", true)]
    [InlineData("FOUND.0", false)]
    [InlineData("FOUND.abc", false)]
    [InlineData("Found Files", false)]
    public void Pastas_do_Chkdsk(string name, bool expected) => Assert.Equal(expected, JunkRules.IsLostFolder(name));

    [Fact]
    public void Limpeza_de_Disco_bloqueia_Downloads_e_Lixeira()
    {
        var modules = DiskCleanupRules.Modules(["Thumbnail Cache", "DownloadsFolder", "Recycle Bin", "Previous Installations", "Modulo Novo", "Temporary Files", ""]);
        Assert.DoesNotContain(modules, m => m.Key is "DownloadsFolder" or "Recycle Bin");
        Assert.Equal(ModuleRisk.Care, modules.Single(m => m.Key == "Previous Installations").Risk);
        Assert.Equal(ModuleRisk.Care, modules.Single(m => m.Key == "Modulo Novo").Risk);
        Assert.Equal(ModuleRisk.Safe, modules.Single(m => m.Key == "Temporary Files").Risk);
        Assert.All(modules, m => Assert.False(m.Selected));
        Assert.Equal(modules.OrderBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase).Select(m => m.Key), modules.Select(m => m.Key));
        Assert.Equal("/sagerun:7771", DiskCleanupRules.Arguments);
    }
}
