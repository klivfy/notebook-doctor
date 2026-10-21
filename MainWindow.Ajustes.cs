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
        LoadServices();
    }

    // ---------- M30: serviços de terceiros que iniciam com o Windows ----------
    public sealed record ServiceRow(string Name, string DisplayName, string Advice, string Reason, string Path, bool CanChange);

    private async void LoadServices()
    {
        var rows = await Task.Run(() => ServiceRules.Candidates(ServiceManager.List(), ServiceManager.SystemRoot)
            .Select(c => new ServiceRow(c.Service.Name, c.Service.DisplayName,
                c.Advice switch { ServiceAdvice.CanBeManual => "Pode ficar manual", ServiceAdvice.Keep => "Deixe automático", _ => "Avalie" },
                c.Reason, ServiceRules.ExecutablePath(c.Service.ImagePath, ServiceManager.SystemRoot), c.Advice != ServiceAdvice.Keep))
            .ToList());
        ServicesGrid.ItemsSource = rows;
        ServicesMsg.Text = rows.Count == 0 ? "Nenhum serviço de terceiros inicia sozinho." : $"{Pt.N(rows.Count, "serviço", "serviços")} de terceiros iniciam com o Windows.";
    }

    private async void ServiceManual_Click(object sender, RoutedEventArgs e)
    {
        if (ServicesGrid.SelectedItem is not ServiceRow r) { ServicesMsg.Text = "Selecione um serviço na lista."; return; }
        if (!r.CanChange) { ServicesMsg.Text = $"{r.DisplayName} é de proteção, hardware ou rede: o app não muda esse."; return; }
        if (Simulate($"Serviço {r.DisplayName} em manual", $"Mudaria o início de \"{r.Name}\" de automático para manual. O serviço não seria parado nem apagado.")) return;
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, "Precisa de administrador", "Mudar serviços exige privilégios elevados. Reiniciar o Notebook Doctor como administrador agora?", "Reiniciar"))
                Host.RestartAsAdmin();
            return;
        }
        if (!Dialogs.Confirm(this, "Deixar o serviço em manual?",
                $"{r.DisplayName}\n{r.Path}\n\nEle deixa de abrir sozinho a partir do próximo boot, mas continua instalado: o programa ainda pode iniciá-lo quando precisar. Dá para desfazer na aba Ações.", "Mudar para manual")) return;
        try
        {
            string restore = await EnsureRestorePointAsync($"Servico {r.Name}");
            ServiceManager.SetStart(r.Name, 3);
            ActionLog.Add($"Serviço em manual: {r.DisplayName}", $"{r.Name}. Ponto de restauração: {restore}", "service", r.Name, "2");
            ShowSnack($"{r.DisplayName} em manual", "Vale a partir do próximo boot.", "Ver em Ações", () => Tabs.SelectedIndex = TabActions);
            LoadServices();
        }
        catch (Exception ex) { ShowSnack("O Windows recusou a mudança", ex.Message); }
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
            string restore = t.NeedsAdmin ? await EnsureRestorePointAsync($"Ajuste: {t.Title}") : "não se aplica";
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
