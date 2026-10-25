using System.IO;

namespace NotebookDoctor.Services;

public sealed class FolderUsage
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public long Bytes { get; set; }
    public double Percent { get; set; }
    public bool Unknown { get; init; }
    /// <summary>Comando de desinstalação (só para aplicativos instalados).</summary>
    public string Tag { get; init; } = "";
    public bool IsFile { get; init; }
    /// <summary>M47: data de instalação informada pelo instalador (nem todos informam).</summary>
    public DateTime? InstallDate { get; init; }
    public string SizeText => Unknown ? "n/d" : Cleaner.FormatBytes(Bytes);
}

/// <summary>Mostra o que ocupa espaço: tamanho de cada pasta de primeiro nível. Só lê; nunca apaga.</summary>
public static class DiskUsage
{
    private static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline, // não segue links; ignora arquivos só na nuvem
    };

    /// <summary>Os N maiores arquivos (a partir de minBytes) dentro de root.</summary>
    public static async Task<List<FolderUsage>> LargestFilesAsync(string root, long minBytes, int top, Action<string> onProgress, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var best = new List<FolderUsage>();
            long seen = 0;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen % 3000 == 0) onProgress(Loc.F("{0:N0} arquivos verificados…", seen));
                long len;
                try { len = f.Length; } catch { continue; }
                if (len < minBytes) continue;
                best.Add(new FolderUsage { Name = f.Name, Path = f.FullName, Bytes = len, IsFile = true });
                if (best.Count > top * 2) best = best.OrderByDescending(x => x.Bytes).Take(top).ToList();
            }
            return best.OrderByDescending(x => x.Bytes).Take(top).ToList();
        }, ct);
    }

    /// <summary>
    /// Arquivos idênticos (mesmo tamanho e mesmo conteúdo), a partir de minBytes. Compara primeiro uma amostra (início + fim)
    /// e só lê o arquivo inteiro quando a amostra coincide.
    /// </summary>
    public static async Task<List<DuplicateGroup>> DuplicatesAsync(string root, long minBytes, int maxGroups, KnownPlaces places, Action<string> onProgress, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var bySize = new Dictionary<long, List<string>>();
            long seen = 0;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen % 3000 == 0) onProgress(Loc.F("{0:N0} arquivos verificados…", seen));
                long len;
                try { len = f.Length; } catch { continue; }
                if (len < minBytes || DuplicateRules.IsSystemPath(f.FullName, places)) continue; // o Windows e os programas não entram
                if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = new();
                l.Add(f.FullName);
            }

            var groups = new List<(long size, List<string> files)>();
            int step = 0;
            foreach (var (size, files) in bySize.Where(kv => kv.Value.Count > 1))
            {
                ct.ThrowIfCancellationRequested();
                onProgress(Loc.F("Comparando conteúdo ({0} grupos de mesmo tamanho)…", ++step));
                var byHash = new Dictionary<string, List<string>>();
                foreach (var path in files)
                {
                    string? sample = HashFile(path, sampleOnly: true, ct);
                    if (sample is null) continue;
                    if (!byHash.TryGetValue(sample, out var l)) byHash[sample] = l = new();
                    l.Add(path);
                }
                foreach (var cand in byHash.Values.Where(v => v.Count > 1))
                {
                    var full = new Dictionary<string, List<string>>();
                    foreach (var path in cand)
                    {
                        string? h = HashFile(path, sampleOnly: false, ct);
                        if (h is null) continue;
                        if (!full.TryGetValue(h, out var l)) full[h] = l = new();
                        l.Add(path);
                    }
                    foreach (var g in full.Values.Where(v => v.Count > 1)) groups.Add((size, g));
                }
            }

            static DuplicateFile Info(string path)
            {
                try { var i = new FileInfo(path); return new DuplicateFile(path, i.LastWriteTime, i.CreationTime); }
                catch (IOException) { return new DuplicateFile(path, DateTime.MinValue, DateTime.MinValue); }
            }
            return groups.OrderByDescending(g => g.size * (g.files.Count - 1)).Take(maxGroups)
                .Select(g => new DuplicateGroup(g.size, g.files.Select(Info).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList())).ToList();
        }, ct);
    }

    /// <summary>C09: quanto cada tipo de arquivo ocupa dentro de root.</summary>
    public static async Task<List<TypeUsage>> ByTypeAsync(string root, Action<string> onProgress, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var totals = new SpaceByType();
            long seen = 0;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen % 3000 == 0) onProgress(Loc.F("{0:N0} arquivos verificados…", seen));
                try { totals.Add(f.FullName, f.Length); } catch (IOException) { }
            }
            return totals.Result();
        }, ct);
    }

    private static string? HashFile(string path, bool sampleOnly, CancellationToken ct) => FileHash.Of(path, sampleOnly, ct);

    /// <summary>Aplicativos instalados (registro do Windows), do maior para o menor. Alguns instaladores não informam o tamanho.</summary>
    public static List<FolderUsage> InstalledApps()
    {
        var apps = new Dictionary<string, FolderUsage>(StringComparer.OrdinalIgnoreCase);
        var keys = new (Microsoft.Win32.RegistryKey hive, string path)[]
        {
            (Microsoft.Win32.Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in keys)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;
                string? display = k.GetValue("DisplayName") as string, uninstall = k.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(uninstall)) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (k.GetValue("ParentKeyName") is not null) continue; // atualizações do Windows
                long kb = k.GetValue("EstimatedSize") is int e ? e : 0;
                string publisher = k.GetValue("Publisher") as string ?? "";
                apps[display] = new FolderUsage
                {
                    Name = publisher.Length > 0 ? $"{display} — {publisher}" : display,
                    Path = k.GetValue("InstallLocation") as string ?? "",
                    Bytes = kb * 1024, Unknown = kb == 0, Tag = uninstall,
                    InstallDate = Timeline.ParseInstallDate(k.GetValue("InstallDate") as string),
                };
            }
        }
        return apps.Values.OrderByDescending(a => a.Bytes).ThenBy(a => a.Name).ToList();
    }

    public static long FolderSize(string dir, CancellationToken ct)
    {
        long total = 0;
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", Opts))
            {
                ct.ThrowIfCancellationRequested();
                try { total += f.Length; } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return total;
    }

    /// <summary>Mede cada subpasta (e os arquivos soltos na raiz) e avisa a cada resultado, para a tela ir enchendo.</summary>
    public static async Task ScanAsync(string root, Action<FolderUsage> onItem, Action<string> onProgress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var entries = new List<(string name, string path)>();
            try { entries.AddRange(Directory.EnumerateDirectories(root).Select(d => (System.IO.Path.GetFileName(d), d))); } catch { }

            int done = 0;
            foreach (var (name, path) in entries)
            {
                ct.ThrowIfCancellationRequested();
                onProgress(Loc.F("Medindo {0} ({1} de {2})…", name, ++done, entries.Count));
                onItem(new FolderUsage { Name = name, Path = path, Bytes = FolderSize(path, ct) });
            }
            long loose = 0;
            try { foreach (var f in new DirectoryInfo(root).EnumerateFiles()) { try { loose += f.Length; } catch { } } } catch { }
            if (loose > 0) onItem(new FolderUsage { Name = "(arquivos soltos nesta pasta)", Path = root, Bytes = loose });
        }, ct);
    }
}
