namespace NotebookDoctor.Services;

/// <summary>Textos em português: plural real em vez de "processo(s)".</summary>
public static class Pt
{
    /// <summary>"1 processo", "3 processos". Use o formato N0 do pt-BR (1.304).</summary>
    public static string N(long n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";

    /// <summary>"512 KB", "120 MB", "1,5 GB" (base 1024).</summary>
    public static string Bytes(long b) =>
        b >= 1L << 30 ? $"{b / 1073741824.0:0.0} GB" : b >= 1L << 20 ? $"{b / 1048576.0:0} MB" : $"{b / 1024.0:0} KB";
}
