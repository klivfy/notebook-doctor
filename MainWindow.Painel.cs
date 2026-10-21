using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Painel: diagnóstico com correções, nota de saúde, medidores, banner de programa pesado, Modo Foco, otimização e relatório.</summary>
public partial class MainWindow
{
    // ---------- Correções do diagnóstico (A14) ----------
    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Finding f || f.FixId is null) return;
        switch (f.FixId)
        {
            case "clean":
                Optimize_Click(sender, e);
                break;
            case "kill" when f.FixArg is { } name:
                var g = Host.Latest?.Groups.FirstOrDefault(x => x.Name == name);
                KillProgram(name, g?.Count ?? 1, g?.MemMB ?? 0);
                break;
            case "power-high" when f.FixArg is { } guid:
                await ActivatePlanAsync(guid);
                break;
            case "wsl-shutdown":
                if (Simulate("Encerrar o WSL", "Executaria \"wsl --shutdown\": todas as distribuições Linux e o Docker Desktop seriam desligados.")) break;
                if (Dialogs.Confirm(this, "Encerrar o WSL?", "Todas as distribuições Linux do WSL (e o Docker Desktop, se usar o WSL) serão desligadas e a memória volta para o Windows.\n\nTerminais, servidores e contêineres em execução nelas serão fechados.", "Encerrar", danger: true))
                {
                    int code = await SystemIntegrity.RunAsync("wsl.exe", "--shutdown", _ => { }, CancellationToken.None);
                    ActionLog.Add("WSL encerrado", $"wsl --shutdown (código {code})");
                    ShowSnack(code == 0 ? "WSL encerrado" : "O WSL não respondeu como esperado",
                              code == 0 ? "A memória volta para o Windows em alguns segundos." : $"O comando terminou com código {code}. Tente de novo ou reinicie o PC.");
                }
                break;
            case "open-security":
                AppInfo.OpenShell("windowsdefender:");
                break;
            case "open-folder" when f.FixArg is { } path:
                // Abre o Explorador já com o arquivo selecionado (argumento entre aspas: não executa o programa)
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
                break;
            case "open-apps":
                AppInfo.OpenShell("ms-settings:appsfeatures");
                break;
            case "open-devices":
                AppInfo.OpenShell("devmgmt.msc");
                break;
            case "open-backup":
                AppInfo.OpenShell("ms-settings:backup");
                break;
            case "battery-report":
                await OpenBatteryReportAsync();
                break;
            case "restart":
                if (Simulate("Reiniciar o Windows", "Reiniciaria o computador em 10 segundos. Programas abertos seriam fechados.")) break;
                if (Dialogs.Confirm(this, "Reiniciar agora?", "Salve seu trabalho antes: todos os programas abertos serão fechados.\n\nO Windows reinicia em 10 segundos.", "Reiniciar", danger: true))
                {
                    ActionLog.Add("Reinício pedido", "shutdown /r /t 10");
                    int code = await SystemIntegrity.RunAsync("shutdown.exe", "/r /t 10", _ => { }, CancellationToken.None);
                    if (code != 0) ShowSnack("O Windows não aceitou o reinício", $"O comando terminou com código {code}. Reinicie pelo menu Iniciar.");
                }
                break;
            case var tab when tab.StartsWith("tab:") && int.TryParse(tab[4..], out int idx):
                Tabs.SelectedIndex = idx;
                break;
        }
    }

    /// <summary>Relatório oficial do Windows (powercfg /batteryreport): capacidade, ciclos e histórico de uso.</summary>
    private async Task OpenBatteryReportAsync()
    {
        string file = System.IO.Path.Combine(AppPaths.ExportDir(), $"bateria-{DateTime.Now:yyyy-MM-dd-HHmm}.html");
        int code = await SystemIntegrity.RunAsync("powercfg.exe", $"/batteryreport /output \"{file}\"", _ => { }, CancellationToken.None);
        if (code == 0 && System.IO.File.Exists(file)) AppInfo.OpenShell(file);
        else ShowSnack("Relatório da bateria indisponível", "O Windows não gerou o relatório. Pode não haver bateria ou o serviço de energia não respondeu.");
    }

    // ---------- Modo Foco (A15) ----------
    private async void Focus_Click(object sender, RoutedEventArgs e)
    {
        FocusBtn.IsEnabled = false;
        try
        {
            if (Host.Focus.Active)
            {
                string msg = await Host.Focus.StopAsync();
                FocusBtn.Content = "Modo Foco";
                ActionLog.Add("Modo Foco desligado", msg);
                ShowSnack("Modo Foco desligado", msg);
            }
            else if (Host.Latest is { } snap)
            {
                if (Simulate("Ligar o Modo Foco", "Baixaria a prioridade dos programas pesados em segundo plano e ativaria Alto desempenho, se existir. Nada seria fechado.")) return;
                string msg = await Host.Focus.StartAsync(snap, Host.Health);
                FocusBtn.Content = "Desligar Modo Foco";
                ActionLog.Add("Modo Foco ligado", msg);
                ShowSnack("Modo Foco ligado", msg + " Nenhum programa foi fechado; ao desligar, tudo volta ao que era.",
                          "Desligar", () => Focus_Click(this, new RoutedEventArgs()));
            }
        }
        finally { FocusBtn.IsEnabled = true; }
    }

    // ---------- Banner de programa pesado (A41) ----------
    private void RefreshHeavyBanner()
    {
        var a = Host.HeavyAlert;
        HeavyBanner.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
        if (a is null) return;
        HeavyTitle.Text = $"{a.Name} está usando {a.CpuPct:0}% da CPU há mais de 3 minutos";
        HeavySub.Text = $"Desde {a.Since:HH:mm} · {a.MemMB} MB de RAM. Se não estiver em uso, encerrar libera o processador.";
    }

    private void HeavyKill_Click(object sender, RoutedEventArgs e)
    {
        if (Host.HeavyAlert is { } a) KillProgram(a.Name, Host.Latest?.Groups.FirstOrDefault(g => g.Name == a.Name)?.Count ?? 1, a.MemMB);
    }

    private void HeavyIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (Host.HeavyAlert is { } a) Host.IgnoreHeavy(a.Name);
    }

    // ---------- Faixa de saúde (A13, A09, A10, A21, A12) ----------
    private void RefreshHealthStrip()
    {
        var h = Host.Health;
        var sc = Host.Score;
        ScoreText.Text = sc.Score > 0 ? sc.Score.ToString() : "–";
        ScoreLabel.Text = sc.Label;
        // Cor só para estado: o número fica na cor do texto (contraste alto) e o estado vai num chip (texto preto/branco sobre a cor)
        string scoreKey = sc.Score <= 0 ? "SurfaceVariant" : sc.Score >= 75 ? "Success" : sc.Score >= 55 ? "Warning" : "Error";
        ScoreChip.SetResourceReference(Border.BackgroundProperty, scoreKey);
        ScoreLabel.SetResourceReference(TextBlock.ForegroundProperty, sc.Score <= 0 ? "OnSurface" : "On" + scoreKey);
        if (sc.Score <= 0) ScoreLabel.Text = "Calculando…";
        System.Windows.Automation.AutomationProperties.SetName(ScoreText, sc.Score > 0 ? $"Nota de saúde {sc.Score} de 100, {sc.Label}" : "Nota de saúde ainda sendo calculada");
        FeedScorePulse(sc.Score);

        if (h.HasDisk)
        {
            DiskHealthTitle.Text = $"{(h.DiskType.Length > 0 ? h.DiskType : "Disco")} · saúde";
            string state = h.DiskHealthy ? "Saudável" : h.DiskHealth;
            DiskHealthText.Text = state + (h.DiskWearPct is { } w ? $" · desgaste {w}%" : "") + (h.DiskTempC is { } t ? $" · {t} °C" : "");
            DiskHealthText.ToolTip = h.DiskWearPct is null ? "Desgaste e temperatura do SSD só aparecem com o app em modo administrador." : null;
        }
        else DiskHealthText.Text = "indisponível";

        string bat = h.BatteryHealthPct is { } b ? $"{b}%" + (h.BatteryCycles is { } c ? $" ({c:N0} ciclos)" : "") : "sem bateria";
        string boot = h.BootSeconds is { } s ? $"{s} s" : "–";
        BatteryBootText.Text = $"{bat} · boot {boot}";
        BatteryBootText.ToolTip = h.BootSeconds is null ? "O tempo de boot vem do log do Windows e pode exigir modo administrador." : null;
    }

    // ---------- Otimizar agora (A01) ----------
    private async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Settings.SimulationMode)
        {
            long would = await Task.Run(() => Cleaner.SafeJunkBytes(Host.Settings.TempDays));
            Simulate("Otimizar agora", $"Apagaria cerca de {Cleaner.FormatBytes(would)} de temporários com mais de {Host.Settings.TempDays} dias" + (AppInfo.IsAdmin ? " e otimizaria o SSD (TRIM)." : "."));
            return;
        }
        OptimizeBtn.IsEnabled = false;
        var before = Host.Latest;
        int scoreBefore = Host.Score.Score;
        long freed = await Host.QuickCleanAsync(notify: false);
        await Task.Delay(4000); // espera uma amostra nova para comparar (A48: antes × depois)
        var after = Host.Latest;
        OptimizeBtn.IsEnabled = true;

        string compare = before is null || after is null ? "" :
            $"Antes → depois: espaço livre {before.DiskFreeGB:0.0} → {after.DiskFreeGB:0.0} GB · memória {before.RamPct:0}% → {after.RamPct:0}% · nota {scoreBefore} → {Host.Score.Score}. ";
        string admin = AppInfo.IsAdmin ? "O SSD também foi otimizado (TRIM)." : "Pastas do Windows e otimização do SSD exigem o modo administrador.";
        // Resultado no próprio app, sem diálogo modal; o detalhe fica no registro de Ações
        ShowSnack($"Otimização concluída: {Cleaner.FormatBytes(freed)} liberados", compare + admin,
                  "Mais opções", () => Tabs.SelectedIndex = TabClean, seconds: 15);
    }

    // ---------- Pulso da nota de saúde (última hora) ----------
    private readonly List<double> _scorePulse = new();
    private readonly List<DateTime> _scoreTimes = new();
    private DateTime _lastPulse = DateTime.MinValue;

    private async void SeedScorePulse()
    {
        var data = await Task.Run(() => Host.History.Load(TimeSpan.FromHours(1)));
        foreach (var d in data.Where(d => d.Score >= 0)) { _scorePulse.Add(d.Score); _scoreTimes.Add(d.T); }
        if (_scorePulse.Count > 0) _lastPulse = _scoreTimes[^1];
    }

    private void FeedScorePulse(int score)
    {
        if (score <= 0 || (DateTime.Now - _lastPulse).TotalSeconds < 30) return;
        _lastPulse = DateTime.Now;
        _scorePulse.Add(score); _scoreTimes.Add(_lastPulse);
        while (_scoreTimes.Count > 0 && (DateTime.Now - _scoreTimes[0]).TotalHours > 1) { _scorePulse.RemoveAt(0); _scoreTimes.RemoveAt(0); }
        ScoreSpark.SetValues(_scorePulse.ToList(), _scoreTimes.ToList());
    }

    private static string FormatRate(double kbs) => kbs >= 1024 ? $"{kbs / 1024:0.0} MB/s" : $"{kbs:0} KB/s";

    // ---------- B20: relatório em PDF; B18: resumo na área de transferência ----------
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Latest is not { } s) return;
        var findings = Diagnosis.Evaluate(Host.Recent, Host.Health, Host.Memory.Suspects);
        try { AppInfo.OpenShell(ReportExporter.ExportPdf(s, findings, Host.History.Load(TimeSpan.FromDays(7)))); }
        catch (Exception ex) { ShowSnack("Não foi possível gerar o PDF", ex.Message); }
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Latest is not { } s) return;
        var findings = Diagnosis.Evaluate(Host.Recent, Host.Health, Host.Memory.Suspects);
        string text = ReportContent.Summary(s, Host.Score, Insights.Bottleneck(Host.Recent), findings);
        try { Clipboard.SetText(text); ShowSnack("Resumo copiado", "Cole com Ctrl+V numa conversa, e-mail ou chamado."); }
        catch { ShowSnack("Não foi possível copiar", "Outro programa está usando a área de transferência. Tente de novo."); }
    }

    // ---------- Relatório ----------
    public void ExportReport() => Export_Click(this, new RoutedEventArgs());

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Latest is not { } s) return;
        var findings = Diagnosis.Evaluate(Host.Recent, Host.Health, Host.Memory.Suspects);
        string file = ReportExporter.Export(s, findings, Host.History.Load(TimeSpan.FromDays(7)));
        AppInfo.OpenShell(file);
    }

    // ---------- Painel ----------
    private void OnSnapshot(Snapshot s)
    {
        if (!IsVisible) return;

        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        SubTitle.Text = $"Ligado há {(up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : $"{up.Hours} h {up.Minutes} min")} · {(AppInfo.IsAdmin ? "administrador" : "usuário padrão")} · rede ↓ {FormatRate(s.NetDownKBs)} ↑ {FormatRate(s.NetUpKBs)}";

        CpuText.Text = $"{s.Cpu:0}%";
        CpuSub.Text = $"Maior: {s.TopByCpu.FirstOrDefault()?.Name ?? "–"}" + (s.TempC > 0 ? $" · {s.TempC:0} °C" : "");
        RamText.Text = $"{s.RamPct:0}%";
        RamSub.Text = $"{s.RamUsedGB:0.0} de {s.RamTotalGB:0.0} GB";
        DiskText.Text = $"{s.DiskBusy:0}%";
        DiskSub.Text = $"ocupado · {s.DiskFreeGB:0} GB livres ({s.DiskFreePct:0}%)";

        var recent = Host.Recent;
        var times = recent.Select(x => x.Time).ToList();
        CpuSpark.SetValues(recent.Select(x => x.Cpu).ToList(), times);
        RamSpark.SetValues(recent.Select(x => x.RamPct).ToList(), times);
        DiskSpark.SetValues(recent.Select(x => x.DiskBusy).ToList(), times);

        CpuBar.Value = s.Cpu; CpuBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.Cpu));
        RamBar.Value = s.RamPct; RamBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.RamPct));
        DiskBar.Value = s.DiskBusy; DiskBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.DiskBusy));

        var findings = Diagnosis.Evaluate(recent, Host.Health, Host.Memory.Suspects);
        foreach (var f in findings) f.WhyOpen = _whyOpen.Contains(f.Title);
        FindingsList.ItemsSource = findings;
        UpdateStatusChip(findings);
        UpdateTaskbarBadge(findings);

        var bn = Insights.Bottleneck(recent);
        BottleneckName.Text = bn.Resource;
        BottleneckText.Text = bn.Explanation;
        RefreshHealthStrip();
        RefreshHeavyBanner();

        if (Tabs.SelectedIndex == TabProcs) RefreshProcesses(s);
    }

    // Escala de carga vinda do tema (LevelLow…LevelCrit): no escuro lima → amarelo → laranja → vermelho;
    // no claro verde → laranja → vermelho, porque lima e amarelo somem sobre o fundo claro.
    private static string LevelKey(double pct) => pct >= 85 ? "LevelCrit" : pct >= 70 ? "LevelHigh" : pct >= 50 ? "LevelMid" : "LevelLow";

    // ---------- B47: selo na barra de tarefas ----------
    private BadgeLevel _badgeLevel = (BadgeLevel)(-1);

    private void UpdateTaskbarBadge(IReadOnlyList<Finding> findings)
    {
        var badge = TaskbarBadge.For(Host.Score.Score, findings);
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        TaskbarItemInfo.Description = badge.Description;
        if (badge.Level == _badgeLevel) return;
        _badgeLevel = badge.Level;
        TaskbarItemInfo.Overlay = badge.Level switch
        {
            BadgeLevel.Problem => BadgeDot("Error", "!"),
            BadgeLevel.Attention => BadgeDot("Warning", "!"),
            _ => null,
        };
    }

    /// <summary>Bolinha 16×16 na cor do estado com um "!" no contraste certo (OnError/OnWarning), como pede o Windows para selos.</summary>
    private System.Windows.Media.ImageSource BadgeDot(string colorKey, string mark)
    {
        var fill = TryFindResource(colorKey) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.OrangeRed;
        var ink = TryFindResource("On" + colorKey) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var text = new System.Windows.Media.FormattedText(mark, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            12, ink, 1.0);
        var group = new System.Windows.Media.DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawEllipse(fill, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1), new Point(8, 8), 7.5, 7.5);
            dc.DrawText(text, new Point(8 - text.Width / 2, 8 - text.Height / 2));
        }
        var img = new System.Windows.Media.DrawingImage(group);
        img.Freeze();
        return img;
    }

    private void UpdateStatusChip(IReadOnlyList<Finding> findings)
    {
        int problems = findings.Count(f => f.Level == "PROBLEMA"), warns = findings.Count(f => f.Level == "ATENCAO");
        (string bg, string fg, string text) = problems > 0 ? ("Error", "OnError", Pt.N(problems, "problema", "problemas"))
                                            : warns > 0 ? ("Warning", "OnWarning", Pt.N(warns, "ponto de atenção", "pontos de atenção"))
                                            : ("Success", "OnSuccess", "Tudo certo");
        StatusChip.SetResourceReference(Border.BackgroundProperty, bg);
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, fg);
        StatusText.Text = text;
    }

    // ---------- M15: explicações abertas no diagnóstico ----------
    private readonly HashSet<string> _whyOpen = new();

    private void WhyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { DataContext: Finding f } t) return;
        if (t.IsChecked == true) _whyOpen.Add(f.Title); else _whyOpen.Remove(f.Title);
        f.WhyOpen = t.IsChecked == true;
    }
}
