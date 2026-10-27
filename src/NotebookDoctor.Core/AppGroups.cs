namespace NotebookDoctor.Services;

/// <param name="Members">Nomes dos processos que formam a linha (um só quando não houve junção)</param>
/// <param name="Heaviest">Leva 10.2: numa linha juntada, o processo que mais pesa (CPU, e memória no empate); vazio quando a linha é um processo só.</param>
public sealed record AppGroup(string Name, IReadOnlyList<string> Members, int Count, double CpuPct, long MemMB, double IoKBs, int Conns,
                              bool Hung = false, bool HasWindow = false, string Heaviest = "")
{
    /// <summary>Processo principal: dá o ícone, a descrição e o local do arquivo.</summary>
    public string Main => Members[0];
    public bool Merged => Members.Count > 1;
}

/// <summary>
/// M22: junta numa linha só os processos de um mesmo aplicativo que têm nomes diferentes (o Steam e o
/// steamwebhelper, o Acrobat e o AcroCEF). A lista é fechada e conservadora: só entram nomes que pertencem a um
/// único aplicativo. Processos compartilhados (msedgewebview2, node, crashpad_handler, QtWebEngineProcess, updaters genéricos)
/// ficam separados, porque juntar errado faria "Encerrar" fechar o programa errado.
/// </summary>
public static class AppGroups
{
    // O primeiro nome de cada família é o processo principal
    private static readonly (string App, string[] Names)[] Families =
    [
        ("Steam", ["steam", "steamwebhelper", "steamservice"]),
        ("Adobe Acrobat", ["acrobat", "acrord32", "acrocef", "rdrcef", "adobecollabsync", "acrodist"]),
        ("Adobe Creative Cloud", ["creative cloud", "adobe desktop service", "adobeipcbroker", "ccxprocess", "cclibrary", "coresync", "adobe crash processor"]),
        ("Microsoft Teams", ["ms-teams", "msteams", "teams"]),
        ("Zoom", ["zoom", "zwebview2agent", "cpthost", "zoomwebview"]),
        ("OneDrive", ["onedrive", "filecoauth", "onedrive.sync.service"]),
        ("Dropbox", ["dropbox", "dropboxupdate"]),
        ("Google Drive", ["googledrivefs"]),
        ("Epic Games", ["epicgameslauncher", "epicwebhelper", "unrealcefsubprocess"]),
        ("EA app", ["eadesktop", "eabackgroundservice", "eaconnect_microsoft"]),
        ("Ubisoft Connect", ["upc", "uplaywebcore", "ubisoftconnect"]),
        ("Opera", ["opera", "opera_crashreporter", "opera_autoupdate"]),
        ("WhatsApp", ["whatsapp", "whatsapp.root"]),
        ("Docker Desktop", ["docker desktop", "com.docker.backend", "com.docker.build", "com.docker.service", "com.docker.dev-envs", "vpnkit"]),
        ("NVIDIA", ["nvidia app", "nvidia share", "nvidia web helper", "nvcontainer", "nvdisplay.container", "nvsphelper64", "nvidia overlay"]),
        ("Logitech G HUB", ["lghub", "lghub_agent", "lghub_updater", "lghub_system_tray"]),
        ("Razer Synapse", ["razer synapse 3", "razer synapse service", "razer synapse service process", "razercentralservice", "rzsdkservice"]),
        ("iCloud", ["icloud", "icloudservices", "icloudhome", "icloudphotos", "icloudckks", "icloudpasswords"]),
    ];

    private static readonly Dictionary<string, (string App, int Rank)> ByName = Build();

    private static Dictionary<string, (string, int)> Build()
    {
        var d = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (app, names) in Families)
            for (int i = 0; i < names.Length; i++) d[names[i]] = (app, i);
        return d;
    }

    /// <summary>Nome do processo principal da família (o primeiro da lista), ou null se o aplicativo não é uma família conhecida.</summary>
    public static string? MainOf(string app) => Families.FirstOrDefault(f => f.App.Equals(app, StringComparison.OrdinalIgnoreCase)).Names?[0];

    /// <summary>Aplicativo a que o processo pertence, ou null quando ele fica sozinho.</summary>
    public static string? AppOf(string processName) => ByName.TryGetValue(processName, out var a) ? a.App : null;

    /// <summary>
    /// Junta os grupos por aplicativo. Só vira uma linha nova quando dois ou mais processos da mesma família estão
    /// abertos; com um só, a linha fica com o nome do processo, como antes.
    /// </summary>
    public static List<AppGroup> Merge(IEnumerable<ProcGroup> groups)
    {
        var result = new List<AppGroup>();
        foreach (var family in groups.GroupBy(g => AppOf(g.Name) ?? "\0" + g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var members = family.OrderBy(g => ByName.TryGetValue(g.Name, out var a) ? a.Rank : 0).ToList();
            string name = members.Count > 1 ? AppOf(members[0].Name)! : members[0].Name;
            string heaviest = members.Count > 1 ? members.OrderByDescending(m => m.CpuPct).ThenByDescending(m => m.MemMB).First().Name : "";
            result.Add(new AppGroup(name, members.Select(m => m.Name).ToList(), members.Sum(m => m.Count), members.Sum(m => m.CpuPct),
                                    members.Sum(m => m.MemMB), members.Sum(m => m.IoKBs), members.Sum(m => m.Conns),
                                    members.Any(m => m.Hung), members.Any(m => m.HasWindow), heaviest));
        }
        return result;
    }

    /// <summary>Sem juntar: cada processo na sua linha (opção desligada).</summary>
    public static List<AppGroup> Single(IEnumerable<ProcGroup> groups) =>
        groups.Select(g => new AppGroup(g.Name, [g.Name], g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, g.Hung, g.HasWindow)).ToList();
}
