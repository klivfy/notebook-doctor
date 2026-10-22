using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class SamplingTests
{
    [Theory]
    [InlineData(true, true, true, 5, 3)]      // à vista: sempre 3 s, mesmo na bateria
    [InlineData(false, false, false, -1, 15)] // tomada
    [InlineData(false, true, false, 80, 30)]  // bateria
    [InlineData(false, true, true, 80, 60)]   // economia de bateria
    [InlineData(false, true, false, 20, 60)]  // carga baixa
    [InlineData(false, true, false, -1, 30)]  // carga desconhecida
    public void Intervalo_conforme_tela_e_energia(bool onScreen, bool battery, bool saver, int pct, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Sampling.Interval(onScreen, battery, saver, pct));

    [Fact]
    public void Recentes_param_em_36_e_em_15_minutos()
    {
        var now = Make.T0;
        var list = Enumerable.Range(0, 50).Select(i => Make.Snap(time: now.AddSeconds(-15 * (49 - i)))).ToList();
        Sampling.TrimRecent(list, now);
        Assert.Equal(36, list.Count);
        Assert.Equal(now, list[^1].Time);

        // Amostras a cada 60 s: 36 seriam 36 minutos; ficam só os últimos 15
        var slow = Enumerable.Range(0, 36).Select(i => Make.Snap(time: now.AddMinutes(-(35 - i)))).ToList();
        Sampling.TrimRecent(slow, now);
        Assert.Equal(16, slow.Count);
        Assert.True(slow.All(s => now - s.Time <= Sampling.RecentWindow));
    }
}

public class TaskRulesTests
{
    private static ScheduledTaskInfo T(string path, string command = @"C:\Program Files\App\app.exe", string author = "",
                                       bool logon = false, bool boot = false, bool repeats = true, bool enabled = true) =>
        new(path, path.Split('\\')[^1], author, command, enabled, logon, boot, repeats);

    [Fact]
    public void Tarefas_do_Windows_e_desativadas_nao_aparecem()
    {
        var list = TaskRules.Candidates([
            T(@"\Microsoft\Windows\Defrag\ScheduledDefrag"),
            T(@"\CCleanerSkipUAC", enabled: false),
            T(@"\AppDoUsuario"),
        ]);
        Assert.Equal(@"\AppDoUsuario", Assert.Single(list).Task.Path);
    }

    [Fact]
    public void Protecao_hardware_e_atualizadores_ficam()
    {
        Assert.Equal(TaskAdvice.Keep, TaskRules.Classify(T(@"\GoogleUpdateTaskMachineCore")).Advice);
        Assert.Equal(TaskAdvice.Keep, TaskRules.Classify(T(@"\Avast Emergency Update")).Advice);
        Assert.Equal(TaskAdvice.Keep, TaskRules.Classify(T(@"\NvDriverUpdateCheckDaily_{B2FE1952}", author: "NVIDIA Corporation")).Advice);
        Assert.Equal(TaskAdvice.Keep, TaskRules.Classify(T(@"\Intel\Intel Telemetry 3")).Advice); // hardware antes de telemetria
    }

    [Fact]
    public void Coleta_de_uso_pode_desativar_e_vem_primeiro()
    {
        Assert.Equal(TaskAdvice.CanDisable, TaskRules.Classify(T(@"\HP\HP Customer Experience Program")).Advice);
        var list = TaskRules.Candidates([
            T(@"\Zoom Launcher", logon: true),
            T(@"\AppX Telemetry"),
            T(@"\OneDrive Standalone Update Task-S-1-5-21"),
        ]);
        Assert.Equal([@"\AppX Telemetry", @"\Zoom Launcher", @"\OneDrive Standalone Update Task-S-1-5-21"], list.Select(x => x.Task.Path));
    }

    [Fact]
    public void Quando_roda()
    {
        Assert.Equal("ao ligar, ao entrar", TaskRules.When(T(@"\X", boot: true, logon: true, repeats: false)));
        Assert.Equal("em horários", TaskRules.When(T(@"\X")));
        Assert.Equal("sob demanda", TaskRules.When(T(@"\X", repeats: false)));
        Assert.StartsWith("Roda toda vez", TaskRules.Classify(T(@"\Zoom", logon: true)).Reason);
    }
}

public class BrowserExtensionTests
{
    private static BrowserExtension? Parse(string json, Func<string, string?>? msg = null) =>
        BrowserExtensionRules.FromChromiumManifest(json, "Chrome", "Perfil padrão", "abc", msg ?? (_ => null));

    [Fact]
    public void Mv2_com_fundo_persistente_em_todos_os_sites_e_pesada()
    {
        var e = Parse("""{"name":"Bloqueador","version":"1.2","manifest_version":2,"background":{"scripts":["bg.js"]},"permissions":["<all_urls>","storage"]}""")!;
        Assert.True(e.AlwaysOn);
        Assert.True(e.AllSites);
        Assert.StartsWith("Pesada", e.Weight);
    }

    [Fact]
    public void Mv2_nao_persistente_e_mv3_nao_ficam_sempre_carregadas()
    {
        Assert.False(Parse("""{"name":"A","background":{"scripts":["bg.js"],"persistent":false}}""")!.AlwaysOn);
        var mv3 = Parse("""{"name":"B","manifest_version":3,"background":{"service_worker":"sw.js"},"host_permissions":["https://*/*"]}""")!;
        Assert.False(mv3.AlwaysOn);
        Assert.True(mv3.AllSites);
        Assert.StartsWith("Média", mv3.Weight);
    }

    [Fact]
    public void Content_script_em_todos_os_sites_conta()
    {
        var e = Parse("""{"name":"C","content_scripts":[{"matches":["*://*/*"],"js":["c.js"]}]}""")!;
        Assert.True(e.AllSites);
        Assert.Equal("Leve", Parse("""{"name":"D","permissions":["https://mail.google.com/*"]}""")!.Weight);
    }

    [Fact]
    public void Nome_traduzido_e_temas_ficam_de_fora()
    {
        var e = Parse("""{"name":"__MSG_appName__","version":"3"}""", k => k == "appName" ? "Documentos Google offline" : null)!;
        Assert.Equal("Documentos Google offline", e.Name);
        Assert.Equal("abc", Parse("""{"name":"__MSG_x__"}""")!.Name); // sem tradução: fica o id
        Assert.Null(Parse("""{"name":"Tema escuro","theme":{"colors":{}}}"""));
        Assert.Null(Parse("isto não é json"));
    }

    [Fact]
    public void Mensagens_e_ordem_dos_idiomas()
    {
        Assert.Equal("Olá", BrowserExtensionRules.Message("""{"AppName":{"message":"Olá"}}""", "appname"));
        Assert.Null(BrowserExtensionRules.Message("""{"other":{"message":"x"}}""", "appName"));
        Assert.Equal(["pt_BR", "pt", "pt_PT", "en", "en_US", "de"], BrowserExtensionRules.LocaleOrder("de"));
        Assert.Equal("fr", BrowserExtensionRules.DefaultLocale("""{"default_locale":"fr"}"""));
    }

    [Fact]
    public void Firefox_lista_so_as_do_usuario_ativas()
    {
        string json = """
        {"addons":[
          {"id":"ublock@raymondhill.net","type":"extension","location":"app-profile","active":true,"version":"1.60","defaultLocale":{"name":"uBlock Origin"}},
          {"id":"off@x","type":"extension","location":"app-profile","active":false,"defaultLocale":{"name":"Desligada"}},
          {"id":"pictureinpicture@mozilla.org","type":"extension","location":"app-builtin","active":true},
          {"id":"tema@x","type":"theme","location":"app-profile","active":true}
        ]}
        """;
        var e = Assert.Single(BrowserExtensionRules.FromFirefox(json, "default-release"));
        Assert.Equal("uBlock Origin", e.Name);
        Assert.Equal("Firefox", e.Browser);
        Assert.Empty(BrowserExtensionRules.FromFirefox("{}", "x"));
    }

    [Fact]
    public void Mais_pesadas_primeiro()
    {
        var list = BrowserExtensionRules.Sort([
            new("Edge", "p", "1", "Leve", "1", false, false),
            new("Chrome", "p", "2", "Pesada", "1", true, true),
            new("Chrome", "p", "3", "Sites", "1", false, true),
        ]);
        Assert.Equal(["Pesada", "Sites", "Leve"], list.Select(e => e.Name));
    }
}

public class OneDriveTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void Atributos_de_arquivo_so_online()
    {
        Assert.True(OneDriveRule.IsOnlineOnly(OneDriveRule.RecallOnDataAccess | 0x20));
        Assert.True(OneDriveRule.IsOnlineOnly(OneDriveRule.Offline));
        Assert.False(OneDriveRule.IsOnlineOnly(0x20)); // arquivo comum
        Assert.False(OneDriveRule.IsOnlineOnly(OneDriveRule.Pinned | 0x20)); // "sempre manter neste dispositivo": ocupa disco
    }

    [Theory]
    [InlineData(25, 60, true)]  // muito arquivo baixado
    [InlineData(5, 10, true)]   // disco apertado
    [InlineData(5, 40, false)]
    [InlineData(2, 5, false)]   // pouco para fazer diferença
    public void Quando_avisar(long localGb, double freePct, bool warn) =>
        Assert.Equal(warn, OneDriveRule.ShouldWarn(new(@"C:\Users\a\OneDrive", localGb * GB, 0), freePct));

    [Fact]
    public void Achado_no_diagnostico_com_botao_para_a_pasta()
    {
        var h = Make.Health() with { OneDrive = new OneDriveInfo(@"C:\Users\a\OneDrive", 30 * GB, 4 * GB, Partial: true) };
        var f = Assert.Single(Diagnosis.Evaluate(Make.Minute(Make.Snap()), h, null, Make.FreshBoot), x => x.Title.StartsWith("OneDrive"));
        Assert.Equal("OneDrive ocupa 30,0 GB ou mais neste computador", f.Title);
        Assert.Contains("4,0 GB já estão só online", f.Detail);
        Assert.Equal("open-folder", f.FixId);
        Assert.Equal(@"C:\Users\a\OneDrive", f.FixArg);
        Assert.Null(OneDriveRule.Evaluate(null, 5));
    }
}
