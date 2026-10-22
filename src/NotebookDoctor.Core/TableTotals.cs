namespace NotebookDoctor.Services;

/// <summary>B33: linha de totais embaixo das tabelas. Com busca ativa, soma só o que está à vista e diz "de quantos".</summary>
public static class TableTotals
{
    private static string Of(int visible, int total, string one, string many) =>
        visible == total ? Pt.N(total, one, many) : $"{visible:N0} de {Pt.N(total, one, many)}";

    public static string Processes(IReadOnlyCollection<(int Count, double Cpu, long MemMB)> visible, int total) =>
        Of(visible.Count, total, "programa", "programas") +
        $" · {Pt.N(visible.Sum(r => r.Count), "processo", "processos")} · CPU {visible.Sum(r => r.Cpu):0}% · memória {Units.Bytes(visible.Sum(r => r.MemMB) << 20)}";

    public static string Space(IReadOnlyCollection<long> visibleBytes, int total) =>
        Of(visibleBytes.Count, total, "item", "itens") + $" · {Units.Bytes(visibleBytes.Sum())}";

    /// <summary>Fase 5: soma só os tamanhos conhecidos e diz quantos ficaram sem tamanho.</summary>
    public static string Programs(IReadOnlyCollection<long?> visibleBytes, int total)
    {
        int unknown = visibleBytes.Count(b => b is null);
        return Of(visibleBytes.Count, total, "item", "itens") + $" · {Units.Bytes(visibleBytes.Sum(b => b ?? 0))}"
               + (unknown > 0 ? $" ({unknown:N0} sem tamanho)" : "");
    }

    public static string Startup(int enabledVisible, int visible, int total) =>
        Of(visible, total, "programa", "programas") + $" · {enabledVisible:N0} {(enabledVisible == 1 ? "ativado" : "ativados")}";
}
