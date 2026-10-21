using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public partial class MainWindow
{
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
}
