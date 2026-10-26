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
        StartupGrid.ItemsSource = _startup;
        ApplyFilter(StartupGrid, StartupSearch.Text, o => o is StartupEntry s ? s.Name + " " + s.Command : "");
        StartupMsg.Text = Loc.F("{0} ativos de {1}", _startup.Count(x => x.Enabled), _startup.Count);
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
