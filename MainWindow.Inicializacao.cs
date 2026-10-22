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
        StartupMsg.Text = $"{_startup.Count(x => x.Enabled)} ativos de {_startup.Count}";
    }

    // Leva 7.3 (C12)
    private void Snooze_Click(object sender, RoutedEventArgs e) { new SnoozeWindow(this).ShowDialog(); RefreshStartup(); }

    // Leva 7.3 (C06)
    private void ContextMenu_Click(object sender, RoutedEventArgs e) => new ContextMenuWindow(this).ShowDialog();

    private void RefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = "Selecione um item."; return; }
        try
        {
            bool before = entry.Enabled;
            if (Simulate($"{(before ? "Desativar" : "Ativar")} {entry.Name} na inicialização", $"Mudaria o registro StartupApproved de {entry.Name}. O programa não seria apagado.")) return;
            string restore = await EnsureRestorePointAsync("Programas de inicializacao");
            StartupManager.SetEnabled(entry, !before);
            StartupGrid.Items.Refresh();
            StartupMsg.Text = $"{entry.Name}: {entry.State.ToLower()}. Vale a partir do próximo login (reversível aqui).";
            ActionLog.Add($"Inicialização: {entry.Name} {(before ? "desativado" : "ativado")}", $"Ponto de restauração: {restore}", "startup", entry.Name, $"{StartupManager.SubKeyOf(entry)}|{before}");
            _ = Host.RefreshHealthAsync();
        }
        catch (Exception ex) { StartupMsg.Text = "Falhou: " + ex.Message; }
    }

    /// <summary>Leva 5.3: abre a aba Programas no programa instalado dono do item.</summary>
    private async void StartupShowProgram_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = "Selecione um item na lista."; return; }
        if (entry.FilePath is not { } path) { StartupMsg.Text = $"{entry.Name}: o arquivo do comando não foi encontrado."; return; }
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            try { path = StartMenu.ReadTarget(path); }
            catch (System.Runtime.InteropServices.COMException) { }
        await RevealProgramForAsync(path);
    }

    // ---------- M27: abrir a pasta do item ----------
    private void StartupOpen_Click(object sender, RoutedEventArgs e)
    {
        if (StartupGrid.SelectedItem is not StartupEntry entry) { StartupMsg.Text = "Selecione um item na lista."; return; }
        if (entry.FilePath is not { } path) { StartupMsg.Text = $"{entry.Name}: o arquivo do comando não foi encontrado."; return; }
        // Argumento entre aspas: o Explorador só seleciona o arquivo, não o executa
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }
}
