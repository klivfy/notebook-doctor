using System.Security;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>Abre chaves pelo caminho completo do Editor do Registro ("HKEY_LOCAL_MACHINE\SOFTWARE\…"), sempre na visão de 64 bits.</summary>
public static class RegistryPaths
{
    public static (RegistryHive Hive, string SubKey) Split(string path)
    {
        string p = Paths.Clean(path);
        int i = p.IndexOf('\\');
        string hive = i < 0 ? p : p[..i], sub = i < 0 ? "" : p[(i + 1)..];
        return (hive.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            _ => throw new ArgumentException($"Chave fora do registro conhecido: {path}"),
        }, sub);
    }

    /// <summary>Abre a chave, ou null quando não existe. Sem permissão de leitura, também null.</summary>
    public static RegistryKey? Open(string path, bool writable = false)
    {
        var (hive, sub) = Split(path);
        var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        if (sub.Length == 0) return root;
        try { return root.OpenSubKey(sub, writable); }
        catch (SecurityException) when (!writable) { return null; }
        finally { root.Dispose(); }
    }

    public static RegistryKey Create(string path)
    {
        var (hive, sub) = Split(path);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return root.CreateSubKey(sub, writable: true);
    }

    public static void DeleteTree(string path)
    {
        var (hive, sub) = Split(path);
        if (sub.Length == 0) throw new ArgumentException("A raiz do registro nunca é apagada.");
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }
}

/// <summary>A varredura de sobras no Windows de verdade: registro e arquivos. Só lê.</summary>
public sealed class WindowsLeftoverSystem : ILeftoverSystem
{
    private static readonly EnumerationOptions Children = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    private static readonly EnumerationOptions Recursive = new() { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

    private static bool Expected(Exception ex) => ex is SecurityException or UnauthorizedAccessException or IOException or ArgumentException;

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex) when (Expected(ex)) { return fallback; }
    }

    /// <summary>Enumera até onde der: uma pasta apagada ou bloqueada no meio do caminho encerra a lista em vez de lançar erro.</summary>
    private static IEnumerable<string> Guarded(Func<IEnumerable<string>> source)
    {
        IEnumerator<string> e;
        try { e = source().GetEnumerator(); }
        catch (Exception ex) when (Expected(ex)) { yield break; }
        using (e)
            while (true)
            {
                string current;
                try
                {
                    if (!e.MoveNext()) yield break;
                    current = e.Current;
                }
                catch (Exception ex) when (Expected(ex)) { yield break; }
                yield return current;
            }
    }

    public IEnumerable<string> SubKeys(string key) => Safe(() =>
    {
        using var k = RegistryPaths.Open(key);
        return k?.GetSubKeyNames() ?? [];
    }, []);

    public bool KeyExists(string key) => Safe(() =>
    {
        using var k = RegistryPaths.Open(key);
        return k is not null;
    }, false);

    public string? StringValue(string key, string? name) => Safe(() =>
    {
        using var k = RegistryPaths.Open(key);
        return k?.GetValue(name ?? "") switch
        {
            string s => s,
            string[] lines => string.Join(" ", lines),
            _ => null,
        };
    }, null);

    public IEnumerable<string> ValueNames(string key) => Safe(() =>
    {
        using var k = RegistryPaths.Open(key);
        return k?.GetValueNames() ?? [];
    }, []);

    public IEnumerable<string> Directories(string dir) =>
        Safe(() => Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, "*", Children).ToList() : [], []);

    public bool DirectoryExists(string dir) => Directory.Exists(dir);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> Files(string dir) =>
        Directory.Exists(dir) ? Guarded(() => Directory.EnumerateFiles(dir, "*", Recursive)) : [];

    public long Size(string path) => Safe(() =>
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0L;
        long total = 0;
        foreach (var f in Guarded(() => Directory.EnumerateFiles(path, "*", Recursive)))
            try { total += new FileInfo(f).Length; } catch (IOException) { }
        return total;
    }, 0L);
}

/// <summary>As pastas conhecidas deste usuário, para as proteções da varredura.</summary>
public static class WindowsPlaces
{
    /// <param name="extra">Pastas que também nunca saem (a pasta de dados do próprio app, por exemplo).</param>
    public static KnownPlaces Current(params string[] extra)
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        string profile = F(Environment.SpecialFolder.UserProfile);
        var content = new List<string>
        {
            F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.MyPictures), F(Environment.SpecialFolder.MyMusic),
            F(Environment.SpecialFolder.MyVideos), F(Environment.SpecialFolder.CommonDocuments), Path.Combine(profile, "Downloads"),
            Environment.GetEnvironmentVariable("OneDrive") ?? "", Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "",
            Environment.GetEnvironmentVariable("OneDriveCommercial") ?? "",
        };
        content.AddRange(extra);
        return new KnownPlaces(
            F(Environment.SpecialFolder.Windows), F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86),
            F(Environment.SpecialFolder.CommonProgramFiles), F(Environment.SpecialFolder.CommonProgramFilesX86), F(Environment.SpecialFolder.CommonApplicationData),
            profile, F(Environment.SpecialFolder.ApplicationData), F(Environment.SpecialFolder.LocalApplicationData),
            F(Environment.SpecialFolder.DesktopDirectory), F(Environment.SpecialFolder.CommonDesktopDirectory),
            F(Environment.SpecialFolder.StartMenu), F(Environment.SpecialFolder.CommonStartMenu),
            content.Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
