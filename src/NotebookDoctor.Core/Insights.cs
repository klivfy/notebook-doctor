namespace NotebookDoctor.Services;

public sealed record HealthScoreResult(int Score, string Label);
public sealed record BottleneckResult(string Resource, string Explanation);

/// <summary>Nota de saúde (0–100) e identificação do gargalo atual, em linguagem simples.</summary>
public static class Insights
{
    public static HealthScoreResult Score(IReadOnlyList<Snapshot> recent, HealthInfo h, TimeSpan? uptime = null)
    {
        if (recent.Count == 0) return new(0, "Analisando");
        var now = recent[^1];
        double cpu = recent.TakeLast(12).Average(s => s.Cpu);
        int score = 100;

        score -= now.RamPct >= 90 ? 20 : now.RamPct >= 80 ? 12 : now.RamPct >= 70 ? 5 : 0;
        score -= cpu >= 80 ? 20 : cpu >= 50 ? 10 : cpu >= 30 ? 4 : 0;
        score -= now.DiskFreePct < 10 ? 20 : now.DiskFreePct < 20 ? 10 : 0;
        score -= now.DiskBusy >= 80 ? 10 : 0;
        if (h.HasDisk && !h.DiskHealthy) score -= 30;
        if (h.DiskWearPct >= 80) score -= 15;
        if (h.BatteryHealthPct < 60) score -= 8; else if (h.BatteryHealthPct < 75) score -= 4;
        if ((uptime ?? Uptime.Current).TotalDays >= 7) score -= 5;
        if (h.RebootPending) score -= 5;
        if (h.StartupEnabled > 10) score -= 5;
        score -= h.TempBytes > 2L << 30 ? 5 : h.TempBytes > 1L << 30 ? 2 : 0;
        if (h.OnAc && h.IsBalancedPlan && cpu >= 25) score -= 3;

        score = Math.Clamp(score, 0, 100);
        return new(score, score >= 90 ? "Excelente" : score >= 75 ? "Bom" : score >= 55 ? "Regular" : "Ruim");
    }

    public static BottleneckResult Bottleneck(IReadOnlyList<Snapshot> recent)
    {
        if (recent.Count == 0) return new("–", "Coletando dados…");
        var now = recent[^1];
        var last = recent.TakeLast(12).ToList();
        double cpu = last.Average(s => s.Cpu), disk = last.Average(s => s.DiskBusy);

        if (cpu >= 70)
        {
            var top = now.TopByCpu.FirstOrDefault();
            return new("Processador", $"A CPU está em {cpu:0}% em média." + (top is { CpuPct: > 5 } ? $" Quem mais usa: {top.Name} ({top.CpuPct:0}%)." : "") + " Feche o que não estiver usando.");
        }
        if (now.RamPct >= 85)
        {
            var top = now.TopByMem.FirstOrDefault();
            return new("Memória RAM", $"{now.RamPct:0}% da memória em uso; o Windows começa a usar o disco no lugar da RAM, o que deixa tudo lento." + (top is null ? "" : $" Maior consumo: {top.Name} ({top.MemMB} MB)."));
        }
        if (disk >= 70)
        {
            var io = now.TopIo;
            return new("Disco", $"O disco está {disk:0}% ocupado: programas esperam leitura/gravação." +
                (io is null ? " Pode ser antivírus, indexação ou atualização em andamento." : $" Quem mais movimenta dados agora: {io.Name} ({io.IoKBs / 1024:0.0} MB/s)."));
        }
        if (now.DiskFreePct < 10)
            return new("Espaço em disco", $"Só {now.DiskFreePct:0}% livre. Pouco espaço atrapalha atualizações e arquivo de paginação.");
        if (cpu >= 45) return new("Processador (leve)", $"A CPU está em {cpu:0}%: ainda há folga, mas algo consome em segundo plano.");
        return new("Nenhum", "Nenhum recurso está no limite agora. Se ainda parecer lento, veja o plano de energia e a temperatura.");
    }
}
