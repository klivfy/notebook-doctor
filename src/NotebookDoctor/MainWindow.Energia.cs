using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Fase 11, leva 11.1: bateria em watts, Modo Colo e Modo sobrevivência.</summary>
public partial class MainWindow
{
    /// <summary>Linha "bateria agora" no cartão de saúde do Início: carga, watts e quanto dura.</summary>
    private void RefreshBatteryNow()
    {
        if (Host.Battery is not { } b) { BatteryNowText.Visibility = Visibility.Collapsed; return; }
        var parts = new List<string> { $"{b.Percent}%" };
        if (BatteryRules.RateText(b) is { Length: > 0 } rate) parts.Add(rate);
        if (!b.OnAc && BatteryRules.Forecast(b.RemainingWh, b.Discharging ? -b.RateW : 0, Host.BatteryLog.DischargeWatts()) is { } f)
            parts.Add(BatteryRules.ForecastText(f));
        else if (b.OnAc) parts.Add(Loc.T(b.Charging ? "carregando" : "na tomada"));
        BatteryNowText.Text = Loc.T("Agora:") + " " + string.Join(" · ", parts);
        BatteryNowText.Visibility = Visibility.Visible;
    }

    private void RefreshEnergy()
    {
        bool lap = Host.Settings.LapModePrevious is not null;
        if (!lap && !AppPaths.Demo && PowerProbe.ReadBoost() is { } boost) lap = !BatteryRules.BoostEnabled(boost.Ac) && !BatteryRules.BoostEnabled(boost.Dc);
        LapBtn.Content = Loc.T(lap ? "Desligar Modo Colo" : "Ligar Modo Colo");
        LapInfo.Text = lap ? Loc.T("Ligado: o turbo do processador está desligado.") : "";
        CfgDynamicHz.IsChecked = Host.Settings.DynamicHz;
        var usb = AppPaths.Demo ? null : PowerProbe.ReadUsbSuspend();
        bool usbOff = PowerAudit.UsbSuspendOff(usb?.Dc);
        UsbSuspendInfo.Text = usb is null ? "" : Loc.T(usbOff ? "Suspensão seletiva USB desligada na bateria: aparelhos USB parados continuam gastando energia."
                                                             : "Suspensão seletiva USB ligada na bateria.");
        UsbSuspendBtn.Visibility = usbOff ? Visibility.Visible : Visibility.Collapsed;
        bool survival = Host.Settings.SurvivalState is not null;
        SurvivalBtn.Content = Loc.T(survival ? "Desligar Modo sobrevivência" : "Ligar Modo sobrevivência…");
        SurvivalInfo.Text = survival ? Loc.T("Ligado: volta sozinho ao ligar na tomada.")
            : Host.Battery is { OnAc: false } b ? Loc.F("Na bateria, com {0}%.", b.Percent) : "";
    }

    private void LapMode_Click(object sender, RoutedEventArgs e) => ToggleLapMode();

    // ---------- Leva 11.2 ----------

    private void DynamicHz_Click(object sender, RoutedEventArgs e)
    {
        Host.Settings.DynamicHz = CfgDynamicHz.IsChecked == true;
        Host.Settings.Save();
        ShowSnack(Host.Settings.DynamicHz ? "Tela em 60 Hz na bateria" : "Taxa da tela sem mudança automática",
                  Host.Settings.DynamicHz ? "Vale a partir da próxima vez que o notebook sair da tomada." : "A tela fica na taxa escolhida no Windows.");
    }

    private void UsbSuspend_Click(object sender, RoutedEventArgs e)
    {
        if (PowerProbe.ReadUsbSuspend() is not { } usb) return;
        if (Simulate("Ligar a suspensão seletiva USB", "Ligaria a suspensão seletiva USB na bateria, no plano de energia ativo.")) return;
        if (!PowerProbe.WriteUsbSuspend(usb.Scheme, usb.Ac, 1)) { UsbSuspendInfo.Text = Loc.T("O Windows não deixou mudar o plano de energia."); return; }
        ActionLog.Add("Suspensão seletiva USB ligada na bateria", Loc.T("Plano de energia ativo"), "usb-suspend", $"{usb.Ac},{usb.Dc},{usb.Scheme}");
        ShowSnack("Suspensão seletiva USB ligada", "Se algum aparelho USB passar a desconectar sozinho, desfaça em Ações.");
        RefreshEnergy();
    }

    private async void SleepCheck_Click(object sender, RoutedEventArgs e)
    {
        SleepInfo.Text = Loc.T("Perguntando ao Windows…");
        var (requests, wake) = AppPaths.Demo
            ? ((List<PowerRequest>?)[new("SYSTEM", "PROCESS", "Teams.exe"), new("DISPLAY", "PROCESS", "chrome.exe")], (List<string>)["Mouse HID", "Intel(R) Wi-Fi 6 AX201"])
            : await Task.Run(() => (PowerProbe.PowerRequests(), PowerProbe.WakeDevices()));
        var lines = new List<string>();
        if (requests is null) lines.Add(Loc.T("Quem segura o computador acordado: abra o app como administrador para ver (o Windows exige)."));
        else if (requests.Count == 0) lines.Add(Loc.T("Nenhum programa está segurando o computador acordado agora."));
        else lines.AddRange(requests.Select(r => $"• {r.Name} {PowerAudit.KindText(r.Kind)}"));
        lines.Add(wake.Count == 0 ? Loc.T("Nenhum aparelho pode acordar o computador sozinho.")
                                  : Loc.F("Podem acordar o computador: {0}.", string.Join(", ", wake)));
        SleepInfo.Text = string.Join("\n", lines);
        WakeBox.ItemsSource = wake;
        WakeBox.SelectedIndex = wake.Count > 0 ? 0 : -1;
        WakeBox.Visibility = WakeOffBtn.Visibility = wake.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WakeOff_Click(object sender, RoutedEventArgs e)
    {
        if (WakeBox.SelectedItem is not string device) return;
        if (Simulate(Loc.F("Não deixar {0} acordar o computador", device), Loc.F("Tiraria de {0} a permissão de acordar o computador.", device))) return;
        if (!AppInfo.IsAdmin) { SleepInfo.Text = Loc.T("Para mudar quem acorda o computador, abra o app como administrador."); return; }
        if (!Dialogs.Confirm(this, Loc.F("Não deixar {0} acordar o computador?", device),
                Loc.T("O aparelho continua funcionando normalmente; ele só não tira mais o computador do modo de suspensão. Para acordar, use o botão de ligar ou abra a tampa."),
                "Confirmar")) return;
        if (!PowerProbe.SetWake(device, allowed: false)) { SleepInfo.Text = Loc.T("O Windows recusou a mudança."); return; }
        ActionLog.Add(Loc.F("{0} não acorda mais o computador", device), "", "wake", device);
        ShowSnack("Feito", Loc.F("{0} não acorda mais o computador.", device));
        SleepCheck_Click(sender, e);
    }

    /// <summary>Leva 11.2, tarefa 09: ao sair da tomada no meio de uma análise longa, pergunta se continua.</summary>
    private void OnPowerSourceChanged(bool onAc)
    {
        if (onAc || _spaceCts is not { IsCancellationRequested: false }) return;
        ShowSnack("Na bateria", "A análise do Espaço continua e gasta bateria. Quer parar e terminar na tomada?", "Parar análise", () => _spaceCts?.Cancel(), seconds: 20);
    }

    internal void ToggleLapMode()
    {
        var s = Host.Settings;
        if (s.LapModePrevious is { } previous)
        {
            // Desligar: devolve o turbo de antes
            var p = previous.Split(',');
            if (Simulate("Desligar o Modo Colo", "Devolveria o turbo do processador ao valor de antes.")) return;
            bool ok = p.Length == 3 && uint.TryParse(p[0], out uint ac) && uint.TryParse(p[1], out uint dc) && Guid.TryParse(p[2], out var scheme) && PowerProbe.WriteBoost(scheme, ac, dc);
            s.LapModePrevious = null;
            s.Save();
            LapInfo.Text = ok ? Loc.T("Modo Colo desligado: o turbo voltou.") : Loc.T("O plano de energia mudou desde então: o turbo segue como está no plano atual.");
            if (ok) ActionLog.Add("Modo Colo desligado", Loc.T("Turbo do processador como antes"));
            RefreshEnergy();
            return;
        }
        if (PowerProbe.ReadBoost() is not { } boost) { LapInfo.Text = Loc.T("O Windows não deixou ler o plano de energia."); return; }
        if (!BatteryRules.BoostEnabled(boost.Ac) && !BatteryRules.BoostEnabled(boost.Dc)) { LapInfo.Text = Loc.T("O turbo já está desligado neste plano de energia."); return; }
        if (Simulate("Ligar o Modo Colo", "Desligaria o turbo do processador no plano de energia ativo, na tomada e na bateria.")) return;
        if (!Dialogs.Confirm(this, Loc.T("Ligar o Modo Colo?"),
                Loc.T("O turbo do processador fica desligado no plano de energia atual. Tarefas pesadas (exportar vídeo, compilar, jogos) ficam um pouco mais lentas; em troca, o notebook esquenta bem menos e fica silencioso.")
                + "\n\n" + Loc.T("Para voltar, clique em \"Desligar Modo Colo\" ou use Desfazer em Ações."), "Ligar")) return;
        if (!PowerProbe.WriteBoost(boost.Scheme, BatteryRules.BoostOff, BatteryRules.BoostOff)) { LapInfo.Text = Loc.T("O Windows não deixou mudar o plano de energia."); return; }
        s.LapModePrevious = $"{boost.Ac},{boost.Dc},{boost.Scheme}";
        s.Save();
        ActionLog.Add("Modo Colo ligado", Loc.T("Turbo do processador desligado no plano de energia"), "boost", s.LapModePrevious);
        ShowSnack("Modo Colo ligado", "O processador fica na velocidade base: menos calor e menos barulho.", "Desfazer", ToggleLapMode);
        RefreshEnergy();
    }

    private void Survival_Click(object sender, RoutedEventArgs e) => ToggleSurvival();

    internal void ToggleSurvival()
    {
        if (SurvivalState.Parse(Host.Settings.SurvivalState) is { } state)
        {
            Host.EndSurvival(state, Loc.T("Desligado por você: tudo voltou como estava."));
            RefreshEnergy();
            return;
        }
        if (Simulate("Ligar o Modo sobrevivência", "Ligaria o economizador de bateria, poria os programas sem janela no modo eficiência, baixaria a tela para 60 Hz e passaria para o tema escuro.")) return;
        if (!Dialogs.Confirm(this, Loc.T("Ligar o Modo sobrevivência?"),
                Loc.T("Liga o economizador de bateria do Windows, põe no modo eficiência os programas sem janela que estão usando a CPU, baixa a tela para 60 Hz e passa para o tema escuro. Nada é fechado.")
                + "\n\n" + Loc.T("Tudo volta sozinho ao ligar na tomada, ou ao clicar de novo."), "Ligar")) return;
        var (newState, done) = Survival.Apply(Host.Latest);
        Host.Settings.SurvivalState = newState.Serialize();
        Host.Settings.Save();
        string text = done.Count > 0 ? string.Join(", ", done) : Loc.T("nada precisava mudar");
        ActionLog.Add("Modo sobrevivência ligado", text, "survival", Host.Settings.SurvivalState);
        ShowSnack("Modo sobrevivência ligado", text);
        RefreshEnergy();
    }
}
