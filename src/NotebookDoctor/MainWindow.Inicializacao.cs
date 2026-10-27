using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Inicialização: programas que abrem com o Windows.</summary>
public partial class MainWindow
{
    private List<StartupEntry> _startup = new();

    private void StartupSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");

    // ---------- Inicialização ----------
    private void RefreshStartup()
    {
        _startup = StartupManager.List();
        // Leva 11.3: atrasados, atraso medido pelo Windows e gasto na bateria
        var delayed = Host.Settings.DelayedStartup;
        var bootByFile = BootDelays.ByFile(Host.Health.BootDelays ?? []);
        var battery = PowerAudit.BatteryUsers(_batteryWeek, DateTime.Now.AddDays(-7), 50).ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var s in _startup)
        {
            s.Delayed = delayed.Any(d => d.Name == s.Name && d.SubKey == StartupManager.SubKeyOf(s)) && !s.Enabled;
            if (BootDelays.For(s.FilePath, bootByFile) is { } boot) s.BootText = BootDelays.Text(boot);
            string proc = System.IO.Path.GetFileNameWithoutExtension(s.FilePath ?? "");
            if (proc.Length > 0 && battery.TryGetValue(proc, out var u)) s.BatteryText = Loc.F("{0:0}% da CPU", u.SharePct);
        }
        StartupGrid.ItemsSource = _startup;
        ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
        StartupMsg.Text = Loc.F("{0} ativos de {1}", _startup.Count(x => x.Enabled), _startup.Count);
    }

    // Histórico da última semana para a coluna "Na bateria" (lido em segundo plano ao abrir a aba)
    private List<HistSample> _batteryWeek = [];

    private async Task LoadBatteryWeekAsync()
    {
        _batteryWeek = await Task.Run(() => Host.History.Load(TimeSpan.FromDays(7)));
        RefreshStartup();
    }

    // Leva 11.3, tarefa 14: o Windows deixa de abrir o item; o app o abre 2 minutos depois do login
    private void DelayStartup_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = Loc.T("Selecione um item."); return; }
        if (entry.Delayed) { StartupMsg.Text = Loc.F("{0} já abre 2 minutos depois. Para desfazer, use Ações.", entry.Name); return; }
        if (!entry.Enabled) { StartupMsg.Text = Loc.F("{0} está desativado: ative antes de atrasar.", entry.Name); return; }
        var s = Host.Settings;
        if (Simulate(Loc.F("Atrasar {0} em 2 minutos", entry.Name), Loc.F("Desativaria {0} na inicialização do Windows e o Notebook Doctor o abriria 2 minutos depois do login.", entry.Name))) return;
        string extra = s.StartWithWindows ? "" : "\n\n" + Loc.T("Para isso, o Notebook Doctor também passa a abrir com o Windows (na bandeja).");
        if (!Dialogs.Confirm(this, Loc.F("Atrasar {0} em 2 minutos?", entry.Name),
                Loc.T("O Windows deixa de abrir o programa sozinho, e o Notebook Doctor o abre 2 minutos depois que você entrar, quando o computador já terminou de ligar. O programa não é apagado.") + extra,
                "Atrasar")) return;
        if (!s.StartWithWindows) { s.StartWithWindows = true; s.ApplyStartWithWindows(); }
        StartupManager.SetEnabled(entry, false);
        s.DelayedStartup = [.. s.DelayedStartup.Where(d => !(d.Name == entry.Name && d.SubKey == StartupManager.SubKeyOf(entry))), new DelayedStartupItem(entry.Name, entry.Command, StartupManager.SubKeyOf(entry))];
        s.Save();
        ActionLog.Add(Loc.F("Inicialização: {0} atrasado 2 min", entry.Name), Loc.T("Aberto pelo Notebook Doctor 2 minutos depois do login"), "startup-delay", entry.Name, StartupManager.SubKeyOf(entry));
        RefreshStartup();
        StartupMsg.Text = Loc.F("{0} abre 2 minutos depois do próximo login.", entry.Name);
    }

    // Leva 7.3 (C12)
    private void Snooze_Click(object sender, RoutedEventArgs e) { new SnoozeWindow(this).ShowDialog(); RefreshStartup(); }

    // Leva 7.3 (C06)
    private void ContextMenu_Click(object sender, RoutedEventArgs e) => new ContextMenuWindow(this).ShowDialog();

    private void RefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = Loc.T("Selecione um item."); return; }
        try
        {
            bool before = entry.Enabled;
            if (Simulate(Loc.F(before ? "Desativar {0} na inicialização" : "Ativar {0} na inicialização", entry.Name), Loc.F("Mudaria o registro StartupApproved de {0}. O programa não seria apagado.", entry.Name))) return;
            string restore = await EnsureRestorePointAsync("Programas de inicializacao");
            StartupManager.SetEnabled(entry, !before);
            StartupGrid.Items.Refresh();
            StartupMsg.Text = $"{entry.Name}: {entry.StateText.ToLower()}. " + Loc.T("Vale a partir do próximo login (reversível aqui).");
            ActionLog.Add(Loc.F(before ? "Inicialização: {0} desativado" : "Inicialização: {0} ativado", entry.Name), Loc.F("Ponto de restauração: {0}", Loc.T(restore)), "startup", entry.Name, $"{StartupManager.SubKeyOf(entry)}|{before}");
            _ = Host.RefreshHealthAsync();
        }
        catch (Exception ex) { StartupMsg.Text = Loc.T("Falhou: ") + ex.Message; }
    }

    /// <summary>Leva 5.3: abre a aba Programas no programa instalado dono do item.</summary>
    private async void StartupShowProgram_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (entry.FilePath is not { } path) { StartupMsg.Text = Loc.F("{0}: o arquivo do comando não foi encontrado.", entry.Name); return; }
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            try { path = StartMenu.ReadTarget(path); }
            catch (System.Runtime.InteropServices.COMException) { }
        await RevealProgramForAsync(path);
    }

    // ---------- M27: abrir a pasta do item ----------
    private void StartupOpen_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = Loc.T("Selecione um item na lista."); return; }
        if (entry.FilePath is not { } path) { StartupMsg.Text = Loc.F("{0}: o arquivo do comando não foi encontrado.", entry.Name); return; }
        // Argumento entre aspas: o Explorador só seleciona o arquivo, não o executa
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }
}
