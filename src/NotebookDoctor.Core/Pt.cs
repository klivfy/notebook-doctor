namespace NotebookDoctor.Services;

/// <summary>Textos em português: plural real em vez de "processo(s)".</summary>
public static class Pt
{
    /// <summary>"1 processo", "3 processos", no formato de número do idioma (1.304 / 1,304). As duas formas passam pela tradução.</summary>
    public static string N(long n, string one, string many) => $"{n:N0} {Loc.T(n == 1 ? one : many)}";

    /// <summary>"512 KB", "120 MB", "1,5 GB", na unidade escolhida em Configurações (<see cref="Units"/>).</summary>
    public static string Bytes(long b) => Units.Bytes(b);
}
