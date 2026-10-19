using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record ProcRow(string Name, int Count, double CpuPct, long MemMB, double IoKBs, int Conns, string About, string SafetyText,
                             System.Windows.Media.ImageSource? Icon = null)
{
    public bool HasIcon => Icon is not null;
}

public partial class MainWindow : Window
{
    private static App Host => App.Current;

    private const int TabProcs = 1, TabClean = 2, TabSpace = 3, TabStartup = 4, TabHistory = 5, TabActions = 6, TabTweaks = 7, TabSettings = 8;

    private List<StartupEntry> _startup = new();

    public MainWindow()
    {
        InitializeComponent();
        FooterText.Text = AppInfo.Copyright;
        AboutText.Text = $"Notebook Doctor {AppInfo.Version}\nSuporte: klivfy.com/suporte";
        RefreshAdminCard();
        LoadSettingsIntoForm();
        RefreshProfileCard();
        Host.SnapshotReady += OnSnapshot;
        Host.HealthUpdated += () => { RefreshHealthStrip(); if (Host.Latest is not null) OnSnapshot(Host.Latest); };
        Host.HeavyAlertChanged += RefreshHeavyBanner;
        Host.MiniChanged += on => CfgMini.IsChecked = on;
        RefreshStartup();
        Tabs.SelectedIndex = Math.Clamp(Host.Settings.LastTab, 0, Tabs.Items.Count - 1);
        SeedScorePulse();
        SimChip.Visibility = Host.Settings.SimulationMode ? Visibility.Visible : Visibility.Collapsed;
        // O "Tamanho do texto" do Windows pode mudar com o app escondido na bandeja
        IsVisibleChanged += (_, _) => { if (IsVisible) TextScale.Apply(); };
        _snackTimer.Tick += (_, _) => HideSnack();

        // Atalhos: Ctrl+1…8 trocam de aba; F5 atualiza a aba atual
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9) { Tabs.SelectedIndex = e.Key - Key.D1; e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshCurrentTab(); e.Handled = true; }
            else if (e.Key == Key.Escape && Snack.IsVisible) { HideSnack(); e.Handled = true; }
        };
    }

    private void RefreshCurrentTab()
    {
        switch (Tabs.SelectedIndex)
        {
            case 0: _ = Host.RefreshHealthAsync(); break;
            case TabClean: Scan_Click(this, new RoutedEventArgs()); break;
            case TabStartup: RefreshStartup(); break;
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); break;
            case TabSettings: LoadPlans(); break;
        }
    }

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
            case var tab when tab.StartsWith("tab:") && int.TryParse(tab[4..], out int idx):
                Tabs.SelectedIndex = idx;
                break;
        }
    }

    // ---------- Perfis (A16) ----------
    private void RefreshProfileCard()
    {
        var p = Profile.All.FirstOrDefault(x => x.Id == Host.Settings.ActiveProfile) ?? Profile.All[0];
        ProfileDesc.Text = $"Ativo: {p.Name}. {p.Description}";
        foreach (var b in new[] { ProfWork, ProfMeeting, ProfBattery, ProfPerf })
            b.SetResourceReference(StyleProperty, (string)b.Tag == p.Id ? typeof(Button) : "TonalButton");
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id || Profile.All.FirstOrDefault(x => x.Id == id) is not { } prof) return;
        if (Simulate($"Ativar o perfil {prof.Name}", $"{prof.Description} Trocaria o plano de energia, os avisos e o Modo Foco conforme o perfil.")) return;
        var notes = new List<string>();

        var plans = await Task.Run(PowerPlans.List);
        var target = prof.FindPlan(plans);
        if (target is null) notes.Add(prof.PlanKind == "high" ? "Plano Alto desempenho não existe: crie-o em Plano de energia." : "O plano deste perfil não existe neste Windows; o plano atual foi mantido.");
        else if (!target.Active) await ActivatePlanAsync(target.Guid);

        Host.Settings.AlertsEnabled = prof.AlertsEnabled;
        notes.Add(prof.AlertsEnabled ? "Avisos ligados." : "Avisos desligados até trocar de perfil.");

        if (prof.Focus && !Host.Focus.Active && Host.Latest is { } snap) notes.Add(await Host.Focus.StartAsync(snap, Host.Health));
        else if (!prof.Focus && Host.Focus.Active) notes.Add(await Host.Focus.StopAsync());
        FocusBtn.Content = Host.Focus.Active ? "Desligar Modo Foco" : "Modo Foco";

        Host.Settings.ActiveProfile = prof.Id;
        Host.Settings.Save();
        LoadSettingsIntoForm();
        RefreshProfileCard();
        ActionLog.Add($"Perfil: {prof.Name}", string.Join(" ", notes));
        ShowSnack($"Perfil {prof.Name} ativo", (target is { } t ? $"Plano de energia: {t.Name}. " : "") + string.Join(" ", notes),
                  "Ver em Ações", () => Tabs.SelectedIndex = TabActions);
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

    // ---------- Espaço em disco (A27) ----------
    private CancellationTokenSource? _spaceCts;
    private readonly List<FolderUsage> _space = new();

    private async void SpaceScan_Click(object sender, RoutedEventArgs e)
    {
        string root = ((SpaceRoot.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "drive" => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
            _ => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        _spaceCts?.Cancel();
        _spaceCts = new CancellationTokenSource();
        var ct = _spaceCts.Token;
        _space.Clear();
        SpaceGrid.ItemsSource = null;
        SpaceScanBtn.IsEnabled = false; SpaceCancelBtn.IsEnabled = true;
        string mode = (SpaceMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "folders";
        void Progress(string m) => Dispatcher.BeginInvoke(() => SpaceMsg.Text = m);
        void Show(List<FolderUsage> items)
        {
            long max = items.Count > 0 ? items.Max(x => x.Bytes) : 0;
            foreach (var x in items) x.Percent = max > 0 ? x.Bytes * 100.0 / max : 0;
            SpaceGrid.ItemsSource = items;
            ApplySpaceFilter();
        }
        try
        {
            if (mode == "large")
            {
                var items = await DiskUsage.LargestFilesAsync(root, 50L << 20, 100, Progress, ct);
                Show(items);
                SpaceMsg.Text = $"{items.Count} maiores arquivos (50 MB ou mais) em {root}. Só leitura.";
                return;
            }
            if (mode == "dupes")
            {
                var items = await DiskUsage.DuplicatesAsync(root, 10L << 20, 60, Progress, ct);
                Show(items);
                int groups = items.Count(i => i.Name.Contains("(cópia 1 de"));
                long extra = items.Where(i => !i.Name.Contains("(cópia 1 de")).Sum(i => i.Bytes);
                SpaceMsg.Text = groups == 0 ? "Nenhum arquivo duplicado de 10 MB ou mais." : $"{Pt.N(groups, "grupo", "grupos")} de arquivos idênticos; as cópias extras ocupam {Cleaner.FormatBytes(extra)}. Confira antes de apagar qualquer uma.";
                return;
            }
            if (mode == "apps")
            {
                var apps = await Task.Run(DiskUsage.InstalledApps, ct);
                Show(apps);
                SpaceMsg.Text = $"{apps.Count} aplicativos. Tamanho informado pelo próprio instalador (\"n/d\" quando ele não informa).";
                return;
            }
            await DiskUsage.ScanAsync(root,
                item => Dispatcher.BeginInvoke(() =>
                {
                    _space.Add(item);
                    long max = _space.Max(x => x.Bytes);
                    foreach (var x in _space) x.Percent = max > 0 ? x.Bytes * 100.0 / max : 0;
                    SpaceGrid.ItemsSource = _space.OrderByDescending(x => x.Bytes).ToList();
                    ApplySpaceFilter();
                }),
                msg => Dispatcher.BeginInvoke(() => SpaceMsg.Text = msg), ct);
            SpaceMsg.Text = $"Pronto: {_space.Count} itens em {root} · total {Cleaner.FormatBytes(_space.Sum(x => x.Bytes))}. Só leitura: nada foi apagado.";
        }
        catch (OperationCanceledException) { SpaceMsg.Text = "Análise cancelada."; }
        finally { SpaceScanBtn.IsEnabled = true; SpaceCancelBtn.IsEnabled = false; }
    }

    private void SpaceCancel_Click(object sender, RoutedEventArgs e) => _spaceCts?.Cancel();

    private void SpaceMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != SpaceMode) return;
        bool apps = (SpaceMode.SelectedItem as ComboBoxItem)?.Tag as string == "apps";
        SpaceRoot.IsEnabled = !apps;
        SpaceUninstallBtn.Visibility = apps ? Visibility.Visible : Visibility.Collapsed;
        SpaceGrid.ItemsSource = null;
        SpaceMsg.Text = "Clique em Analisar.";
    }

    private void SpaceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage f) { SpaceMsg.Text = "Selecione um item na lista."; return; }
        if (f.IsFile && System.IO.File.Exists(f.Path))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\""); } catch { }
        }
        else if (System.IO.Directory.Exists(f.Path)) AppInfo.OpenShell(f.Path);
        else SpaceMsg.Text = "Este item não informa uma pasta.";
    }

    // ---------- Desinstalador assistido (A29) ----------
    private void SpaceUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceGrid.SelectedItem is not FolderUsage app || app.Tag.Length == 0) { SpaceMsg.Text = "Selecione um aplicativo."; return; }
        if (Simulate("Abrir o desinstalador", $"Executaria o desinstalador registrado de {app.Name}: {app.Tag}")) return;
        if (!Dialogs.Confirm(this, "Desinstalar aplicativo?",
                $"Abrir o desinstalador de:\n{app.Name}\n\nO próprio desinstalador do programa vai abrir e pedir confirmação. O Notebook Doctor não remove nada sozinho.",
                "Abrir desinstalador", danger: true)) return;
        try
        {
            // O comando vem do registro do próprio instalador; o cmd interpreta as aspas como o Windows faria em "Aplicativos instalados"
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{app.Tag}\"") { UseShellExecute = false, CreateNoWindow = true });
            ActionLog.Add("Desinstalador aberto", app.Name);
            SpaceMsg.Text = "Desinstalador aberto. Clique em Analisar depois para atualizar a lista.";
        }
        catch (Exception ex) { SpaceMsg.Text = "Não foi possível abrir o desinstalador: " + ex.Message; }
    }

    // ---------- Integridade do Windows (A24) ----------
    private CancellationTokenSource? _integrityCts;

    private async Task RunIntegrityAsync(bool repair)
    {
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, "Precisa de administrador", "SFC e DISM só funcionam com privilégios elevados. Reiniciar o Notebook Doctor como administrador agora?", "Reiniciar"))
                Host.RestartAsAdmin();
            return;
        }
        if (repair && Simulate("Reparar arquivos do Windows", "Executaria \"sfc /scannow\" e \"DISM /Online /Cleanup-Image /RestoreHealth\".")) return;
        if (repair && !Dialogs.Confirm(this, "Reparar arquivos do Windows?", "O Windows vai verificar e corrigir arquivos do sistema (sfc /scannow e DISM /RestoreHealth). Pode levar de 10 a 30 minutos e usa bastante disco e CPU. Não desligue o computador durante o processo.", "Reparar")) return;

        _integrityCts = new CancellationTokenSource();
        var ct = _integrityCts.Token;
        IntegrityCheckBtn.IsEnabled = IntegrityRepairBtn.IsEnabled = false; IntegrityCancelBtn.IsEnabled = true;
        IntegrityLog.Clear();
        void Log(string line) => Dispatcher.BeginInvoke(() => { IntegrityLog.AppendText(line + "\n"); IntegrityLog.ScrollToEnd(); });
        try
        {
            Log(repair ? "== SFC /scannow ==" : "== SFC /verifyonly ==");
            int sfc = await SystemIntegrity.Sfc(repair, Log, ct);
            Log($"SFC terminou (código {sfc}).");
            Log(repair ? "== DISM /RestoreHealth ==" : "== DISM /ScanHealth ==");
            int dism = await SystemIntegrity.Dism(repair, Log, ct);
            Log($"DISM terminou (código {dism}).");
            ActionLog.Add(repair ? "Reparo de integridade (SFC+DISM)" : "Verificação de integridade (SFC+DISM)", $"SFC={sfc}, DISM={dism}");
        }
        catch (OperationCanceledException) { Log("Cancelado."); }
        finally { IntegrityCheckBtn.IsEnabled = IntegrityRepairBtn.IsEnabled = true; IntegrityCancelBtn.IsEnabled = false; }
    }

    private async void IntegrityCheck_Click(object sender, RoutedEventArgs e) => await RunIntegrityAsync(repair: false);
    private async void IntegrityRepair_Click(object sender, RoutedEventArgs e) => await RunIntegrityAsync(repair: true);
    private void IntegrityCancel_Click(object sender, RoutedEventArgs e) => _integrityCts?.Cancel();

    /// <summary>Confirma, encerra e registra. Encerrar é definitivo: trabalho não salvo se perde.</summary>
    private void KillProgram(string name, int count, long memMB)
    {
        if (ProcessTools.IsProtected(name))
        {
            ShowSnack("Processo do sistema", $"\"{name}\" faz parte do Windows e não pode ser encerrado por aqui.");
            return;
        }
        var about = ProcessKnowledge.Describe(name);
        if (Simulate($"Encerrar {name}", $"Fecharia {Pt.N(count, "processo", "processos")} de {name} ({memMB:N0} MB). Trabalho não salvo nele seria perdido.")) return;
        if (!Dialogs.Confirm(this, "Encerrar programa?",
                $"Encerrar \"{name}\" ({Pt.N(count, "processo", "processos")}, {memMB:N0} MB)?\n\nO que é: {about.Description}.\nSeguro encerrar: {about.SafetyText}.\n\nTrabalho não salvo nesse programa será perdido.",
                "Encerrar", danger: true)) return;
        int n = ProcessTools.Kill(name);
        string done = n == 1 ? $"1 processo de {name} encerrado." : $"{n} processos de {name} encerrados.";
        ProcMsg.Text = n > 0 ? done : "Não foi possível encerrar: falta permissão ou o programa já fechou.";
        if (n > 0) { ActionLog.Add("Programa encerrado", $"{name} ({Pt.N(n, "processo", "processos")}, {memMB:N0} MB)"); ShowSnack(done, $"{memMB:N0} MB de memória liberados."); }
        RefreshHeavyBanner();
    }

    private async Task ActivatePlanAsync(string guid)
    {
        var plans = await Task.Run(PowerPlans.List);
        var previous = plans.FirstOrDefault(p => p.Active);
        var target = plans.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (target is null || previous is null || previous.Guid == target.Guid) return;
        if (Simulate("Trocar o plano de energia", $"Trocaria o plano \"{previous.Name}\" por \"{target.Name}\" (powercfg /setactive).")) return;

        string restore = await RestorePoint.EnsureAsync(Host.Settings, "Plano de energia");
        bool ok = await Task.Run(() => PowerPlans.SetActive(target.Guid));
        PlanMsg.Text = ok ? $"Plano ativo: {target.Name}" : "Não foi possível trocar o plano.";
        if (ok) ActionLog.Add("Plano de energia alterado", $"{previous.Name} → {target.Name}. Ponto de restauração: {restore}", "power", previous.Guid);
        _ = Host.RefreshHealthAsync();
        LoadPlans();
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

    // ---------- Suporte, relatório e otimização ----------
    private void Support_Click(object sender, RoutedEventArgs e) => AppInfo.OpenSupport();
    private void OpenData_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell(AppPaths.Dir);

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

    // ---------- Aviso flutuante (snackbar) ----------
    private readonly System.Windows.Threading.DispatcherTimer _snackTimer = new();
    private Action? _snackAction;

    /// <summary>Mostra o resultado de uma ação sem bloquear a tela. Some sozinho; Esc ou o X fecham antes.</summary>
    private void ShowSnack(string title, string text, string? actionLabel = null, Action? action = null, int seconds = 10)
    {
        SnackTitle.Text = title;
        SnackText.Text = text;
        SnackText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _snackAction = action;
        SnackAction.Content = actionLabel;
        SnackAction.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        Snack.Visibility = Visibility.Visible;
        _snackTimer.Stop();
        _snackTimer.Interval = TimeSpan.FromSeconds(seconds + text.Length / 25); // tempo de leitura cresce com o texto
        _snackTimer.Start();
    }

    private void HideSnack() { _snackTimer.Stop(); Snack.Visibility = Visibility.Collapsed; }
    private void SnackClose_Click(object sender, RoutedEventArgs e) => HideSnack();
    private void SnackAction_Click(object sender, RoutedEventArgs e) { var a = _snackAction; HideSnack(); a?.Invoke(); }

    // ---------- Barra lateral compacta em janelas estreitas ----------
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = ActualWidth < 960 * Math.Max(1, TextScale.Factor * 0.8);
        Nav.Tag = compact ? "compact" : null;
        NavPane.MinWidth = compact ? 0 : 200;
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

    // ---------- Busca nas tabelas (M24) ----------
    private static void ApplyFilter(DataGrid grid, string text, Func<object, string> key)
    {
        if (grid.ItemsSource is null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        string q = text.Trim();
        view.Filter = q.Length == 0 ? null : o => key(o).Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplySpaceFilter() => ApplyFilter(SpaceGrid, SpaceSearch.Text, o => o is FolderUsage f ? f.Name + " " + f.Path : "");
    private void ProcSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
    private void StartupSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
    private void SpaceSearch_Changed(object sender, TextChangedEventArgs e) => ApplySpaceFilter();

    // ---------- Detalhe do processo (A47) ----------
    private void ProcGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name);
    }

    // Mesmo detalhe do clique duplo, alcançável pelo teclado
    private void ProcDetails_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is ProcRow r) Dialogs.ShowProcessDetails(this, r.Name);
        else ProcMsg.Text = "Selecione um programa na lista.";
    }

    // ---------- Teste de conexão (A46) ----------
    private async void NetTest_Click(object sender, RoutedEventArgs e)
    {
        NetTestBtn.IsEnabled = false;
        NetTestResult.Text = "Testando… (uns 10 segundos)";
        try { NetTestResult.Text = string.Join("\n", await NetTest.RunAsync()); }
        finally { NetTestBtn.IsEnabled = true; }
    }

    public void ReloadSettings() { LoadSettingsIntoForm(); RefreshProfileCard(); }

    public void ExportReport() => Export_Click(this, new RoutedEventArgs());

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Latest is not { } s) return;
        var findings = Diagnosis.Evaluate(Host.Recent, Host.Health, Host.Memory.Suspects);
        string file = ReportExporter.Export(s, findings, Host.History.Load(TimeSpan.FromDays(7)));
        AppInfo.OpenShell(file);
    }

    // ---------- Energia e permissões ----------
    private List<PowerPlan> _plans = new();

    private async void LoadPlans()
    {
        _plans = await Task.Run(PowerPlans.List);
        PlanBox.ItemsSource = _plans;
        PlanBox.SelectedItem = _plans.FirstOrDefault(p => p.Active);
        bool hasHigh = _plans.Any(p => p.Name.Contains("desempenho", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("performance", StringComparison.OrdinalIgnoreCase));
        CreateHighBtn.Visibility = hasHigh ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ApplyPlan_Click(object sender, RoutedEventArgs e)
    {
        if (PlanBox.SelectedItem is not PowerPlan plan) return;
        await ActivatePlanAsync(plan.Guid);
    }

    private async void CreateHigh_Click(object sender, RoutedEventArgs e)
    {
        if (Simulate("Criar plano Alto desempenho", "Executaria \"powercfg /duplicatescheme\" a partir do modelo oficial. O plano não seria ativado.")) return;
        if (!Dialogs.Confirm(this, "Criar plano Alto desempenho?", "O Windows ganhará um novo plano de energia (cópia do modelo oficial Alto desempenho). Ele não é ativado automaticamente. Pode consumir mais bateria.", "Criar")) return;
        var plan = await Task.Run(PowerPlans.CreateHighPerformance);
        PlanMsg.Text = plan is null ? "O Windows não permitiu criar o plano neste computador." : $"Plano \"{plan.Name}\" criado.";
        if (plan is not null) ActionLog.Add("Plano de energia criado", plan.Name, "plan-created", plan.Guid);
        _ = Host.RefreshHealthAsync();
        LoadPlans();
    }

    private void RefreshAdminCard()
    {
        bool admin = AppInfo.IsAdmin;
        AdminText.Text = admin
            ? "O app está com permissão de administrador: a limpeza inclui as pastas temporárias do Windows e a otimização do SSD (TRIM, que avisa o disco sobre o espaço livre)."
            : "O app roda sem privilégios elevados, por segurança. Para limpar também as pastas temporárias do Windows e otimizar o SSD (TRIM, que avisa o disco sobre o espaço livre), reinicie como administrador. O Windows pedirá sua confirmação.";
        AdminBtn.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Confirm(this, "Reiniciar como administrador?", "O Notebook Doctor será fechado e aberto novamente com privilégios elevados. O Windows vai pedir sua confirmação.", "Reiniciar"))
            Host.RestartAsAdmin();
    }

    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.Items.Count - 1);

    // Fechar a janela só a esconde: o app continua na bandeja.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
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

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    // ---------- Processos ----------
    private void RefreshProcesses(Snapshot s)
    {
        var selected = (ProcGrid.SelectedItem as ProcRow)?.Name;
        var sort = ProcGrid.Items.SortDescriptions.ToList();
        ProcGrid.ItemsSource = s.Groups.OrderByDescending(g => g.CpuPct).ThenByDescending(g => g.MemMB).Select(g =>
        {
            var about = ProcessKnowledge.Describe(g.Name);
            return new ProcRow(g.Name, g.Count, g.CpuPct, g.MemMB, g.IoKBs, g.Conns, about.Description, about.SafetyText, ProcessIcons.Get(g.Name));
        }).ToList();
        ApplyFilter(ProcGrid, ProcSearch.Text, o => o is ProcRow r ? r.Name + " " + r.About : "");
        foreach (var d in sort) ProcGrid.Items.SortDescriptions.Add(d);
        if (selected is not null) ProcGrid.SelectedItem = ProcGrid.Items.OfType<ProcRow>().FirstOrDefault(g => g.Name == selected);
    }

    private void KillSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        KillProgram(g.Name, g.Count, g.MemMB);
    }

    // ---------- Prioridade manual (A18) ----------
    private void LowerPriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: true);
    private void RestorePriority_Click(object sender, RoutedEventArgs e) => SetPriority(lower: false);

    private void SetPriority(bool lower)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessTools.IsProtected(g.Name)) { ProcMsg.Text = $"{g.Name} é do sistema: a prioridade não é alterada."; return; }
        if (Simulate(lower ? $"Baixar a prioridade de {g.Name}" : $"Restaurar a prioridade de {g.Name}",
                     $"Mudaria {Pt.N(g.Count, "processo", "processos")} para prioridade {(lower ? "abaixo do normal" : "normal")}.")) return;
        int n = ProcessTools.SetPriority(g.Name, lower ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal);
        ProcMsg.Text = n > 0 ? $"{g.Name}: prioridade {(lower ? "reduzida" : "normal")} em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível alterar: falta permissão.";
        if (n > 0 && lower) ActionLog.Add($"Prioridade reduzida: {g.Name}", Pt.N(n, "processo", "processos"), "priority", g.Name);
    }

    // ---------- Limpeza ----------
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        CleanMsg.Text = "Analisando…";
        int days = Host.Settings.TempDays;
        var items = new System.Collections.ObjectModel.ObservableCollection<CleanItem>();
        CleanGrid.ItemsSource = items;
        void Header() => CleanHeader.Text = $"{Cleaner.FormatBytes(items.Where(i => i.Selected).Sum(i => i.Bytes))} marcados para limpeza · {Cleaner.FormatBytes(items.Sum(i => i.Bytes))} no total (temporários com mais de {days} dias; caches inteiros).";
        // Cada local aparece assim que é medido; caches grandes (npm, NuGet) chegam por último
        // O total "marcados para limpeza" acompanha cada caixa marcada ou desmarcada
        await Cleaner.ScanProgressiveAsync(days, item => Dispatcher.BeginInvoke(() => { item.PropertyChanged += (_, _) => Header(); items.Add(item); Header(); }));
        await Dispatcher.InvokeAsync(() => { }); // garante que os últimos itens já entraram na lista
        Header();
        CleanMsg.Text = "";
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        CleanGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (CleanGrid.ItemsSource is not IEnumerable<CleanItem> source || !source.Any(i => i.Selected)) { CleanMsg.Text = "Nada selecionado."; return; }
        var items = source.ToList();
        var chosen = items.Where(i => i.Selected).ToList();
        string list = string.Join("\n", chosen.Select(i => $"• {i.Label} ({i.SizeText})"));
        string extra = chosen.Any(i => i.IsRecycleBin) ? "\n\nA Lixeira será esvaziada DEFINITIVAMENTE." : "";
        if (Simulate("Limpar os itens marcados", $"Apagaria: {string.Join("; ", chosen.Select(i => $"{i.Label} ({i.SizeText})"))}.")) return;
        if (!Dialogs.Confirm(this, "Confirmar limpeza", $"Apagar os itens abaixo?\n\n{list}{extra}", "Apagar", danger: chosen.Any(i => i.IsRecycleBin))) return;

        CleanMsg.Text = "Limpando…";
        int days = Host.Settings.TempDays;
        long freed = await Task.Run(() => Cleaner.Clean(items, days));
        CleanMsg.Text = $"{Cleaner.FormatBytes(freed)} liberados. Arquivos em uso foram mantidos.";
        ActionLog.Add("Limpeza (seção Limpeza)", $"{Cleaner.FormatBytes(freed)} liberados: {string.Join(", ", chosen.Select(i => i.Label.Split('(')[0].Trim()))}");
        _ = Host.RefreshHealthAsync();
        Scan_Click(sender, e);
    }

    // ---------- Inicialização ----------
    private void RefreshStartup()
    {
        _startup = StartupManager.List();
        StartupGrid.ItemsSource = _startup;
        ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
        StartupMsg.Text = $"{_startup.Count(x => x.Enabled)} ativos de {_startup.Count}";
    }

    private void RefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = "Selecione um item."; return; }
        try
        {
            bool before = entry.Enabled;
            if (Simulate($"{(before ? "Desativar" : "Ativar")} {entry.Name} na inicialização", $"Mudaria o registro StartupApproved de {entry.Name}. O programa não seria apagado.")) return;
            string restore = await RestorePoint.EnsureAsync(Host.Settings, "Programas de inicializacao");
            StartupManager.SetEnabled(entry, !before);
            StartupGrid.Items.Refresh();
            StartupMsg.Text = $"{entry.Name}: {entry.State.ToLower()}. Vale a partir do próximo login (reversível aqui).";
            ActionLog.Add($"Inicialização: {entry.Name} {(before ? "desativado" : "ativado")}", $"Ponto de restauração: {restore}", "startup", entry.Name, $"{StartupManager.SubKeyOf(entry)}|{before}");
            _ = Host.RefreshHealthAsync();
        }
        catch (Exception ex) { StartupMsg.Text = "Falhou: " + ex.Message; }
    }

    // ---------- Ações (registro + desfazer) ----------
    private void LoadActions() => ActionsGrid.ItemsSource = ActionLog.Load().OrderByDescending(a => a.Time).ToList();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionEntry entry) { ActionsMsg.Text = "Selecione uma ação na lista."; return; }
        if (!entry.CanUndo) { ActionsMsg.Text = entry.Undone ? "Essa ação já foi desfeita." : "Essa ação é definitiva e não pode ser desfeita."; return; }
        if (Simulate($"Desfazer: {entry.Title}", entry.Detail)) return;
        ActionsMsg.Text = ActionLog.Undo(entry);
        ActionLog.Add($"Desfeito: {entry.Title}", entry.Detail);
        RefreshStartup();
        LoadActions();
        _ = Host.RefreshHealthAsync();
    }

    // ---------- Histórico ----------
    private async void LoadHistory()
    {
        var window = PeriodBox.SelectedIndex switch { 1 => TimeSpan.FromDays(7), 2 => TimeSpan.FromDays(30), _ => TimeSpan.FromHours(24) };
        var data = await Task.Run(() => Host.History.Load(window));
        _histData = data;
        LoadAlerts(window);
        FillProcTimeline(data);
        if (data.Count < 2)
        {
            HistInfo.Text = "Ainda sem dados suficientes: o app grava uma amostra por minuto.";
            HCpu.SetValues(Array.Empty<double>()); HRam.SetValues(Array.Empty<double>()); HFree.SetValues(Array.Empty<double>()); HScore.SetValues(Array.Empty<double>());
            HOffenders.Text = "–";
            return;
        }
        HistInfo.Text = $"{data.Count} amostras";
        var times = HistoryStore.DownsampleTimes(data.Select(d => d.T).ToList(), 300);
        HCpu.SetValues(HistoryStore.Downsample(data.Select(d => d.Cpu).ToList(), 300), times);
        HRam.SetValues(HistoryStore.Downsample(data.Select(d => d.Ram).ToList(), 300), times);
        HFree.SetValues(HistoryStore.Downsample(data.Select(d => d.FreePct).ToList(), 300), times);
        HCpuLabel.Text = $"CPU · média {data.Average(d => d.Cpu):0}% · pico {data.Max(d => d.Cpu):0}%";
        HRamLabel.Text = $"RAM · média {data.Average(d => d.Ram):0}% · pico {data.Max(d => d.Ram):0}%";
        HDiskLabel.Text = $"Espaço livre no disco · agora {data[^1].FreePct:0}%";
        var scores = data.Where(d => d.Score >= 0).Select(d => (double)d.Score).ToList();
        HScore.SetValues(HistoryStore.Downsample(scores, 300));
        HScoreLabel.Text = scores.Count > 1 ? $"Nota de saúde · média {scores.Average():0} · mínima {scores.Min():0}" : "Nota de saúde · coletando dados";

        var heavy = data.Where(d => d.Cpu >= 50 || d.Ram >= 80).ToList();
        var basis = heavy.Count >= 5 ? heavy : data;
        var ranking = basis.SelectMany(d => d.Top).GroupBy(n => n)
            .Select(g => (Name: g.Key, Pct: g.Count() * 100.0 / basis.Count)).OrderByDescending(x => x.Pct).Take(6);
        string head = heavy.Count >= 5 ? $"Nos {heavy.Count} momentos de carga alta (CPU ≥ 50% ou RAM ≥ 80%):\n" : "Nenhum período de carga alta registrado; ranking geral:\n";
        HOffenders.Text = head + string.Join("\n", ranking.Select(r => $"• {r.Name} — entre os 3 maiores em {r.Pct:0}% das amostras"));
    }

    // ---------- Histórico: CSV (M06), linha do tempo por programa (M05) e avisos (M46) ----------
    private List<HistSample> _histData = new();

    private string PeriodLabel => PeriodBox.SelectedIndex switch { 1 => "7-dias", 2 => "30-dias", _ => "24-horas" };

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_histData.Count == 0) { HistInfo.Text = "Ainda não há dados no período para exportar."; return; }
        try
        {
            string file = HistoryCsv.Export(_histData, PeriodLabel);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
            ShowSnack("Histórico exportado", $"{Pt.N(_histData.Count, "amostra", "amostras")} em {System.IO.Path.GetFileName(file)}. O Explorador abriu na pasta do arquivo.");
        }
        catch (Exception ex) { ShowSnack("Não foi possível exportar", ex.Message); }
    }

    private void LoadAlerts(TimeSpan window)
    {
        var alerts = AlertLog.Load(window);
        AlertsGrid.ItemsSource = alerts;
        AlertsGrid.Visibility = alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsEmpty.Visibility = alerts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FillProcTimeline(List<HistSample> data)
    {
        string? keep = ProcTimelineBox.SelectedItem as string;
        // Programas do período, dos que mais pesaram para os menos (CPU somada + memória, 1 GB ≈ 10 pontos de CPU)
        var names = data.Where(d => d.P is not null).SelectMany(d => d.P!)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Weight: g.Sum(kv => kv.Value[0] + kv.Value[1] / 100)))
            .Where(x => x.Weight > 0 && !ProcessTools.IsSystem(x.Name) || x.Weight > 50)
            .OrderByDescending(x => x.Weight).Select(x => x.Name).Take(60).ToList();
        ProcTimelineBox.ItemsSource = names;
        if (names.Count == 0)
        {
            ProcTimelineInfo.Text = "Os dados por programa começam a ser gravados nesta versão (uma amostra por minuto).";
            PCpu.SetValues(Array.Empty<double>()); PRam.SetValues(Array.Empty<double>());
            return;
        }
        ProcTimelineBox.SelectedItem = keep is not null && names.Contains(keep) ? keep : names[0];
    }

    private void ProcTimeline_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProcTimelineBox.SelectedItem is not string name) return;
        var rows = _histData.Where(d => d.P is not null).ToList();
        // Ausente na amostra = não estava entre os mais pesados (fica como zero)
        double[] Val(HistSample d, int i) => d.P!.TryGetValue(name, out var v) ? v : [0, 0];
        var cpu = rows.Select(d => Val(d, 0)[0]).ToList();
        var ram = rows.Select(d => Val(d, 0)[1]).ToList();
        var times = HistoryStore.DownsampleTimes(rows.Select(d => d.T).ToList(), 300);
        PCpu.SetValues(HistoryStore.Downsample(cpu, 300), times);
        PRam.Max = Math.Max(100, ram.DefaultIfEmpty(0).Max() * 1.1);
        PRam.SetValues(HistoryStore.Downsample(ram, 300), times);
        int present = rows.Count(d => d.P!.ContainsKey(name));
        PCpuLabel.Text = $"CPU · média {cpu.DefaultIfEmpty(0).Average():0.0}% · pico {cpu.DefaultIfEmpty(0).Max():0}%";
        PRamLabel.Text = $"Memória · pico {ram.DefaultIfEmpty(0).Max():N0} MB";
        ProcTimelineInfo.Text = rows.Count == 0 ? "" : $"Entre os mais pesados em {present * 100.0 / rows.Count:0}% das amostras";
    }

    // ---------- Exceções de avisos (M20) a partir da tabela de processos ----------
    private void AlertException_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        var s = Host.Settings;
        if (s.IsAlertException(g.Name)) { ProcMsg.Text = $"{g.Name} já está nas exceções."; return; }
        s.AlertExceptions = AppSettings.ParseNames(string.Join("\n", s.AlertExceptions.Append(g.Name)));
        s.Save();
        LoadSettingsIntoForm();
        if (Host.HeavyAlert?.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase) == true) Host.IgnoreHeavy(g.Name);
        ActionLog.Add($"Sem avisos: {g.Name}", "Adicionado às exceções de avisos (Configurações › Exceções).");
        ShowSnack($"{g.Name} não gera mais avisos", "Ele continua sendo medido e aparece normalmente nas tabelas.",
                  "Desfazer", () =>
                  {
                      s.AlertExceptions.RemoveAll(n => n.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                      s.Save();
                      LoadSettingsIntoForm();
                  });
    }

    // ---------- Pacote para o suporte (M17) ----------
    private async void SupportPackage_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.IsEnabled = false;
        try
        {
            var snap = Host.Latest;
            var findings = Diagnosis.Evaluate(Host.Recent, Host.Health, Host.Memory.Suspects);
            var health = Host.Health;
            string zip = await Task.Run(() => SupportPackage.Create(snap, findings, health, Host.History.Load(TimeSpan.FromDays(7))));
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\"");
            ActionLog.Add("Pacote para o suporte gerado", System.IO.Path.GetFileName(zip));
            ShowSnack("Pacote criado", "Anexe o arquivo .zip selecionado no atendimento. Ele não leva seu nome de usuário nem o nome do computador.",
                      "Abrir suporte", AppInfo.OpenSupport, seconds: 15);
        }
        catch (Exception ex) { ShowSnack("Não foi possível criar o pacote", ex.Message); }
        finally { btn.IsEnabled = true; }
    }

    // ---------- Atualização (A50) ----------
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        UpdateMsg.Text = "Procurando…";
        try
        {
            var (result, info) = await Updater.CheckAsync();
            Host.Settings.LastUpdateCheck = DateTime.Now;
            Host.Settings.Save();
            Host.SetPendingUpdate(result == UpdateCheck.Available ? info : null);
            UpdateMsg.Text = result switch
            {
                UpdateCheck.UpToDate => $"Você já tem a versão mais recente ({AppInfo.Version}).",
                UpdateCheck.NotPublished => "Nenhuma atualização publicada pela Klivfy no momento.",
                UpdateCheck.Offline => "Sem conexão com klivfy.com. Tente de novo mais tarde.",
                _ => "",
            };
            if (result == UpdateCheck.Available) OfferUpdate();
        }
        finally { UpdateBtn.IsEnabled = true; }
    }

    /// <summary>Mostra a versão nova e, com confirmação, baixa, confere a assinatura e abre o instalador.</summary>
    public void OfferUpdate()
    {
        if (Host.PendingUpdate is not { } info) return;
        UpdateMsg.Text = $"Versão {info.Version} disponível. {info.Notes}".Trim();
        ShowSnack($"Versão {info.Version} disponível", info.Notes, "Instalar", () => _ = InstallUpdateAsync(info), seconds: 20);
    }

    private async Task InstallUpdateAsync(UpdateInfo info)
    {
        if (!Dialogs.Confirm(this, $"Instalar a versão {info.Version}?",
                "O instalador será baixado de klivfy.com e conferido (hash e assinatura digital da Klivfy). Depois, o Notebook Doctor fecha e o instalador abre. Suas configurações e o histórico são mantidos.",
                "Baixar e instalar")) return;
        UpdateBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p => UpdateMsg.Text = $"Baixando… {p}%");
            string file = await Updater.DownloadAndVerifyAsync(info, progress);
            UpdateMsg.Text = "Instalador conferido. Abrindo…";
            ActionLog.Add($"Atualização para {info.Version}", "Instalador verificado (SHA-256 e assinatura Klivfy).");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            Host.QuitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateMsg.Text = "Atualização cancelada: " + ex.Message;
            ShowSnack("Atualização não instalada", ex.Message);
        }
        finally { UpdateBtn.IsEnabled = true; }
    }

    // ---------- M15: explicações abertas no diagnóstico ----------
    private readonly HashSet<string> _whyOpen = new();

    private void WhyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { DataContext: Finding f } t) return;
        if (t.IsChecked == true) _whyOpen.Add(f.Title); else _whyOpen.Remove(f.Title);
        f.WhyOpen = t.IsChecked == true;
    }

    // ---------- A38: modo simulação ----------
    /// <summary>Com a simulação ligada, mostra o que seria feito e devolve true (quem chamou não faz nada).</summary>
    private bool Simulate(string action, string details)
    {
        if (!Host.Settings.SimulationMode) return false;
        ShowSnack($"Simulação: {action}", details + " Nada foi alterado.", "Desligar simulação", () => SetSimulation(false), seconds: 15);
        return true;
    }

    private void SetSimulation(bool on)
    {
        Host.Settings.SimulationMode = on;
        Host.Settings.Save();
        SimMode.IsChecked = on;
        SimChip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SimMode_Click(object sender, RoutedEventArgs e) => SetSimulation(SimMode.IsChecked == true);
    private void SimChip_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = TabTweaks;

    // ---------- Ajustes do Windows (A31–A35) ----------
    private async void LoadTweaks()
    {
        SimMode.IsChecked = Host.Settings.SimulationMode;
        var s = Host.Latest;
        TweaksList.ItemsSource = await Task.Run(() => WindowsTweaks.Read(s?.RamTotalGB ?? 8, s?.DiskFreePct ?? 50));
    }

    private void TweakSimulate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Tweak t)
            ShowSnack($"Simulação: {t.ActionLabel}", t.Preview + " Nada foi alterado.", seconds: 15);
    }

    private async void Tweak_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Tweak t) return;
        if (t.NeedsAdmin && !AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, "Precisa de administrador", $"\"{t.Title}\" muda uma configuração do sistema. Reiniciar o Notebook Doctor como administrador agora? O Windows vai pedir sua confirmação.", "Reiniciar"))
                Host.RestartAsAdmin();
            return;
        }
        if (Simulate(t.ActionLabel, t.Preview)) return;
        if (!Dialogs.Confirm(this, $"{t.ActionLabel}?", $"{t.Preview}\n\nPode ser desfeito depois na seção Ações.", t.ActionLabel)) return;
        var btn = (Button)sender;
        btn.IsEnabled = false;
        try
        {
            string restore = t.NeedsAdmin ? await RestorePoint.EnsureAsync(Host.Settings, $"Ajuste: {t.Title}") : "não se aplica";
            var (msg, prev) = await WindowsTweaks.ApplyAsync(t);
            ActionLog.Add($"{t.Title}: {t.ActionLabel}", $"{msg} Ponto de restauração: {restore}", "tweak", t.Id, prev);
            ShowSnack(msg, t.Id == "pagefile" ? "Reinicie o computador para valer." : "", "Ver em Ações", () => Tabs.SelectedIndex = TabActions);
            _ = Host.RefreshHealthAsync();
        }
        catch (UnauthorizedAccessException) { ShowSnack("O Windows recusou a mudança", "Abra o app como administrador e tente de novo."); }
        catch (Exception ex) { ShowSnack("Ajuste não aplicado", ex.Message); }
        finally { btn.IsEnabled = true; LoadTweaks(); }
    }

    // ---------- A19: modo eficiência ----------
    private void Efficiency_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcRow g) { ProcMsg.Text = "Selecione um programa na lista."; return; }
        if (ProcessTools.IsProtected(g.Name)) { ProcMsg.Text = $"{g.Name} é protegido: o modo eficiência não é aplicado."; return; }
        if (Simulate($"Modo eficiência em {g.Name}", $"Ligaria o modo econômico do Windows (EcoQoS) e a prioridade baixa em {Pt.N(g.Count, "processo", "processos")}. O programa continuaria aberto.")) return;
        int n = ProcessTools.SetEfficiency(g.Name, on: true);
        ProcMsg.Text = n > 0 ? $"{g.Name}: modo eficiência em {Pt.N(n, "processo", "processos")}. Vale até o programa ser reaberto." : "Não foi possível aplicar: falta permissão.";
        if (n > 0) ActionLog.Add($"Modo eficiência: {g.Name}", Pt.N(n, "processo", "processos"), "efficiency", g.Name);
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && e.Source == PeriodBox) LoadHistory();
    }

    // ---------- Configurações ----------
    private void LoadSettingsIntoForm()
    {
        var s = Host.Settings;
        CfgTheme.SelectedIndex = s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
        CfgStart.IsChecked = s.StartWithWindows;
        CfgAlerts.IsChecked = s.AlertsEnabled;
        CfgToasts.IsChecked = s.NativeNotifications;
        CfgWeekly.IsChecked = s.WeeklyCleanup;
        CfgRestore.IsChecked = s.RestorePointBeforeChanges;
        CfgMini.IsChecked = s.MiniWidget;
        CfgUpdates.IsChecked = s.CheckUpdates;
        CfgHotkey.IsChecked = s.GlobalHotkey;
        CfgExceptions.Text = string.Join(Environment.NewLine, s.AlertExceptions);
        CfgProtected.Text = string.Join(Environment.NewLine, s.ExtraProtected);
        CfgDays.Text = s.TempDays.ToString();
        CfgCpu.Text = s.CpuAlertPct.ToString();
        CfgRam.Text = s.RamAlertPct.ToString();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = Host.Settings;
        s.ThemeMode = (CfgTheme.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        s.StartWithWindows = CfgStart.IsChecked == true;
        s.AlertsEnabled = CfgAlerts.IsChecked == true;
        s.NativeNotifications = CfgToasts.IsChecked == true;
        s.WeeklyCleanup = CfgWeekly.IsChecked == true;
        s.RestorePointBeforeChanges = CfgRestore.IsChecked == true;
        s.CheckUpdates = CfgUpdates.IsChecked == true;
        s.GlobalHotkey = CfgHotkey.IsChecked == true;
        s.AlertExceptions = AppSettings.ParseNames(CfgExceptions.Text);
        // Os do Windows já são protegidos; a lista do usuário guarda só os extras
        s.ExtraProtected = AppSettings.ParseNames(CfgProtected.Text).Where(n => !ProcessTools.IsSystem(n)).ToList();
        ProcessTools.SetUserProtected(s.ExtraProtected);
        s.TempDays = ParseOr(CfgDays.Text, s.TempDays, 1, 365);
        s.CpuAlertPct = ParseOr(CfgCpu.Text, s.CpuAlertPct, 30, 100);
        s.RamAlertPct = ParseOr(CfgRam.Text, s.RamAlertPct, 30, 100);
        s.Save();
        s.ApplyStartWithWindows();
        Host.ApplySettingsChanged();
        Host.SetMiniWidget(CfgMini.IsChecked == true);
        bool hotkeyOk = Host.ApplyHotkey();
        LoadSettingsIntoForm();
        CfgMsg.Text = hotkeyOk ? "Salvo." : $"Salvo. O atalho {GlobalHotkey.Label} já é usado por outro programa, então ficou desligado.";
    }

    private static int ParseOr(string text, int fallback, int min, int max) =>
        int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : fallback;

    // ---------- Abas ----------
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return; // ignora seleções de DataGrid/ComboBox internos
        switch (Tabs.SelectedIndex)
        {
            case TabProcs when Host.Latest is { } s: RefreshProcesses(s); break;
            case TabClean when CleanGrid.ItemsSource is null: Scan_Click(this, new RoutedEventArgs()); break; // a análise só lê, então roda ao abrir
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); break;
            case TabSettings: CfgMsg.Text = ""; LoadPlans(); break;
        }
        Host.Settings.LastTab = Tabs.SelectedIndex;
        Host.Settings.Save();
    }
}
