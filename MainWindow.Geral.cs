using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class MainWindow
{
    private const double MinTableHeight = 220;

    /// <summary>
    /// Páginas de tabela: em condições normais a tabela ocupa o espaço que sobra. Com texto muito ampliado (ou janela baixa), o cabeçalho
    /// e os botões consomem a altura toda e a tabela chegava a 0 px. Aí a página passa a rolar e a tabela ganha uma altura mínima.
    /// </summary>
    private void InitTablePages()
    {
        foreach (var (page, grid) in new[] { (ProcPage, ProcGrid), (CleanPage, CleanGrid), (SpacePage, SpaceGrid), (StartupPage, StartupGrid), (ActionsPage, ActionsGrid) })
        {
            void Update(object? s, SizeChangedEventArgs e) => UpdateTablePage(page, grid);
            page.SizeChanged += Update;
            grid.SizeChanged += Update;
        }
    }

    private readonly Dictionary<ScrollViewer, DateTime> _tableToggled = new();

    private void UpdateTablePage(ScrollViewer page, DataGrid grid)
    {
        if (!grid.IsVisible || page.Content is not FrameworkElement content) return;
        // Trocar de modo muda o layout, que dispara este método de novo: sem pausa entre trocas a tela entrava em laço
        if (_tableToggled.TryGetValue(page, out var last) && DateTime.UtcNow - last < TimeSpan.FromMilliseconds(600)) return;

        if (page.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            if (grid.ActualHeight >= MinTableHeight || page.ViewportHeight <= 0) return;
            _tableToggled[page] = DateTime.UtcNow;
            page.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            grid.Height = MinTableHeight + 40;
        }
        else
        {
            // Volta ao modo normal quando a tabela, esticada, ainda ficaria com folga acima do mínimo.
            // A altura "de moldura" é a do conteúdo medido sem a tabela; sem medida válida, não decide.
            if (content.DesiredSize.Height <= 0 || double.IsNaN(grid.Height)) return;
            double chrome = content.DesiredSize.Height - grid.Height;
            if (page.ViewportHeight - chrome >= MinTableHeight + 60)
            {
                _tableToggled[page] = DateTime.UtcNow;
                grid.ClearValue(FrameworkElement.HeightProperty);
                page.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
        }
    }

    /// <summary>Texto de tabela corta com reticências e mostra o conteúdo inteiro na dica; colunas que já têm estilo próprio ficam como estão.</summary>
    private void ApplyCellTextStyle()
    {
        var style = (Style)FindResource("CellText");
        foreach (var grid in LogicalDescendants<DataGrid>(this))
            foreach (var col in grid.Columns.OfType<DataGridTextColumn>().Where(c => ReferenceEquals(c.ElementStyle, DataGridTextColumn.DefaultElementStyle)))
                col.ElementStyle = style;
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T hit) yield return hit;
            foreach (var deeper in LogicalDescendants<T>(child)) yield return deeper;
        }
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
