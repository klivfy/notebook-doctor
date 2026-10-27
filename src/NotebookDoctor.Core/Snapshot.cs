namespace NotebookDoctor.Services;

/// <param name="IoKBs">Leitura+gravação em KB/s (inclui arquivos, rede e pipes: é o "movimento de dados" do programa, não só disco)</param>
/// <param name="Conns">Conexões TCP abertas (indica quem está usando a rede; o Windows não informa banda por app sem administrador)</param>
/// <param name="Hung">Leva 10.2: alguma janela do programa parou de responder (o Windows diz "Não está respondendo").</param>
/// <param name="HasWindow">Algum processo do programa tem janela visível (aplicativo, e não só segundo plano).</param>
public sealed record ProcGroup(string Name, int Count, double CpuPct, long MemMB, double IoKBs = 0, int Conns = 0, bool Hung = false, bool HasWindow = false);

public sealed record Snapshot(
    DateTime Time,
    double Cpu,
    double RamPct,
    double RamUsedGB,
    double RamTotalGB,
    double DiskBusy,
    double DiskFreePct,
    double DiskFreeGB,
    IReadOnlyList<ProcGroup> Groups,
    double CpuPerf = -1, // % da frequência máxima em que a CPU está rodando (-1 = indisponível)
    double TempC = -1,   // temperatura da zona térmica ACPI, quando o Windows a expõe (-1 = indisponível)
    double NetDownKBs = 0, double NetUpKBs = 0)
{
    public ProcGroup? TopIo => Groups.Where(g => !g.Name.Equals("NotebookDoctor", StringComparison.OrdinalIgnoreCase)).OrderByDescending(g => g.IoKBs).FirstOrDefault(g => g.IoKBs > 0);
    // O próprio app fica fora dos rankings: ele não deve se acusar nem aparecer como "maior consumidor".
    private IEnumerable<ProcGroup> Others => Groups.Where(g => !g.Name.Equals("NotebookDoctor", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ProcGroup> TopByCpu => Others.OrderByDescending(g => g.CpuPct).Take(5);
    public IEnumerable<ProcGroup> TopByMem => Others.OrderByDescending(g => g.MemMB).Take(5);
    public IEnumerable<ProcGroup> Heavy => Others.OrderByDescending(g => g.CpuPct + g.MemMB / 100.0);
}
