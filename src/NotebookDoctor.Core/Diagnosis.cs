namespace NotebookDoctor.Services;

/// <summary>
/// Um achado do diagnóstico. Quando há correção, <see cref="FixId"/> diz qual:
/// "kill" (encerrar programa), "clean" (limpar temporários), "power-high" (ativar Alto desempenho) ou "tab:N" (abrir a aba N).
/// </summary>
public sealed record Finding(string Level, string Title, string Detail, string Advice,
                             string? FixId = null, string? FixLabel = null, string? FixArg = null)
{
    public string Tag => Level == "PROBLEMA" ? "PROBLEMA" : Level == "ATENCAO" ? "ATENÇÃO" : "OK";
    public bool HasFix => FixId is not null;

    /// <summary>M15: em linguagem simples, por que isso deixa o notebook lento ou arriscado.</summary>
    public string Why => Title switch
    {
        var t when t.StartsWith("Memória RAM") =>
            "Quando a RAM acaba, o Windows passa a usar o disco como memória, que é dezenas de vezes mais lento. É aí que tudo começa a travar e as janelas demoram a trocar.",
        var t when t.StartsWith("CPU") =>
            "Com o processador ocupado, cada clique espera na fila. Um único programa preso em segundo plano basta para deixar tudo lento.",
        var t when t.StartsWith("O processador está reduzindo") =>
            "Para não superaquecer ou gastar bateria, o processador baixa a própria velocidade. Ele fica bem mais lento sem que nada pareça errado.",
        var t when t.StartsWith("Temperatura") || t.StartsWith("Disco quente") =>
            "O calor obriga o hardware a desacelerar e, com o tempo, encurta a vida da bateria, do SSD e da placa. Saídas de ar bloqueadas são a causa mais comum.",
        var t when t.StartsWith("Disco virtual do") =>
            "O disco virtual do Linux cresce quando você baixa ou cria arquivos dentro dele, mas não encolhe sozinho quando eles são apagados. O espaço continua ocupado no disco do Windows.",
        var t when t.StartsWith("WSL") =>
            "A máquina virtual do WSL/Docker reserva memória para si e raramente a devolve, mesmo parada. Essa RAM fica indisponível para o resto do Windows.",
        var t when t.StartsWith("Mais de um antivírus") =>
            "Cada antivírus examina todo arquivo aberto. Com dois ao mesmo tempo, o trabalho dobra e eles podem bloquear um ao outro, deixando abrir programas e copiar arquivos bem mais lento.",
        var t when t.Contains("Proteção") || t.Contains("vírus") || t.Contains("Firewall") =>
            "Sem proteção atualizada, um programa malicioso pode rodar escondido, gastar recursos e expor seus dados.",
        var t when t.StartsWith("Possível vazamento") =>
            "Um programa que só acumula memória acaba empurrando os outros para o disco. Reiniciar só ele devolve tudo de uma vez.",
        var t when t.Contains("driver") =>
            "Um driver com erro pode deixar o dispositivo lento, instável ou fazendo o sistema tentar de novo sem parar em segundo plano.",
        var t when t.StartsWith("Programa sem assinatura") =>
            "Programas legítimos costumam ser assinados pelo fabricante. Um executável sem assinatura, rodando de uma pasta pessoal, merece conferência.",
        var t when t.StartsWith("Plano Alto desempenho na bateria") =>
            "O Alto desempenho mantém o processador acelerado o tempo todo. Na bateria, isso esvazia a carga mais rápido e esquenta o notebook sem necessidade.",
        var t when t.StartsWith("Plano de energia") =>
            "No plano Equilibrado, o processador fica em frequência baixa a maior parte do tempo. Na tomada, isso só custa agilidade.",
        var t when t.StartsWith("Disco saturado") =>
            "Com o disco 100% ocupado, abrir qualquer arquivo ou programa espera na fila. É a sensação de \"tudo congelado\".",
        var t when t.Contains("espaço") || t.StartsWith("Disco quase cheio") =>
            "SSDs ficam mais lentos quando quase cheios, e o Windows precisa de espaço livre para atualizações e memória virtual.",
        var t when t.Contains("saúde") || t.StartsWith("SSD") =>
            "Um disco que falha pode levar seus arquivos junto. O aviso vem do próprio disco, então vale fazer backup já.",
        var t when t.StartsWith("Bateria") =>
            "Bateria gasta entrega menos energia, e o Windows reduz o desempenho para não desligar de repente.",
        var t when t.StartsWith("Reinício pendente") || t.StartsWith("Computador ligado") =>
            "Atualizações incompletas e memória acumulada só são resolvidas com um reinício de verdade.",
        var t when t.StartsWith("Inicialização lenta") || t.StartsWith("Muitos programas") =>
            "Cada programa que abre com o Windows disputa disco e CPU logo no começo e continua ocupando memória o dia todo.",
        var t when t.StartsWith("Programa pesado") =>
            "Esse programa sozinho está tomando uma fatia grande do computador. Se não estiver em uso, fechar devolve essa capacidade na hora.",
        var t when t.StartsWith("Muito lixo") =>
            "Temporários antigos não servem mais a ninguém: só ocupam espaço e deixam antivírus e buscas mais lentos.",
        _ => "",
    };

    public bool HasWhy => Why.Length > 0;
    /// <summary>A explicação está aberta na tela (sobrevive à atualização da lista a cada 3 s).</summary>
    public bool WhyOpen { get; set; }
}

public static class Diagnosis
{
    public const int TabProcs = 1, TabClean = 2, TabSpace = 3, TabStartup = 4, TabSettings = 8;

    /// <param name="uptime">Tempo ligado; nulo usa o do Windows (os testes passam um valor fixo).</param>
    public static List<Finding> Evaluate(IReadOnlyList<Snapshot> recent, HealthInfo h, IReadOnlyList<LeakSuspect>? leaks = null, TimeSpan? uptime = null)
    {
        var f = new List<Finding>();
        if (recent.Count == 0) return f;
        var now = recent[^1];
        double cpuAvg = recent.TakeLast(12).Average(s => s.Cpu);

        if (now.RamPct >= 85) f.Add(new("PROBLEMA", "Memória RAM quase cheia", $"{now.RamPct:0}% em uso ({now.RamUsedGB:0.0} de {now.RamTotalGB:0.0} GB)", "Feche abas e programas pesados (veja a lista abaixo).", $"tab:{TabProcs}", "Ver programas"));
        else if (now.RamPct >= 75) f.Add(new("ATENCAO", "Memória RAM alta", $"{now.RamPct:0}% em uso", "Reduza abas do navegador e apps em segundo plano.", $"tab:{TabProcs}", "Ver programas"));

        if (cpuAvg >= 70) f.Add(new("PROBLEMA", "CPU sobrecarregada", $"{cpuAvg:0}% em média no último minuto", "Veja quais programas mais consomem CPU.", $"tab:{TabProcs}", "Ver programas"));
        else if (cpuAvg >= 40) f.Add(new("ATENCAO", "CPU com carga alta", $"{cpuAvg:0}% em média no último minuto", "Algo está consumindo CPU em segundo plano.", $"tab:{TabProcs}", "Ver programas"));

        // A08: sob carga, a CPU deveria rodar perto da frequência máxima. Bem abaixo disso = limite térmico ou de energia
        var loaded = recent.TakeLast(12).Where(s => s.Cpu >= 60 && s.CpuPerf >= 0).ToList();
        if (loaded.Count >= 4 && loaded.Average(s => s.CpuPerf) < 65)
            f.Add(new("ATENCAO", "O processador está reduzindo a velocidade sob carga",
                      $"Rodando a {loaded.Average(s => s.CpuPerf):0}% da frequência máxima com a CPU em {loaded.Average(s => s.Cpu):0}%",
                      "Costuma ser calor (limpe as saídas de ar, use em superfície firme) ou limite de energia (use a tomada e o plano Alto desempenho).",
                      h.HighPlanGuid is not null && h.IsBalancedPlan ? "power-high" : $"tab:{TabSettings}",
                      h.HighPlanGuid is not null && h.IsBalancedPlan ? "Ativar Alto desempenho" : "Ver energia", h.HighPlanGuid));

        if (now.TempC >= 90) f.Add(new("PROBLEMA", "Temperatura muito alta", $"{now.TempC:0} °C", "Desligue pesados, limpe as saídas de ar e use o notebook em superfície firme.", $"tab:{TabProcs}", "Ver programas"));
        else if (now.TempC >= 80) f.Add(new("ATENCAO", "Temperatura alta", $"{now.TempC:0} °C", "Verifique a ventilação; calor faz o processador reduzir a velocidade.", $"tab:{TabProcs}", "Ver programas"));

        // M41: a máquina virtual do WSL/Docker reserva RAM e quase nunca devolve sozinha
        long vmMB = now.Groups.Where(g => g.Name.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase)).Sum(g => g.MemMB);
        if (vmMB >= 2048)
            f.Add(new("ATENCAO", "WSL/Docker ocupando muita memória", $"{vmMB / 1024.0:0.0} GB reservados pela máquina virtual (vmmem)",
                      "Limite a memória em %UserProfile%\\.wslconfig (ex.: [wsl2] memory=4GB) ou encerre o WSL quando não estiver usando.",
                      "wsl-shutdown", "Encerrar WSL"));

        // M40: o .vhdx do WSL/Docker cresce conforme o uso, mas não devolve o espaço quando arquivos são apagados dentro dele
        foreach (var vd in (h.VirtualDisks ?? Array.Empty<VirtualDisk>()).Where(v => v.Bytes >= 20L << 30).OrderByDescending(v => v.Bytes).Take(2))
            f.Add(new("ATENCAO", $"Disco virtual do {vd.Name} ocupa {Pt.Bytes(vd.Bytes)}", vd.Path,
                      "Com o WSL desligado (wsl --shutdown), compacte: no WSL 2.0+ use \"wsl --manage <distro> --set-sparse true\"; no Docker Desktop, \"Clean / Purge data\" em Troubleshoot.",
                      "open-folder", "Abrir local", vd.Path));

        var sec = h.Sec;
        if (sec.DefenderRealtime == false && sec.OtherAv.Count == 0)
            f.Add(new("PROBLEMA", "Proteção em tempo real do Windows desativada", "Nenhum outro antivírus foi encontrado", "Reative a proteção na Segurança do Windows.", "open-security", "Abrir Segurança"));
        else if (sec.SignatureAgeDays >= 7 && sec.OtherAv.Count == 0)
            f.Add(new("ATENCAO", "Definições de vírus desatualizadas", $"Última atualização há {sec.SignatureAgeDays} dias", "Atualize pela Segurança do Windows (Windows Update).", "open-security", "Abrir Segurança"));
        // M32: dois antivírus em tempo real examinam cada arquivo duas vezes e podem travar um ao outro
        var activeAv = sec.OtherAvActive ?? Array.Empty<string>();
        var avNames = activeAv.Concat(sec.DefenderRealtime == true ? ["Microsoft Defender"] : Array.Empty<string>()).ToList();
        if (avNames.Count >= 2)
            f.Add(new("ATENCAO", "Mais de um antivírus ativo", string.Join(", ", avNames),
                      "Mantenha só um antivírus com proteção em tempo real. Desinstale o que não usa (o Windows reativa o Defender sozinho se ficar sem nenhum).",
                      "open-apps", "Abrir Aplicativos"));

        if (sec.FirewallOff.Count > 0)
            f.Add(new("ATENCAO", "Firewall desativado", $"Perfis sem proteção: {string.Join(", ", sec.FirewallOff)}", "Reative o Firewall do Windows, a menos que outro firewall esteja em uso.", "open-security", "Abrir Segurança"));

        foreach (var l in (leaks ?? Array.Empty<LeakSuspect>()).Take(2))
            f.Add(new("ATENCAO", $"Possível vazamento de memória: {l.Name}", $"Cresceu de {l.FromMB} MB para {l.ToMB} MB em {l.Minutes} min, sem devolver memória",
                      "Reiniciar o programa devolve a memória. Salve seu trabalho antes.", ProcessTools.IsProtected(l.Name) ? null : "kill", ProcessTools.IsProtected(l.Name) ? null : "Encerrar", l.Name));

        if (h.DriverErrors.Count > 0)
            f.Add(new("ATENCAO", $"{Pt.N(h.DriverErrors.Count, "dispositivo", "dispositivos")} com driver em erro", string.Join(", ", h.DriverErrors.Take(3)) + (h.DriverErrors.Count > 3 ? "…" : ""),
                      "Atualize ou reinstale o driver pelo Gerenciador de Dispositivos ou pelo site do fabricante.",
                      "open-devices", "Abrir Gerenciador de Dispositivos"));

        foreach (var u in h.Unsigned.Take(2))
            f.Add(new("ATENCAO", $"Programa sem assinatura digital: {u.Name}", u.Path,
                      "Está rodando de uma pasta de dados do usuário e não tem assinatura. Pode ser legítimo (programa pequeno ou caseiro); se não reconhece, verifique a origem e rode o antivírus.",
                      "open-folder", "Abrir local", u.Path));

        if (h.OnAc && h.IsBalancedPlan && cpuAvg >= 25)
            f.Add(new("ATENCAO", "Plano de energia limita a velocidade", $"Plano \"{h.ActivePlan}\" com o notebook na tomada",
                      "O plano Equilibrado reduz a frequência do processador. Alto desempenho deixa o notebook mais ágil na tomada.",
                      h.HighPlanGuid is not null ? "power-high" : $"tab:{TabSettings}",
                      h.HighPlanGuid is not null ? "Ativar Alto desempenho" : "Criar plano", h.HighPlanGuid));

        // A02: na bateria, o Alto desempenho só gasta carga e esquenta
        if (!h.OnAc && h.HasBattery && Profile.Matches("high", h.ActivePlan))
            f.Add(new("ATENCAO", "Plano Alto desempenho na bateria", $"Plano \"{h.ActivePlan}\" sem a tomada",
                      "Na bateria, o Equilibrado dura bem mais e esquenta menos. Volte ao Alto desempenho quando ligar na tomada.",
                      $"tab:{TabSettings}", "Ver energia"));

        if (now.DiskBusy >= 80) f.Add(new("PROBLEMA", "Disco saturado", $"{now.DiskBusy:0}% ocupado", "Pode ser antivírus, indexação ou atualização em andamento. Veja quem mais movimenta dados.", $"tab:{TabProcs}", "Ver programas"));
        if (now.DiskFreePct < 10) f.Add(new("PROBLEMA", "Disco quase cheio", $"{now.DiskFreePct:0}% livre ({now.DiskFreeGB:0} GB)", "Libere espaço.", $"tab:{TabClean}", "Abrir Limpeza"));
        else if (now.DiskFreePct < 20) f.Add(new("ATENCAO", "Pouco espaço em disco", $"{now.DiskFreePct:0}% livre", "Mantenha pelo menos 20% livre em SSD.", $"tab:{TabClean}", "Abrir Limpeza"));

        if (h.HasDisk && !h.DiskHealthy)
            f.Add(new("PROBLEMA", "O disco informa problema de saúde", $"{h.DiskName}: {h.DiskHealth}", "Faça backup dos seus arquivos agora e consulte o fabricante do disco.", "open-backup", "Abrir backup"));
        if (h.DiskWearPct >= 80)
            f.Add(new("ATENCAO", "SSD com desgaste alto", $"{h.DiskWearPct}% da vida útil consumida", "Planeje a troca do SSD e mantenha backups em dia.", "open-backup", "Abrir backup"));
        if (h.DiskTempC >= 70)
            f.Add(new("ATENCAO", "Disco quente", $"{h.DiskTempC} °C", "Verifique a ventilação do notebook."));

        if (h.BatteryHealthPct is < 60)
            f.Add(new("ATENCAO", "Bateria muito desgastada", $"{h.BatteryHealthPct}% da capacidade original" + (h.BatteryCycles is { } c ? $" · {c:N0} ciclos" : ""), "Sem a tomada, o desempenho tende a cair. Considere trocar a bateria.", "battery-report", "Relatório da bateria"));
        else if (h.BatteryHealthPct is < 75)
            f.Add(new("ATENCAO", "Bateria com desgaste", $"{h.BatteryHealthPct}% da capacidade original", "A autonomia está reduzida; ainda não é urgente.", "battery-report", "Relatório da bateria"));

        if (h.RebootPending)
            f.Add(new("ATENCAO", "Reinício pendente", "O Windows aguarda reinício para concluir atualizações", "Salve seu trabalho e reinicie o computador.", "restart", "Reiniciar…"));
        if (h.BootSeconds is > 90)
            f.Add(new("ATENCAO", "Inicialização lenta", $"Último boot levou {h.BootSeconds} s" + (h.BootAvgSeconds is { } a ? $" (média {a} s)" : ""), "Desative programas que iniciam com o Windows.", $"tab:{TabStartup}", "Abrir Inicialização"));

        foreach (var g in now.Heavy.Where(g => g.CpuPct >= 10 || g.MemMB >= 1500).Take(4))
        {
            bool canKill = !ProcessTools.IsProtected(g.Name);
            f.Add(new("ATENCAO", $"Programa pesado: {g.Name}", $"{g.CpuPct:0}% de CPU e {g.MemMB:N0} MB de RAM em {Pt.N(g.Count, "processo", "processos")}", "Feche ou reinicie esse programa se não estiver usando.",
                      canKill ? "kill" : null, canKill ? "Encerrar" : null, g.Name));
        }

        if (h.StartupEnabled > 10) f.Add(new("ATENCAO", "Muitos programas iniciam com o Windows", $"{h.StartupEnabled} ativos", "Desative os que não precisa.", $"tab:{TabStartup}", "Abrir Inicialização"));
        if (h.TempBytes > 1L << 30) f.Add(new("ATENCAO", "Muito lixo em temporários", Pt.Bytes(h.TempBytes), "Limpeza segura de arquivos temporários antigos.", "clean", "Limpar agora"));

        double uptimeDays = (uptime ?? Uptime.Current).TotalDays;
        if (uptimeDays >= 7) f.Add(new("ATENCAO", "Computador ligado há muito tempo", $"{uptimeDays:0.0} dias sem reiniciar", "Reinicie o Windows.", "restart", "Reiniciar…"));

        if (f.Count == 0) f.Add(new("OK", "Tudo certo", "Nenhum problema relevante agora.", ""));
        return f.OrderBy(x => x.Level == "PROBLEMA" ? 0 : x.Level == "ATENCAO" ? 1 : 2).ToList();
    }
}
