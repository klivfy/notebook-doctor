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
        bool survival = Host.Settings.SurvivalState is not null;
        SurvivalBtn.Content = Loc.T(survival ? "Desligar Modo sobrevivência" : "Ligar Modo sobrevivência…");
        SurvivalInfo.Text = survival ? Loc.T("Ligado: volta sozinho ao ligar na tomada.")
            : Host.Battery is { OnAc: false } b ? Loc.F("Na bateria, com {0}%.", b.Percent) : "";
    }

    private void LapMode_Click(object sender, RoutedEventArgs e) => ToggleLapMode();

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
