using System.Diagnostics;

namespace NotebookDoctor.Services;

public sealed record BrowserCleanResult(long Bytes, int Files, int Databases, List<string> Errors);

public sealed record CookieSiteScan(List<(string Site, int Count)> Sites, int LockedProfiles);

/// <summary>
/// Leva 6.2: apaga dados dos navegadores. Só com o navegador fechado (nunca encerra à força), só os passos que o Core
/// montou e que passam por <see cref="BrowserSafety"/>, e só dentro da pasta do perfil.
/// </summary>
public static class BrowserCleaner
{
    /// <summary>Navegadores com processo ativo. O Opera e o Opera GX têm o mesmo processo: os dois contam como abertos.</summary>
    public static HashSet<string> RunningIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in BrowserCatalog.All)
            foreach (var name in b.Processes)
            {
                Process[] procs = [];
                try { procs = Process.GetProcessesByName(name); } catch (InvalidOperationException) { }
                bool any = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (any) ids.Add(b.Id);
            }
        return ids;
    }

    /// <summary>Pede para fechar as janelas (CloseMainWindow). Devolve quantas janelas receberam o pedido; nunca mata o processo.</summary>
    public static int RequestClose(IEnumerable<string> browserIds)
    {
        int asked = 0;
        foreach (var id in browserIds)
            foreach (var name in BrowserCatalog.ById(id)?.Processes ?? [])
            {
                Process[] procs = [];
                try { procs = Process.GetProcessesByName(name); } catch (InvalidOperationException) { }
                foreach (var p in procs)
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++; }
                    catch (InvalidOperationException) { }
                    finally { p.Dispose(); }
                }
            }
        return asked;
    }

    private static string Full(BrowserProfile profile, string relative) => Path.GetFullPath(Path.Combine(profile.Dir, relative.Replace('/', '\\')));

    private static long LengthOf(string path)
    {
        long n = 0;
        foreach (var f in new[] { path, path + "-wal", path + "-journal", path + "-shm" })
            try { if (File.Exists(f)) n += new FileInfo(f).Length; } catch (IOException) { }
        return n;
    }

    private static IEnumerable<string> FilesOf(BrowserProfile profile, DeleteFilesStep step)
    {
        foreach (var rel in step.Relative)
        {
            string full = Full(profile, rel);
            string dir = Path.GetDirectoryName(full)!, pattern = Path.GetFileName(full);
            if (!Paths.IsUnder(dir, profile.Dir) || !Directory.Exists(dir)) continue;
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            IEnumerable<string> found;
            try
            {
                found = Glob.HasWildcard(pattern)
                    ? Directory.EnumerateFiles(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Where(f => Glob.IsMatch(pattern, Path.GetFileName(f))).ToList()
                    : File.Exists(full) ? [full] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in found) if (Paths.IsUnder(f, profile.Dir)) yield return f;
        }
    }

    /// <summary>Quanto os passos ocupam no disco (para bancos SQLite, o arquivo inteiro: com a lista de sites a manter, sai menos).</summary>
    public static long Estimate(BrowserProfile profile, IEnumerable<BrowserDataKind> kinds, IReadOnlyList<string> keepSites)
    {
        long total = 0;
        foreach (var step in BrowserPlans.For(profile, kinds, keepSites))
            if (step is DeleteFilesStep f) foreach (var file in FilesOf(profile, f)) { try { total += new FileInfo(file).Length; } catch (IOException) { } }
            else if (step is SqlStep s) total += LengthOf(Full(profile, s.Db));
        return total;
    }

    /// <param name="running">Navegadores abertos agora; nulo = consulta o sistema (os testes informam, para não depender do que está aberto na máquina).</param>
    public static BrowserCleanResult Clean(IEnumerable<BrowserProfile> profiles, IEnumerable<BrowserDataKind> kinds, IReadOnlyList<string> keepSites,
        CancellationToken ct = default, IReadOnlySet<string>? running = null)
    {
        var kindList = kinds.ToList();
        running ??= RunningIds();
        long bytes = 0;
        int files = 0, dbs = 0;
        var errors = new List<string>();
        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            if (running.Contains(profile.Browser.Id)) { errors.Add(Loc.F("{0}: o navegador está aberto.", profile.Display)); continue; }
            foreach (var step in BrowserPlans.For(profile, kindList, keepSites))
            {
                if (!BrowserSafety.IsAllowedStep(step)) continue; // o Core já filtra; aqui é a segunda barreira
                try
                {
                    if (step is DeleteFilesStep f)
                    {
                        foreach (var file in FilesOf(profile, f).ToList())
                            try
                            {
                                long len = new FileInfo(file).Length;
                                File.SetAttributes(file, FileAttributes.Normal);
                                File.Delete(file);
                                bytes += len;
                                files++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{profile.Display}: {Path.GetFileName(file)}"); }
                    }
                    else if (step is SqlStep s && CleanDb(profile, s) is { } freed)
                    {
                        bytes += freed;
                        dbs++;
                    }
                }
                catch (SqliteException ex) { errors.Add($"{profile.Display}: {ex.Message}"); }
            }
        }
        return new BrowserCleanResult(bytes, files, dbs, errors);
    }

    /// <summary>Executa os comandos num banco, tudo ou nada. Null quando o banco não existe neste perfil.</summary>
    private static long? CleanDb(BrowserProfile profile, SqlStep step)
    {
        string path = Full(profile, step.Db);
        if (!Paths.IsUnder(path, profile.Dir) || !File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        long before = LengthOf(path);
        using (var db = SqliteDb.Open(path))
        {
            db.Execute("BEGIN IMMEDIATE");
            try
            {
                foreach (var sql in step.Statements) db.Execute(sql);
                db.Execute("COMMIT");
            }
            catch
            {
                try { db.Execute("ROLLBACK"); } catch (SqliteException) { }
                throw;
            }
            // Encolher o arquivo é um bônus: se não der, os dados já saíram
            try { db.Execute("PRAGMA wal_checkpoint(TRUNCATE)"); db.Execute("VACUUM"); } catch (SqliteException) { }
        }
        return Math.Max(0, before - LengthOf(path));
    }

    /// <summary>Os sites que têm cookies, para a pessoa escolher quais manter. Com o navegador aberto o banco costuma estar travado.</summary>
    public static CookieSiteScan ReadCookieSites(IEnumerable<BrowserProfile> profiles)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int locked = 0;
        foreach (var profile in profiles)
        {
            var (relative, sql) = profile.Browser.Family == BrowserFamily.Firefox
                ? ("cookies.sqlite", "SELECT host, COUNT(*) FROM moz_cookies GROUP BY host")
                : (File.Exists(Full(profile, @"Network\Cookies")) ? @"Network\Cookies" : "Cookies", "SELECT host_key, COUNT(*) FROM cookies GROUP BY host_key");
            string source = Full(profile, relative);
            if (!File.Exists(source)) continue;
            string copy = Path.Combine(Path.GetTempPath(), "nd-cookies-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = File.Create(copy))
                    src.CopyTo(dst);
                using var db = SqliteDb.Open(copy, readOnly: true);
                foreach (var row in db.Query(sql))
                {
                    string site = SiteName.Of(row[0]);
                    if (site.Length == 0) continue;
                    counts[site] = counts.GetValueOrDefault(site) + (int.TryParse(row[1], out int n) ? n : 1);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException) { locked++; }
            finally { try { File.Delete(copy); } catch (IOException) { } }
        }
        return new CookieSiteScan(counts.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList(), locked);
    }
}
