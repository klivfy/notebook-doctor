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
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = ActualWidth < 960 * Math.Max(1, TextScale.Factor * 0.8);
        Nav.Tag = compact ? "compact" : null;
        NavPane.MinWidth = compact ? 0 : 200;
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
        e.Cancel = true;
        Hide();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

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
