using System.Runtime.InteropServices;

namespace NotebookDoctor.Services;

/// <summary>
/// M29: lê e liga/desliga tarefas do Agendador de Tarefas pela interface COM do próprio Windows (Schedule.Service),
/// sem depender do texto do schtasks, que muda com o idioma. Desativar não apaga a tarefa.
/// </summary>
public static class ScheduledTasks
{
    // TASK_TRIGGER_TYPE2
    private const int TriggerBoot = 8, TriggerLogon = 9, TriggerTime = 1, TriggerDaily = 2, TriggerWeekly = 3, TriggerMonthly = 4, TriggerMonthlyDow = 5, TriggerIdle = 6;
    private const int ActionExec = 0;
    private const int IncludeHidden = 1;

    public static List<ScheduledTaskInfo> List()
    {
        var result = new List<ScheduledTaskInfo>();
        dynamic? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            Walk(service.GetFolder(@"\"), result);
        }
        catch (COMException) { } // serviço do Agendador parado ou bloqueado por política: lista vazia
        finally { if (service is not null) Marshal.FinalReleaseComObject(service); }
        return result;
    }

    private static void Walk(dynamic folder, List<ScheduledTaskInfo> result)
    {
        string path = folder.Path;
        if (path.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return; // tarefas do Windows nem são lidas
        try
        {
            foreach (dynamic task in folder.GetTasks(IncludeHidden))
            {
                try { result.Add(Read(task)); }
                catch (COMException) { } // tarefa corrompida ou sem permissão de leitura
            }
            foreach (dynamic sub in folder.GetFolders(0)) Walk(sub, result);
        }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ScheduledTaskInfo Read(dynamic task)
    {
        dynamic def = task.Definition;
        bool logon = false, boot = false, repeats = false;
        foreach (dynamic t in def.Triggers)
        {
            if (!(bool)t.Enabled) continue;
            int type = t.Type;
            if (type == TriggerLogon) logon = true;
            else if (type == TriggerBoot) boot = true;
            else if (type is TriggerTime or TriggerDaily or TriggerWeekly or TriggerMonthly or TriggerMonthlyDow or TriggerIdle) repeats = true;
        }
        string command = "";
        foreach (dynamic a in def.Actions)
        {
            if ((int)a.Type != ActionExec) continue;
            command = CommandLine.ExecutablePath(Environment.ExpandEnvironmentVariables((string)(a.Path ?? ""))) ?? "";
            break;
        }
        string author = "";
        try { author = (string)(def.RegistrationInfo.Author ?? ""); } catch (COMException) { }
        DateTime? last = null;
        try { DateTime lr = task.LastRunTime; if (lr.Year > 2000) last = lr; } catch (COMException) { }
        return new ScheduledTaskInfo((string)task.Path, (string)task.Name, author, command, (bool)task.Enabled, logon, boot, repeats, last);
    }

    /// <summary>Liga ou desliga a tarefa. Tarefas criadas para todos os usuários exigem administrador (UnauthorizedAccessException).</summary>
    public static void SetEnabled(string path, bool enabled)
    {
        dynamic? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            int cut = path.LastIndexOf('\\');
            dynamic folder = service.GetFolder(cut <= 0 ? @"\" : path[..cut]);
            dynamic task = folder.GetTask(path[(cut + 1)..]);
            task.Enabled = enabled;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005) { throw new UnauthorizedAccessException("Sem permissão para mudar esta tarefa.", ex); }
        finally { if (service is not null) Marshal.FinalReleaseComObject(service); }
    }
}
