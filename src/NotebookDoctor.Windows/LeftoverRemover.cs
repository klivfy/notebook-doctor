using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

public sealed record RemovalResult(Leftover Item, bool Ok, bool PendingReboot, string Error);

/// <summary>
/// Apaga as sobras escolhidas na revisão, guardando cada uma no backup antes. Um item que não pôde ir para o backup
/// não é apagado. Arquivos em uso, com administrador, saem no próximo reinício.
/// </summary>
public static class LeftoverRemover
{
    /// <summary>Pastas maiores que isso vão para a Lixeira em vez de serem copiadas para o backup.</summary>
    public const long RecycleAbove = 500L << 20;

    public static List<RemovalResult> Remove(IEnumerable<Leftover> items, BackupSession backup, bool isAdmin, CancellationToken ct = default)
    {
        var results = new List<RemovalResult>();
        foreach (var l in LeftoverScanner.RemovalOrder(items))
        {
            ct.ThrowIfCancellationRequested();
            try { results.Add(RemoveOne(l, backup, isAdmin)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                results.Add(new RemovalResult(l, false, false, Loc.T("Sem permissão: exige administrador.")));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or COMException or ArgumentException)
            {
                results.Add(new RemovalResult(l, false, false, ex.Message));
            }
        }
        return results;
    }

    private static RemovalResult Ok(Leftover l) => new(l, true, false, "");

    private static RemovalResult RemoveOne(Leftover l, BackupSession backup, bool isAdmin)
    {
        switch (l.Kind)
        {
            case LeftoverKind.Service:
            {
                string name = Paths.Leaf(l.Path);
                BackupStore.Run("sc.exe", $"stop \"{name}\"", 30_000);
                backup.AddKey(l.Path, BackupKind.Service);
                int code = BackupStore.Run("sc.exe", $"delete \"{name}\"");
                if (code == 1060) { RegistryPaths.DeleteTree(l.Path); return Ok(l); } // não estava registrado: sobrou só a chave
                if (code == 1072) return new RemovalResult(l, true, true, ""); // marcado para sair no reinício
                return code == 0 ? Ok(l) : new RemovalResult(l, false, false, Loc.F("O Windows não removeu o serviço (código {0}).", code));
            }
            case LeftoverKind.Task:
            {
                if (TaskSchedulerCom.Xml(l.Path) is not { } xml) return Ok(l); // já não existe
                backup.AddTask(l.Path, xml);
                TaskSchedulerCom.Delete(l.Path);
                return Ok(l);
            }
            case LeftoverKind.RegistryValue:
            {
                using var k = RegistryPaths.Open(l.Path, writable: true);
                if (k?.GetValue(l.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } data) return Ok(l);
                backup.AddValue(l.Path, l.ValueName, k.GetValueKind(l.ValueName), data);
                k.DeleteValue(l.ValueName, throwOnMissingValue: false);
                return Ok(l);
            }
            case LeftoverKind.RegistryKey:
            {
                using (var k = RegistryPaths.Open(l.Path)) if (k is null) return Ok(l);
                backup.AddKey(l.Path);
                RegistryPaths.DeleteTree(l.Path);
                return Ok(l);
            }
            case LeftoverKind.File:
            {
                if (!File.Exists(l.Path)) return Ok(l);
                backup.AddFile(l.Path);
                return DeleteFile(l, isAdmin);
            }
            default:
            {
                if (!Directory.Exists(l.Path)) return Ok(l);
                if (l.Bytes > RecycleAbove)
                {
                    if (!Recycle(l.Path)) return new RemovalResult(l, false, false, Loc.T("A Lixeira não aceitou a pasta."));
                    backup.AddRecycled(l.Path, l.Bytes);
                    return Ok(l);
                }
                backup.AddFolder(l.Path);
                return DeleteFolder(l, isAdmin);
            }
        }
    }

    private static RemovalResult DeleteFile(Leftover l, bool isAdmin)
    {
        try
        {
            File.SetAttributes(l.Path, FileAttributes.Normal);
            File.Delete(l.Path);
            return Ok(l);
        }
        catch (IOException) when (isAdmin && MoveFileEx(l.Path, null, DelayUntilReboot))
        {
            return new RemovalResult(l, true, true, "");
        }
    }

    /// <summary>
    /// Apaga arquivo por arquivo; os que estão em uso saem no reinício (com administrador) ou ficam, e a pasta fica
    /// junto. Links e junções dentro da pasta são removidos sem entrar neles: o destino pode ser de outra pessoa ou programa.
    /// </summary>
    private static RemovalResult DeleteFolder(Leftover l, bool isAdmin)
    {
        int busy = 0;
        bool pending = false;

        void Fail(string path)
        {
            if (isAdmin && MoveFileEx(path, null, DelayUntilReboot)) pending = true;
            else busy++;
        }

        void Tree(string dir)
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                try
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        if (entry is DirectoryInfo) Directory.Delete(entry.FullName); // só o link
                        else File.Delete(entry.FullName);
                    }
                    else if (entry is DirectoryInfo sub) Tree(sub.FullName);
                    else
                    {
                        entry.Attributes = FileAttributes.Normal;
                        entry.Delete();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(entry.FullName); }
            }
            try { Directory.Delete(dir); }
            catch (IOException) { if (isAdmin && MoveFileEx(dir, null, DelayUntilReboot)) pending = true; } // um arquivo pendente segura a pasta até o reinício
        }

        Tree(l.Path);
        return busy > 0
            ? new RemovalResult(l, false, false, Loc.F("{0} arquivos em uso: feche o programa e tente de novo.", busy))
            : new RemovalResult(l, true, pending, "");
    }

    internal static bool Recycle(string path)
    {
        var op = new ShFileOp
        {
            wFunc = FoDelete,
            pFrom = path + "\0\0",
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    private const int DelayUntilReboot = 0x4;
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? newName, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOp op);
}

/// <summary>Tarefas do Agendador pela interface COM do Windows: o XML para o backup e a exclusão.</summary>
internal static class TaskSchedulerCom
{
    private static T With<T>(string path, Func<dynamic, string, T> action)
    {
        dynamic? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect();
            int cut = path.LastIndexOf('\\');
            dynamic folder = service.GetFolder(cut <= 0 ? @"\" : path[..cut]);
            return action(folder, path[(cut + 1)..]);
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005) { throw new UnauthorizedAccessException(ex.Message, ex); }
        finally { if (service is not null) Marshal.FinalReleaseComObject(service); }
    }

    public static string? Xml(string path)
    {
        try { return With(path, (folder, name) => (string)folder.GetTask(name).Xml); }
        catch (COMException ex) when ((uint)ex.HResult is 0x80070002 or 0x80070003) { return null; } // a tarefa ou a pasta não existe
    }

    public static void Delete(string path) => With(path, (folder, name) => { folder.DeleteTask(name, 0); return 0; });
}
