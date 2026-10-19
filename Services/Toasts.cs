using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Notificações nativas do Windows (central de notificações) com botões.
/// Sem pacotes extras: registra um AppUserModelID no HKCU e usa a API WinRT de toasts via PowerShell.
/// Os botões abrem links "notebookdoctor:" que só navegam no app (abrir painel, abrir aba, gerar relatório);
/// nenhuma ação destrutiva pode ser disparada por link, porque qualquer página web também consegue abrir esse tipo de link.
/// </summary>
public static class Toasts
{
    public const string Aumid = "Klivfy.NotebookDoctor";
    public const string Scheme = "notebookdoctor";

    public static void EnsureRegistered()
    {
        try
        {
            string icon = Path.Combine(AppPaths.Dir, "icon.png");
            if (!File.Exists(icon))
            {
                var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/icon-256.png"));
                if (res is not null) using (var fs = File.Create(icon)) res.Stream.CopyTo(fs);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}"))
            {
                k.SetValue("DisplayName", "Notebook Doctor");
                k.SetValue("IconUri", icon);
            }
            if (Environment.ProcessPath is { } exe)
            {
                using var p = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
                p.SetValue("", "URL:Notebook Doctor");
                p.SetValue("URL Protocol", "");
                using var cmd = p.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            }
        }
        catch { /* sem permissão no registro: o app cai para o balão da bandeja */ }
    }

    /// <summary>Mostra a notificação. Retorna false se não deu (o chamador usa o balão da bandeja).</summary>
    public static bool Show(string title, string text, params (string label, string action)[] buttons)
    {
        try
        {
            static string X(string s) => SecurityElement.Escape(s) ?? "";
            var actions = new StringBuilder();
            foreach (var (label, action) in buttons)
                actions.Append($"<action content=\"{X(label)}\" activationType=\"protocol\" arguments=\"{Scheme}:{X(action)}\"/>");
            string xml = $"<toast activationType=\"protocol\" launch=\"{Scheme}:open\"><visual><binding template=\"ToastGeneric\"><text>{X(title)}</text><text>{X(text)}</text></binding></visual>"
                       + (buttons.Length > 0 ? $"<actions>{actions}</actions>" : "") + "</toast>";

            // O XML vai em Base64 para não haver problema de aspas dentro do script
            string xml64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
            string script = $$"""
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$d = New-Object Windows.Data.Xml.Dom.XmlDocument
$d.LoadXml([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{xml64}}')))
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{{Aumid}}').Show((New-Object Windows.UI.Notifications.ToastNotification $d))
""";
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
            { CreateNoWindow = true, UseShellExecute = false });
            return p is not null;
        }
        catch { return false; }
    }
}
