using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

public sealed class ActionEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Como desfazer: "power" (volta ao plano anterior), "startup" (reativa/desativa item), "plan-created" (apaga o plano criado), "leftovers" (restaura o backup das sobras). Vazio = não desfazível.</summary>
    public string UndoKind { get; set; } = "";
    public string UndoArg1 { get; set; } = "";
    public string UndoArg2 { get; set; } = "";
    public bool Undone { get; set; }

    public string TimeText => Time.ToString("dd/MM HH:mm");
    public bool CanUndo => UndoKind.Length > 0 && !Undone;
    public string Status => Undone ? "Desfeito" : UndoKind.Length > 0 ? "Pode desfazer" : "Definitivo";
}

/// <summary>Registro das ações feitas pelo app (últimas 200), com desfazer quando é possível reverter.</summary>
public static class ActionLog
{
    private static readonly object Lock = new();
    private static string FilePath => Path.Combine(AppPaths.Dir, "acoes.json");

    public static List<ActionEntry> Load()
    {
        lock (Lock)
        {
            try { return JsonSerializer.Deserialize<List<ActionEntry>>(File.ReadAllText(FilePath)) ?? new(); }
            catch { return new(); }
        }
    }

    private static void Save(List<ActionEntry> list)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(list.OrderByDescending(e => e.Time).Take(200).ToList())); } catch { }
    }

    public static void Add(string title, string detail = "", string undoKind = "", string arg1 = "", string arg2 = "")
    {
        lock (Lock)
        {
            var list = Load();
            list.Add(new ActionEntry { Title = title, Detail = detail, UndoKind = undoKind, UndoArg1 = arg1, UndoArg2 = arg2 });
            Save(list);
        }
    }

    /// <summary>Desfaz a ação. Retorna uma mensagem para o usuário.</summary>
    public static string Undo(ActionEntry entry)
    {
        string msg;
        switch (entry.UndoKind)
        {
            case "power":
                msg = PowerPlans.SetActive(entry.UndoArg1) ? "Plano de energia anterior restaurado." : "Não foi possível restaurar o plano de energia.";
                break;
            case "startup":
                // UndoArg1 = nome do item; UndoArg2 = "SubChave|estadoAnterior"
                var parts = entry.UndoArg2.Split('|');
                StartupManager.SetEnabled(parts[0], entry.UndoArg1, bool.Parse(parts[1]));
                msg = "Item de inicialização restaurado.";
                break;
            case "service":
                // UndoArg1 = nome do serviço; UndoArg2 = tipo de início anterior (2 = automático)
                try { ServiceManager.SetStart(entry.UndoArg1, int.Parse(entry.UndoArg2)); }
                catch (UnauthorizedAccessException) { return "Serviços são do sistema: abra o app como administrador para desfazer."; }
                catch (Exception ex) { return "Não foi possível desfazer: " + ex.Message; }
                msg = "O serviço volta a iniciar com o Windows no próximo boot.";
                break;
            case "task":
                // UndoArg1 = caminho da tarefa no Agendador
                try { ScheduledTasks.SetEnabled(entry.UndoArg1, true); }
                catch (UnauthorizedAccessException) { return "Essa tarefa é de todos os usuários: abra o app como administrador para desfazer."; }
                catch (Exception ex) { return "Não foi possível desfazer: " + ex.Message; }
                msg = "A tarefa voltou a ficar ativa.";
                break;
            case "priority":
                msg = ProcessActions.SetPriority(entry.UndoArg1, ProcessPriorityClass.Normal) > 0
                    ? "Prioridade normal restaurada." : "O programa não está mais aberto: nada a restaurar.";
                break;
            case "efficiency":
                msg = ProcessActions.SetEfficiency(entry.UndoArg1, on: false) > 0
                    ? "Modo eficiência desligado." : "O programa não está mais aberto: nada a restaurar.";
                break;
            case "tweak":
                // UndoArg1 = id do ajuste; UndoArg2 = estado anterior
                try { msg = WindowsTweaks.Undo(entry.UndoArg1, entry.UndoArg2); }
                catch (UnauthorizedAccessException) { return "Esse ajuste é do sistema: abra o app como administrador para desfazer."; }
                catch (Exception ex) { return "Não foi possível desfazer: " + ex.Message; }
                break;
            case "leftovers":
                // UndoArg1 = id da sessão de backup; o que precisar de administrador avisa na mensagem
                msg = new BackupStore(AppPaths.Backups).Restore(entry.UndoArg1);
                break;
            case "ctxmenu":
                // UndoArg1 = id do backup do manipulador desativado (pasta context-backups)
                var back = ContextMenuEditor.EnableBySession(entry.UndoArg1, AppPaths.Dir, out bool back1);
                if (back is null) return Loc.T("Este item do menu de contexto já foi reativado.");
                if (!back1) return Loc.T("Não consegui devolver a chave. Tente como administrador.") + " " + back;
                msg = Loc.T("Item do menu de contexto de volta.");
                break;
            case "ctxverb":
                // UndoArg1 = "Hive|Local|Nome"
                var vp = entry.UndoArg1.Split('|');
                if (vp.Length != 3 || !ContextMenuRules.Valid(vp[0], vp[1], vp[2])) return Loc.T("Esta ação não pode ser desfeita.");
                try { ContextMenuEditor.SetVerbHidden(new ContextMenuItem(MenuItemKind.Verb, vp[0], vp[1], vp[2], vp[2], "", "", false, "", true), false); }
                catch (UnauthorizedAccessException) { return Loc.T("Este item vale para todos os usuários: abra o app como administrador para desfazer."); }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Loc.T("Não foi possível desfazer: ") + ex.Message; }
                msg = Loc.T("Item do menu de contexto de volta.");
                break;
            case "snooze":
                // UndoArg1 = chave do aplicativo em soneca
                var snoozed = global::NotebookDoctor.App.Current.Snooze.Load().FirstOrDefault(a => a.Key == entry.UndoArg1);
                if (snoozed is null) return Loc.T("Este aplicativo já foi acordado.");
                var woke = SnoozeEngine.Wake(snoozed, new WindowsSnoozeSystem());
                if (SnoozeEngine.FullyAwake(snoozed)) global::NotebookDoctor.App.Current.Snooze.Remove(snoozed.Key);
                else
                {
                    global::NotebookDoctor.App.Current.Snooze.Save(snoozed);
                    return Loc.T("Algumas peças exigem administrador: abra o app como administrador e desfaça de novo.");
                }
                msg = Loc.F("{0} acordou: inicialização, serviços e tarefas de volta.", snoozed.Name);
                break;
            case "driver":
                // UndoArg1 = id da cópia do driver anterior (pasta em driver-backups)
                if (!AppInfo.IsAdmin) return Loc.T("Voltar ao driver anterior exige administrador: abra o app como administrador e tente de novo.");
                var backup = DriverUpdater.Find(AppPaths.Dir, entry.UndoArg1);
                if (backup is null) return Loc.T("A cópia do driver anterior não existe mais.");
                RestoreOutcome outcome;
                try { outcome = Task.Run(() => DriverUpdater.RestoreAsync(backup, AppPaths.Dir)).GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or ArgumentException) { return Loc.T("Não foi possível voltar ao driver anterior: ") + ex.Message; }
                msg = DriverTexts.Restore(outcome);
                if (outcome != RestoreOutcome.Restored) return msg; // continua desfazível: a pessoa pode tentar de novo
                break;
            case "plan-created":
                msg = PowerPlans.Delete(entry.UndoArg1) ? "Plano criado foi removido." : "Não foi possível remover (talvez esteja ativo): troque de plano e tente de novo.";
                break;
            default:
                return "Esta ação não pode ser desfeita.";
        }
        lock (Lock)
        {
            var list = Load();
            var match = list.FirstOrDefault(e => e.Time == entry.Time && e.Title == entry.Title);
            if (match is not null) { match.Undone = true; Save(list); }
        }
        entry.Undone = true;
        return msg;
    }
}
