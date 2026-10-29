namespace NotebookDoctor.Services;

/// <summary>
/// Um achado do diagnóstico. Quando há correção, <see cref="FixId"/> diz qual:
/// "kill" (encerrar programa), "clean" (limpar temporários), "power-high" (ativar Alto desempenho) ou "tab:N" (abrir a aba N).
///
/// Fase 1 (transparência): <see cref="Evidence"/>, <see cref="ProposedChange"/>, <see cref="Risk"/> e <see cref="Undo"/>
/// mostram de forma explícita o que foi observado, o que muda, o impacto esperado e como voltar atrás.
/// </summary>
public sealed record Finding(string Level, string Title, string Detail, string Advice,
                             string? FixId = null, string? FixLabel = null, string? FixArg = null,
                             string? Evidence = null, string? ProposedChange = null, string? Risk = null, string? Undo = null,
                             string? Confidence = null)
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
        var t when t.StartsWith("Windows Update desativado") || t.StartsWith("Atualização do Windows") || t.StartsWith("Windows sem atualizar") =>
            "Atualizações trazem correções de segurança e de desempenho. Uma atualização que falha costuma tentar de novo em segundo plano, gastando disco e rede sem terminar.",
        var t when t.StartsWith("Vídeo sem o driver") || t.StartsWith("Driver de vídeo") =>
            "Sem o driver certo, quem desenha a tela é o processador, não a placa de vídeo. Janelas, vídeos e o navegador ficam mais lentos e a bateria dura menos.",
        var t when t.StartsWith("Otimização automática") || t.StartsWith("Manutenção automática") =>
            "Com o PC parado, o Windows faz TRIM no SSD, atualiza componentes e verifica o sistema. Desligado, o SSD vai ficando mais lento ao gravar e as pendências se acumulam.",
        var t when t.StartsWith("OneDrive") =>
            "Arquivos que o OneDrive mantém baixados ocupam o disco do mesmo jeito que na nuvem. Com o disco cheio, o SSD fica mais lento e o Windows não consegue se atualizar.",
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

    private string FallbackRisk => Level == "PROBLEMA" ? "Alto" : Level == "ATENCAO" ? "Médio" : "Baixo";
    private string FallbackConfidence => Level == "PROBLEMA" ? "Alta" : Level == "ATENCAO" ? "Média" : "Alta";
    private string FallbackUndo => HasFix ? "Você pode desfazer pela seção Ações quando houver desfazer disponível para essa mudança." : "Este aviso é informativo e não aplica mudança automática.";
    private string FallbackProposedChange => Advice;

    public bool HasWhy => Why.Length > 0;

    // U1: o que a tela mostra, no idioma em uso. Title, Advice e FixLabel ficam em português porque a explicação
    // (Why) e os relatórios os usam como chave; títulos com nomes chegam prontos em TitleShown.
    public string? TitleShown { get; init; }
    public string TitleText => TitleShown ?? Loc.T(Title);
    public string AdviceText => Loc.T(Advice);
    public string? FixLabelText => FixLabel is null ? null : Loc.T(FixLabel);
    public string WhyText => Loc.T(Why);
    public string TagText => Tag == "OK" ? Tag : Loc.T(Tag); // "OK" é igual em todo idioma (a tradução de "OK" é a do botão)
    public string WhyLabel => Loc.T("Por que isso importa?");
    public string EvidenceText => Evidence is { Length: > 0 } ? Loc.T(Evidence) : Detail;
    public string ProposedChangeText => Loc.T(ProposedChange ?? FallbackProposedChange);
    public string RiskText => Loc.T(Risk ?? FallbackRisk);
    public string ConfidenceText => Loc.T(Confidence ?? FallbackConfidence);
    public string UndoText => Loc.T(Undo ?? FallbackUndo);
    public bool HasTransparency => HasFix || Level != "OK";

    /// <summary>A explicação está aberta na tela (sobrevive à atualização da lista a cada 3 s).</summary>
    public bool WhyOpen { get; set; }
}

public static class Diagnosis
{
    public const int TabProcs = AppTabs.Processes, TabClean = AppTabs.Clean, TabSpace = AppTabs.Space, TabStartup = AppTabs.Startup, TabSettings = AppTabs.Settings;

    /// <param name="uptime">Tempo ligado; nulo usa o do Windows (os testes passam um valor fixo).</param>
    /// <param name="battery">Leva 11.1: leituras recentes da bateria (a mais nova por último); vazio ou nulo sem bateria.</param>
    public static List<Finding> Evaluate(IReadOnlyList<Snapshot> recent, HealthInfo h, IReadOnlyList<LeakSuspect>? leaks = null, TimeSpan? uptime = null,
                                         IReadOnlyList<BatteryReading>? battery = null, BatteryHistory? batteryLog = null, string manufacturer = "")
    {
        var f = new List<Finding>();
        if (recent.Count == 0) return f;
        var now = recent[^1];
        double cpuAvg = recent.TakeLast(12).Average(s => s.Cpu);

        if (now.RamPct >= 85) f.Add(new("PROBLEMA", "Memória RAM quase cheia", Loc.F("{0:0}% em uso ({1})", now.RamPct, Units.GbOf(now.RamUsedGB, now.RamTotalGB)), "Feche abas e programas pesados (veja a lista abaixo).", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "Uso de RAM acima de 85%.",
            ProposedChange = "Abrir Processos para fechar ou reduzir os programas mais pesados.",
            Risk = "Alto",
            Undo = "Se fechar algo por engano, abra o programa novamente."
        });
        else if (now.RamPct >= 75) f.Add(new("ATENCAO", "Memória RAM alta", Loc.F("{0:0}% em uso", now.RamPct), "Reduza abas do navegador e apps em segundo plano.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "Uso de RAM acima de 75%.",
            ProposedChange = "Abrir Processos para reduzir consumo em segundo plano.",
            Risk = "Médio",
            Undo = "Se fechar algo por engano, abra o programa novamente."
        });

        if (cpuAvg >= 70) f.Add(new("PROBLEMA", "CPU sobrecarregada", Loc.F("{0:0}% em média no último minuto", cpuAvg), "Veja quais programas mais consomem CPU.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "CPU média acima de 70% no último minuto.",
            ProposedChange = "Abrir Processos para reduzir o uso de CPU dos programas com maior consumo.",
            Risk = "Alto",
            Undo = "Se fechar algo por engano, abra o programa novamente."
        });
        else if (cpuAvg >= 40) f.Add(new("ATENCAO", "CPU com carga alta", Loc.F("{0:0}% em média no último minuto", cpuAvg), "Algo está consumindo CPU em segundo plano.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "CPU média acima de 40% no último minuto.",
            ProposedChange = "Abrir Processos para verificar e reduzir consumo em segundo plano.",
            Risk = "Médio",
            Undo = "Se fechar algo por engano, abra o programa novamente."
        });

        // A08: sob carga, a CPU deveria rodar perto da frequência máxima. Bem abaixo disso = limite térmico ou de energia
        var loaded = recent.TakeLast(12).Where(s => s.Cpu >= 60 && s.CpuPerf >= 0).ToList();
        // Leva 11.1, tarefa 04: quente e lento ao mesmo tempo é estrangulamento térmico; diz quanto foi perdido e oferece o Modo Colo
        int? thermalLoss = loaded.Count >= 4 ? BatteryRules.ThermalLossPct(loaded.Average(s => s.TempC), loaded.Average(s => s.CpuPerf), loaded.Average(s => s.Cpu)) : null;
        if (thermalLoss is { } loss)
            f.Add(new("PROBLEMA", "O calor está tirando velocidade do processador",
                      Loc.F("{0}% mais lento que o normal, a {1}", loss, Units.Temp(loaded.Average(s => s.TempC))),
                      "O processador se freia para não passar do limite de temperatura. O Modo Colo desliga o turbo: um pouco menos de pico, mas bem menos calor e barulho, e a velocidade fica estável. Use também em superfície firme e com as saídas de ar livres.",
                      "lap-mode", "Ligar Modo Colo")
            {
                TitleShown = Loc.F("O calor está tirando {0}% da velocidade do processador", loss),
                Evidence = "Queda de desempenho sob carga com temperatura alta.",
                ProposedChange = "Ativar Modo Colo para reduzir calor e estabilizar o desempenho.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Você pode desligar o Modo Colo a qualquer momento."
            });
        else if (loaded.Count >= 4 && loaded.Average(s => s.CpuPerf) < 65)
            f.Add(new("ATENCAO", "O processador está reduzindo a velocidade sob carga",
                      Loc.F("Rodando a {0:0}% da frequência máxima com a CPU em {1:0}%", loaded.Average(s => s.CpuPerf), loaded.Average(s => s.Cpu)),
                      "Costuma ser calor (limpe as saídas de ar, use em superfície firme) ou limite de energia (use a tomada e o plano Alto desempenho).",
                      h.HighPlanGuid is not null && h.IsBalancedPlan ? "power-high" : $"tab:{TabSettings}",
                      h.HighPlanGuid is not null && h.IsBalancedPlan ? "Ativar Alto desempenho" : "Ver energia", h.HighPlanGuid)
            {
                Evidence = "Frequência efetiva abaixo do esperado durante carga alta.",
                ProposedChange = "Ativar Alto desempenho na tomada ou revisar ventilação e energia.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Se trocar o plano de energia, a mudança pode ser revertida na seção Ações ou em Configurações."
            });

        if (now.TempC >= 90) f.Add(new("PROBLEMA", "Temperatura muito alta", Units.Temp(now.TempC), "Desligue pesados, limpe as saídas de ar e use o notebook em superfície firme.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "Temperatura atual acima de 90°C.",
            ProposedChange = "Abrir Processos para reduzir carga e baixar a temperatura.",
            Risk = "Alto",
            Confidence = "Alta",
            Undo = "Se encerrar algo por engano, abra o programa novamente."
        });
        else if (now.TempC >= 80) f.Add(new("ATENCAO", "Temperatura alta", Units.Temp(now.TempC), "Verifique a ventilação; calor faz o processador reduzir a velocidade.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "Temperatura atual acima de 80°C.",
            ProposedChange = "Abrir Processos para aliviar carga e revisar ventilação.",
            Risk = "Médio",
            Confidence = "Alta",
            Undo = "Se encerrar algo por engano, abra o programa novamente."
        });

        // Leva 11.1: carregador fraco e bateria no fim
        if (battery is { Count: > 0 })
        {
            if (BatteryRules.SlowCharger(battery) is { } charger) f.Add(charger);
            // Leva 11.3, tarefa 12: sempre na tomada a 100% gasta a bateria; o limite de carga fica no app do fabricante
            if (batteryLog is not null && BatteryRules.AlwaysPluggedFull(batteryLog.Usage))
                f.Add(new("ATENCAO", "Sempre na tomada com a bateria em 100%",
                          Loc.T("Nos últimos 3 dias de uso, a bateria passou a maior parte do tempo cheia, na tomada"),
                          BatteryRules.ChargeLimitAdvice(manufacturer) + " " + Loc.T("Parar em cerca de 80% quando o notebook vive na tomada faz a bateria durar anos a mais."),
                          "drivers-oem", "Ver o app do fabricante"));
            if (BatteryRules.OfferSurvival(battery[^1]))
                f.Add(new("PROBLEMA", "Bateria quase no fim", Loc.F("{0}% e descarregando", battery[^1].Percent),
                          "O Modo sobrevivência liga o economizador de bateria, deixa os programas sem janela no modo eficiência, baixa a tela para 60 Hz e passa para o tema escuro. Tudo volta ao ligar na tomada.",
                          "survival", "Modo sobrevivência"));
        }

        // M41: a máquina virtual do WSL/Docker reserva RAM e quase nunca devolve sozinha
        long vmMB = now.Groups.Where(g => g.Name.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase)).Sum(g => g.MemMB);
        if (vmMB >= 2048)
            f.Add(new("ATENCAO", "WSL/Docker ocupando muita memória", Loc.F("{0} reservados pela máquina virtual (vmmem)", Units.Gb(vmMB / 1024.0)),
                      "Limite a memória em %UserProfile%\\.wslconfig (ex.: [wsl2] memory=4GB) ou encerre o WSL quando não estiver usando.",
                      "wsl-shutdown", "Encerrar WSL")
            {
                Evidence = "Processo vmmem reservando mais de 2 GB de memória.",
                ProposedChange = "Encerrar o WSL para devolver memória ao Windows.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Você pode abrir o WSL ou Docker novamente quando precisar."
            });

        // M40: o .vhdx do WSL/Docker cresce conforme o uso, mas não devolve o espaço quando arquivos são apagados dentro dele
        foreach (var vd in (h.VirtualDisks ?? Array.Empty<VirtualDisk>()).Where(v => v.Bytes >= 20L << 30).OrderByDescending(v => v.Bytes).Take(2))
            f.Add(new("ATENCAO", $"Disco virtual do {vd.Name} ocupa {Pt.Bytes(vd.Bytes)}", vd.Path,
                      "Com o WSL desligado (wsl --shutdown), compacte: no WSL 2.0+ use \"wsl --manage <distro> --set-sparse true\"; no Docker Desktop, \"Clean / Purge data\" em Troubleshoot.",
                      "open-folder", "Abrir local", vd.Path) { TitleShown = Loc.F("Disco virtual do {0} ocupa {1}", vd.Name, Pt.Bytes(vd.Bytes)) });

        // M33: OneDrive com tudo baixado
        if (OneDriveRule.Evaluate(h.OneDrive, now.DiskFreePct) is { } od) f.Add(od);

        // A22, A23 e A39: atualizações travadas, driver de vídeo e manutenção do Windows desligada
        f.AddRange(UpdateRules.Evaluate(h.Updates, now.Time));
        f.AddRange(DriverRules.Evaluate(h.Gpus, now.Time));
        f.AddRange(MaintenanceRules.Evaluate(h.Maintenance, h.DiskType));

        var sec = h.Sec;
        if (sec.DefenderRealtime == false && sec.OtherAv.Count == 0)
            f.Add(new("PROBLEMA", "Proteção em tempo real do Windows desativada", Loc.T("Nenhum outro antivírus foi encontrado"), "Reative a proteção na Segurança do Windows.", "open-security", "Abrir Segurança")
            {
                Evidence = "Proteção em tempo real desligada e nenhum antivírus alternativo detectado.",
                ProposedChange = "Abrir Segurança do Windows para reativar a proteção em tempo real.",
                Risk = "Alto",
                Confidence = "Alta",
                Undo = "Após reativar, você pode ajustar as configurações de proteção na Segurança do Windows."
            });
        else if (sec.SignatureAgeDays >= 7 && sec.OtherAv.Count == 0)
            f.Add(new("ATENCAO", "Definições de vírus desatualizadas", Loc.F("Última atualização há {0} dias", sec.SignatureAgeDays), "Atualize pela Segurança do Windows (Windows Update).", "open-security", "Abrir Segurança")
            {
                Evidence = "Definições de vírus sem atualização recente.",
                ProposedChange = "Abrir Segurança do Windows para atualizar assinaturas.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Atualizações de assinatura não exigem desfazer."
            });
        // M32: dois antivírus em tempo real examinam cada arquivo duas vezes e podem travar um ao outro
        var activeAv = sec.OtherAvActive ?? Array.Empty<string>();
        var avNames = activeAv.Concat(sec.DefenderRealtime == true ? ["Microsoft Defender"] : Array.Empty<string>()).ToList();
        if (avNames.Count >= 2)
            f.Add(new("ATENCAO", "Mais de um antivírus ativo", string.Join(", ", avNames),
                      "Mantenha só um antivírus com proteção em tempo real. Desinstale o que não usa (o Windows reativa o Defender sozinho se ficar sem nenhum).",
                      "open-apps", "Abrir Aplicativos")
            {
                Evidence = "Mais de um antivírus ativo detectado ao mesmo tempo.",
                ProposedChange = "Abrir Aplicativos para remover o antivírus que você não usa.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Se remover por engano, reinstale o antivírus desejado."
            });

        if (sec.FirewallOff.Count > 0)
            f.Add(new("ATENCAO", "Firewall desativado", Loc.F("Perfis sem proteção: {0}", string.Join(", ", sec.FirewallOff)), "Reative o Firewall do Windows, a menos que outro firewall esteja em uso.", "open-security", "Abrir Segurança")
            {
                Evidence = "Um ou mais perfis de firewall estão desativados.",
                ProposedChange = "Abrir Segurança do Windows para reativar o firewall.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "Você pode ajustar os perfis de firewall na Segurança do Windows."
            });

        foreach (var l in (leaks ?? Array.Empty<LeakSuspect>()).Take(2))
            f.Add(new("ATENCAO", $"Possível vazamento de memória: {l.Name}", Loc.F("Cresceu de {0} MB para {1} MB em {2} min, sem devolver memória", l.FromMB, l.ToMB, l.Minutes),
                      "Reiniciar o programa devolve a memória. Salve seu trabalho antes.", ProcessTools.IsProtected(l.Name) ? null : "kill", ProcessTools.IsProtected(l.Name) ? null : "Encerrar", l.Name)
                  { TitleShown = Loc.F("Possível vazamento de memória: {0}", l.Name) });

        if (h.DriverErrors.Count > 0)
            f.Add(new("ATENCAO", $"{Pt.N(h.DriverErrors.Count, "dispositivo", "dispositivos")} com driver em erro", string.Join(", ", h.DriverErrors.Take(3)) + (h.DriverErrors.Count > 3 ? "…" : ""),
                      "Atualize ou reinstale o driver pelo Gerenciador de Dispositivos ou pelo site do fabricante.",
                      "open-devices", "Abrir Gerenciador de Dispositivos") { TitleShown = Loc.F("{0} com driver em erro", Pt.N(h.DriverErrors.Count, "dispositivo", "dispositivos")) });

        foreach (var u in h.Unsigned.Take(2))
            f.Add(new("ATENCAO", $"Programa sem assinatura digital: {u.Name}", u.Path,
                      "Está rodando de uma pasta de dados do usuário e não tem assinatura. Pode ser legítimo (programa pequeno ou caseiro); se não reconhece, verifique a origem e rode o antivírus.",
                      "open-folder", "Abrir local", u.Path) { TitleShown = Loc.F("Programa sem assinatura digital: {0}", u.Name) });

        if (h.OnAc && h.IsBalancedPlan && cpuAvg >= 25)
            f.Add(new("ATENCAO", "Plano de energia limita a velocidade", Loc.F("Plano \"{0}\" com o notebook na tomada", h.ActivePlan),
                      "O plano Equilibrado reduz a frequência do processador. Alto desempenho deixa o notebook mais ágil na tomada.",
                      h.HighPlanGuid is not null ? "power-high" : $"tab:{TabSettings}",
                      h.HighPlanGuid is not null ? "Ativar Alto desempenho" : "Criar plano", h.HighPlanGuid));

        // A02: na bateria, o Alto desempenho só gasta carga e esquenta
        if (!h.OnAc && h.HasBattery && Profile.Matches("high", h.ActivePlan))
            f.Add(new("ATENCAO", "Plano Alto desempenho na bateria", Loc.F("Plano \"{0}\" sem a tomada", h.ActivePlan),
                      "Na bateria, o Equilibrado dura bem mais e esquenta menos. Volte ao Alto desempenho quando ligar na tomada.",
                      $"tab:{TabSettings}", "Ver energia"));

        if (now.DiskBusy >= 80) f.Add(new("PROBLEMA", "Disco saturado", Loc.F("{0:0}% ocupado", now.DiskBusy), "Pode ser antivírus, indexação ou atualização em andamento. Veja quem mais movimenta dados.", $"tab:{TabProcs}", "Ver programas")
        {
            Evidence = "Atividade de disco acima de 80%.",
            ProposedChange = "Abrir Processos para identificar quem está usando mais disco agora.",
            Risk = "Alto",
            Undo = "Este passo só abre análise; não muda configurações por si só."
        });
        if (now.DiskFreePct < 10) f.Add(new("PROBLEMA", "Disco quase cheio", Loc.F("{0:0}% livre ({1})", now.DiskFreePct, Units.Gb(now.DiskFreeGB, "0")), "Libere espaço.", $"tab:{TabClean}", "Abrir Limpeza")
        {
            Evidence = "Espaço livre abaixo de 10%.",
            ProposedChange = "Abrir Limpeza para remover arquivos temporários e liberar espaço.",
            Risk = "Alto",
            Undo = "Ao apagar, os itens removidos podem ser restaurados pela seção Ações quando houver desfazer."
        });
        else if (now.DiskFreePct < 20) f.Add(new("ATENCAO", "Pouco espaço em disco", Loc.F("{0:0}% livre", now.DiskFreePct), "Mantenha pelo menos 20% livre em SSD.", $"tab:{TabClean}", "Abrir Limpeza")
        {
            Evidence = "Espaço livre abaixo de 20%.",
            ProposedChange = "Abrir Limpeza para prevenção de lentidão por falta de espaço.",
            Risk = "Médio",
            Undo = "Ao apagar, os itens removidos podem ser restaurados pela seção Ações quando houver desfazer."
        });

        if (h.HasDisk && !h.DiskHealthy)
            f.Add(new("PROBLEMA", "O disco informa problema de saúde", $"{h.DiskName}: {Loc.T(h.DiskHealth)}", "Faça backup dos seus arquivos agora e consulte o fabricante do disco.", "open-backup", "Abrir backup")
            {
                Evidence = "Indicador de saúde do disco reportado como problemático pelo sistema.",
                ProposedChange = "Abrir backup e proteger os arquivos imediatamente.",
                Risk = "Alto",
                Confidence = "Alta",
                Undo = "Não há desfazer para falha física de disco; a ação recomendada é prevenção com backup."
            });
        if (h.DiskWearPct >= 80)
            f.Add(new("ATENCAO", "SSD com desgaste alto", Loc.F("{0}% da vida útil consumida", h.DiskWearPct), "Planeje a troca do SSD e mantenha backups em dia.", "open-backup", "Abrir backup")
            {
                Evidence = "Desgaste do SSD em nível elevado.",
                ProposedChange = "Abrir backup e planejar substituição do SSD.",
                Risk = "Médio",
                Confidence = "Alta",
                Undo = "A recomendação é preventiva; sem alteração automática no sistema."
            });
        if (h.DiskTempC >= 70)
            f.Add(new("ATENCAO", "Disco quente", Units.Temp(h.DiskTempC.Value), "Verifique a ventilação do notebook.")
            {
                Evidence = "Temperatura do disco acima de 70°C.",
                ProposedChange = "Melhorar ventilação e reduzir carga até a temperatura normalizar.",
                Risk = "Médio",
                Confidence = "Média",
                Undo = "Este aviso é informativo e não aplica mudança automática."
            });

        if (h.BatteryHealthPct is < 60)
            f.Add(new("ATENCAO", "Bateria muito desgastada", Loc.F("{0}% da capacidade original", h.BatteryHealthPct) + (h.BatteryCycles is { } c ? " · " + Pt.N(c, "ciclo", "ciclos") : ""), "Sem a tomada, o desempenho tende a cair. Considere trocar a bateria.", "battery-report", "Relatório da bateria"));
        else if (h.BatteryHealthPct is < 75)
            f.Add(new("ATENCAO", "Bateria com desgaste", Loc.F("{0}% da capacidade original", h.BatteryHealthPct), "A autonomia está reduzida; ainda não é urgente.", "battery-report", "Relatório da bateria"));

        if (h.RebootPending)
            f.Add(new("ATENCAO", "Reinício pendente", Loc.T("O Windows aguarda reinício para concluir atualizações"), "Salve seu trabalho e reinicie o computador.", "restart", "Reiniciar…")
            {
                Evidence = "O Windows informou atualização pendente de conclusão por reinício.",
                ProposedChange = "Reiniciar o computador para concluir as atualizações pendentes.",
                Risk = "Médio",
                Undo = "Após reiniciar não há desfazer, mas a ação só conclui mudanças já pendentes do Windows."
            });
        if (h.BootSeconds is > 90)
            f.Add(new("ATENCAO", "Inicialização lenta", Loc.F("Último boot levou {0} s", h.BootSeconds) + (h.BootAvgSeconds is { } a ? " " + Loc.F("(média {0} s)", a) : ""), "Desative programas que iniciam com o Windows.", $"tab:{TabStartup}", "Abrir Inicialização"));

        foreach (var g in now.Heavy.Where(g => g.CpuPct >= 10 || g.MemMB >= 1500).Take(4))
        {
            bool canKill = !ProcessTools.IsProtected(g.Name);
            f.Add(new("ATENCAO", $"Programa pesado: {g.Name}", Loc.F("{0:0}% de CPU e {1:N0} MB de RAM em {2}", g.CpuPct, g.MemMB, Pt.N(g.Count, "processo", "processos")), "Feche ou reinicie esse programa se não estiver usando.",
                      canKill ? "kill" : null, canKill ? "Encerrar" : null, g.Name) { TitleShown = Loc.F("Programa pesado: {0}", g.Name) });
        }

        if (h.StartupEnabled > 10) f.Add(new("ATENCAO", "Muitos programas iniciam com o Windows", Pt.N(h.StartupEnabled, "ativo", "ativos"), "Desative os que não precisa.", $"tab:{TabStartup}", "Abrir Inicialização")
        {
            Evidence = "Mais de 10 programas ativos na inicialização.",
            ProposedChange = "Abrir Inicialização para desativar os itens que não são necessários no início do Windows.",
            Risk = "Médio",
            Undo = "Mudanças de inicialização podem ser revertidas na seção Ações."
        });
        if (h.TempBytes > 1L << 30) f.Add(new("ATENCAO", "Muito lixo em temporários", Pt.Bytes(h.TempBytes), "Limpeza segura de arquivos temporários antigos.", "clean", "Limpar agora")
        {
            Evidence = "Volume de temporários acima de 1 GB.",
            ProposedChange = "Executar limpeza segura de temporários antigos.",
            Risk = "Baixo",
            Undo = "Quando houver desfazer disponível, use a seção Ações para restaurar."
        });

        double uptimeDays = (uptime ?? Uptime.Current).TotalDays;
        if (uptimeDays >= 7) f.Add(new("ATENCAO", "Computador ligado há muito tempo", Loc.F("{0:0.0} dias sem reiniciar", uptimeDays), "Reinicie o Windows.", "restart", "Reiniciar…")
        {
            Evidence = "Tempo de atividade sem reinício acima de 7 dias.",
            ProposedChange = "Reiniciar o Windows para renovar memória e concluir pendências.",
            Risk = "Baixo",
            Confidence = "Alta",
            Undo = "Após reiniciar não há desfazer, mas você pode reabrir seus programas normalmente."
        });

        if (f.Count == 0) f.Add(new("OK", "Tudo certo", Friendly.Pick(Friendly.AllGood, DateTime.Today), "")
        {
            Evidence = "Sem sinais críticos ou alertas relevantes no momento.",
            ProposedChange = "Manter monitoramento ativo e usar o app normalmente.",
            Risk = "Baixo",
            Confidence = "Alta",
            Undo = "Nenhuma ação aplicada."
        }); // B27
        return f.OrderBy(x => x.Level == "PROBLEMA" ? 0 : x.Level == "ATENCAO" ? 1 : 2).ToList();
    }
}
