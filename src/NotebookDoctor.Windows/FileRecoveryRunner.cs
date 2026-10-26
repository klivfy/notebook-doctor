using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NotebookDoctor.Services;

public sealed record RecoveryRun(int ExitCode, string LastLines, bool Cancelled);

/// <summary>
/// Leva 8.3 (V01): roda o <c>winfr</c> (Windows File Recovery, da Microsoft; vem da Microsoft Store) e acompanha a saída. O winfr pede
/// administrador e uma confirmação ("Continue? y/n"); o app só responde "y" depois de a pessoa ter confirmado na tela do próprio app.
/// </summary>
public static class FileRecoveryRunner
{
    /// <summary>Página do Windows File Recovery na Microsoft Store.</summary>
    public const string StoreUri = "ms-windows-store://pdp/?productid=9N26S50LN705";

    /// <summary>O winfr existe e responde? Falso quando não está instalado.</summary>
    public static async Task<bool> AvailableAsync(string exe = "winfr")
    {
        try
        {
            var run = await RunAsync(exe, ["/?"], null, null, CancellationToken.None, answer: null, limit: TimeSpan.FromSeconds(20));
            return run.LastLines.Trim().Length > 0;
        }
        catch (Win32Exception) { return false; }
    }

    /// <exception cref="Win32Exception">O executável não foi encontrado.</exception>
    public static async Task<RecoveryRun> RunAsync(string exe, string[] args, IProgress<int>? percent, IProgress<string>? lines, CancellationToken ct,
                                                   string? answer = "y", TimeSpan? limit = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var tail = new Queue<string>();
        void Handle(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (tail) { tail.Enqueue(line.Trim()); while (tail.Count > 12) tail.Dequeue(); }
            lines?.Report(line.Trim());
            if (RecoveryRules.ParsePercent(line) is { } n) percent?.Report(n);
        }
        p.OutputDataReceived += (_, e) => Handle(e.Data);
        p.ErrorDataReceived += (_, e) => Handle(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            if (answer is not null) await p.StandardInput.WriteLineAsync(answer);
            p.StandardInput.Close();
        }
        catch (IOException) { /* o processo já terminou: sem ninguém para ler a resposta */ }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit ?? TimeSpan.FromHours(24));
        try { await p.WaitForExitAsync(timeout.Token); p.WaitForExit(); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            string partial;
            lock (tail) partial = string.Join("\n", tail);
            return new RecoveryRun(-1, partial, Cancelled: true);
        }
        string last;
        lock (tail) last = string.Join("\n", tail);
        return new RecoveryRun(p.ExitCode, last, false);
    }
}
