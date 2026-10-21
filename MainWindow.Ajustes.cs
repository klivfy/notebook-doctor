using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Ajustes: ajustes do Windows com simulação e desfazer.</summary>
public partial class MainWindow
{
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
