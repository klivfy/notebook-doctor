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
                    ActionLog.Add("WSL encerrado", Loc.F("wsl --shutdown (código {0})", code));
                    ShowSnack(code == 0 ? "WSL encerrado" : "O WSL não respondeu como esperado",
                              code == 0 ? "A memória volta para o Windows em alguns segundos." : Loc.F("O comando terminou com código {0}. Tente de novo ou reinicie o PC.", code));
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
            // A22, A23 e A39
            case "open-update":
                AppInfo.OpenShell("ms-settings:windowsupdate");
                break;
            case "open-optional-updates":
                AppInfo.OpenShell("ms-settings:windowsupdate-optionalupdates");
                break;
            case "open-defrag":
                AppInfo.OpenShell("dfrgui.exe");
                break;
            case "open-maintenance":
                try { System.Diagnostics.Process.Start("control.exe", "/name Microsoft.ActionCenter"); } catch { }
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
                    if (code != 0) ShowSnack("O Windows não aceitou o reinício", Loc.F("O comando terminou com código {0}. Reinicie pelo menu Iniciar.", code));
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
                FocusBtn.Content = Loc.T("Modo Foco");
                ActionLog.Add("Modo Foco desligado", msg);
                ShowSnack("Modo Foco desligado", msg);
            }
            else if (Host.Latest is { } snap)
            {
                if (Simulate("Ligar o Modo Foco", "Baixaria a prioridade dos programas pesados em segundo plano e ativaria Alto desempenho, se existir. Nada seria fechado.")) return;
                string msg = await Host.Focus.StartAsync(snap, Host.Health);
                FocusBtn.Content = Loc.T("Desligar Modo Foco");
                ActionLog.Add("Modo Foco ligado", msg);
                ShowSnack("Modo Foco ligado", msg + " " + Loc.T("Nenhum programa foi fechado; ao desligar, tudo volta ao que era."),
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
        HeavyTitle.Text = Loc.F("{0} está usando {1:0}% da CPU há mais de 3 minutos", a.Name, a.CpuPct);
        HeavySub.Text = Loc.F("Desde {0:HH:mm} · {1} MB de RAM. Se não estiver em uso, encerrar libera o processador.", a.Since, a.MemMB);
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
        ScoreLabel.Text = Loc.T(sc.Label);
        // Cor só para estado: o número fica na cor do texto (contraste alto) e o estado vai num chip (texto preto/branco sobre a cor)
        string scoreKey = sc.Score <= 0 ? "SurfaceVariant" : sc.Score >= 75 ? "Success" : sc.Score >= 55 ? "Warning" : "Error";
        // Redesign: fundo suave do estado e texto na cor do estado (contraste conferido por ThemeContrastTests)
        ScoreChip.SetResourceReference(Border.BackgroundProperty, sc.Score <= 0 ? scoreKey : scoreKey + "Container");
        ScoreLabel.SetResourceReference(TextBlock.ForegroundProperty, sc.Score <= 0 ? "OnSurface" : scoreKey);
        if (sc.Score <= 0) ScoreLabel.Text = Loc.T("Calculando…");
        System.Windows.Automation.AutomationProperties.SetName(ScoreText, sc.Score > 0 ? Loc.F("Nota de saúde {0} de 100, {1}", sc.Score, Loc.T(sc.Label)) : Loc.T("Nota de saúde ainda sendo calculada"));
        FeedScorePulse(sc.Score);

        if (h.HasDisk)
        {
            DiskHealthTitle.Text = Loc.F("{0} · saúde", h.DiskType.Length > 0 ? h.DiskType : Loc.T("Disco"));
            string state = Loc.T(h.DiskHealthy ? "Saudável" : h.DiskHealth);
            DiskHealthText.Text = state + (h.DiskWearPct is { } w ? " · " + Loc.F("desgaste {0}%", w) : "") + (h.DiskTempC is { } t ? " · " + Units.Temp(t) : "");
            DiskHealthText.ToolTip = h.DiskWearPct is null ? Loc.T("Desgaste e temperatura do SSD só aparecem com o app em modo administrador.") : null;
        }
        else DiskHealthText.Text = Loc.T("indisponível");

        string bat = h.BatteryHealthPct is { } b ? $"{b}%" + (h.BatteryCycles is { } c ? " " + Loc.F("({0:N0} ciclos)", c) : "") : Loc.T("sem bateria");
        string boot = h.BootSeconds is { } s ? $"{s} s" : "–";
        BatteryBootText.Text = $"{bat} · boot {boot}";
        BatteryBootText.ToolTip = h.BootSeconds is null ? Loc.T("O tempo de boot vem do log do Windows e pode exigir modo administrador.") : null;
    }

    // ---------- Otimizar agora (A01) ----------
    private async void Optimize_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Settings.SimulationMode)
        {
            long would = await Task.Run(() => Cleaner.SafeJunkBytes(Host.Settings.TempDays));
            Simulate("Otimizar agora", Loc.F(AppInfo.IsAdmin ? "Apagaria cerca de {0} de temporários com mais de {1} dias e otimizaria o SSD (TRIM)." : "Apagaria cerca de {0} de temporários com mais de {1} dias.", Cleaner.FormatBytes(would), Host.Settings.TempDays));
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
            Loc.F("Antes → depois: espaço livre {0} → {1} · memória {2:0}% → {3:0}% · nota {4} → {5}.", Units.Gb(before.DiskFreeGB), Units.Gb(after.DiskFreeGB), before.RamPct, after.RamPct, scoreBefore, Host.Score.Score) + " ";
        string admin = Loc.T(AppInfo.IsAdmin ? "O SSD também foi otimizado (TRIM)." : "Pastas do Windows e otimização do SSD exigem o modo administrador.");
        // Resultado no próprio app, sem diálogo modal; o detalhe fica no registro de Ações
        var outcome = CleanOutcome.Describe(freed);
        ShowSnack(outcome.Celebrate ? Loc.T(outcome.Title) : Loc.F("Otimização concluída: {0} liberados", Cleaner.FormatBytes(freed)), compare + admin,
                  "Mais opções", () => Tabs.SelectedIndex = TabClean, seconds: 15);
        PlayDoneSound();
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

    // ---------- B19: imagem do painel ----------
    private void PainelImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // O painel inteiro, inclusive a parte fora da tela, no fundo do tema e na escala do monitor
            var el = PainelContent;
            double w = el.ActualWidth, h = el.ActualHeight;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(el);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                                                                          dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background ?? System.Windows.Media.Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(el), null, new Rect(0, 0, w, h));
            }
            bmp.Render(visual);
            bmp.Freeze();

            string file = System.IO.Path.Combine(AppPaths.ExportDir(), $"painel-{DateTime.Now:yyyy-MM-dd_HHmm}.png");
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(file)) png.Save(fs);
            bool copied = true;
            try { Clipboard.SetImage(bmp); } catch { copied = false; } // outro programa segurando a área de transferência
            ShowSnack("Imagem do painel salva", (copied ? Loc.T("Também foi copiada: cole com Ctrl+V.") + " " : "") + System.IO.Path.GetFileName(file),
                      "Abrir", () => AppInfo.OpenShell(file), seconds: 12);
        }
        catch (Exception ex) { ShowSnack("Não foi possível salvar a imagem", ex.Message); }
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
    /// <summary>"3 d 4 h", "2 h 5 min" ou só "4 min" quando ainda não passou uma hora.</summary>
    private static string Uptime(TimeSpan up) =>
        up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : up.Hours > 0 ? $"{up.Hours} h {up.Minutes} min" : $"{up.Minutes} min";

    private void OnSnapshot(Snapshot s)
    {
        if (!IsVisible) return;

        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        SubTitle.Text = Loc.F("Ligado há {0} · {1} · rede ↓ {2} ↑ {3}", Uptime(up), Loc.T(AppInfo.IsAdmin ? "administrador" : "usuário padrão"), FormatRate(s.NetDownKBs), FormatRate(s.NetUpKBs));

        CpuText.Text = $"{s.Cpu:0}%";
        CpuSub.Text = Loc.F("Maior: {0}", s.TopByCpu.FirstOrDefault()?.Name ?? "–") + (s.TempC > 0 ? " · " + Units.Temp(s.TempC) : "");
        RamText.Text = $"{s.RamPct:0}%";
        RamSub.Text = Units.GbOf(s.RamUsedGB, s.RamTotalGB);
        DiskText.Text = $"{s.DiskBusy:0}%";
        DiskSub.Text = Loc.F("ocupado · {0} livres ({1:0}%)", Units.Gb(s.DiskFreeGB, "0"), s.DiskFreePct);

        var recent = Host.Recent;
        var times = recent.Select(x => x.Time).ToList();
        CpuSpark.SetValues(recent.Select(x => x.Cpu).ToList(), times);
        RamSpark.SetValues(recent.Select(x => x.RamPct).ToList(), times);
        DiskSpark.SetValues(recent.Select(x => x.DiskBusy).ToList(), times);

        Motion.SetValue(CpuBar, s.Cpu); CpuBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.Cpu));
        Motion.SetValue(RamBar, s.RamPct); RamBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.RamPct));
        Motion.SetValue(DiskBar, s.DiskBusy); DiskBar.SetResourceReference(ProgressBar.ForegroundProperty, LevelKey(s.DiskBusy));

        var findings = Diagnosis.Evaluate(recent, Host.Health, Host.Memory.Suspects);
        foreach (var f in findings) f.WhyOpen = _whyOpen.Contains(f.Title);
        FindingsList.ItemsSource = findings;
        UpdateStatusChip(findings);
        UpdateTaskbarBadge(findings);
        UpdateTiles(s, findings);

        var bn = Insights.Bottleneck(recent);
        BottleneckName.Text = Loc.T(bn.Resource);
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

    // ---------- Redesign V4: atalhos 2 × 2 do Painel ----------
    private void UpdateTiles(Snapshot s, IReadOnlyList<Finding> findings)
    {
        int problems = findings.Count(f => f.Level == "PROBLEMA"), warns = findings.Count(f => f.Level == "ATENCAO");
        TileDiagValue.Text = problems + warns == 0 ? Loc.T("Tudo certo") : Pt.N(problems + warns, Loc.T("ponto a ver"), Loc.T("pontos a ver"));
        TileProcValue.Text = Pt.N(s.Groups.Count, Loc.T("programa aberto"), Loc.T("programas abertos"));
        TileDiskValue.Text = Loc.F("{0} livres ({1}%)", Units.Gb(s.DiskFreeGB, "0"), s.DiskFreePct.ToString("0"));
    }

    private void TileDiagnosis_Click(object sender, RoutedEventArgs e) => DiagnosisCard.BringIntoView();
    private void TileProcesses_Click(object sender, RoutedEventArgs e) => SelectTab(AppTabs.Processes);
    private void TileClean_Click(object sender, RoutedEventArgs e) => SelectTab(AppTabs.Clean);
    private void TileStartup_Click(object sender, RoutedEventArgs e) => SelectTab(AppTabs.Startup);

    /// <summary>"Mais": exportar e compartilhar, num menu, para o cartão principal ter uma ação só.</summary>
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } b) return;
        menu.PlacementTarget = b;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void UpdateStatusChip(IReadOnlyList<Finding> findings)
    {
        int problems = findings.Count(f => f.Level == "PROBLEMA"), warns = findings.Count(f => f.Level == "ATENCAO");
        // Redesign: chip no fundo suave do estado com o texto na cor do estado (como o InfoBar do Windows)
        (string bg, string fg, string text) = problems > 0 ? ("ErrorContainer", "Error", Pt.N(problems, "problema", "problemas"))
                                            : warns > 0 ? ("WarningContainer", "Warning", Pt.N(warns, "ponto de atenção", "pontos de atenção"))
                                            : ("SuccessContainer", "Success", Loc.T("Tudo certo"));
        StatusChip.SetResourceReference(Border.BackgroundProperty, bg);
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, fg);
        StatusText.Text = text;
        // Ponto de pendência no item do Painel da navegação
        if (Nav.Items[AppTabs.Panel] is ListBoxItem panelItem) NavLabel.SetBadge(panelItem, problems + warns > 0);
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
