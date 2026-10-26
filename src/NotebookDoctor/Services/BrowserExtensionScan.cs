using System.IO;

namespace NotebookDoctor.Services;

/// <summary>M31: encontra os perfis do Chrome, Edge, Brave, Vivaldi e Firefox e lê as extensões instaladas (só leitura).</summary>
public static class BrowserExtensionScan
{
    /// <summary>Navegadores Chromium: nome, pasta "User Data" e executável para abrir a página de extensões.</summary>
    public static readonly (string Name, string UserData, string Exe, string Page)[] Chromium =
    [
        ("Chrome", @"Google\Chrome\User Data", "chrome.exe", "chrome://extensions"),
        ("Edge", @"Microsoft\Edge\User Data", "msedge.exe", "edge://extensions"),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data", "brave.exe", "brave://extensions"),
        ("Vivaldi", @"Vivaldi\User Data", "vivaldi.exe", "vivaldi://extensions"),
    ];

    public static List<BrowserExtension> List()
    {
        var all = new List<BrowserExtension>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (name, userData, _, _) in Chromium)
        {
            string root = Path.Combine(local, userData);
            if (!Directory.Exists(root)) continue;
            foreach (var profile in Profiles(root))
                all.AddRange(ReadChromiumProfile(name, profile));
        }
        all.AddRange(ReadFirefox());
        return BrowserExtensionRules.Sort(all);
    }

    // "Default", "Profile 1", "Profile 2"… (a pasta "System Profile" e "Guest Profile" ficam de fora)
    private static IEnumerable<string> Profiles(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal))
                .Where(d => Directory.Exists(Path.Combine(d, "Extensions")))
                .ToList();
        }
        catch { return []; }
    }

    private static IEnumerable<BrowserExtension> ReadChromiumProfile(string browser, string profileDir)
    {
        var list = new List<BrowserExtension>();
        string profile = Path.GetFileName(profileDir) == "Default" ? "Perfil padrão" : Path.GetFileName(profileDir).Replace("Profile", "Perfil");
        try
        {
            foreach (var extDir in Directory.EnumerateDirectories(Path.Combine(profileDir, "Extensions")))
            {
                string id = Path.GetFileName(extDir);
                if (id == "Temp") continue;
                // Cada versão instalada fica numa subpasta; vale a mais recente
                var versionDir = Directory.EnumerateDirectories(extDir).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
                string manifestPath = versionDir is null ? "" : Path.Combine(versionDir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                string manifest = File.ReadAllText(manifestPath);
                string? defLocale = BrowserExtensionRules.DefaultLocale(manifest);
                string? Message(string key)
                {
                    foreach (var loc in BrowserExtensionRules.LocaleOrder(defLocale))
                    {
                        string file = Path.Combine(versionDir!, "_locales", loc, "messages.json");
                        if (File.Exists(file) && BrowserExtensionRules.Message(File.ReadAllText(file), key) is { } m) return m;
                    }
                    return null;
                }
                var ext = BrowserExtensionRules.FromChromiumManifest(manifest, browser, profile, id, Message, FolderSize(versionDir!));
                if (ext is not null) list.Add(ext);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return list;
    }

    private static IEnumerable<BrowserExtension> ReadFirefox()
    {
        var list = new List<BrowserExtension>();
        string profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
        try
        {
            if (!Directory.Exists(profiles)) return list;
            foreach (var dir in Directory.EnumerateDirectories(profiles))
            {
                string file = Path.Combine(dir, "extensions.json");
                if (!File.Exists(file)) continue;
                // "abcd1234.default-release" → "default-release"
                string name = Path.GetFileName(dir);
                name = name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name;
                list.AddRange(BrowserExtensionRules.FromFirefox(File.ReadAllText(file), name));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return list;
    }

    private static long FolderSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => f.Length); }
        catch { return 0; }
    }

    /// <summary>Abre a página de extensões do navegador (o Windows acha o executável pelo registro "App Paths").</summary>
    public static void OpenPage(string browser)
    {
        if (browser == "Firefox") { Start("firefox.exe", "about:addons"); return; }
        var b = Chromium.FirstOrDefault(c => c.Name == browser);
        if (b.Name is not null) Start(b.Exe, b.Page);
    }

    private static void Start(string exe, string arg)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arg) { UseShellExecute = true }); }
        catch { /* navegador não instalado para este usuário */ }
    }
}
