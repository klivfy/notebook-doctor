using System.ComponentModel;

namespace NotebookDoctor.Services;

/// <summary>Resultado de uma consulta de atualizações: a lista e quem a produziu ("COM" ou "CLI").</summary>
public sealed record UpdateScan(WingetList List, string Engine, bool TimedOut = false, int ExitCode = 0, bool HasTable = true);

/// <summary>Texto dos dados e dos termos de um pacote, no formato que a pessoa lê na janela de termos.</summary>
public sealed record UpdateDetails(string Text, string Engine);

/// <summary>O motor não pode ser usado agora (Instalador de Aplicativo ausente ou sem o servidor COM, catálogo fora do ar, sem permissão).</summary>
public sealed class UpdateEngineUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Prioridade 5 (D2/C14): ponto único de consulta ao catálogo do WinGet. A API COM do Instalador de Aplicativo é o motor
/// principal; a linha de comando é a reserva. Quem atualiza (instala) continua pela linha de comando, com os aceites que a
/// pessoa deu na tela: nenhum motor aceita termos por conta própria.
/// </summary>
public interface IUpdateEngine
{
    string Name { get; }

    /// <param name="sourceTermsAccepted">A pessoa já aceitou os termos da fonte na tela. Falso: o motor não pode aceitar por ela.</param>
    Task<UpdateScan> ListUpgradesAsync(bool sourceTermsAccepted, CancellationToken ct);

    /// <returns>Texto do pacote com licença e termos; <c>null</c> quando o motor não tem o pacote.</returns>
    Task<UpdateDetails?> ShowAsync(string id, bool sourceTermsAccepted, CancellationToken ct);
}

/// <summary>Reserva: interpreta a saída de <c>winget upgrade</c> e <c>winget show</c>.</summary>
public sealed class CliUpdateEngine : IUpdateEngine
{
    public string Name => "CLI";

    public async Task<UpdateScan> ListUpgradesAsync(bool sourceTermsAccepted, CancellationToken ct)
    {
        WingetRun run;
        try { run = await WingetRunner.ListUpgradesAsync(sourceTermsAccepted, ct); }
        catch (Win32Exception ex) { throw new UpdateEngineUnavailableException("O winget não foi encontrado.", ex); }
        var list = WingetCommands.ParseUpgrades(run.Output);
        return new UpdateScan(list, Name, run.TimedOut, run.ExitCode, run.Output.Contains("----"));
    }

    public async Task<UpdateDetails?> ShowAsync(string id, bool sourceTermsAccepted, CancellationToken ct)
    {
        WingetRun run;
        try { run = await WingetRunner.ShowAsync(id, sourceTermsAccepted, ct); }
        catch (Win32Exception ex) { throw new UpdateEngineUnavailableException("O winget não foi encontrado.", ex); }
        string text = WingetShowText.Clean(run.Output);
        return text.Length > 0 ? new UpdateDetails(text, Name) : null;
    }
}

/// <summary>
/// Tenta o motor principal e, se ele falhar por qualquer motivo que não seja cancelamento, passa para a reserva sem a
/// pessoa notar. Depois de uma falha o principal fica de lado até o app reiniciar: não vale pagar o tempo de uma
/// ativação COM que não funciona a cada consulta.
/// </summary>
public sealed class FailoverUpdateEngine(IUpdateEngine? primary, IUpdateEngine fallback) : IUpdateEngine
{
    private volatile bool _primaryFailed;

    public string Name => Active.Name;
    public Exception? LastPrimaryError { get; private set; }
    public bool UsingPrimary => primary is not null && !_primaryFailed;

    private IUpdateEngine Active => UsingPrimary ? primary! : fallback;

    public Task<UpdateScan> ListUpgradesAsync(bool sourceTermsAccepted, CancellationToken ct) =>
        Run(e => e.ListUpgradesAsync(sourceTermsAccepted, ct), ct);

    public Task<UpdateDetails?> ShowAsync(string id, bool sourceTermsAccepted, CancellationToken ct) =>
        Run(e => e.ShowAsync(id, sourceTermsAccepted, ct), ct);

    private async Task<T> Run<T>(Func<IUpdateEngine, Task<T>> call, CancellationToken ct)
    {
        if (UsingPrimary)
        {
            try { return await call(primary!); }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException)
            {
                // COMException, DllNotFound, TypeLoad, UnauthorizedAccess (processo elevado) etc.: a reserva responde no lugar
                LastPrimaryError = ex;
                _primaryFailed = true;
            }
        }
        return await call(fallback);
    }
}
