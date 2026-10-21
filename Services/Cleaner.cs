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
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", Opts))
            {
                bool ok;
                try { ok = item.IgnoreAge || f.LastWriteTime < cutoff; } catch { ok = false; }
                if (ok) yield return f;
            }
    }

    private static void Measure(CleanItem item, int days)
    {
        long bytes = 0; int n = 0;
        foreach (var f in Files(item, days)) { try { bytes += f.Length; n++; } catch { } }
        item.Bytes = bytes; item.Files = n;
    }

    private static CleanItem? RecycleBinItem()
    {
        var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        // A lixeira começa desmarcada: apagar dela é definitivo e precisa de decisão explícita.
        return Native.SHQueryRecycleBinW(null, ref info) == 0
            ? new CleanItem { Label = "Lixeira (apaga definitivamente)", IsRecycleBin = true, Selected = false, Bytes = info.i64Size, Files = (int)info.i64NumItems }
            : null;
    }

    private static List<CleanItem> AllTargets(bool selectSafe) =>
        SafeTargets(selectSafe).Concat(BrowserTargets()).Concat(DevTargets()).Concat(WindowsUpdateTargets()).Where(i => i.Paths.Any(Directory.Exists)).ToList();

    /// <summary>Análise completa (sem pressa): mede tudo e devolve a lista pronta.</summary>
    public static List<CleanItem> Scan(int days, bool selectSafeByDefault = true)
    {
        var items = AllTargets(selectSafeByDefault);
        Parallel.ForEach(items, i => Measure(i, days));
        if (RecycleBinItem() is { } bin) items.Add(bin);
        return items;
    }

    /// <summary>
    /// Análise para a tela: mede os locais em paralelo e entrega cada um assim que fica pronto
    /// (caches de desenvolvimento podem ter centenas de milhares de arquivos e demorar).
    /// </summary>
    public static async Task ScanProgressiveAsync(int days, Action<CleanItem> onItem)
    {
        var items = AllTargets(true);
        if (RecycleBinItem() is { } bin) onItem(bin);
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
    public static long Clean(IEnumerable<CleanItem> items, int days)
    {
        long freed = 0;
        foreach (var item in items.Where(i => i.Selected && i.CanClean))
        {
            if (item.IsRecycleBin)
            {
                if (Native.SHEmptyRecycleBinW(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND) == 0)
                    freed += item.Bytes;
                continue;
            }
            foreach (var f in Files(item, days).ToList())
            {
                try { long len = f.Length; f.Delete(); freed += len; } catch { /* em uso ou sem permissão */ }
            }
            foreach (var dir in item.Paths) RemoveEmptyDirs(dir);
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
