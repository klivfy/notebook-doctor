using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// ARC-03: registro centralizado e seguro de falhas inesperadas em <c>erros.log</c>, com trava de concorrência
/// e teto de tamanho (512 KB) para nunca crescer sem limite nem derrubar quem chamou.
/// </summary>
public static class ErrorJournal
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Sync = new();

    public static string DefaultDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookDoctor");

    public static void Record(Exception ex, string? context = null, string? logDir = null)
    {
        if (ex is null) return;
        string dir = string.IsNullOrWhiteSpace(logDir) ? DefaultDir : logDir;
        if (string.IsNullOrWhiteSpace(dir)) return;

        string header = string.IsNullOrWhiteSpace(context)
            ? $"{DateTime.Now:s} {ex}\n\n"
            : $"{DateTime.Now:s} [{context}] {ex}\n\n";

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "erros.log");
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    string prev = Path.Combine(dir, "erros.anterior.log");
                    if (File.Exists(prev)) SafeDelete.FileAtExactPath(prev, ignoreReadOnly: true);
                    File.Move(file, prev);
                }
                File.AppendAllText(file, header, Encoding.UTF8);
            }
            catch (Exception ioEx) when (ioEx is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Falha de gravação no próprio log nunca pode propagar
            }
        }
    }
}

