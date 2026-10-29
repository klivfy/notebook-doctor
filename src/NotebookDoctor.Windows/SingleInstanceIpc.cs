using System.Diagnostics;
using System.IO.Pipes;

namespace NotebookDoctor.Services;

/// <summary>
/// ARC-04: coordenação de instância única por sessão de usuário (Mutex local) e encaminhamento seguro de links/argumentos
/// via pipe nomeado restrito ao próprio usuário (<see cref="PipeOptions.CurrentUserOnly"/>).
/// </summary>
public sealed class SingleInstanceIpc : IDisposable
{
    public const int MaxMessageLength = 200;

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public string PipeName { get; }
    public bool IsFirstInstance => _ownsMutex;

    private SingleInstanceIpc(Mutex mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        PipeName = pipeName;
    }

    public static SingleInstanceIpc Acquire(bool isDemo, string? scopeSuffix = null)
    {
        string suffix = (isDemo ? ".Demo" : "") + (string.IsNullOrEmpty(scopeSuffix) ? "" : "." + scopeSuffix);
        string mutexName = @"Local\NotebookDoctor.SingleInstance" + suffix;
        string pipeName = $"NotebookDoctor.{Environment.UserName}{suffix}";
        bool createdNew;
        var mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        return new SingleInstanceIpc(mutex, createdNew, pipeName);
    }

    /// <summary>Reinício elevado ou troca de idioma: aguarda o processo anterior encerrar antes de disputar o Mutex.</summary>
    public static void WaitParentExit(IReadOnlyList<string> args, int timeoutMs = 15_000)
    {
        string? waitArg = args.FirstOrDefault(a => a.StartsWith("--wait-parent=", StringComparison.Ordinal));
        if (waitArg is null || !int.TryParse(waitArg.AsSpan(14), out int parentPid)) return;
        try
        {
            using var proc = Process.GetProcessById(parentPid);
            proc.WaitForExit(timeoutMs);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Processo pai já encerrou ou não é acessível
        }
    }

    /// <summary>Converte argumentos de linha de comando num pedido de navegação <c>notebookdoctor:...</c>.</summary>
    public static string BuildActivationMessage(IReadOnlyList<string> args, string scheme)
    {
        string? uri = args.FirstOrDefault(a => a.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase));
        if (uri is not null) return uri;
        string? tabRequest = args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.Ordinal));
        return tabRequest is not null ? $"{scheme}:tab/{tabRequest[6..]}" : $"{scheme}:open";
    }

    public static bool TrySend(string pipeName, string message, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message.Length > MaxMessageLength ? message[..MaxMessageLength] : message);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false; // outra instância está fechando ou indisponível
        }
    }

    public static async Task ListenAsync(string pipeName, Action<string> onMessage, Action<Exception>? onError, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                string? msg = await reader.ReadLineAsync(ct);
                if (msg is not null)
                    onMessage(msg.Length > MaxMessageLength ? msg[..MaxMessageLength] : msg);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* chamado em thread diferente da que criou o Mutex */ }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}

