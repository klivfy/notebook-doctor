using System.IO;

namespace NotebookDoctor.Services;

public sealed class CleanItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selected = true;
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Falso quando o local exige administrador e o app não está elevado: a linha fica desmarcada e travada.</summary>
    public bool CanClean { get; init; } = true;
    public string Label { get; init; } = "";
    public string[] Paths { get; init; } = Array.Empty<string>();
    public bool IsRecycleBin { get; init; }
    /// <summary>M34: unidade da lixeira ("D:\"); nulo = todas as unidades.</summary>
    public string? RecycleRoot { get; init; }
    /// <summary>Quais arquivos da pasta entram (ex.: "thumbcache_*.db"). Com filtro, as subpastas vazias não são removidas.</summary>
    public string Pattern { get; init; } = "*";
    /// <summary>Caches de navegador não têm "idade": apaga tudo que não estiver em uso.</summary>
    public bool IgnoreAge { get; init; }
    public long Bytes { get; set; }
    public int Files { get; set; }
    public string SizeText => Cleaner.FormatBytes(Bytes);
}

/// <summary>Limpeza segura: só temporários e caches. Nunca toca em documentos, downloads, cookies ou senhas.</summary>
public static class Cleaner
{
    private static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // nunca segue junctions/links
        ReturnSpecialDirectories = false,
    };

    public static string FormatBytes(long b) => Pt.Bytes(b);

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static IEnumerable<CleanItem> SafeTargets(bool defaultSelected)
    {
        string win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        yield return new CleanItem { Label = "Temporários do usuário", Paths = [Path.GetTempPath().TrimEnd('\\')], Selected = defaultSelected };
        bool admin = AppInfo.IsAdmin;
        yield return new CleanItem { Label = admin ? "Temporários do Windows" : "Temporários do Windows (exige administrador)", Paths = [Path.Combine(win, "Temp")], Selected = defaultSelected && admin, CanClean = admin };
        yield return new CleanItem { Label = "Cache de internet do Windows", Paths = [Path.Combine(Local, @"Microsoft\Windows\INetCache")], Selected = defaultSelected };
        yield return new CleanItem { Label = "Relatórios de erro (WER)", Paths = [Path.Combine(Local, @"Microsoft\Windows\WER")], Selected = defaultSelected };
        yield return new CleanItem { Label = "Dumps de falha de aplicativos", Paths = [Path.Combine(Local, "CrashDumps")], Selected = defaultSelected };
    }

    /// <summary>Caches de navegadores: desmarcados por padrão (o navegador recria; páginas podem demorar mais na 1ª abertura).</summary>
    private static IEnumerable<CleanItem> BrowserTargets()
    {
        foreach (var (name, root) in new[]
        {
            ("Google Chrome", Path.Combine(Local, @"Google\Chrome\User Data")),
            ("Microsoft Edge", Path.Combine(Local, @"Microsoft\Edge\User Data")),
            ("Brave", Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")),
        })
        {
            if (!Directory.Exists(root)) continue;
            var paths = new List<string>();
            foreach (var profile in Directory.EnumerateDirectories(root).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ")))
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                    paths.Add(Path.Combine(profile, sub));
            paths.Add(Path.Combine(root, "ShaderCache"));
            yield return new CleanItem { Label = $"Cache do {name} (feche o navegador antes)", Paths = paths.Where(Directory.Exists).ToArray(), Selected = false, IgnoreAge = true };
        }
        string ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ff))
            yield return new CleanItem
            {
                Label = "Cache do Firefox (feche o navegador antes)",
                Paths = Directory.EnumerateDirectories(ff).Select(d => Path.Combine(d, "cache2")).Where(Directory.Exists).ToArray(),
                Selected = false, IgnoreAge = true,
            };
    }

    /// <summary>Caches de ferramentas de desenvolvimento: seguros (são baixados de novo), mas a próxima compilação/instalação fica mais lenta.</summary>
    private static IEnumerable<CleanItem> DevTargets()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (label, path) in new[]
        {
            ("Cache do npm", Path.Combine(Local, "npm-cache")),
            ("Cache HTTP do NuGet", Path.Combine(Local, @"NuGet\v3-cache")),
            ("Pacotes NuGet globais (restaurados na próxima compilação)", Path.Combine(home, @".nuget\packages")),
            ("Cache do Gradle", Path.Combine(home, @".gradle\caches")),
            ("Cache do pip", Path.Combine(Local, @"pip\cache")),
            ("Cache do Yarn", Path.Combine(Local, @"Yarn\Cache")),
            ("Armazém do pnpm", Path.Combine(Local, @"pnpm\store")),
        })
            if (Directory.Exists(path))
                yield return new CleanItem { Label = $"Dev · {label} (baixado de novo quando preciso)", Paths = [path], Selected = false, IgnoreAge = true };
    }

    /// <summary>
    /// M39: caches de apps de mensagem e do VS Code (Electron/WebView2). Só as pastas de cache: conversas, login e
    /// configurações ficam. Desmarcados por padrão; com o app aberto, os arquivos em uso são pulados.
    /// O Spotify fica de fora de propósito: a pasta de cache dele guarda também as músicas baixadas para ouvir offline.
    /// </summary>
    private static IEnumerable<CleanItem> AppTargets()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] web = ["Cache", "Code Cache", "GPUCache"];
        string[] In(string root, params string[] subs) => subs.Select(s => Path.Combine(root, s)).ToArray();
        foreach (var (label, paths) in new (string, string[])[]
        {
            ("Discord", In(Path.Combine(roaming, "discord"), web)),
            ("Slack", In(Path.Combine(roaming, "Slack"), [.. web, @"Service Worker\CacheStorage"])),
            ("Microsoft Teams (clássico)", In(Path.Combine(roaming, @"Microsoft\Teams"), [.. web, "tmp"])),
            ("Microsoft Teams", In(Path.Combine(Local, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\Default"), web)),
            ("Visual Studio Code", In(Path.Combine(roaming, "Code"), [.. web, "CachedData", "CachedExtensionVSIXs"])),
        })
        {
            var existing = paths.Where(Directory.Exists).ToArray();
            if (existing.Length > 0)
                yield return new CleanItem { Label = $"Cache do {label} (feche o app antes; conversas e login ficam)", Paths = existing, Selected = false, IgnoreAge = true };
        }

        // M35: o Explorador recria as miniaturas; os arquivos em uso agora são pulados e saem na próxima vez
        yield return new CleanItem
        {
            Label = "Miniaturas e ícones do Explorador (recriados quando preciso)",
            Paths = [Path.Combine(Local, @"Microsoft\Windows\Explorer")], Selected = false, IgnoreAge = true, Pattern = "thumbcache_*.db",
        };
        yield return new CleanItem
        {
            Label = "Cache de ícones do Explorador (recriado quando preciso)",
            Paths = [Path.Combine(Local, @"Microsoft\Windows\Explorer")], Selected = false, IgnoreAge = true, Pattern = "iconcache_*.db",
        };
    }

    /// <summary>Cache de atualizações do Windows: só o Windows consegue apagar com segurança, então exige administrador.</summary>
    private static IEnumerable<CleanItem> WindowsUpdateTargets()
    {
        string win = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        bool admin = AppInfo.IsAdmin;
        string suffix = admin ? "" : " (exige administrador)";
        yield return new CleanItem { Label = "Download de atualizações do Windows" + suffix, Paths = [Path.Combine(win, @"SoftwareDistribution\Download")], Selected = false, IgnoreAge = true, CanClean = admin };
        yield return new CleanItem { Label = "Cache de Otimização de Entrega" + suffix, Paths = [Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")], Selected = false, IgnoreAge = true, CanClean = admin };
    }

    private static IEnumerable<FileInfo> Files(CleanItem item, int days)
    {
        var cutoff = DateTime.Now.AddDays(-days);
        foreach (var dir in item.Paths.Where(Directory.Exists))
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles(item.Pattern, Opts))
            {
                bool ok;
                try { ok = item.IgnoreAge || f.LastWriteTime < cutoff; } catch { ok = false; }
                if (ok) yield return f;
            }
    }

    /// <summary>B35: os arquivos que a limpeza apagaria neste local (sem apagar nada).</summary>
    public static List<PreviewFile> Preview(CleanItem item, int days)
    {
        var list = new List<PreviewFile>();
        foreach (var f in Files(item, days))
            try { list.Add(new PreviewFile(f.FullName, f.Length, f.LastWriteTime)); } catch { /* sumiu ou sem permissão */ }
        return list;
    }

    private static void Measure(CleanItem item, int days)
    {
        long bytes = 0; int n = 0;
        foreach (var f in Files(item, days)) { try { bytes += f.Length; n++; } catch { } }
        item.Bytes = bytes; item.Files = n;
    }

    /// <summary>
    /// M34: uma linha de lixeira por unidade que tem itens, para esvaziar só a do disco que está cheio.
    /// Começam desmarcadas: apagar da lixeira é definitivo e precisa de decisão explícita.
    /// </summary>
    private static IEnumerable<CleanItem> RecycleBinItems()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
        foreach (var root in drives)
        {
            var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
            if (Native.SHQueryRecycleBinW(root, ref info) != 0 || info.i64NumItems == 0) continue;
            yield return new CleanItem
            {
                Label = $"Lixeira de {root.TrimEnd('\\')} (apaga definitivamente)", IsRecycleBin = true, RecycleRoot = root,
                Selected = false, Bytes = info.i64Size, Files = (int)info.i64NumItems,
            };
        }
    }

    private static List<CleanItem> AllTargets(bool selectSafe) =>
        SafeTargets(selectSafe).Concat(BrowserTargets()).Concat(AppTargets()).Concat(DevTargets()).Concat(WindowsUpdateTargets()).Where(i => i.Paths.Any(Directory.Exists)).ToList();

    /// <summary>Análise completa (sem pressa): mede tudo e devolve a lista pronta.</summary>
    public static List<CleanItem> Scan(int days, bool selectSafeByDefault = true)
    {
        var items = AllTargets(selectSafeByDefault);
        Parallel.ForEach(items, i => Measure(i, days));
        items.AddRange(RecycleBinItems());
        return items;
    }

    /// <summary>
    /// Análise para a tela: mede os locais em paralelo e entrega cada um assim que fica pronto
    /// (caches de desenvolvimento podem ter centenas de milhares de arquivos e demorar).
    /// </summary>
    public static async Task ScanProgressiveAsync(int days, Action<CleanItem> onItem)
    {
        var items = AllTargets(true);
        foreach (var bin in RecycleBinItems()) onItem(bin);
        await Task.WhenAll(items.Select(i => Task.Run(() => { Measure(i, days); onItem(i); })));
    }

    /// <summary>Só os temporários seguros (sem navegadores, caches de dev e Windows Update): usado na nota de saúde.</summary>
    public static long SafeJunkBytes(int days)
    {
        var items = SafeTargets(false).Where(i => i.Paths.Any(Directory.Exists)).ToList();
        Parallel.ForEach(items, i => Measure(i, days));
        return items.Sum(i => i.Bytes);
    }

    /// <summary>Apaga os itens marcados. Arquivos em uso ou protegidos são ignorados. Retorna bytes liberados.</summary>
    /// <param name="progress">B12: etapa atual (1-based), total de etapas e o nome do local.</param>
    public static long Clean(IEnumerable<CleanItem> items, int days, IProgress<(int Step, int Steps, string Label)>? progress = null)
    {
        long freed = 0;
        var chosen = items.Where(i => i.Selected && i.CanClean).ToList();
        for (int step = 0; step < chosen.Count; step++)
        {
            var item = chosen[step];
            progress?.Report((step + 1, chosen.Count, item.Label));
            if (item.IsRecycleBin)
            {
                if (Native.SHEmptyRecycleBinW(IntPtr.Zero, item.RecycleRoot, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND) == 0)
                    freed += item.Bytes;
                continue;
            }
            foreach (var f in Files(item, days).ToList())
            {
                try { long len = f.Length; f.Delete(); freed += len; } catch { /* em uso ou sem permissão */ }
            }
            if (item.Pattern == "*") foreach (var dir in item.Paths) RemoveEmptyDirs(dir);
        }
        return freed;
    }

    private static void RemoveEmptyDirs(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var d in new DirectoryInfo(root).EnumerateDirectories("*", Opts).OrderByDescending(d => d.FullName.Length))
            {
                try { if (!d.EnumerateFileSystemInfos().Any()) d.Delete(); } catch { }
            }
        }
        catch { }
    }

    /// <summary>"Otimizar agora": só os itens seguros (sem navegadores e sem Lixeira). Retorna bytes liberados.</summary>
    public static long QuickClean(int days)
    {
        var items = SafeTargets(true).Where(i => i.Paths.Any(Directory.Exists)).ToList();
        long freed = Clean(items, days);
        if (AppInfo.IsAdmin) Trim();
        return freed;
    }

    /// <summary>TRIM do SSD do sistema (exige administrador). Não faz nada em HDD.</summary>
    private static void Trim()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("defrag.exe", $"{Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\')} /L") { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(120000);
        }
        catch { }
    }
}
