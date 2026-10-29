using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// M33: quanto da pasta do OneDrive está baixado neste computador. Só lê os atributos dos arquivos (não abre
/// nenhum, então nada é baixado). Pasta grande demora: o resultado vale por 6 horas, e a contagem para em
/// 20 segundos ou 300 mil arquivos.
/// </summary>
public static class OneDriveScan
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(20);
    private const int MaxFiles = 300_000;
    private static OneDriveInfo? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;
    private static readonly object Lock = new();

    public static OneDriveInfo? Get()
    {
        lock (Lock)
        {
            if (DateTime.Now - _cachedAt < CacheFor) return _cached;
            _cached = Scan();
            _cachedAt = DateTime.Now;
            return _cached;
        }
    }

    private static OneDriveInfo? Scan()
    {
        if (Folders().FirstOrDefault(Directory.Exists) is not { } folder) return null;
        long local = 0, online = 0;
        int count = 0;
        var watch = Stopwatch.StartNew();
        bool partial = false;
        var files = new FileSystemEnumerable<(long Size, int Attr)>(folder,
            (ref FileSystemEntry e) => (e.Length, (int)e.Attributes),
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false })
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,
        };
        try
        {
            foreach (var (size, attr) in files)
            {
                if (OneDriveRule.IsOnlineOnly(attr)) online += size; else local += size;
                if (++count >= MaxFiles || watch.Elapsed > MaxTime) { partial = true; break; }
            }
        }
        catch (IOException) { partial = true; }
        catch (UnauthorizedAccessException) { partial = true; }
        return new OneDriveInfo(folder, local, online, partial);
    }

    /// <summary>
    /// Pastas do OneDrive deste usuário: contas configuradas no registro, bibliotecas do SharePoint e do Teams sincronizadas
    /// (C5: ficam fora da pasta do OneDrive, em "C:\Users\ana\Empresa") e, de reserva, as variáveis de ambiente.
    /// </summary>
    public static IEnumerable<string> Folders()
    {
        var list = new List<string>();
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var k = accounts!.OpenSubKey(name);
                if (k?.GetValue("UserFolder") is string f && f.Length > 0) list.Add(f);
                // Cada biblioteca sincronizada aparece como um valor cujo nome é a pasta local
                using var tenants = k?.OpenSubKey("Tenants");
                foreach (var tenant in tenants?.GetSubKeyNames() ?? [])
                {
                    using var t = tenants!.OpenSubKey(tenant);
                    list.AddRange((t?.GetValueNames() ?? []).Where(v => Path.IsPathFullyQualified(v)));
                }
                using var mounts = k?.OpenSubKey("ScopeIdToMountPointPathCache");
                foreach (var v in mounts?.GetValueNames() ?? [])
                    if (mounts!.GetValue(v) is string mount && Path.IsPathFullyQualified(mount)) list.Add(mount);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        foreach (var v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } f) list.Add(f);
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

