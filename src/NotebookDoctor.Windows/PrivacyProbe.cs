using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 12, leva 12.3: redes Wi-Fi salvas, arquivo hosts e proxy, limpeza dos recentes antes de compartilhar a tela e os
/// ajustes do modo apresentação (ícones da área de trabalho e tela sempre acesa). Tudo que muda algo guarda backup.
/// </summary>
public static class PrivacyProbe
{
    // ---------- redes Wi-Fi salvas ----------

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanGetProfile(IntPtr handle, ref Guid iface, string name, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);

    private const uint PlaintextKey = 4; // WLAN_PROFILE_GET_PLAINTEXT_KEY: a senha só vem aberta com administrador

    /// <summary>As redes Wi-Fi salvas (a senha aparece só com o app como administrador).</summary>
    public static List<WifiProfile> WifiProfiles()
    {
        var result = new List<WifiProfile>();
        IntPtr handle = IntPtr.Zero, ifaces = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return result;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out ifaces) != 0 || ifaces == IntPtr.Zero) return result;
            int count = Marshal.ReadInt32(ifaces);
            for (int i = 0; i < Math.Min(count, 4); i++)
            {
                var guid = Marshal.PtrToStructure<Guid>(ifaces + 8 + i * 532);
                if (WlanGetProfileList(handle, ref guid, IntPtr.Zero, out IntPtr list) != 0 || list == IntPtr.Zero) continue;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    for (int j = 0; j < Math.Min(n, 200); j++)
                    {
                        string name = Marshal.PtrToStringUni(list + 8 + j * 516) ?? ""; // WLAN_PROFILE_INFO: nome (256 caracteres) + flags
                        if (name.Length == 0 || result.Any(r => r.Ssid == name)) continue;
                        uint flags = PlaintextKey;
                        if (WlanGetProfile(handle, ref guid, name, IntPtr.Zero, out IntPtr xml, ref flags, out _) != 0 || xml == IntPtr.Zero) continue;
                        try
                        {
                            if (NetworkPrivacy.ParseProfile(Marshal.PtrToStringUni(xml) ?? "") is { } p) result.Add(p);
                        }
                        finally { WlanFreeMemory(xml); }
                    }
                }
                finally { WlanFreeMemory(list); }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException) { }
        finally
        {
            if (ifaces != IntPtr.Zero) WlanFreeMemory(ifaces);
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
        return result.OrderBy(p => p.Ssid, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------- hosts e proxy ----------

    public static string HostsPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    public static string[] ReadHosts()
    {
        try { return File.ReadAllLines(HostsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Volta o hosts ao padrão do Windows, com o atual no backup. Exige administrador.</summary>
    public static bool RestoreHosts(BackupSession backup)
    {
        try
        {
            backup.AddFile(HostsPath);
            File.WriteAllText(HostsPath, NetworkPrivacy.DefaultHosts);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private const string InternetSettings = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>Endereço do script de proxy (PAC) do usuário; null sem script.</summary>
    public static string? Pac()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            return k?.GetValue("AutoConfigURL") is string url && url.Length > 0 ? url : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>Tira o script de proxy (PAC) do usuário, com o valor no backup.</summary>
    public static bool RemovePac(BackupSession backup)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
            if (k?.GetValue("AutoConfigURL") is not string url) return false;
            backup.AddValue(InternetSettings, "AutoConfigURL", RegistryValueKind.String, url);
            k.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    // ---------- antes de compartilhar a tela ----------

    private static string RecentDir => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    private static readonly string[] MruKeys =
    [
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
    ];

    /// <summary>Quantos itens recentes aparecem hoje (atalhos dos arquivos abertos e listas de atalhos da barra de tarefas).</summary>
    public static int RecentCount()
    {
        try { return Directory.Exists(RecentDir) ? Directory.EnumerateFiles(RecentDir, "*", SearchOption.AllDirectories).Count() : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// Limpa os arquivos recentes do Explorador e do Iniciar, as listas de atalhos e o que foi digitado na barra de endereços
    /// e no Executar. Tudo vai antes para o backup (pasta inteira e chaves), e o Desfazer devolve. Devolve quantos itens saíram.
    /// </summary>
    public static int ClearRecent(BackupSession backup)
    {
        int removed = 0;
        if (Directory.Exists(RecentDir))
        {
            backup.AddFolder(RecentDir);
            foreach (var f in Directory.EnumerateFiles(RecentDir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        foreach (var key in MruKeys)
        {
            string sub = key["HKEY_CURRENT_USER\\".Length..];
            using var k = Registry.CurrentUser.OpenSubKey(sub, writable: true);
            if (k is null || k.ValueCount == 0) continue;
            backup.AddKey(key);
            foreach (var name in k.GetValueNames()) { k.DeleteValue(name, throwOnMissingValue: false); removed++; }
        }
        return removed;
    }

    // ---------- modo apresentação ----------

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;

    /// <summary>Tela e computador acordados enquanto a apresentação durar (vale para a thread que chamou).</summary>
    public static void KeepAwake(bool on) =>
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    /// <summary>Os ícones da área de trabalho estão aparecendo (Exibir › Mostrar ícones da área de trabalho)?</summary>
    public static bool DesktopIconsVisible()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return k?.GetValue("HideIcons") is not int v || v == 0;
    }

    /// <summary>
    /// Alterna "Mostrar ícones da área de trabalho" como o próprio menu do Explorador faz (o comando 0x7402 da área de
    /// trabalho). Devolve false quando a área de trabalho não foi achada.
    /// </summary>
    public static bool ToggleDesktopIcons()
    {
        IntPtr view = FindWindowExW(FindWindowW("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            // Com papel de parede em apresentação de slides, a área de trabalho mora numa janela WorkerW
            for (IntPtr w = FindWindowExW(IntPtr.Zero, IntPtr.Zero, "WorkerW", null); w != IntPtr.Zero && view == IntPtr.Zero; w = FindWindowExW(IntPtr.Zero, w, "WorkerW", null))
                view = FindWindowExW(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        if (view == IntPtr.Zero) return false;
        SendMessageW(view, 0x0111 /* WM_COMMAND */, 0x7402, IntPtr.Zero);
        return true;
    }
}
