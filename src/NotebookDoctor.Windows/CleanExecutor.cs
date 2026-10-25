using System.Security;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>O computador de verdade para o motor de limpeza: a leitura de sobras mais arquivos de uma pasta e datas.</summary>
public sealed class WindowsCleanSystem : WindowsLeftoverSystem, ICleanSystem
{
    private static readonly EnumerationOptions Top = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    public IEnumerable<string> DirectFiles(string dir) =>
        Directory.Exists(dir) ? Guarded(() => Directory.EnumerateFiles(dir, "*", Top)) : [];

    public DateTime LastWrite(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return DateTime.Now; }
    }
}

/// <param name="Cancelled">C6: a pessoa cancelou no meio; o resultado conta o que já saiu, para entrar em Ações com Desfazer.</param>
public sealed record RuleCleanResult(long Bytes, int Files, int Keys, int Skipped, List<string> Errors, bool Cancelled = false);

/// <summary>
/// Leva 6.1: apaga o que o plano do motor mostrou. Antes de cada gravação confere de novo as proteções (a mesma
/// <see cref="CleanSafety"/> e o mesmo <see cref="CleanEngine.Matches"/> da análise), então nada fora da pasta ou da
/// chave declarada sai. Só o registro vai para o backup: arquivos de cache e rastros não valem o espaço de uma cópia.
/// Arquivos em uso são pulados, links e junções nunca são seguidos.
/// </summary>
public static class CleanExecutor
{
    private static readonly EnumerationOptions Walk = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    /// <param name="onAction">Ações que o executor não faz sozinho (a área de transferência precisa da janela do app); devolve se deu certo.</param>
    /// <param name="ct">Cancelar não lança exceção: para no próximo arquivo ou chave e devolve o que já foi feito, com Cancelled.</param>
    public static RuleCleanResult Run(IEnumerable<CleanPlanEntry> entries, KnownPlaces places, BackupSession backup, bool isAdmin,
        Func<CleanAction, bool>? onAction = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0, keys = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var e in entries.Where(e => e.Selected))
        {
            if (ct.IsCancellationRequested) break;
            if (e.NeedsAdmin && !isAdmin) { skipped++; continue; }
            progress?.Report(Loc.T(e.Name));
            foreach (var t in e.Files)
            {
                var (b, n, s) = CleanFiles(t, places, ct);
                bytes += b; files += n; skipped += s;
            }
            foreach (var r in e.Registry)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (CleanKey(r, backup)) keys++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
                {
                    skipped++;
                    errors.Add($"{e.Name}: {ex.Message}");
                }
            }
            if (ct.IsCancellationRequested) break;
            if (e.Rule.Action != CleanAction.None && !Act(e.Rule.Action, onAction)) { skipped++; errors.Add(e.Name); }
        }
        return new RuleCleanResult(bytes, files, keys, skipped, errors, ct.IsCancellationRequested);
    }

    private static bool Act(CleanAction action, Func<CleanAction, bool>? onAction)
    {
        if (action == CleanAction.FlushDns) return BackupStore.Run(30_000, "ipconfig.exe", "/flushdns") == 0;
        return onAction?.Invoke(action) ?? false;
    }

    private static bool CleanKey(RegTarget r, BackupSession backup)
    {
        if (!CleanSafety.RegKeyAllowed(r.Key)) return false;
        if (r.ValueName is null)
        {
            using (var k = RegistryPaths.Open(r.Key)) if (k is null) return false;
            backup.AddKey(r.Key);
            RegistryPaths.DeleteTree(r.Key);
            return true;
        }
        using var key = RegistryPaths.Open(r.Key, writable: true);
        if (key?.GetValue(r.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } data) return false;
        backup.AddValue(r.Key, r.ValueName, key.GetValueKind(r.ValueName), data);
        key.DeleteValue(r.ValueName, throwOnMissingValue: false);
        return true;
    }

    private static (long Bytes, int Files, int Skipped) CleanFiles(FileTarget t, KnownPlaces places, CancellationToken ct)
    {
        long bytes = 0;
        int files = 0, skipped = 0;
        if (!CleanSafety.DirAllowed(t.Dir, t.Recurse, t.RemoveSelf, t.Patterns, places) || !Directory.Exists(t.Dir)) return (0, 0, 0);
        if (new DirectoryInfo(t.Dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) return (0, 0, 1); // a pasta é um link: o destino não é dela
        if (SafeDelete.Root(t.Dir) is not { } root) return (0, 0, 1);
        var options = t.Recurse ? new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint } : Walk;
        foreach (var f in new DirectoryInfo(t.Dir).EnumerateFiles("*", options))
        {
            if (ct.IsCancellationRequested) return (bytes, files, skipped);
            try
            {
                if (!CleanEngine.Matches(t, f.FullName, () => f.LastWriteTime)) continue;
                long len = f.Length;
                // Pelo identificador: uma subpasta trocada por junção depois da lista não leva a exclusão para fora da regra
                if (!SafeDelete.File(f.FullName, root, ignoreReadOnly: true)) { skipped++; continue; }
                bytes += len;
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; } // em uso ou sem permissão
        }
        if (t.Recurse && t.Patterns.Any(p => p is "*" or "*.*")) RemoveEmptyDirs(t.Dir, root, t);
        if (t.RemoveSelf)
        {
            try { SafeDelete.Directory(t.Dir, root); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return (bytes, files, skipped);
    }

    /// <summary>Subpastas que ficaram vazias; a pasta da regra só sai com REMOVESELF, e as poupadas pela regra ficam.</summary>
    private static void RemoveEmptyDirs(string dir, string root, FileTarget t)
    {
        try
        {
            foreach (var d in new DirectoryInfo(dir).EnumerateDirectories("*", Walk.WithRecurse()).OrderByDescending(d => d.FullName.Length))
            {
                if (t.ExcludedPaths.Any(x => Paths.IsUnder(d.FullName, x) || Paths.IsUnder(x, d.FullName))) continue;
                try { if (!d.EnumerateFileSystemInfos().Any()) SafeDelete.Directory(d.FullName, root); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static EnumerationOptions WithRecurse(this EnumerationOptions o) => new()
    {
        IgnoreInaccessible = o.IgnoreInaccessible, AttributesToSkip = o.AttributesToSkip, RecurseSubdirectories = true,
    };

    /// <summary>Apaga os arquivos inúteis marcados. .bak e .old vão para a Lixeira (podem ser da pessoa); o resto sai direto.</summary>
    public static RuleCleanResult DeleteJunk(IEnumerable<JunkItem> items, KnownPlaces places, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0, skipped = 0;
        foreach (var i in items.Where(i => i.Selected))
        {
            ct.ThrowIfCancellationRequested();
            string path = i.Path;
            // Mesmas barreiras da busca: nada no Windows, em Program Files nem no conteúdo da pessoa (exceto as travas ~$)
            if (!CleanSafety.IsPlainPath(path) || Paths.IsUnder(path, places.Windows) || Paths.IsUnder(path, places.ProgramFiles) || Paths.IsUnder(path, places.ProgramFilesX86)) { skipped++; continue; }
            if (JunkRules.InUserContent(path, places) && i.Kind != JunkKind.OfficeLock) { skipped++; continue; }
            try
            {
                if (i.IsFolder)
                {
                    if (!JunkRules.IsLostFolder(Paths.Leaf(path)) || !Directory.Exists(path) || new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped++; continue; }
                    if (!LeftoverRemover.Recycle(path)) { skipped++; continue; }
                    files++;
                    bytes += i.Bytes;
                    continue;
                }
                if (!File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped++; continue; }
                if (JunkRules.Classify(Paths.Leaf(path), File.GetLastWriteTime(path), DateTime.Now, JunkRules.InUserContent(path, places)) is null) { skipped++; continue; } // mudou desde a busca
                if (i.Kind is JunkKind.Backup or JunkKind.OldFile)
                {
                    if (!LeftoverRemover.Recycle(path)) { skipped++; continue; }
                }
                else
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                files++;
                bytes += i.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new RuleCleanResult(bytes, files, 0, skipped, []);
    }
}

/// <summary>Limpeza de Disco do Windows: marca os módulos escolhidos com um StateFlags nosso e roda o cleanmgr. Exige administrador.</summary>
public static class DiskCleanupRunner
{
    private const string Caches = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    public static List<DiskModule> Available()
    {
        using var k = RegistryPaths.Open(Caches);
        return DiskCleanupRules.Modules(k?.GetSubKeyNames() ?? []);
    }

    /// <summary>Marca, roda o cleanmgr e tira as marcas. Devolve false se o cleanmgr não terminou bem.</summary>
    public static bool Run(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var chosen = keys.Where(k => !DiskCleanupRules.IsBlocked(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (chosen.Count == 0) return true;
        var all = Available().Select(m => m.Key).ToList();
        try
        {
            foreach (var key in all)
            {
                using var k = RegistryPaths.Open($@"{Caches}\{key}", writable: true);
                if (k is null) continue;
                if (chosen.Contains(key)) k.SetValue(DiskCleanupRules.StateFlagsName, 2, RegistryValueKind.DWord);
                else k.DeleteValue(DiskCleanupRules.StateFlagsName, throwOnMissingValue: false);
            }
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cleanmgr.exe", DiskCleanupRules.Arguments) { UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return false;
            while (!p.WaitForExit(500))
                if (ct.IsCancellationRequested) { try { p.Kill(true); } catch (InvalidOperationException) { } ct.ThrowIfCancellationRequested(); }
            return p.ExitCode == 0;
        }
        finally
        {
            foreach (var key in all)
                try
                {
                    using var k = RegistryPaths.Open($@"{Caches}\{key}", writable: true);
                    k?.DeleteValue(DiskCleanupRules.StateFlagsName, throwOnMissingValue: false);
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }
}
