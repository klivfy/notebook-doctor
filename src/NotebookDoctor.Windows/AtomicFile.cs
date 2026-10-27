using System.IO;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 9.2: gravação por arquivo temporário e troca. Uma queda de energia ou o app fechado no meio da gravação deixa o
/// arquivo antigo inteiro, nunca um pela metade (que, ao abrir de novo, viraria configurações zeradas ou Ações vazias).
/// </summary>
public static class AtomicFile
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string text)
    {
        string temp = path + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Utf8.GetBytes(text);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines) =>
        WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));

    /// <summary>
    /// Guarda ao lado uma cópia de um arquivo que não pôde ser lido, antes que a próxima gravação o substitua. Devolve o
    /// caminho da cópia, ou null se não havia arquivo ou a cópia falhou.
    /// </summary>
    public static string? KeepCopy(string path, string suffix)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string copy = $"{path}.{suffix}";
            File.Copy(path, copy, overwrite: true);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
