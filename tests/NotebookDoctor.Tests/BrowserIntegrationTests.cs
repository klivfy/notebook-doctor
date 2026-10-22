using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>Perfis de mentira com bancos SQLite de verdade (winsqlite3.dll), na CI do Windows.</summary>
[SupportedOSPlatform("windows")]
public class BrowserIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NDBrowser" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Make(string relative, string? text = null)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (text is not null) File.WriteAllText(path, text);
        return path;
    }

    private string Db(string relative, params string[] sql)
    {
        string path = Make(relative);
        using var db = SqliteDb.Open(path, create: true);
        foreach (var s in sql) db.Execute(s);
        return path;
    }

    private static List<string[]> Rows(string path, string sql)
    {
        using var db = SqliteDb.Open(path, readOnly: true);
        return db.Query(sql);
    }

    [WindowsFact]
    public void Chromium_limpa_so_o_pedido_e_mantem_os_sites_da_lista()
    {
        string profileDir = Path.Combine(_root, "Chrome", "Default");
        var profile = new BrowserProfile(BrowserCatalog.Chrome, "Padrão", profileDir);
        string cookies = Db(@"Chrome\Default\Network\Cookies",
            "CREATE TABLE cookies (host_key TEXT, name TEXT, value TEXT)",
            "INSERT INTO cookies VALUES ('.google.com','a','1'), ('mail.google.com','b','2'), ('.exemplo.com','c','3'), ('banco_1.test','d','4'), ('.notgoogle.com','e','5')");
        string web = Db(@"Chrome\Default\Web Data",
            "CREATE TABLE autofill (name TEXT, value TEXT)", "INSERT INTO autofill VALUES ('email','a@b.c')",
            "CREATE TABLE autofill_profiles (guid TEXT)", "INSERT INTO autofill_profiles VALUES ('endereco-salvo')",
            "CREATE TABLE credit_cards (guid TEXT)", "INSERT INTO credit_cards VALUES ('cartao')");
        string history = Make(@"Chrome\Default\History", "historico");
        string session = Make(@"Chrome\Default\Sessions\Session_1", "abas");
        string preferences = Make(@"Chrome\Default\Preferences", "{}");
        string bookmarks = Make(@"Chrome\Default\Bookmarks", "{}");
        string logins = Make(@"Chrome\Default\Login Data", "senhas");

        var result = BrowserCleaner.Clean([profile], Enum.GetValues<BrowserDataKind>(), ["google.com"]);

        Assert.Empty(result.Errors);
        Assert.Equal(["a", "b"], Rows(cookies, "SELECT name FROM cookies ORDER BY name").Select(r => r[0])); // ".notgoogle.com" não é do google.com
        Assert.Empty(Rows(web, "SELECT * FROM autofill"));
        Assert.Single(Rows(web, "SELECT * FROM autofill_profiles")); // endereços e cartões salvos ficam
        Assert.Single(Rows(web, "SELECT * FROM credit_cards"));
        Assert.False(File.Exists(history));
        Assert.False(File.Exists(session));
        Assert.True(File.Exists(preferences));
        Assert.True(File.Exists(bookmarks));
        Assert.True(File.Exists(logins));
        Assert.Equal(2, result.Databases);
        Assert.True(result.Files >= 2);
    }

    [WindowsFact]
    public void Firefox_preserva_favoritos_e_so_apaga_o_que_ninguem_referencia()
    {
        string profileDir = Path.Combine(_root, "Fox");
        var profile = new BrowserProfile(BrowserCatalog.Firefox, "default-release", profileDir);
        string places = Db(@"Fox\places.sqlite",
            "CREATE TABLE moz_places (id INTEGER PRIMARY KEY, url TEXT, foreign_count INTEGER DEFAULT 0)",
            "CREATE TABLE moz_historyvisits (id INTEGER PRIMARY KEY, place_id INTEGER)",
            "CREATE TABLE moz_bookmarks (id INTEGER PRIMARY KEY, fk INTEGER, title TEXT)",
            "CREATE TABLE moz_inputhistory (place_id INTEGER, input TEXT)",
            "INSERT INTO moz_places VALUES (1,'https://favorito.test',1), (2,'https://qualquer.test',0)",
            "INSERT INTO moz_historyvisits VALUES (1,1), (2,2)",
            "INSERT INTO moz_bookmarks VALUES (1,1,'Meu favorito')",
            "INSERT INTO moz_inputhistory VALUES (2,'qua')");
        string cookies = Db(@"Fox\cookies.sqlite",
            "CREATE TABLE moz_cookies (host TEXT, name TEXT)", "INSERT INTO moz_cookies VALUES ('.github.com','a'), ('exemplo.com','b')");
        string forms = Db(@"Fox\formhistory.sqlite", "CREATE TABLE moz_formhistory (fieldname TEXT, value TEXT)", "INSERT INTO moz_formhistory VALUES ('q','x')");
        string logins = Make(@"Fox\logins.json", "{}");
        string key4 = Make(@"Fox\key4.db", "chaves");

        var result = BrowserCleaner.Clean([profile], Enum.GetValues<BrowserDataKind>(), ["github.com"]);

        Assert.Empty(result.Errors);
        Assert.Empty(Rows(places, "SELECT * FROM moz_historyvisits"));
        Assert.Empty(Rows(places, "SELECT * FROM moz_inputhistory"));
        Assert.Equal(["https://favorito.test"], Rows(places, "SELECT url FROM moz_places").Select(r => r[0]));
        Assert.Equal(["Meu favorito"], Rows(places, "SELECT title FROM moz_bookmarks").Select(r => r[0]));
        Assert.Equal(["a"], Rows(cookies, "SELECT name FROM moz_cookies").Select(r => r[0]));
        Assert.Empty(Rows(forms, "SELECT * FROM moz_formhistory"));
        Assert.True(File.Exists(logins));
        Assert.True(File.Exists(key4));
    }

    [WindowsFact]
    public void Erro_num_banco_desfaz_so_aquele_banco_e_segue_com_os_outros()
    {
        var profile = new BrowserProfile(BrowserCatalog.Chrome, "Padrão", Path.Combine(_root, "C2", "Default"));
        string web = Db(@"C2\Default\Web Data", "CREATE TABLE outra (x TEXT)", "INSERT INTO outra VALUES ('fica')"); // sem a tabela autofill
        string cookies = Db(@"C2\Default\Network\Cookies", "CREATE TABLE cookies (host_key TEXT)", "INSERT INTO cookies VALUES ('.a.com')");

        var result = BrowserCleaner.Clean([profile], [BrowserDataKind.FormHistory, BrowserDataKind.Cookies], []);

        Assert.Single(result.Errors);
        Assert.Single(Rows(web, "SELECT * FROM outra"));
        Assert.Empty(Rows(cookies, "SELECT * FROM cookies"));
        Assert.Equal(1, result.Databases);
    }

    [WindowsFact]
    public void Lista_os_sites_com_cookies_para_a_pessoa_escolher()
    {
        var profile = new BrowserProfile(BrowserCatalog.Chrome, "Padrão", Path.Combine(_root, "C3", "Default"));
        Db(@"C3\Default\Network\Cookies", "CREATE TABLE cookies (host_key TEXT, name TEXT)",
            "INSERT INTO cookies VALUES ('.google.com','a'), ('mail.google.com','b'), ('.loja.com.br','c')");
        var scan = BrowserCleaner.ReadCookieSites([profile]);
        Assert.Equal(0, scan.LockedProfiles);
        Assert.Equal([("google.com", 2), ("loja.com.br", 1)], scan.Sites);
    }

    [WindowsFact]
    public void Estimativa_conta_os_arquivos_do_pedido()
    {
        var profile = new BrowserProfile(BrowserCatalog.Chrome, "Padrão", Path.Combine(_root, "C4", "Default"));
        Make(@"C4\Default\History", new string('x', 1000));
        Make(@"C4\Default\Preferences", new string('x', 5000));
        Assert.Equal(1000, BrowserCleaner.Estimate(profile, [BrowserDataKind.History], []));
    }

    [WindowsFact]
    public void Detecta_processos_sem_erro()
    {
        var running = BrowserCleaner.RunningIds();
        Assert.All(running, id => Assert.NotNull(BrowserCatalog.ById(id)));
    }
}
