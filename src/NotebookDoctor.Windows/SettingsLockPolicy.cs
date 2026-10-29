using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// B38: senha das configurações definida pela TI. Fica em HKLM, que só administradores alteram, então quem usa
/// o computador não consegue tirá-la editando os próprios arquivos. Vale mais que a senha definida no app.
/// </summary>
public static class SettingsLockPolicy
{
    public const string KeyPath = @"SOFTWARE\Policies\Klivfy\NotebookDoctor";
    public const string ValueName = "SettingsLock";

    public static string? Read()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
            return k?.GetValue(ValueName) is string s && PasswordHash.IsValidFormat(s) ? s.Trim() : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // sem permissão de leitura ou registro indisponível: segue sem política
        }
    }
}

