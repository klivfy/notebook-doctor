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
        LoadTasks();
        LoadExtensions();
        LoadSystemCleanup();
    }

    // ---------- M37/M36: pontos de restauração e logs de eventos ----------
    private bool _systemCleanupLoaded;
    private List<RestorePointInfo> _restorePoints = new();

    /// <summary>Usa o PowerShell (alguns segundos): roda na primeira vez que a aba abre e em "Verificar".</summary>
    private async void LoadSystemCleanup(bool force = false)
    {
        if (_systemCleanupLoaded && !force) return;
        _systemCleanupLoaded = true;
        RestoreInfo.Text = "Verificando…";
        var (points, used) = await Task.Run(RestorePoints.List);
        _restorePoints = points;
        RestoreInfo.Text = RestorePointRules.Describe(points, used)
                           + (points.Count == 0 && !AppInfo.IsAdmin ? " Sem administrador, o Windows pode não mostrar os pontos." : "");
        var logs = await Task.Run(EventLogs.Large);
        EventLogsGrid.ItemsSource = logs ?? new List<EventLogFile>();
        string min = Pt.Bytes(EventLogRules.MinBytes);
        EventLogsInfo.Text = logs is null ? "Para ver o tamanho dos logs, abra o app como administrador."
            : logs.Count == 0 ? $"Nenhum log passa de {min}. O de Segurança nunca entra."
            : $"{Pt.N(logs.Count, "log passa", "logs passam")} de {min} ({Pt.Bytes(logs.Sum(l => l.Bytes))} no total). O de Segurança nunca entra.";
    }

    private void SystemCleanupRefresh_Click(object sender, RoutedEventArgs e) => LoadSystemCleanup(force: true);

    /// <summary>Sem administrador, oferece reabrir e devolve true (quem chamou para).</summary>
    private bool AskAdmin(string what)
    {
        if (AppInfo.IsAdmin) return false;
        if (Dialogs.ConfirmOnce(this, Host.Settings, "admin-restart", "Precisa de administrador", $"{what} exige privilégios elevados. Reiniciar o Notebook Doctor como administrador agora?", "Reiniciar"))
            Host.RestartAsAdmin();
        return true;
    }

    private async void RestoreOld_Click(object sender, RoutedEventArgs e)
    {
        if (_restorePoints.Count == 0) { RestoreInfo.Text = "Nenhum ponto de restauração encontrado. Clique em Verificar (como administrador)."; return; }
        var delete = RestorePointRules.ToDelete(_restorePoints);
        if (delete.Count == 0) { RestoreInfo.Text = "Só há um ponto de restauração: ele fica, nada a apagar."; return; }
        var keep = _restorePoints.Except(delete).First();
        string keepText = $"{keep.Created:dd/MM/yyyy HH:mm}" + (keep.Description.Length > 0 ? $" ({keep.Description})" : "");
        if (Simulate($"Apagar {Pt.N(delete.Count, "ponto de restauração antigo", "pontos de restauração antigos")}", $"Manteria o mais recente, de {keepText}.")) return;
        if (AskAdmin("Apagar pontos de restauração")) return;
        string list = string.Join("\n", delete.Take(8).Select(p => $"• {p.Created:dd/MM/yyyy HH:mm} {p.Description}")) + (delete.Count > 8 ? $"\n… e mais {delete.Count - 8}" : "");
        if (!Dialogs.Confirm(this, $"Apagar {Pt.N(delete.Count, "ponto de restauração", "pontos de restauração")}?",
                $"{list}\n\nFica o mais recente, de {keepText}.\n\nOs apagados não voltam: não será mais possível restaurar o Windows para essas datas.", "Apagar", danger: true)) return;
        int ok = await Task.Run(() => delete.Count(p => RestorePoints.Remove(p.Sequence)));
        ActionLog.Add("Pontos de restauração antigos apagados", $"{ok} de {delete.Count}; mantido o de {keepText}");
        ShowSnack(ok == delete.Count ? $"{Pt.N(ok, "ponto antigo apagado", "pontos antigos apagados")}" : $"{ok} de {delete.Count} apagados",
                  ok == delete.Count ? $"Ficou o de {keepText}." : "O Windows recusou alguns; tente de novo como administrador.");
        LoadSystemCleanup(force: true);
    }

    private async void EventLogClear_Click(object sender, RoutedEventArgs e)
    {
        if (EventLogsGrid.SelectedItem is not EventLogFile log) { EventLogsInfo.Text = "Selecione um log na lista."; return; }
        if (Simulate($"Limpar o log {log.Name}", $"Executaria \"wevtutil cl {log.Name}\" e liberaria cerca de {log.SizeText}.")) return;
        if (AskAdmin("Limpar logs de eventos")) return;
        if (!Dialogs.Confirm(this, "Limpar este log de eventos?",
                $"{log.Name} ({log.SizeText})\n\nTodos os eventos dele são apagados para sempre. Se estiver investigando um problema, salve uma cópia antes pelo Visualizador de Eventos.", "Limpar", danger: true)) return;
        try
        {
            int code = await EventLogs.ClearAsync(log.Name);
            ActionLog.Add($"Log de eventos limpo: {log.Name}", $"{log.SizeText}; wevtutil terminou com código {code}");
            ShowSnack(code == 0 ? $"{log.Name} limpo" : "O Windows não limpou o log", code == 0 ? $"Cerca de {log.SizeText} liberados." : $"wevtutil terminou com código {code}.");
        }
        catch (Exception ex) { ShowSnack("Não foi possível limpar", ex.Message); }
        LoadSystemCleanup(force: true);
    }

    private void EventViewer_Click(object sender, RoutedEventArgs e) => AppInfo.OpenShell("eventvwr.msc");

    // ---------- M29: tarefas agendadas de terceiros ----------
    public sealed record TaskRow(string Path, string Name, string When, string Advice, string Reason, string Command, bool CanChange);

    private async void LoadTasks()
    {
        var rows = await Task.Run(() => TaskRules.Candidates(ScheduledTasks.List())
            .Select(c => new TaskRow(c.Task.Path, c.Task.Name, TaskRules.When(c.Task),
                c.Advice switch { TaskAdvice.CanDisable => "Pode desativar", TaskAdvice.Keep => "Deixe ativa", _ => "Avalie" },
                c.Reason, c.Task.Command, c.Advice != TaskAdvice.Keep))
            .ToList());
        TasksGrid.ItemsSource = rows;
        TasksMsg.Text = rows.Count == 0 ? "Nenhuma tarefa de terceiros ativa." : $"{Pt.N(rows.Count, "tarefa ativa", "tarefas ativas")} de terceiros.";
    }

    private void TasksRefresh_Click(object sender, RoutedEventArgs e) => LoadTasks();

    private async void TaskDisable_Click(object sender, RoutedEventArgs e)
    {
        if (TasksGrid.SelectedItem is not TaskRow r) { TasksMsg.Text = "Selecione uma tarefa na lista."; return; }
        if (!r.CanChange) { TasksMsg.Text = $"{r.Name} é de proteção, hardware ou atualização: o app não desativa essa."; return; }
        if (Simulate($"Desativar a tarefa {r.Name}", $"Desativaria \"{r.Path}\" no Agendador de Tarefas. A tarefa não seria apagada.")) return;
        if (!Dialogs.Confirm(this, "Desativar a tarefa?",
                $"{r.Name}\n{r.Command}\n\nEla deixa de rodar ({r.When}), mas continua no Agendador de Tarefas. Dá para desfazer na aba Ações.", "Desativar")) return;
        try
        {
            string restore = await EnsureRestorePointAsync($"Tarefa {r.Name}");
            await Task.Run(() => ScheduledTasks.SetEnabled(r.Path, false));
            ActionLog.Add($"Tarefa agendada desativada: {r.Name}", $"{r.Path}. Ponto de restauração: {restore}", "task", r.Path);
            ShowSnack($"{r.Name} desativada", "Ela não roda mais até ser reativada.", "Ver em Ações", () => Tabs.SelectedIndex = TabActions);
            LoadTasks();
        }
        catch (UnauthorizedAccessException)
        {
            // Tarefas criadas para todos os usuários só mudam como administrador
            if (!AppInfo.IsAdmin && Dialogs.ConfirmOnce(this, Host.Settings, "admin-restart", "Precisa de administrador",
                    $"\"{r.Name}\" foi criada para todos os usuários e só muda com privilégios elevados. Reiniciar o Notebook Doctor como administrador agora?", "Reiniciar"))
                Host.RestartAsAdmin();
            else TasksMsg.Text = "O Windows não deixou mudar esta tarefa.";
        }
        catch (Exception ex) { ShowSnack("O Windows recusou a mudança", ex.Message); }
    }

    // ---------- M31: extensões de navegador ----------
    private async void LoadExtensions()
    {
        var list = await Task.Run(BrowserExtensionScan.List);
        ExtensionsGrid.ItemsSource = list;
        int heavy = list.Count(x => x.AlwaysOn && x.AllSites);
        ExtensionsMsg.Text = list.Count == 0 ? "Nenhuma extensão encontrada no Chrome, Edge, Brave, Vivaldi ou Firefox."
            : $"{Pt.N(list.Count, "extensão", "extensões")}" + (heavy > 0 ? $", {Pt.N(heavy, "pesada", "pesadas")}." : ".");
    }

    private void ExtensionsRefresh_Click(object sender, RoutedEventArgs e) => LoadExtensions();

    private void ExtensionsOpen_Click(object sender, RoutedEventArgs e)
    {
        var ext = ExtensionsGrid.SelectedItem as BrowserExtension ?? (ExtensionsGrid.ItemsSource as IEnumerable<BrowserExtension>)?.FirstOrDefault();
        if (ext is null) { ExtensionsMsg.Text = "Nenhum navegador com extensões foi encontrado."; return; }
        BrowserExtensionScan.OpenPage(ext.Browser);
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
            if (Dialogs.ConfirmOnce(this, Host.Settings, "admin-restart", "Precisa de administrador", "Mudar serviços exige privilégios elevados. Reiniciar o Notebook Doctor como administrador agora?", "Reiniciar"))
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
            if (Dialogs.ConfirmOnce(this, Host.Settings, "admin-restart", "Precisa de administrador", $"\"{t.Title}\" muda uma configuração do sistema. Reiniciar o Notebook Doctor como administrador agora? O Windows vai pedir sua confirmação.", "Reiniciar"))
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
