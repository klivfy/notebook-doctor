using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace NotebookDoctor.Services;

public static class AppInfo
{
    public const string SupportUrl = KlivfyUrls.Support;
    public const string Copyright = "© 2026 Klivfy Tecnologia e Consultoria em TI LTDA. Todos os direitos reservados.";

    public static string Version =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0").Split('+')[0];

    public static bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void OpenSupport() => OpenShell(SupportUrl);

    /// <summary>Abre uma página do site da Klivfy (termos, privacidade). Endereço que não seja https no domínio da Klivfy não abre.</summary>
    public static void OpenKlivfy(string url) { if (KlivfyUrls.IsTrusted(url)) OpenShell(url); }

    /// <summary>Abre uma URL ou arquivo com o aplicativo padrão do Windows.</summary>
    public static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* sem navegador padrão ou bloqueado: nada a fazer */ }
    }

    /// <summary>Reabre o app com permissão de administrador (o Windows mostra o aviso do UAC). Retorna false se o usuário negar.</summary>
    public static bool RestartElevated()
    {
        if (Environment.ProcessPath is not { } exe) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--wait-parent={Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // UAC recusado
    }
}
