using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record ProcRow(string Name, int Count, double CpuPct, long MemMB, double IoKBs, int Conns, string About, string SafetyText,
                             System.Windows.Media.ImageSource? Icon = null, string Severity = "", IReadOnlyList<string>? Members = null)
{
    /// <summary>M22: processos que a linha representa (vários quando o aplicativo foi agrupado).</summary>
    public IReadOnlyList<string> Names => Members ?? [Name];
    public bool Merged => Names.Count > 1;
    /// <summary>Basta um processo protegido para a linha toda ficar protegida.</summary>
    public bool IsProtected => Names.Any(ProcessTools.IsProtected);
    public bool HasIcon => Icon is not null;
    /// <summary>B32: lido pelo Narrador junto com o nome ("uso alto").</summary>
    public string SeverityText => RowSeverity.Spoken(Severity);
}

public partial class MainWindow : Window
{
    private static App Host => App.Current;

    private const int TabProcs = 1, TabClean = 2, TabSpace = 3, TabStartup = 4, TabHistory = 5, TabActions = 6, TabTweaks = 7, TabSettings = 8;

    public MainWindow()
    {
        InitializeComponent();
        Localizer.ApplyTree(this); // M13: antes de o GridMemory ler os títulos das colunas
        RestorePlacement();
        foreach (var grid in Grids()) GridMemory.Attach(grid, Host.Settings, grid == CleanGrid ? 2 : 1); // Limpeza: a caixa de marcar e o local
        // B33: a lista muda (dados novos, busca, ordenação) e o rodapé acompanha
        foreach (var grid in new[] { ProcGrid, SpaceGrid, StartupGrid })
            ((System.Collections.Specialized.INotifyCollectionChanged)grid.Items).CollectionChanged += (_, _) => UpdateTotals(grid);
        FooterText.Text = AppInfo.Copyright;
        AboutText.Text = $"Notebook Doctor {AppInfo.Version}\nSuporte: klivfy.com/suporte"
                         + (AppPaths.Portable ? $"\nModo portátil: dados em {AppPaths.Dir}" : "");
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
        if (AppPaths.Demo)
        {
            // B26: deixa claro, na janela e na barra de tarefas, que os números não são deste computador
            Title += " · demonstração";
            Loaded += (_, _) => ShowSnack("Modo demonstração", "Painel, Processos e Histórico mostram dados fictícios. A simulação está ligada: nenhuma ação altera este computador.", seconds: 20);
        }
        // O "Tamanho do texto" do Windows pode mudar com o app escondido na bandeja
        IsVisibleChanged += (_, _) => { if (IsVisible) TextScale.Apply(); };
        _snackTimer.Tick += (_, _) => HideSnack();

        // Atalhos: Ctrl+1…8 trocam de aba; F5 atualiza a aba atual
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9) { Tabs.SelectedIndex = e.Key - Key.D1; e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshCurrentTab(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F && SearchBoxOfCurrentTab() is { } box) { box.Focus(); box.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && Snack.IsVisible) { HideSnack(); e.Handled = true; }
        };
    }

    /// <summary>M12: Ctrl+F vai para a busca da aba atual (Processos, Espaço e Inicialização têm uma).</summary>
    private TextBox? SearchBoxOfCurrentTab() => Tabs.SelectedIndex switch
    {
        TabProcs => ProcSearch,
        TabSpace => SpaceSearch,
        TabStartup => StartupSearch,
        _ => null,
    };

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
        int n = ProcessActions.Kill(name);
        string done = n == 1 ? $"1 processo de {name} encerrado." : $"{n} processos de {name} encerrados.";
        ProcMsg.Text = n > 0 ? done : "Não foi possível encerrar: falta permissão ou o programa já fechou.";
        if (n > 0) { ActionLog.Add("Programa encerrado", $"{name} ({Pt.N(n, "processo", "processos")}, {memMB:N0} MB)"); ShowSnack(done, $"{memMB:N0} MB de memória liberados."); }
        RefreshHeavyBanner();
    }

    // ---------- Suporte (botões no cabeçalho e em Configurações) ----------
    private void Support_Click(object sender, RoutedEventArgs e) => AppInfo.OpenSupport();
    private void OpenData_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell(AppPaths.Dir);

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
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyNavDensity();

    /// <summary>Menu só com ícones na janela estreita ou no modo compacto (B15).</summary>
    public void ApplyNavDensity()
    {
        bool icons = Density.NavIconsOnly(ActualWidth, TextScale.Factor, Host.Settings.Compact);
        Nav.Tag = icons ? "compact" : null;
        NavPane.MinWidth = icons ? 0 : 200;
    }

    // ---------- Busca nas tabelas (M24) ----------
    private static void ApplyFilter(DataGrid grid, string text, Func<object, string> key)
    {
        if (grid.ItemsSource is null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        string q = text.Trim();
        view.Filter = q.Length == 0 ? null : o => key(o).Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.Items.Count - 1);

    // Fechar a janela só a esconde: o app continua na bandeja.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Host.Quitting) return; // encerrando o app: fecha de fato
        e.Cancel = true;
        if (!Host.Settings.CloseExits && !Host.Settings.TrayHintShown)
        {
            // B49: na primeira vez, explica para onde o app foi
            Host.Settings.TrayHintShown = true;
            Host.Notify("O Notebook Doctor continua na bandeja", "Ele segue de olho no desempenho. Clique no ícone perto do relógio para abrir; para sair, use o menu desse ícone.");
        }
        // B10: encerra depois que este Closing terminar (fechar de dentro dele é reentrante); ExitApp salva a posição
        if (Host.Settings.CloseExits) { Dispatcher.BeginInvoke(Host.ExitApp); return; }
        SavePlacement();
        Hide();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    // ---------- B07/B45: reabrir no mesmo lugar e no mesmo monitor ----------
    private void RestorePlacement()
    {
        var s = Host.Settings;
        if (s.WinLeft is not { } x || s.WinTop is not { } y || s.WinWidth is not { } w || s.WinHeight is not { } h) return;
        if (WindowPlacement.Fit(new Box(x, y, w, h), WorkAreas(), MinWidth, MinHeight) is not { } fit) return; // monitor sumiu: centraliza
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (fit.X, fit.Y, fit.Width, fit.Height);
        if (s.WinMaximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Área útil de cada monitor (sem a barra de tarefas), convertida para as unidades do WPF.</summary>
    private static List<Box> WorkAreas()
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        double scale = primary is null || SystemParameters.PrimaryScreenWidth <= 0 ? 1 : primary.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        return System.Windows.Forms.Screen.AllScreens
            .Select(sc => new Box(sc.WorkingArea.X / scale, sc.WorkingArea.Y / scale, sc.WorkingArea.Width / scale, sc.WorkingArea.Height / scale))
            .ToList();
    }

    private void UpdateTotals(DataGrid grid)
    {
        int total = (grid.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
        if (grid == ProcGrid)
            ProcTotal.Text = total == 0 ? "" : TableTotals.Processes(ProcGrid.Items.OfType<ProcRow>().Select(r => (r.Count, r.CpuPct, r.MemMB)).ToList(), total);
        else if (grid == SpaceGrid)
            SpaceTotal.Text = total == 0 ? "" : TableTotals.Space(SpaceGrid.Items.OfType<FolderUsage>().Select(f => f.Bytes).ToList(), total);
        else if (grid == StartupGrid)
        {
            var visible = StartupGrid.Items.OfType<StartupEntry>().ToList();
            StartupTotal.Text = total == 0 ? "" : TableTotals.Startup(visible.Count(x => x.Enabled), visible.Count, total);
        }
    }

    /// <summary>B13: som discreto ao terminar uma limpeza, se ligado em Configurações (o mesmo aviso suave do Windows).</summary>
    private void PlayDoneSound()
    {
        if (Host.Settings.PlaySounds) System.Media.SystemSounds.Asterisk.Play();
    }

    private DataGrid[] Grids() => [ProcGrid, CleanGrid, SpaceGrid, StartupGrid, AlertsGrid, ActionsGrid, ServicesGrid, TasksGrid, ExtensionsGrid, EventLogsGrid];

    public void SavePlacement()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized && RestoreBounds.IsEmpty) return;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (r.IsEmpty || r.Width <= 0) return;
        var s = Host.Settings;
        GridMemory.SaveWidths(Grids(), s);
        (s.WinLeft, s.WinTop, s.WinWidth, s.WinHeight) = (r.Left, r.Top, r.Width, r.Height);
        s.WinMaximized = WindowState == WindowState.Maximized;
        s.Save();
    }

    // ---------- A36: ponto de restauração antes de mudar o sistema ----------
    private bool _protectionWarned;

    /// <summary>
    /// Tenta criar o ponto. Se a Proteção do Sistema estiver desligada, avisa uma vez por sessão por notificação
    /// (o aviso flutuante seria coberto pela confirmação da própria mudança).
    /// </summary>
    private async Task<string> EnsureRestorePointAsync(string reason)
    {
        string result = await RestorePoint.EnsureAsync(Host.Settings, reason);
        if (result == RestorePoint.ProtectionOff && !_protectionWarned)
        {
            _protectionWarned = true;
            Host.Notify("Ponto de restauração não criado", "A Proteção do Sistema parece desligada. A mudança foi feita e pode ser desfeita na aba Ações.",
                        ("Abrir Proteção do Sistema", "protection"));
        }
        return result;
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
        if (!on && AppPaths.Demo)
        {
            SimMode.IsChecked = true;
            ShowSnack("Modo demonstração", "Aqui a simulação fica sempre ligada: nenhuma ação altera este computador.");
            return;
        }
        if (!on && !Host.EnsureUnlocked("desligar o modo simulação")) { SimMode.IsChecked = true; return; } // B38
        Host.Settings.SimulationMode = on;
        Host.Settings.Save();
        SimMode.IsChecked = on;
        SimChip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SimMode_Click(object sender, RoutedEventArgs e) => SetSimulation(SimMode.IsChecked == true);
    private void SimChip_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = TabTweaks;

    // ---------- Abas ----------
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return; // ignora seleções de DataGrid/ComboBox internos
        Motion.EnterContent((Tabs.SelectedItem as TabItem)?.Content as FrameworkElement);
        switch (Tabs.SelectedIndex)
        {
            case TabProcs when Host.Latest is { } s: RefreshProcesses(s); break;
            case TabClean when CleanGrid.ItemsSource is null: Scan_Click(this, new RoutedEventArgs()); break; // a análise só lê, então roda ao abrir
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); break;
            case TabSettings: CfgMsg.Text = ""; LoadPlans(); RefreshLockCard(); break;
        }
        Host.Settings.LastTab = Tabs.SelectedIndex;
        Host.Settings.Save();
    }
}
