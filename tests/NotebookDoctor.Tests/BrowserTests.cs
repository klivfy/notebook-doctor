using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class SiteNameTests
{
    [Theory]
    [InlineData("Google.com", "google.com")]
    [InlineData("https://www.Exemplo.com.br/caminho?x=1#a", "exemplo.com.br")]
    [InlineData(".mail.google.com", "mail.google.com")]
    [InlineData("localhost:8080", "localhost")]
    [InlineData("http://user:senha@banco.test:443/x", "banco.test")]
    [InlineData("loja_1.test", "loja_1.test")]
    [InlineData("", null)]
    [InlineData("a'b.com", null)]
    [InlineData("a b.com", null)]
    [InlineData("caf\u00e9.com", null)]
    [InlineData("x.com'; DROP TABLE cookies;--", null)]
    [InlineData("a..com", null)]
    [InlineData("%", null)]
    public void Normaliza_o_nome_do_site(string input, string? expected) => Assert.Equal(expected, SiteName.Normalize(input));

    [Theory]
    [InlineData(".mail.google.com", "google.com")]
    [InlineData("google.com", "google.com")]
    [InlineData("www.exemplo.com.br", "exemplo.com.br")]
    [InlineData(".loja.com.br", "loja.com.br")]
    [InlineData("a.b.c.d.org", "d.org")]
    [InlineData("localhost", "localhost")]
    [InlineData("192.168.0.10", "192.168.0.10")]
    public void Site_de_um_host_de_cookie(string host, string expected) => Assert.Equal(expected, SiteName.Of(host));
}

public class BrowserPlanTests
{
    private static readonly BrowserProfile Chrome = new(BrowserCatalog.Chrome, "Default", @"C:\Users\ana\AppData\Local\Google\Chrome\User Data\Default");
    private static readonly BrowserProfile Fox = new(BrowserCatalog.Firefox, "default-release", @"C:\Users\ana\AppData\Roaming\Mozilla\Firefox\Profiles\abc.default-release");
    private static readonly BrowserDataKind[] All = Enum.GetValues<BrowserDataKind>();

    [Fact]
    public void Nao_existe_opcao_para_senhas_nem_favoritos()
    {
        Assert.DoesNotContain(Enum.GetNames<BrowserDataKind>(), n => n.Contains("Password", StringComparison.OrdinalIgnoreCase) || n.Contains("Bookmark", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("firefox")]
    public void Todo_passo_montado_passa_nas_barreiras(string id)
    {
        var profile = id == "chrome" ? Chrome : Fox;
        var steps = BrowserPlans.For(profile, All, ["google.com", "loja_1.test"]);
        Assert.NotEmpty(steps);
        Assert.All(steps, s => Assert.True(BrowserSafety.IsAllowedStep(s)));
        Assert.Equal(steps.Count, BrowserPlans.For(profile, All, ["google.com"]).Count); // a lista de sites não cria nem tira passos
    }

    [Fact]
    public void Cookies_com_e_sem_lista_de_sites()
    {
        var all = Assert.IsType<SqlStep>(BrowserPlans.For(Fox, [BrowserDataKind.Cookies], []).Single());
        Assert.Equal(["DELETE FROM moz_cookies"], all.Statements);
        var kept = Assert.IsType<SqlStep>(BrowserPlans.For(Fox, [BrowserDataKind.Cookies], ["google.com", "a_b.test"]).Single());
        string sql = Assert.Single(kept.Statements);
        Assert.StartsWith("DELETE FROM moz_cookies WHERE NOT (", sql);
        Assert.Contains("host = 'google.com' OR host LIKE '%.google.com' ESCAPE '\\'", sql);
        Assert.Contains(@"LIKE '%.a\_b.test'", sql);
        var chrome = BrowserPlans.For(Chrome, [BrowserDataKind.Cookies], ["google.com"]).OfType<SqlStep>().ToList();
        Assert.Equal([@"Network\Cookies", "Cookies"], chrome.Select(s => s.Db));
        Assert.All(chrome, s => Assert.Contains("host_key", s.Statements[0]));
    }

    [Fact]
    public void Nome_malicioso_na_lista_de_sites_nunca_chega_ao_SQL()
    {
        Assert.Equal("", BrowserPlans.KeepClause("host_key", ["x'; DROP TABLE cookies;--", "a b", ""]));
        string clause = BrowserPlans.KeepClause("host_key", ["x'; DROP TABLE cookies;--", "ok.com"]);
        Assert.DoesNotContain("DROP", clause);
        Assert.Contains("'ok.com'", clause);
        // lista com nomes, mas nenhum válido: os cookies ficam como estão (nunca vira "apagar todos")
        Assert.Empty(BrowserPlans.For(Fox, [BrowserDataKind.Cookies], ["x'; DROP"]));
        Assert.Empty(BrowserPlans.For(Chrome, [BrowserDataKind.Cookies], ["a b"]));
        var step = Assert.IsType<SqlStep>(BrowserPlans.For(Fox, [BrowserDataKind.Cookies], ["x'; DROP", "ok.com"]).Single());
        Assert.DoesNotContain(";", step.Statements[0]);
    }

    [Fact]
    public void Firefox_nao_apaga_o_banco_inteiro_de_historico()
    {
        var steps = BrowserPlans.For(Fox, [BrowserDataKind.History], []);
        Assert.Single(steps);
        Assert.IsType<SqlStep>(steps[0]); // places.sqlite guarda os favoritos
        Assert.All(((SqlStep)steps[0]).Statements, q => Assert.DoesNotContain("moz_bookmarks WHERE", q.Replace("SELECT fk FROM moz_bookmarks WHERE fk IS NOT NULL", "")));
    }

    [Theory]
    [InlineData("Login Data")]
    [InlineData(@"Default\Login Data")]
    [InlineData("logins.json")]
    [InlineData("key4.db")]
    [InlineData("Bookmarks")]
    [InlineData("places.sqlite")]
    [InlineData("Preferences")]
    [InlineData("Web Data")]
    [InlineData(@"..\Local State")]
    [InlineData(@"C:\Windows\x")]
    [InlineData(@"\\servidor\x")]
    [InlineData(@"Extensions\abc\manifest.json")]
    [InlineData("")]
    public void Arquivos_que_nunca_saem(string relative) => Assert.False(BrowserSafety.IsAllowedStep(new DeleteFilesStep(BrowserDataKind.History, [relative])));

    [Theory]
    [InlineData("Login Data", "DELETE FROM logins")]
    [InlineData("places.sqlite", "DELETE FROM moz_bookmarks")]
    [InlineData("places.sqlite", "DELETE FROM moz_places WHERE 1; DROP TABLE moz_bookmarks")]
    [InlineData("Web Data", "DELETE FROM credit_cards")]
    [InlineData("Web Data", "DELETE FROM autofill_profiles")]
    [InlineData("Web Data", "DROP TABLE autofill")]
    [InlineData("Web Data", "DELETE FROM autofill; DELETE FROM credit_cards")]
    [InlineData(@"..\x", "DELETE FROM cookies")]
    public void Comandos_que_nunca_rodam(string db, string sql) => Assert.False(BrowserSafety.IsAllowedStep(new SqlStep(BrowserDataKind.FormHistory, db, [sql])));

    [Fact]
    public void Descobre_navegadores_e_perfis()
    {
        var p = LeftoverScannerTests.Places;
        var sys = new FakeCleanSystem()
            .File($@"{p.Local}\Google\Chrome\User Data\Default\Preferences").File($@"{p.Local}\Google\Chrome\User Data\Profile 1\Preferences")
            .File($@"{p.Local}\Google\Chrome\User Data\System Profile\Preferences").Dir($@"{p.Local}\Google\Chrome\User Data\Crashpad")
            .File($@"{p.Local}\Microsoft\Edge\User Data\Default\Preferences")
            .File($@"{p.Roaming}\Opera Software\Opera Stable\Preferences")
            .File($@"{p.Roaming}\Mozilla\Firefox\Profiles\x1y2.default-release\prefs.js").File($@"{p.Roaming}\Mozilla\Firefox\Profiles\lixo\arquivo.txt");
        var found = BrowserProfiles.Discover(p, sys);
        Assert.Equal(["chrome|Padrão", "chrome|Profile 1", "edge|Padrão", "opera|Padrão", "firefox|default-release"], found.Select(f => f.Key));
    }
}

public class BrowserWatchTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 12, 0, 0);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);
    private static HashSet<string> Set(params string[] ids) => new(ids);

    [Fact]
    public void Avisa_uma_vez_depois_do_tempo_de_calma()
    {
        var w = new BrowserWatch();
        Assert.Empty(w.Step(Set("chrome"), T0, Settle));
        Assert.Empty(w.Step(Set(), T0.AddSeconds(5), Settle));              // sumiu agora
        Assert.Empty(w.Step(Set(), T0.AddSeconds(10), Settle));             // 5 s de calma
        Assert.Equal(["chrome"], w.Step(Set(), T0.AddSeconds(16), Settle)); // 11 s
        Assert.Empty(w.Step(Set(), T0.AddSeconds(30), Settle));             // não repete
    }

    [Fact]
    public void Navegador_que_reabre_logo_em_seguida_nao_conta()
    {
        var w = new BrowserWatch();
        w.Step(Set("edge"), T0, Settle);
        w.Step(Set(), T0.AddSeconds(2), Settle);
        Assert.Empty(w.Step(Set("edge"), T0.AddSeconds(4), Settle));
        Assert.Empty(w.Step(Set("edge"), T0.AddSeconds(60), Settle));
        Assert.Empty(w.Step(Set(), T0.AddSeconds(61), Settle));
        Assert.Equal(["edge"], w.Step(Set(), T0.AddSeconds(80), Settle));
    }

    [Fact]
    public void Navegador_que_ja_estava_fechado_no_inicio_nao_dispara()
    {
        var w = new BrowserWatch();
        Assert.Empty(w.Step(Set(), T0, Settle));
        Assert.Empty(w.Step(Set(), T0.AddMinutes(5), Settle));
    }

    [Fact]
    public void Cada_navegador_no_seu_tempo()
    {
        var w = new BrowserWatch();
        w.Step(Set("chrome", "firefox"), T0, Settle);
        w.Step(Set("firefox"), T0.AddSeconds(1), Settle);
        Assert.Equal(["chrome"], w.Step(Set("firefox"), T0.AddSeconds(20), Settle));
        Assert.Equal(["firefox"], w.Step(Set(), T0.AddSeconds(21).AddSeconds(0), Settle).Concat(w.Step(Set(), T0.AddSeconds(40), Settle)));
    }
}

public class ScheduleAndMonitorTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 15, 0, 0);

    [Theory]
    [InlineData(ScheduleKind.Daily, "2026-10-22 08:00", false)]
    [InlineData(ScheduleKind.Daily, "2026-10-21 23:59", true)]
    [InlineData(ScheduleKind.Weekly, "2026-10-16 15:00", false)]
    [InlineData(ScheduleKind.Weekly, "2026-10-15 15:00", true)]
    [InlineData(ScheduleKind.Monthly, "2026-09-23 15:00", false)]
    [InlineData(ScheduleKind.Monthly, "2026-09-22 15:00", true)]
    [InlineData(ScheduleKind.Off, "2020-01-01 00:00", false)]
    public void Quando_a_limpeza_agendada_vence(ScheduleKind kind, string last, bool due) =>
        Assert.Equal(due, ScheduleRules.Due(kind, DateTime.Parse(last, System.Globalization.CultureInfo.InvariantCulture), Now, ranThisSession: false));

    [Fact]
    public void Ao_iniciar_roda_uma_vez_por_sessao()
    {
        Assert.True(ScheduleRules.Due(ScheduleKind.AtStart, Now, Now, ranThisSession: false));
        Assert.False(ScheduleRules.Due(ScheduleKind.AtStart, Now.AddYears(-1), Now, ranThisSession: true));
    }

    [Theory]
    [InlineData("daily", ScheduleKind.Daily)]
    [InlineData("MONTHLY", ScheduleKind.Monthly)]
    [InlineData("start", ScheduleKind.AtStart)]
    [InlineData("off", ScheduleKind.Off)]
    [InlineData("weekly", ScheduleKind.Weekly)]
    [InlineData("", ScheduleKind.Weekly)]
    [InlineData("lixo", ScheduleKind.Weekly)]
    public void Le_o_tipo_salvo(string text, ScheduleKind expected)
    {
        Assert.Equal(expected, ScheduleRules.Parse(text));
        Assert.Equal(expected, ScheduleRules.Parse(ScheduleRules.Key(expected)));
    }

    [Fact]
    public void Monitor_avisa_acima_do_limite_uma_vez_por_dia()
    {
        long limit = JunkMonitorRules.DefaultLimitBytes;
        Assert.False(JunkMonitorRules.ShouldNotify(limit - 1, limit, DateTime.MinValue, Now));
        Assert.True(JunkMonitorRules.ShouldNotify(limit, limit, DateTime.MinValue, Now));
        Assert.False(JunkMonitorRules.ShouldNotify(limit * 2, limit, Now.AddHours(-23), Now));
        Assert.True(JunkMonitorRules.ShouldNotify(limit * 2, limit, Now.AddHours(-24), Now));
        Assert.False(JunkMonitorRules.ShouldNotify(limit * 2, 0, DateTime.MinValue, Now)); // limite 0 = desligado
        Assert.True(JunkMonitorRules.CheckDue(Now.AddMinutes(-60), Now));
        Assert.False(JunkMonitorRules.CheckDue(Now.AddMinutes(-59), Now));
    }
}
