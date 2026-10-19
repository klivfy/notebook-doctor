namespace NotebookDoctor.Services;

/// <summary>Textos em português: plural real em vez de "processo(s)".</summary>
public static class Pt
{
    /// <summary>"1 processo", "3 processos". Use o formato N0 do pt-BR (1.304).</summary>
    public static string N(long n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";
}
