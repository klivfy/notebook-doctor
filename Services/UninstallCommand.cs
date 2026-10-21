using System.IO;

namespace NotebookDoctor.Services;

/// <summary>
/// Comando de desinstalação lido do registro, separado em executável + argumentos.
/// É executado direto (sem cmd.exe), então "&amp;", "|" e redirecionamentos viram texto comum em vez de comandos extras.
/// </summary>
public sealed record UninstallCommand(string Executable, string Arguments)
{
    public string Display => Arguments.Length == 0 ? Executable : $"{Executable} {Arguments}";

    /// <summary>Resolve o comando ou retorna null se o executável não existir. <paramref name="exists"/> existe para testes.</summary>
    public static UninstallCommand? Parse(string? raw, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string cmd = Environment.ExpandEnvironmentVariables(raw.Trim());

        if (cmd[0] == '"')
        {
            int close = cmd.IndexOf('"', 1);
            if (close < 0) return null;
            return Resolve(cmd[1..close], cmd[(close + 1)..].Trim(), exists);
        }

        // Sem aspas, o caminho pode ter espaços ("C:\Program Files\App\unins000.exe /S"): testa o prefixo mais curto que existe.
        for (int i = 0; i <= cmd.Length; i++)
        {
            if (i < cmd.Length && cmd[i] != ' ') continue;
            if (Resolve(cmd[..i], cmd[i..].Trim(), exists) is { } found) return found;
        }
        return null;
    }

    private static UninstallCommand? Resolve(string exe, string args, Func<string, bool> exists)
    {
        if (exe.Length == 0) return null;
        if (!Path.IsPathRooted(exe))
        {
            // "MsiExec.exe /X{...}": só procura na pasta do sistema, nunca no diretório atual nem no PATH.
            if (exe.Contains('\\') || exe.Contains('/')) return null;
            exe = Path.Combine(Environment.SystemDirectory, exe);
        }
        return exists(exe) ? new UninstallCommand(exe, args) : null;
    }
}
