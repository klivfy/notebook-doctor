using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

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
}
