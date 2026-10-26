using System.Text;

namespace NotebookDoctor.Services;

/// <summary>Leva 8.4 (D01): roda o <c>Optimize-Volume</c> do Windows (análise, desfragmentação de HD ou TRIM de SSD) e acompanha a saída.</summary>
public static class DefragRunner
{
    public static Task<RecoveryRun> RunAsync(string letter, DefragAction action, IProgress<int>? percent, IProgress<string>? lines, CancellationToken ct)
    {
        // A letra passou por DefragRules.ValidLetter (Script lança se não for A a Z). A saída vem em UTF-8 para os acentos não quebrarem.
        string script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + DefragRules.Script(letter, action);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return FileRecoveryRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            percent, lines, ct, answer: null, limit: TimeSpan.FromHours(24));
    }
}
