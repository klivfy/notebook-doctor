using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Programa que abre junto com o Windows (entrada de Inicialização).</summary>
public sealed record SnoozeStartup(string Name, string ApprovalSubKey, string Command, string ExePath, bool Enabled);

/// <summary>Processo em execução, já somado por nome.</summary>
public sealed record SnoozeProcess(string Name, string ExePath, long MemMb, double CpuSeconds, bool HasWindow, int Count);

/// <summary>Tudo o que um aplicativo deixa rodando sozinho, junto num lugar só.</summary>
public sealed class SnoozeApp
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public List<SnoozeStartup> Startup { get; init; } = [];
    public List<ServiceInfo> Services { get; init; } = [];
    public List<ScheduledTaskInfo> Tasks { get; init; } = [];
    /// <summary>Processos de fundo (sem janela) que fecham quando o app dorme.</summary>
    public List<SnoozeProcess> Background { get; init; } = [];
    /// <summary>Processos com janela: se houver, o programa está em uso e não dorme.</summary>
    public List<SnoozeProcess> Windowed { get; init; } = [];
    /// <summary>Nomes de processo (sem .exe, minúsculos) que, ao abrir, indicam que a pessoa quer usar o programa.</summary>
    public List<string> MainExes { get; init; } = [];
    /// <summary>Motivo para nunca adormecer, ou null.</summary>
    public string? Block { get; init; }
    /// <summary>Aviso extra a mostrar antes (ex.: pára a sincronização), ou null.</summary>
    public string? Warn { get; init; }

    public long RamMb => Background.Sum(p => p.MemMb);
    public double CpuSeconds => Background.Sum(p => p.CpuSeconds);
    public int Parts => Startup.Count + Services.Count + Tasks.Count + Background.Count;
    public bool InUse => Windowed.Count > 0;
    public bool CanSleep => Block is null && !InUse && Parts > 0;
}

/// <summary>Uma peça do aplicativo que o modo soneca desligou, com o jeito de devolver.</summary>
public sealed class SnoozeItem
{
    /// <summary>"startup", "service" ou "task".</summary>
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    /// <summary>Só na inicialização: a subchave do StartupApproved ("Run", "StartupFolder").</summary>
    public string Sub { get; set; } = "";
    /// <summary>Valor a devolver: tipo de início do serviço (2) ou "1" para ligado.</summary>
    public int Prev { get; set; }
    /// <summary>Está desligado agora por causa do soneca.</summary>
    public bool Applied { get; set; }
}

/// <summary>Um aplicativo em soneca. É o que o "Acordar" e o "Desfazer" de Ações usam.</summary>
public sealed class SnoozedApp
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Since { get; set; }
    public bool WakeOnOpen { get; set; }
    /// <summary>Acordado por ter sido aberto; volta a dormir quando fechar.</summary>
    public bool Awake { get; set; }
    public long RamMb { get; set; }
    public double CpuSeconds { get; set; }
    public List<string> MainExes { get; set; } = [];
    public List<SnoozeItem> Items { get; set; } = [];
    public int Killed { get; set; }
}

public enum WatchAction { Wake, Resnooze }

public static class SnoozeRules
{
    /// <summary>Quantas verificações seguidas sem o programa aberto antes de voltar a dormir (evita dormir no meio de um reinício).</summary>
    public const int ResnoozeAfter = 3;
    /// <summary>De quanto em quanto tempo o app olha os processos enquanto há aplicativos em soneca.</summary>
    public static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(3);

    // Palavras inteiras (separadas por qualquer coisa que não seja letra ou número) que tiram o aplicativo do soneca.
    private static readonly string[] BlockWords =
    [
        // antivírus e segurança
        "defender", "antivirus", "antimalware", "malware", "malwarebytes", "security", "firewall", "endpoint", "kaspersky", "avast", "avg", "norton", "mcafee",
        "eset", "bitdefender", "sophos", "crowdstrike", "trellix", "windefend",
        // áudio, vídeo, touchpad, rede e outros drivers
        "audio", "realtek", "nahimic", "waves", "dolby", "nvidia", "amd", "radeon", "intel", "synaptics", "elan", "touchpad", "fingerprint", "biometric",
        "bluetooth", "wifi", "wlan", "vpn", "driver",
        // acesso remoto
        "teamviewer", "anydesk", "vnc", "rdp", "remote", "parsec", "splashtop", "rustdesk", "logmein",
        // cópia de segurança
        "backup",
    ];

    // Sincronização: pode dormir, mas com aviso, porque a sincronização para até acordar
    private static readonly string[] SyncWords = ["onedrive", "dropbox", "googledrivefs", "icloud", "pcloud", "mega", "sync", "nextcloud", "syncthing", "box"];

    // Nunca entram: o próprio app e o sistema
    private static readonly string[] SelfWords = ["notebookdoctor"];

    private static readonly string[] AppRoots =
    [
        @"\program files (x86)\", @"\program files\", @"\programdata\", @"\appdata\local\programs\", @"\appdata\local\", @"\appdata\roaming\",
    ];

    private static readonly string[] SharedFolders = ["common files", "microsoft", "windowsapps", "temp", "packages", "programs"];

    public static string KeyOf(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Nome do aplicativo para um arquivo: a família conhecida (<see cref="AppGroups"/>), senão a pasta do programa logo
    /// abaixo de Program Files / AppData, senão o nome do próprio arquivo.
    /// </summary>
    public static string AppNameOf(string? exePath, string fallbackName)
    {
        string baseName = Regex.Replace(fallbackName ?? "", @"\.exe$", "", RegexOptions.IgnoreCase).Trim();
        if (AppGroups.AppOf(baseName) is { } family) return family;
        string file = System.IO.Path.GetFileNameWithoutExtension((exePath ?? "").Replace('/', '\\').Split('\\').LastOrDefault() ?? "");
        if (file.Length > 0 && AppGroups.AppOf(file) is { } familyByFile) return familyByFile;

        string path = (exePath ?? "").Replace('/', '\\');
        string lower = path.ToLowerInvariant();
        foreach (var root in AppRoots)
        {
            int i = lower.IndexOf(root, StringComparison.Ordinal);
            if (i < 0) continue;
            var parts = path[(i + root.Length)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            // a última parte é o arquivo; as de antes são pastas
            var folders = parts.Take(Math.Max(0, parts.Length - 1)).ToList();
            string? folder = folders.FirstOrDefault(f => !SharedFolders.Contains(f, StringComparer.OrdinalIgnoreCase));
            if (folder is not null) return folder;
        }
        return baseName.Length > 0 ? baseName : file;
    }

    private static IEnumerable<string> Tokens(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(t => t.Length > 0);

    private static string? Find(IEnumerable<string> tokens, string[] words) => tokens.FirstOrDefault(t => words.Contains(t));

    public static string? BlockReason(string appName, IEnumerable<string> componentText, IEnumerable<string> userNever)
    {
        string key = KeyOf(appName);
        if (userNever.Any(n => KeyOf(n) == key)) return "Você pediu para nunca adormecer";
        var tokens = Tokens(appName + " " + string.Join(" ", componentText)).ToList();
        if (SelfWords.Contains(KeyOf(appName)) || tokens.Contains("notebookdoctor")) return "É o próprio Notebook Doctor";
        if (Find(tokens, BlockWords) is not null) return "Segurança, driver, rede ou acesso remoto: fica como está";
        return null;
    }

    public static string? WarnReason(string appName, IEnumerable<string> componentText) =>
        Find(Tokens(appName + " " + string.Join(" ", componentText)), SyncWords) is not null
            ? "Sincronização: enquanto dorme, ela para. Os arquivos só sincronizam depois que acordar" : null;

    private static bool UnderWindows(string path, string systemRoot) =>
        path.Length > 0 && path.StartsWith(systemRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static string ExeBase(string path) =>
        System.IO.Path.GetFileNameWithoutExtension((path ?? "").Replace('/', '\\').Split('\\').LastOrDefault() ?? "").ToLowerInvariant();

    /// <summary>Junta inicialização, serviços, tarefas e processos por aplicativo. Só entram peças de terceiros.</summary>
    public static List<SnoozeApp> Build(
        IEnumerable<SnoozeStartup> startup, IEnumerable<ServiceInfo> services, IEnumerable<ScheduledTaskInfo> tasks,
        IEnumerable<SnoozeProcess> processes, string systemRoot, IEnumerable<string> userNever)
    {
        var never = userNever.ToList();
        var parts = new Dictionary<string, (string Name, List<SnoozeStartup> S, List<ServiceInfo> V, List<ScheduledTaskInfo> T, List<SnoozeProcess> P)>();
        (string Name, List<SnoozeStartup> S, List<ServiceInfo> V, List<ScheduledTaskInfo> T, List<SnoozeProcess> P) Slot(string name)
        {
            string key = KeyOf(name);
            if (!parts.TryGetValue(key, out var slot)) parts[key] = slot = (name, [], [], [], []);
            return slot;
        }

        foreach (var s in startup.Where(s => s.Enabled && !UnderWindows(s.ExePath, systemRoot)))
            Slot(AppNameOf(s.ExePath, s.Name)).S.Add(s);

        foreach (var (svc, _, _) in ServiceRules.Candidates(services, systemRoot).Where(c => c.Advice != ServiceAdvice.Keep))
            Slot(AppNameOf(ServiceRules.ExecutablePath(svc.ImagePath, systemRoot), svc.DisplayName)).V.Add(svc);

        foreach (var (task, _, _) in TaskRules.Candidates(tasks).Where(c => c.Advice != TaskAdvice.Keep))
            Slot(AppNameOf(task.Command, task.Name)).T.Add(task);

        foreach (var p in processes.Where(p => !UnderWindows(p.ExePath, systemRoot) && !ProcessTools.IsProtected(p.Name)))
        {
            string name = AppNameOf(p.ExePath, p.Name);
            // Só vale processo de um aplicativo que também deixa algo rodando sozinho; senão vira ruído
            if (parts.ContainsKey(KeyOf(name))) Slot(name).P.Add(p);
        }

        var result = new List<SnoozeApp>();
        foreach (var (key, slot) in parts)
        {
            var text = slot.S.Select(s => s.Name + " " + s.ExePath)
                .Concat(slot.V.Select(v => v.Name + " " + v.DisplayName + " " + v.ImagePath))
                .Concat(slot.T.Select(t => t.Path + " " + t.Command))
                .Concat(slot.P.Select(p => p.Name + " " + p.ExePath)).ToList();
            var mains = slot.S.Select(s => ExeBase(s.ExePath)).Where(n => n.Length > 0).ToList();
            if (AppGroups.MainOf(slot.Name) is { } main) mains.Insert(0, main.ToLowerInvariant());
            result.Add(new SnoozeApp
            {
                Key = key, Name = slot.Name, Startup = slot.S, Services = slot.V, Tasks = slot.T,
                Background = slot.P.Where(p => !p.HasWindow).ToList(), Windowed = slot.P.Where(p => p.HasWindow).ToList(),
                MainExes = mains.Distinct().ToList(),
                Block = BlockReason(slot.Name, text, never), Warn = WarnReason(slot.Name, text),
            });
        }
        return result.OrderBy(a => a.CanSleep ? 0 : 1).ThenByDescending(a => a.RamMb).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// O que o vigia faz com cada aplicativo em soneca. Acorda quando um processo principal abre; volta a dormir só depois de
    /// <see cref="ResnoozeAfter"/> verificações seguidas sem nenhum aberto.
    /// </summary>
    public static List<(SnoozedApp App, WatchAction Action)> Decide(IEnumerable<SnoozedApp> apps, ISet<string> running, IReadOnlyDictionary<string, int> absent)
    {
        var result = new List<(SnoozedApp, WatchAction)>();
        foreach (var app in apps.Where(a => a.WakeOnOpen && a.MainExes.Count > 0))
        {
            bool open = app.MainExes.Any(running.Contains);
            if (!app.Awake && open) result.Add((app, WatchAction.Wake));
            else if (app.Awake && !open && absent.GetValueOrDefault(app.Key) >= ResnoozeAfter) result.Add((app, WatchAction.Resnooze));
        }
        return result;
    }

    /// <summary>Atualiza a contagem de verificações seguidas sem o programa aberto.</summary>
    public static Dictionary<string, int> CountAbsent(IEnumerable<SnoozedApp> apps, ISet<string> running, IReadOnlyDictionary<string, int> before)
    {
        var next = new Dictionary<string, int>();
        foreach (var app in apps.Where(a => a.Awake))
            next[app.Key] = app.MainExes.Any(running.Contains) ? 0 : before.GetValueOrDefault(app.Key) + 1;
        return next;
    }

    public static string Describe(SnoozeApp a)
    {
        var bits = new List<string>();
        if (a.Startup.Count > 0) bits.Add(Pt.N(a.Startup.Count, Loc.T("inicialização"), Loc.T("inicializações")));
        if (a.Services.Count > 0) bits.Add(Pt.N(a.Services.Count, Loc.T("serviço"), Loc.T("serviços")));
        if (a.Tasks.Count > 0) bits.Add(Pt.N(a.Tasks.Count, Loc.T("tarefa"), Loc.T("tarefas")));
        if (a.Background.Count > 0) bits.Add(Loc.F("{0} em segundo plano", Pt.N(a.Background.Sum(p => p.Count), Loc.T("processo"), Loc.T("processos"))));
        return string.Join(", ", bits);
    }
}

/// <summary>As ações do sistema que o soneca usa. Cada uma lança <see cref="UnauthorizedAccessException"/> quando falta administrador.</summary>
public interface ISnoozeSystem
{
    void SetStartup(string approvalSubKey, string name, bool enabled);
    void SetServiceStart(string name, int start);
    void SetTask(string path, bool enabled);
    /// <summary>Encerra os processos com esse nome. Devolve quantos.</summary>
    int Kill(string processName);
}

public sealed record SnoozeResult(int Done, int Failed, bool NeedsAdmin, int Killed);

/// <summary>Aplica e desfaz o soneca sobre um <see cref="ISnoozeSystem"/>. Peça que falha fica registrada como não aplicada: nada se perde.</summary>
public static class SnoozeEngine
{
    public static List<SnoozeItem> Plan(SnoozeApp app)
    {
        var items = new List<SnoozeItem>();
        items.AddRange(app.Startup.Where(s => s.Enabled).Select(s => new SnoozeItem { Kind = "startup", Id = s.Name, Sub = s.ApprovalSubKey, Prev = 1 }));
        items.AddRange(app.Services.Where(s => s.Start == 2).Select(s => new SnoozeItem { Kind = "service", Id = s.Name, Prev = 2 }));
        items.AddRange(app.Tasks.Where(t => t.Enabled).Select(t => new SnoozeItem { Kind = "task", Id = t.Path, Prev = 1 }));
        return items;
    }

    /// <param name="killBackground">Fecha os processos de fundo. Quem chama já confirmou com a pessoa.</param>
    public static (SnoozedApp Record, SnoozeResult Result) Sleep(SnoozeApp app, ISnoozeSystem sys, bool killBackground, bool wakeOnOpen, DateTime now)
    {
        if (!app.CanSleep) throw new InvalidOperationException("O aplicativo não pode dormir agora.");
        var record = new SnoozedApp
        {
            Key = app.Key, Name = app.Name, Since = now, WakeOnOpen = wakeOnOpen, RamMb = app.RamMb, CpuSeconds = app.CpuSeconds,
            MainExes = [.. app.MainExes], Items = Plan(app),
        };
        var result = Apply(record, sys);
        int killed = 0;
        if (killBackground)
            foreach (var p in app.Background) killed += sys.Kill(p.Name);
        record.Killed = killed;
        // Nada feito e nada fechado: não há o que registrar
        return (record, result with { Killed = killed });
    }

    /// <summary>Desliga as peças que ainda não estão desligadas (idempotente). Serve para dormir e para voltar a dormir.</summary>
    public static SnoozeResult Apply(SnoozedApp record, ISnoozeSystem sys)
    {
        int done = 0, failed = 0;
        bool admin = false;
        foreach (var item in record.Items.Where(i => !i.Applied))
        {
            try
            {
                switch (item.Kind)
                {
                    case "startup": sys.SetStartup(item.Sub, item.Id, false); break;
                    case "service": sys.SetServiceStart(item.Id, 3); break;
                    case "task": sys.SetTask(item.Id, false); break;
                    default: continue;
                }
                item.Applied = true;
                done++;
            }
            catch (UnauthorizedAccessException) { failed++; admin = true; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException) { failed++; }
        }
        record.Awake = false;
        return new SnoozeResult(done, failed, admin, 0);
    }

    /// <summary>Devolve as peças desligadas. As que falham continuam marcadas como desligadas, para tentar de novo.</summary>
    public static SnoozeResult Wake(SnoozedApp record, ISnoozeSystem sys)
    {
        int done = 0, failed = 0;
        bool admin = false;
        foreach (var item in record.Items.Where(i => i.Applied))
        {
            try
            {
                switch (item.Kind)
                {
                    case "startup": sys.SetStartup(item.Sub, item.Id, true); break;
                    case "service": sys.SetServiceStart(item.Id, item.Prev); break;
                    case "task": sys.SetTask(item.Id, true); break;
                    default: continue;
                }
                item.Applied = false;
                done++;
            }
            catch (UnauthorizedAccessException) { failed++; admin = true; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException) { failed++; }
        }
        return new SnoozeResult(done, failed, admin, 0);
    }

    /// <summary>Todas as peças estão de volta: o registro pode sair da lista.</summary>
    public static bool FullyAwake(SnoozedApp record) => record.Items.All(i => !i.Applied);
}
