namespace NotebookDoctor.Services;

/// <summary>
/// B32: destaque das linhas que mais pesam. "high" (vermelho) e "mid" (laranja); vazio = sem destaque.
/// A cor nunca é o único sinal: o número da coluna e o texto lido pelo Narrador dizem o mesmo.
/// </summary>
public static class RowSeverity
{
    public static string Process(double cpuPct, long memMB, double ramTotalGB)
    {
        double memShare = ramTotalGB > 0 ? memMB / (ramTotalGB * 1024) : 0;
        return cpuPct >= 50 || memShare >= 0.25 ? "high" : cpuPct >= 20 || memShare >= 0.10 ? "mid" : "";
    }

    /// <summary>Pelo texto de impacto da inicialização ("Alto (…)", "Médio (…)").</summary>
    public static string Startup(string impact) =>
        impact.StartsWith("Alto", StringComparison.Ordinal) ? "high" : impact.StartsWith("Médio", StringComparison.Ordinal) ? "mid" : "";

    public static string Spoken(string severity) => severity switch { "high" => "uso alto", "mid" => "uso médio", _ => "" };
}

/// <summary>B14: mensagem ao fim da limpeza, com comemoração a partir de 1 GB.</summary>
public static class CleanOutcome
{
    public const long CelebrateBytes = 1L << 30;

    public static (string Title, string Text, bool Celebrate) Describe(long freed, DateTime? day = null)
    {
        var d = day ?? DateTime.Today;
        if (freed >= CelebrateBytes)
        {
            long photos = freed / (3L << 20); // foto de celular ≈ 3 MB
            return (Loc.F("Ótimo: {0} liberados!", Units.Bytes(freed)), Loc.F("Espaço para cerca de {0:N0} fotos de celular.", photos) + " " + Friendly.Pick(Friendly.Freed, d), true);
        }
        return freed > 0
            ? (Loc.F("{0} liberados", Units.Bytes(freed)), Friendly.Pick(Friendly.Freed, d), false)
            : (Loc.T("Nada para liberar"), Friendly.Pick(Friendly.NothingFreed, d), false);
    }
}
