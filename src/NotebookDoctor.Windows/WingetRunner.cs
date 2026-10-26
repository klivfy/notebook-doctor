using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using NotebookDoctor.Services;

namespace NotebookDoctor.Services;

public sealed record WingetRun(int ExitCode, string Output, bool TimedOut);

/// <summary>
/// Leva 7.1 (C14): executa o <c>winget</c> (Instalador de Aplicativo) pela linha de comando, sem shell: os argumentos
/// vão em lista e o id do pacote já passou por <see cref="ProgramUpdateRules.IdOk"/> em <see cref="WingetCommands"/>.
/// O pedido de permissão (UAC) de instaladores que exigem administrador é do próprio winget.
/// </summary>
public static class WingetRunner
{
    /// <summary>Endereço do Instalador de Aplicativo na Microsoft Store, para quando o winget não existe.</summary>
    public const string AppInstallerStore = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1";

    /// <summary>O winget responde? Falso quando o Instalador de Aplicativo não está instalado.</summary>
    public static async Task<bool> AvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var run = await RunAsync(["--version"], TimeSpan.FromSeconds(20), ct);
            return run.ExitCode == 0 && run.Output.Trim().Length > 0;
        }
        catch (Win32Exception) { return false; }
    }

    /// <exception cref="Win32Exception">O winget não foi encontrado.</exception>
    public static async Task<WingetRun> RunAsync(string[] arguments, TimeSpan limit, CancellationToken ct = default, Action<string>? onLine = null, bool killOnTimeout = true)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var text = new StringBuilder();
        void Append(string? line)
        {
            if (line is null) return;
            lock (text) text.AppendLine(line);
            onLine?.Invoke(line);
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Start();
        process.StandardInput.Close(); // nada pergunta nada: sem entrada, o winget não espera resposta
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            process.WaitForExit(); // esvazia as filas de saída
        }
        catch (OperationCanceledException)
        {
            // Instalador no meio do caminho não é morto por tempo: deixaria o programa pela metade. Só cancelamento e consultas morrem.
            if (killOnTimeout || ct.IsCancellationRequested)
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            string partial;
            lock (text) partial = text.ToString();
            if (ct.IsCancellationRequested) throw;
            return new WingetRun(-1, partial, TimedOut: true);
        }
        string all;
        lock (text) all = text.ToString();
        return new WingetRun(process.ExitCode, all, false);
    }

    public static Task<WingetRun> ListUpgradesAsync(bool sourceTermsAccepted, CancellationToken ct) =>
        RunAsync(WingetCommands.ListUpgrades(sourceTermsAccepted), TimeSpan.FromMinutes(3), ct);

    public static Task<WingetRun> ShowAsync(string id, bool sourceTermsAccepted, CancellationToken ct) =>
        RunAsync(WingetCommands.Show(id, sourceTermsAccepted), TimeSpan.FromMinutes(2), ct);

    /// <summary>
    /// Atualiza um pacote. Não há cancelamento no meio: interromper o instalador deixaria o programa pela metade. Passados
    /// 45 minutos o app deixa de esperar (sem matar o instalador) e avisa.
    /// </summary>
    public static Task<WingetRun> UpgradeAsync(string id, bool sourceTermsAccepted, bool packageTermsAccepted, Action<string>? onLine = null) =>
        RunAsync(WingetCommands.Upgrade(id, sourceTermsAccepted, packageTermsAccepted), TimeSpan.FromMinutes(45), CancellationToken.None, onLine, killOnTimeout: false);

    /// <summary>Instala um pacote. Como na atualização, não há cancelamento no meio e o app deixa de esperar após 45 minutos.</summary>
    public static Task<WingetRun> InstallAsync(string id, bool sourceTermsAccepted, bool packageTermsAccepted) =>
        RunAsync(WingetCommands.Install(id, sourceTermsAccepted, packageTermsAccepted), TimeSpan.FromMinutes(45), CancellationToken.None, killOnTimeout: false);
}
