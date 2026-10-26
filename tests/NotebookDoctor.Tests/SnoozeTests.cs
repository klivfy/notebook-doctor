using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

internal sealed class FakeSnoozeSystem : ISnoozeSystem
{
    public List<string> Calls { get; } = [];
    public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Killable { get; } = new(StringComparer.OrdinalIgnoreCase);

    private void Check(string id) { if (Denied.Contains(id)) throw new UnauthorizedAccessException(); }

    public void SetStartup(string approvalSubKey, string name, bool enabled) { Check(name); Calls.Add($"startup:{approvalSubKey}:{name}:{enabled}"); }
    public void SetServiceStart(string name, int start) { Check(name); Calls.Add($"service:{name}:{start}"); }
    public void SetTask(string path, bool enabled) { Check(path); Calls.Add($"task:{path}:{enabled}"); }
    public int Kill(string processName) { Calls.Add($"kill:{processName}"); return Killable.GetValueOrDefault(processName); }
}

public class SnoozeRulesTests
{
    private const string Win = @"C:\Windows";

    private static ServiceInfo Svc(string name, string image, int start = 2) => new(name, name, image, start);
    private static ScheduledTaskInfo Task(string path, string command, bool enabled = true) =>
        new(path, path.TrimStart('\\'), "", command, enabled, AtLogon: true, AtBoot: false, Repeats: false);

    private static List<SnoozeApp> Apps(IEnumerable<string>? never = null) => SnoozeRules.Build(
        startup:
        [
            new("Discord", "Run", @"C:\Users\a\AppData\Local\Discord\Update.exe --processStart Discord.exe", @"C:\Users\a\AppData\Local\Discord\Update.exe", true),
            new("Steam", "Run", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", @"C:\Program Files (x86)\Steam\steam.exe", true),
            new("OneDrive", "Run", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe /background", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", true),
            new("SecurityHealth", "Run", @"C:\Windows\System32\SecurityHealthSystray.exe", @"C:\Windows\System32\SecurityHealthSystray.exe", true),
            new("Desligado", "Run", @"C:\Program Files\Outro\outro.exe", @"C:\Program Files\Outro\outro.exe", false),
            new("RealtekAudio", "Run", @"C:\Program Files\Realtek\Audio\RtkNGUI64.exe", @"C:\Program Files\Realtek\Audio\RtkNGUI64.exe", true),
            new("AnyDesk", "Run", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe --tray", @"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", true),
        ],
        services:
        [
            Svc("SteamService", @"""C:\Program Files (x86)\Common Files\Steam\steamservice.exe"" /RunAsService"),
            Svc("DiscordUpdater", @"""C:\Users\a\AppData\Local\Discord\updater.exe"""),
            Svc("WinMgmt", @"C:\Windows\system32\svchost.exe -k netsvcs"),                  // do Windows: nunca
            Svc("AdobeUpdateService", @"C:\Program Files\Common Files\Adobe\AdobeUpdateService.exe", start: 3), // já manual
        ],
        tasks:
        [
            Task(@"\SteamTelemetry", @"C:\Program Files (x86)\Steam\bin\telemetry.exe"),
            Task(@"\Microsoft\Windows\Defrag\ScheduledDefrag", @"C:\Windows\system32\defrag.exe"),
            Task(@"\DiscordUpdate", @"C:\Users\a\AppData\Local\Discord\Update.exe"),             // atualizador: fica como está
        ],
        processes:
        [
            new("steamwebhelper", @"C:\Program Files (x86)\Steam\bin\cef\steamwebhelper.exe", 300, 120, HasWindow: false, Count: 4),
            new("steam", @"C:\Program Files (x86)\Steam\steam.exe", 150, 60, HasWindow: false, Count: 1),
            new("Discord", @"C:\Users\a\AppData\Local\Discord\app-1.0\Discord.exe", 400, 90, HasWindow: true, Count: 1),
            new("svchost", @"C:\Windows\System32\svchost.exe", 50, 5, HasWindow: false, Count: 20),
            new("random", @"C:\Tools\random.exe", 80, 1, HasWindow: false, Count: 1),             // sem nada que rode sozinho: ignorado
        ],
        Win, never ?? []);

    private static SnoozeApp App(string name, IEnumerable<string>? never = null) => Apps(never).Single(a => a.Name == name);

    [Fact]
    public void Junta_inicializacao_servico_tarefa_e_processos_do_mesmo_aplicativo()
    {
        var steam = App("Steam");
        Assert.Single(steam.Startup);
        Assert.Equal(["SteamService"], steam.Services.Select(s => s.Name));
        Assert.Equal(["SteamTelemetry"], steam.Tasks.Select(t => t.Name));
        Assert.Equal(2, steam.Background.Count);                      // steam e steamwebhelper, ambos sem janela
        Assert.Equal(450, steam.RamMb);
        Assert.Equal(180, steam.CpuSeconds);
        Assert.Contains("steam", steam.MainExes);
        Assert.Null(steam.Block);
        Assert.True(steam.CanSleep);
    }

    [Fact]
    public void Programa_com_janela_aberta_esta_em_uso_e_nao_dorme()
    {
        var discord = App("Discord");
        Assert.True(discord.InUse);
        Assert.False(discord.CanSleep);
        Assert.Empty(discord.Tasks);                                  // o atualizador fica como está
        Assert.Equal(["DiscordUpdater"], discord.Services.Select(s => s.Name));
    }

    [Fact]
    public void So_entra_o_que_roda_sozinho_de_terceiros_e_ligado()
    {
        var all = Apps();
        Assert.DoesNotContain(all, a => a.Startup.Any(s => s.Name == "Desligado"));                // já desligado
        Assert.DoesNotContain(all, a => a.Services.Any(s => s.Name is "WinMgmt" or "AdobeUpdateService"));
        Assert.DoesNotContain(all, a => a.Startup.Any(s => s.Name == "SecurityHealth"));           // dentro do Windows
        Assert.DoesNotContain(all, a => a.Background.Any(p => p.Name is "svchost" or "random"));
    }

    [Theory]
    [InlineData("RtkNGUI64")]
    [InlineData("AnyDesk")]
    public void Audio_e_acesso_remoto_nunca_dormem(string app)
    {
        var a = Apps().Single(x => SnoozeRules.KeyOf(x.Name) == SnoozeRules.KeyOf(app) || x.Startup.Any(s => s.ExePath.Contains(app, StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(a.Block);
        Assert.False(a.CanSleep);
    }

    [Fact]
    public void Sincronizacao_dorme_mas_com_aviso()
    {
        var onedrive = App("OneDrive");
        Assert.Null(onedrive.Block);
        Assert.NotNull(onedrive.Warn);
        Assert.True(onedrive.CanSleep);
        Assert.Null(App("Steam").Warn);
    }

    [Fact]
    public void Lista_do_usuario_de_nunca_adormecer_vale_sem_diferenciar_forma()
    {
        Assert.Null(App("Steam").Block);
        Assert.NotNull(App("Steam", never: ["STEAM"]).Block);
        Assert.NotNull(App("Steam", never: ["Steam "]).Block);
        Assert.Null(App("OneDrive", never: ["Steam"]).Block);
    }

    [Fact]
    public void O_proprio_app_nunca_dorme()
    {
        var self = SnoozeRules.Build([new("Notebook Doctor", "Run", @"C:\Tools\NotebookDoctor.exe", @"C:\Tools\NotebookDoctor.exe", true)], [], [], [], Win, []);
        Assert.NotNull(self.Single().Block);
        Assert.False(self.Single().CanSleep);
    }

    [Fact]
    public void Palavra_parecida_nao_bloqueia_so_a_palavra_inteira()
    {
        // "intellij" não é "intel"; "Remoto" não é "remote"
        var ide = SnoozeRules.Build([new("IntelliJ IDEA", "Run", @"C:\Program Files\JetBrains\IntelliJ\bin\idea64.exe", @"C:\Program Files\JetBrains\IntelliJ\bin\idea64.exe", true)], [], [], [], Win, []);
        Assert.Null(ide.Single().Block);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "steamwebhelper", "Steam")]                  // família conhecida pelo nome
    [InlineData(@"C:\Program Files\JetBrains\IntelliJ\bin\idea64.exe", "x", "JetBrains")]                // pasta logo abaixo de Program Files
    [InlineData(@"C:\Users\a\AppData\Local\Programs\Foo App\foo.exe", "foo", "Foo App")]
    [InlineData(@"C:\Program Files\Common Files\Vendor\helper.exe", "helper", "Vendor")]                // pasta compartilhada não é o app
    [InlineData(@"C:\Tools\solto.exe", "solto", "solto")]                                                // sem pasta de app: o nome do arquivo
    [InlineData("", "Alguma Coisa.exe", "Alguma Coisa")]
    public void Nome_do_aplicativo(string path, string name, string expected) => Assert.Equal(expected, SnoozeRules.AppNameOf(path, name));

    // ---------- motor ----------

    [Fact]
    public void Dormir_desliga_cada_peca_e_fecha_o_fundo_guardando_como_voltar()
    {
        var sys = new FakeSnoozeSystem();
        sys.Killable["steamwebhelper"] = 4; sys.Killable["steam"] = 1;
        var (record, result) = SnoozeEngine.Sleep(App("Steam"), sys, killBackground: true, wakeOnOpen: true, new DateTime(2026, 10, 22));
        Assert.Equal(3, result.Done);                                 // 1 inicialização + 1 serviço + 1 tarefa
        Assert.Equal(0, result.Failed);
        Assert.Equal(5, result.Killed);
        Assert.All(record.Items, i => Assert.True(i.Applied));
        Assert.Contains("startup:Run:Steam:False", sys.Calls);
        Assert.Contains("service:SteamService:3", sys.Calls);
        Assert.Contains(@"task:\SteamTelemetry:False", sys.Calls);
        Assert.Equal(450, record.RamMb);
        Assert.True(record.WakeOnOpen);
    }

    [Fact]
    public void Acordar_devolve_o_valor_de_antes_de_cada_peca()
    {
        var sys = new FakeSnoozeSystem();
        var (record, _) = SnoozeEngine.Sleep(App("Steam"), sys, killBackground: false, wakeOnOpen: false, DateTime.Now);
        sys.Calls.Clear();
        var woke = SnoozeEngine.Wake(record, sys);
        Assert.Equal(3, woke.Done);
        Assert.Contains("startup:Run:Steam:True", sys.Calls);
        Assert.Contains("service:SteamService:2", sys.Calls);       // o tipo de início de antes, não "manual"
        Assert.Contains(@"task:\SteamTelemetry:True", sys.Calls);
        Assert.True(SnoozeEngine.FullyAwake(record));
    }

    [Fact]
    public void Servico_sem_administrador_nao_e_marcado_como_desligado_e_nada_se_perde()
    {
        var sys = new FakeSnoozeSystem();
        sys.Denied.Add("SteamService");
        var (record, result) = SnoozeEngine.Sleep(App("Steam"), sys, killBackground: false, wakeOnOpen: true, DateTime.Now);
        Assert.Equal(2, result.Done);
        Assert.Equal(1, result.Failed);
        Assert.True(result.NeedsAdmin);
        Assert.False(record.Items.Single(i => i.Kind == "service").Applied);   // continua automático
        sys.Calls.Clear();
        SnoozeEngine.Wake(record, sys);
        Assert.DoesNotContain(sys.Calls, c => c.StartsWith("service:"));       // só devolve o que desligou
    }

    [Fact]
    public void Acordar_com_falha_mantem_a_peca_para_tentar_de_novo()
    {
        var sys = new FakeSnoozeSystem();
        var (record, _) = SnoozeEngine.Sleep(App("Steam"), sys, false, false, DateTime.Now);
        sys.Denied.Add("SteamService");
        var woke = SnoozeEngine.Wake(record, sys);
        Assert.Equal(1, woke.Failed);
        Assert.False(SnoozeEngine.FullyAwake(record));
        Assert.True(record.Items.Single(i => i.Kind == "service").Applied);
        sys.Denied.Clear();
        Assert.Equal(1, SnoozeEngine.Wake(record, sys).Done);
        Assert.True(SnoozeEngine.FullyAwake(record));
    }

    [Fact]
    public void Voltar_a_dormir_e_idempotente()
    {
        var sys = new FakeSnoozeSystem();
        var (record, _) = SnoozeEngine.Sleep(App("Steam"), sys, false, true, DateTime.Now);
        SnoozeEngine.Wake(record, sys);
        record.Awake = true;
        sys.Calls.Clear();
        var again = SnoozeEngine.Apply(record, sys);
        Assert.Equal(3, again.Done);
        Assert.False(record.Awake);
        sys.Calls.Clear();
        Assert.Equal(0, SnoozeEngine.Apply(record, sys).Done);                  // tudo já desligado: nada a fazer
        Assert.Empty(sys.Calls);
    }

    [Fact]
    public void Aplicativo_que_nao_pode_dormir_nao_dorme()
    {
        Assert.Throws<InvalidOperationException>(() => SnoozeEngine.Sleep(App("Discord"), new FakeSnoozeSystem(), true, true, DateTime.Now)); // aberto
        Assert.Throws<InvalidOperationException>(() => SnoozeEngine.Sleep(App("Steam", never: ["steam"]), new FakeSnoozeSystem(), true, true, DateTime.Now));
    }

    // ---------- vigia ----------

    private static SnoozedApp Snoozed(string key = "steam", bool wake = true, bool awake = false) =>
        new() { Key = key, Name = key, WakeOnOpen = wake, Awake = awake, MainExes = ["steam"] };

    private static HashSet<string> Running(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Abriu_o_programa_principal_acorda_e_ajudante_sozinho_nao_acorda()
    {
        var apps = new[] { Snoozed() };
        var none = new Dictionary<string, int>();
        var d = SnoozeRules.Decide(apps, Running("steam"), none);
        Assert.Single(d);
        Assert.Equal(WatchAction.Wake, d[0].Action);
        Assert.Empty(SnoozeRules.Decide(apps, Running("steamwebhelper", "chrome"), none));
        Assert.Empty(SnoozeRules.Decide(apps, Running(), none));
    }

    [Fact]
    public void Sem_acordar_ao_abrir_o_vigia_nao_faz_nada()
    {
        Assert.Empty(SnoozeRules.Decide([Snoozed(wake: false)], Running("steam"), new Dictionary<string, int>()));
    }

    [Fact]
    public void Volta_a_dormir_so_depois_de_varias_verificacoes_seguidas_sem_o_programa()
    {
        var app = Snoozed(awake: true);
        var absent = new Dictionary<string, int>();
        // Fechou: conta 1, 2, 3; só na terceira volta a dormir
        for (int i = 1; i <= SnoozeRules.ResnoozeAfter; i++)
        {
            absent = SnoozeRules.CountAbsent([app], Running(), absent);
            var d = SnoozeRules.Decide([app], Running(), absent);
            if (i < SnoozeRules.ResnoozeAfter) Assert.Empty(d);
            else Assert.Equal(WatchAction.Resnooze, Assert.Single(d).Action);
        }
        // Reabriu no meio: a contagem zera
        absent = SnoozeRules.CountAbsent([app], Running("steam"), absent);
        Assert.Equal(0, absent["steam"]);
        Assert.Empty(SnoozeRules.Decide([app], Running("steam"), absent));
    }

    [Fact]
    public void Contagem_so_vale_para_quem_esta_acordado()
    {
        var absent = SnoozeRules.CountAbsent([Snoozed(awake: false), Snoozed("outro", awake: true)], Running(), new Dictionary<string, int>());
        Assert.DoesNotContain("steam", absent.Keys);
        Assert.Equal(1, absent["outro"]);
    }
}

[SupportedOSPlatform("windows")]
public class SnoozeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-snooze-" + Guid.NewGuid().ToString("N"));
    private string File => Path.Combine(_dir, "snooze.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static SnoozedApp App(string key) => new()
    {
        Key = key, Name = key, Since = new DateTime(2026, 10, 22, 9, 30, 0), WakeOnOpen = true, RamMb = 450, MainExes = ["steam"],
        Items = [new SnoozeItem { Kind = "service", Id = "SteamService", Prev = 2, Applied = true }, new SnoozeItem { Kind = "startup", Id = "Steam", Sub = "Run", Prev = 1, Applied = true }],
    };

    [WindowsFact]
    public void Grava_le_troca_e_remove_sem_deixar_arquivo_temporario()
    {
        var store = new SnoozeStore(File);
        Assert.Empty(store.Load());
        store.Save(App("steam"));
        store.Save(App("discord"));
        var changed = App("steam");
        changed.Awake = true;
        store.Save(changed);                                   // troca, não duplica
        var list = store.Load();
        Assert.Equal(2, list.Count);
        var steam = list.Single(a => a.Key == "steam");
        Assert.True(steam.Awake);
        Assert.Equal(2, steam.Items.Count);
        Assert.Equal(2, steam.Items.Single(i => i.Kind == "service").Prev);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), steam.Since);
        store.Remove("steam");
        Assert.Single(store.Load());
        store.Remove("nao-existe");
        Assert.False(System.IO.File.Exists(File + ".tmp"));
    }

    [WindowsFact]
    public void Arquivo_estragado_ou_ausente_vira_lista_vazia_e_nao_quebra()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "isto não é json");
        var store = new SnoozeStore(File);
        Assert.Empty(store.Load());
        store.Save(App("steam"));                              // e dá para gravar por cima
        Assert.Single(store.Load());
    }

    [WindowsFact]
    public void Mudanca_sob_trava_so_grava_quando_algo_mudou()
    {
        var store = new SnoozeStore(File);
        store.Update(_ => false);
        Assert.False(System.IO.File.Exists(File));
        store.Update(list => { list.Add(App("steam")); return true; });
        Assert.True(System.IO.File.Exists(File));
    }
}
