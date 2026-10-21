using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Configurações: preferências, perfis, energia, administrador, rede, integridade, suporte e atualização.</summary>
public partial class MainWindow
{
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

    /// <summary>Troca o plano com ponto de restauração e registro em Ações. Retorna se trocou de fato.</summary>
    private async Task<bool> ActivatePlanAsync(string guid)
    {
        var plans = await Task.Run(PowerPlans.List);
        var previous = plans.FirstOrDefault(p => p.Active);
        var target = plans.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (target is null || previous is null || previous.Guid == target.Guid) return false;
        if (Simulate("Trocar o plano de energia", $"Trocaria o plano \"{previous.Name}\" por \"{target.Name}\" (powercfg /setactive).")) return false;

        string restore = await EnsureRestorePointAsync("Plano de energia");
        bool ok = await Task.Run(() => PowerPlans.SetActive(target.Guid));
        PlanMsg.Text = ok ? $"Plano ativo: {target.Name}" : "Não foi possível trocar o plano.";
        if (ok) ActionLog.Add("Plano de energia alterado", $"{previous.Name} → {target.Name}. Ponto de restauração: {restore}", "power", previous.Guid);
        _ = Host.RefreshHealthAsync();
        LoadPlans();
        return ok;
    }

    /// <summary>A02: sugestão ao ligar ou tirar da tomada, com a troca a um clique.</summary>
    public void ShowPowerSuggestion(PowerSuggestion s) =>
        ShowSnack(s.Title, s.Reason, $"Usar {s.Plan.Name}", async () =>
        {
            if (await ActivatePlanAsync(s.Plan.Guid)) ShowSnack($"Plano ativo: {s.Plan.Name}", "Dá para desfazer na aba Ações.");
        }, seconds: 20);

    // ---------- Teste de conexão (A46) ----------
    private async void NetTest_Click(object sender, RoutedEventArgs e)
    {
        NetTestBtn.IsEnabled = false;
        NetTestResult.Text = "Testando… (uns 10 segundos)";
        try { NetTestResult.Text = string.Join("\n", await NetTest.RunAsync()); }
        finally { NetTestBtn.IsEnabled = true; }
    }

    public void ReloadSettings() { LoadSettingsIntoForm(); RefreshProfileCard(); }

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
}
