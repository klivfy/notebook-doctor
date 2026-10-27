using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record ProcRow(string Name, int Count, double CpuPct, long MemMB, double IoKBs, int Conns, string About, string SafetyText,
                             System.Windows.Media.ImageSource? Icon = null, string Severity = "", IReadOnlyList<string>? Members = null,
                             bool Hung = false, bool HasWindow = false, bool Pinned = false, Safety Safety = Safety.Unknown)
{
    /// <summary>M22: processos que a linha representa (vários quando o aplicativo foi agrupado).</summary>
    public IReadOnlyList<string> Names => Members ?? [Name];
    public bool Merged => Names.Count > 1;
    /// <summary>Basta um processo protegido para a linha toda ficar protegida.</summary>
    public bool IsProtected => Names.Any(ProcessTools.IsProtected);
    public bool HasIcon => Icon is not null;
    /// <summary>B32: lido pelo Narrador junto com o nome ("uso alto").</summary>
    public string SeverityText => string.Join(", ", new[] { Hung ? Loc.T("não está respondendo") : "", Pinned ? Loc.T("fixado no topo") : "", RowSeverity.Spoken(Severity) }
        .Where(t => t.Length > 0));
}

public partial class MainWindow : Window
{
    private static App Host => App.Current;

    private const int TabProcs = AppTabs.Processes, TabClean = AppTabs.Clean, TabSpace = AppTabs.Space, TabPrograms = AppTabs.Programs,
        TabStartup = AppTabs.Startup, TabHistory = AppTabs.History, TabActions = AppTabs.Actions, TabTweaks = AppTabs.Tweaks, TabSystem = AppTabs.System, TabSettings = AppTabs.Settings;

    public MainWindow()
    {
        InitializeComponent();
        Localizer.ApplyTree(this); // M13: antes de o GridMemory ler os títulos das colunas
        // Rótulos curtos do trilho de navegação: propriedade própria, fora da tradução automática
        foreach (var item in Nav.Items.OfType<ListBoxItem>())
            if (NavLabel.GetShort(item) is { } shortLabel) NavLabel.SetShort(item, Loc.T(shortLabel));
        RestorePlacement();
        foreach (var grid in Grids()) GridMemory.Attach(grid, Host.Settings, grid == CleanGrid ? 2 : 1); // Limpeza: a caixa de marcar e o local
        // B33: a lista muda (dados novos, busca, ordenação) e o rodapé acompanha
        foreach (var grid in new[] { ProcGrid, SpaceGrid, ProgGrid, StartupGrid })
            ((System.Collections.Specialized.INotifyCollectionChanged)grid.Items).CollectionChanged += (_, _) => UpdateTotals(grid);
        AboutCopyright.Text = AppInfo.Copyright;
        AboutText.Text = Loc.F("Notebook Doctor {0}\nSuporte: klivfy.com/suporte", AppInfo.Version)
                         + (AppPaths.Portable ? "\n" + Loc.F("Modo portátil: dados em {0}", AppPaths.Dir) : "");
        RefreshAdminCard();
        LoadSettingsIntoForm();
        RefreshProfileCard();
        Host.SnapshotReady += OnSnapshot;
        Host.PowerSourceChanged += onAc => Dispatcher.Invoke(() => OnPowerSourceChanged(onAc));
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
            Loaded += (_, _) => ShowSnack("Modo demonstração", "Início, Processos e Histórico mostram dados fictícios. A simulação está ligada: nenhuma ação altera este computador.", seconds: 20);
        }
        // O "Tamanho do texto" do Windows pode mudar com o app escondido na bandeja
        IsVisibleChanged += (_, _) => { if (IsVisible) TextScale.Apply(); };
        _snackTimer.Tick += (_, _) => HideSnack();
        // U6 (WCAG 2.2.1): com o mouse ou o foco no aviso, a contagem para; ao sair, recomeça do início
        Snack.MouseEnter += (_, _) => _snackTimer.Stop();
        Snack.MouseLeave += (_, _) => RestartSnackTimer();
        Snack.IsKeyboardFocusWithinChanged += (_, e) => { if ((bool)e.NewValue) _snackTimer.Stop(); else RestartSnackTimer(); };

        // Atalhos: Ctrl+1…9 trocam de aba e Ctrl+0 abre Configurações; F5 atualiza a aba atual
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D0 and <= Key.D9) { Tabs.SelectedIndex = e.Key == Key.D0 ? TabSettings : e.Key - Key.D1; e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshCurrentTab(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F && SearchBoxOfCurrentTab() is { } box) { box.Focus(); box.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && Snack.IsVisible) { HideSnack(); e.Handled = true; }
        };
    }

    /// <summary>M12: Ctrl+F vai para a busca da aba atual (Processos, Espaço, Programas e Inicialização têm uma).</summary>
    private TextBox? SearchBoxOfCurrentTab() => Tabs.SelectedIndex switch
    {
        TabProcs => ProcSearch,
        TabSpace => SpaceSearch,
        TabPrograms => ProgSearch,
        TabStartup => StartupSearch,
        _ => null,
    };

    private void RefreshCurrentTab()
    {
        switch (Tabs.SelectedIndex)
        {
            case 0: _ = Host.RefreshHealthAsync(); break;
            case TabClean: Scan_Click(this, new RoutedEventArgs()); break;
            case TabPrograms: _ = LoadProgramsAsync(); break;
            case TabStartup: _ = LoadBatteryWeekAsync(); break;
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); RefreshEnergy(); break;
            case TabSettings: LoadPlans(); break;
        }
    }

    /// <summary>Confirma, encerra e registra. Encerrar é definitivo: trabalho não salvo se perde.</summary>
    private void KillProgram(string name, int count, long memMB)
    {
        if (ProcessTools.IsProtected(name))
        {
            ShowSnack("Processo do sistema", Loc.F("\"{0}\" faz parte do Windows e não pode ser encerrado por aqui.", name));
            return;
        }
        var about = ProcessKnowledge.Describe(name);
        if (Simulate(Loc.F("Encerrar {0}", name), Loc.F("Fecharia {0} de {1} ({2:N0} MB). Trabalho não salvo nele seria perdido.", Pt.N(count, "processo", "processos"), name, memMB))) return;
        if (!Dialogs.Confirm(this, "Encerrar programa?",
                Loc.F("Encerrar \"{0}\" ({1}, {2:N0} MB)?", name, Pt.N(count, "processo", "processos"), memMB) + "\n\n" + Loc.F("O que é: {0}.", about.Description) + "\n" + Loc.F("Seguro encerrar: {0}.", about.SafetyText) + "\n\n" + Loc.T("Trabalho não salvo nesse programa será perdido."),
                "Encerrar", danger: true)) return;
        int n = ProcessActions.Kill(name);
        string done = n == 1 ? Loc.F("1 processo de {0} encerrado.", name) : Loc.F("{0} processos de {1} encerrados.", n, name);
        ProcMsg.Text = n > 0 ? done : Loc.T("Não foi possível encerrar: falta permissão ou o programa já fechou.");
        if (n > 0) { ActionLog.Add("Programa encerrado", $"{name} ({Pt.N(n, "processo", "processos")}, {memMB:N0} MB)"); ShowSnack(done, Loc.F("{0:N0} MB de memória liberados.", memMB)); }
        RefreshHeavyBanner();
    }

    // ---------- Suporte (botões no cabeçalho e em Configurações) ----------
    private void Support_Click(object sender, RoutedEventArgs e) => AppInfo.OpenSupport();
    private void OpenData_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell(AppPaths.Dir);

    // ---------- Aviso flutuante (snackbar) ----------
    private readonly System.Windows.Threading.DispatcherTimer _snackTimer = new();
    private Action? _snackAction;

    /// <summary>Mostra o resultado de uma ação sem bloquear a tela. Some sozinho; Esc ou o X fecham antes.</summary>
    internal void ShowSnack(string title, string text, string? actionLabel = null, Action? action = null, int seconds = 10)
    {
        // U1: o aviso já está carregado, então a tradução automática da janela não o alcança; textos montados chegam prontos (Loc.F)
        (title, text, actionLabel) = (Loc.T(title), Loc.T(text), actionLabel is null ? null : Loc.T(actionLabel));
        SnackTitle.Text = title;
        SnackText.Text = text;
        SnackText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _snackAction = action;
        SnackAction.Content = actionLabel;
        SnackAction.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        Snack.Visibility = Visibility.Visible;
        // O texto pode ser o mesmo do aviso anterior: pede a leitura de novo, agora que está na tela
        LiveRegions.Announce(SnackTitle);
        if (text.Length > 0) LiveRegions.Announce(SnackText);
        _snackTimer.Stop();
        _snackTimer.Interval = TimeSpan.FromSeconds(seconds + text.Length / 25); // tempo de leitura cresce com o texto
        RestartSnackTimer();
    }

    /// <summary>U6: aviso com ação (Instalar, Ver em Ações) só sai quando a pessoa fecha ou usa; os outros somem sozinhos.</summary>
    private void RestartSnackTimer()
    {
        _snackTimer.Stop();
        if (Snack.Visibility != Visibility.Visible || _snackAction is not null || SnackAction.Visibility == Visibility.Visible) return;
        if (Snack.IsMouseOver || Snack.IsKeyboardFocusWithin) return;
        _snackTimer.Start();
    }

    private void HideSnack() { _snackTimer.Stop(); Snack.Visibility = Visibility.Collapsed; }
    private void SnackClose_Click(object sender, RoutedEventArgs e) => HideSnack();
    private void SnackAction_Click(object sender, RoutedEventArgs e) { var a = _snackAction; HideSnack(); a?.Invoke(); }

    // ---------- Barra lateral compacta em janelas estreitas ----------
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { ApplyNavDensity(); ApplyPainelLayout(); }

    /// <summary>B46: sem espaço para o título e os avisos lado a lado (texto grande, janela estreita), os avisos descem para baixo do título.</summary>
    private void PageHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PageTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        HeaderChips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        bool below = PageHeader.ActualWidth < PageTitle.DesiredSize.Width + HeaderChips.DesiredSize.Width + 16;
        if ((Grid.GetRow(HeaderChips) == 1) == below) return;
        Grid.SetRow(HeaderChips, below ? 1 : 0);
        Grid.SetColumn(HeaderChips, below ? 0 : 1);
        Grid.SetColumnSpan(HeaderChips, below ? 2 : 1);
        HeaderChips.Margin = below ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        HeaderChips.HorizontalAlignment = below ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
    }

    /// <summary>Menu só com ícones na janela estreita ou no modo compacto (B15).</summary>
    public void ApplyNavDensity()
    {
        // Redesign V3: trilho (ícone sobre o rótulo) é o padrão; o modo largo encolhe para só ícones em janela estreita
        bool rail = Host.Settings.NavStyle != "expanded";
        bool icons = !rail && Density.NavIconsOnly(ActualWidth, TextScale.Factor, Host.Settings.Compact);
        Nav.Tag = rail ? "rail" : icons ? "compact" : null;
        NavPane.Width = rail ? 88 * Math.Max(1, TextScale.Factor * 0.9) : double.NaN;
        NavPane.MinWidth = rail || icons ? 0 : 220;
    }

    // ---------- Busca nas tabelas (M24) ----------
    private static void ApplyFilter(DataGrid grid, string text, Func<object, string> key)
    {
        if (grid.ItemsSource is null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        string q = text.Trim();
        grid.Tag = q; // leva 10.3: as células com SearchHighlight grifam a busca
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
        if (WindowPlacement.Fit(new Box(x, y, w, h), WindowFit.WorkAreas(), MinWidth, MinHeight) is not { } fit) return; // monitor sumiu: centraliza
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (fit.X, fit.Y, fit.Width, fit.Height);
        if (s.WinMaximized) WindowState = WindowState.Maximized;
    }


    private void UpdateTotals(DataGrid grid)
    {
        int total = (grid.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
        if (grid == ProcGrid)
            ProcTotal.Text = total == 0 ? "" : TableTotals.Processes(ProcGrid.Items.OfType<ProcRow>().Select(r => (r.Count, r.CpuPct, r.MemMB)).ToList(), total);
        else if (grid == SpaceGrid)
            SpaceTotal.Text = total == 0 ? "" : TableTotals.Space(SpaceGrid.Items.OfType<FolderUsage>().Select(f => f.Bytes).ToList(), total);
        else if (grid == ProgGrid)
            ProgTotal.Text = total == 0 ? "" : TableTotals.Programs(ProgGrid.Items.OfType<ProgramRow>()
                .Select(r => r.Program.SizeSource == SizeSource.None ? (long?)null : r.Bytes).ToList(), total);
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

    private DataGrid[] Grids() => [ProcGrid, CleanGrid, SpaceGrid, ProgGrid, StartupGrid, AlertsGrid, ActionsGrid, ServicesGrid, TasksGrid, ExtensionsGrid, EventLogsGrid];

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
    internal bool Simulate(string action, string details)
    {
        if (!Host.Settings.SimulationMode) return false;
        ShowSnack(Loc.F("Simulação: {0}", Loc.T(action)), Loc.T(details) + " " + Loc.T("Nada foi alterado."), "Desligar simulação", () => SetSimulation(false), seconds: 15);
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
        if (!on && !Host.EnsureUnlocked(Loc.T("desligar o modo simulação"))) { SimMode.IsChecked = true; return; } // B38
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
        if (Tabs.SelectedIndex == TabClean) UsageStats.Use("limpeza"); // M18: só conta, e só com a opção ligada
        else if (Tabs.SelectedIndex == TabPrograms) UsageStats.Use("programas");
        else if (Tabs.SelectedIndex == TabStartup) UsageStats.Use("inicializacao");
        switch (Tabs.SelectedIndex)
        {
            case TabProcs when Host.Latest is { } s: RefreshProcesses(s); break;
            case TabClean when CleanGrid.ItemsSource is null: Scan_Click(this, new RoutedEventArgs()); break; // a análise só lê, então roda ao abrir
            case TabPrograms when !_progLoaded: _ = LoadProgramsAsync(); break;
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); RefreshEnergy(); break;
            case TabStartup: _ = LoadBatteryWeekAsync(); break;
            case TabSystem when _sysSections is null: _ = LoadSystemAsync(); break;
            case TabSettings: CfgMsg.Text = ""; LoadPlans(); RefreshLockCard(); break;
        }
        Host.Settings.LastTab = Tabs.SelectedIndex;
        Host.Settings.Save();
    }
}
