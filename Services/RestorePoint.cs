using System.Diagnostics;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Ponto de restauração do Windows antes de alterações de sistema (plano de energia, inicialização).
/// Criar ponto exige administrador e a "Proteção do Sistema" ativa; o Windows também limita a 1 ponto por 24 h.
/// Sem isso o app segue em frente e registra o motivo: as alterações dele já têm "Desfazer" próprio.
/// </summary>
public static class RestorePoint
{
    public const string ProtectionOff = "não criado (Proteção do Sistema desativada?)";

    public static async Task<string> EnsureAsync(AppSettings settings, string reason)
    {
        if (!settings.RestorePointBeforeChanges) return "desativado nas configurações";
        if (!AppInfo.IsAdmin) return "não criado (exige administrador)";
        if ((DateTime.Now - settings.LastRestorePoint).TotalHours < 24) return "já existe um ponto recente (< 24 h)";

        string safe = new string(reason.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-').ToArray());
        string script = $"Checkpoint-Computer -Description 'Notebook Doctor - {safe}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop";
        return await Task.Run(() =>
        {
            try
            {
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true });
                if (p is null) return "não criado";
                if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } return "não criado (demorou demais)"; }
                if (p.ExitCode != 0) return ProtectionOff;
                settings.LastRestorePoint = DateTime.Now;
                settings.Save();
                return "criado";
            }
            catch { return "não criado"; }
        });
    }
}
