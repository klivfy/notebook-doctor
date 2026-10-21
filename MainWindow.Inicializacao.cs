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
}
