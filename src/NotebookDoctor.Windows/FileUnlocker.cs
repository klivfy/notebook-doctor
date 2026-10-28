using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NotebookDoctor.Services;

/// <summary>Resultado de fechar um programa que travava o arquivo.</summary>
public enum CloseResult { Closed, AlreadyGone, NotAllowed, Failed }

/// <summary>Resultado de ejetar uma unidade removível.</summary>
public enum EjectResult { Ejected, InUse, NotRemovable, Failed }

/// <summary>
/// Fase 10, leva 10.1: quem está usando um arquivo, uma pasta ou um pendrive, pelo Gerenciador de Reinicialização do
/// Windows (Restart Manager, a mesma API documentada que os instaladores usam para pedir "feche estes programas"). Só
/// leitura, até a pessoa confirmar o fechamento de um programa; protegidos e serviços nunca são encerrados.
/// </summary>
public static class FileUnlocker
{
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] fileNames, uint apps, IntPtr appList, uint services, IntPtr serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? list, ref uint rebootReasons);

    /// <summary>Os arquivos a perguntar: o próprio arquivo, ou os de dentro da pasta ou unidade (sem seguir junções), até o limite.</summary>
    public static List<string> FilesOf(string path, LockTargetKind kind)
    {
        if (kind == LockTargetKind.File) return [path];
        string root = kind == LockTargetKind.Drive ? FileLockRules.DriveRoot(path) : path;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try { return Directory.EnumerateFiles(root, "*", opts).Take(FileLockRules.MaxFiles).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Quem está segurando estes arquivos agora. Lista vazia: ninguém (ou o Windows não respondeu).</summary>
    public static List<LockingProcess> WhoIsUsing(IReadOnlyList<string> files)
    {
        var found = new List<LockingProcess>();
        if (files.Count == 0) return found;
        if (RmStartSession(out uint session, 0, new StringBuilder(33)) != 0) return found;
        try
        {
            if (RmRegisterResources(session, (uint)files.Count, files.ToArray(), 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return found;
            uint reasons = 0, count = 0;
            RmProcessInfo[]? list = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int rc = RmGetList(session, out uint needed, ref count, list, ref reasons);
                if (rc == 0) break;
                if (rc != ErrorMoreData) return found;
                count = needed;
                list = new RmProcessInfo[needed];
            }
            if (list is null) return found;
            for (int i = 0; i < count; i++)
            {
                var info = list[i];
                long start = ((long)(uint)info.Process.StartTime.dwHighDateTime << 32) | (uint)info.Process.StartTime.dwLowDateTime;
                found.Add(new LockingProcess(info.Process.ProcessId, ImageName(info.Process.ProcessId, info.AppName), info.AppName,
                    Enum.IsDefined(typeof(LockAppType), info.ApplicationType) ? (LockAppType)info.ApplicationType : LockAppType.Unknown,
                    info.ServiceShortName ?? "", start));
            }
        }
        finally { RmEndSession(session); }
        return found;
    }

    private static string ImageName(int pid, string fallback)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return FileLockRules.ProcessName(fallback); }
    }

    /// <summary>
    /// Fecha o programa: primeiro pede para fechar a janela (ele pode perguntar se quer salvar), e só encerra se em 5 s não
    /// fechou. Protegidos, serviços, o Explorador (que se reinicia) e o próprio app nunca passam daqui.
    /// </summary>
    public static CloseResult Close(LockingProcess target)
    {
        if (FileLockRules.Classify(target, Environment.ProcessId) != LockVerdict.CanClose) return CloseResult.NotAllowed;
        Process p;
        try { p = Process.GetProcessById(target.Pid); }
        catch (ArgumentException) { return CloseResult.AlreadyGone; }
        using (p)
        {
            try
            {
                // Outro programa pode ter herdado o PID depois da consulta
                if (target.StartFileTime != 0 && p.StartTime.ToFileTimeUtc() != target.StartFileTime) return CloseResult.AlreadyGone;
                if (p.HasExited) return CloseResult.AlreadyGone;
                if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(5000)) return CloseResult.Closed;
                p.Kill();
                return p.WaitForExit(5000) ? CloseResult.Closed : CloseResult.Failed;
            }
            catch (InvalidOperationException) { return CloseResult.AlreadyGone; }
            catch (System.ComponentModel.Win32Exception) { return CloseResult.Failed; }
        }
    }

    /// <summary>Reinicia o Explorador de Arquivos: encerra e, se o Windows não o abrir sozinho em 3 s, abre de novo.</summary>
    public static bool RestartExplorer()
    {
        var all = Process.GetProcessesByName("explorer");
        try
        {
            foreach (var p in all)
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var p in all) p.Dispose(); }
        for (int i = 0; i < 6; i++)
        {
            Thread.Sleep(500);
            var again = Process.GetProcessesByName("explorer");
            foreach (var p in again) p.Dispose();
            if (again.Length > 0) return true;
        }
        try
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var started = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return started is not null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    // ---------- ejetar pendrive ----------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 0x3, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020, IoctlMediaRemoval = 0x002D4804, IoctlEjectMedia = 0x002D4808;

    /// <summary>Unidades removíveis (pendrives, cartões) prontas para ejetar.</summary>
    public static List<DriveInfo> RemovableDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Ejeta como o "Remover hardware com segurança": trava o volume (falha se algum programa ainda usa a unidade),
    /// desmonta e pede ao dispositivo para ejetar. Nada é apagado.
    /// </summary>
    public static EjectResult Eject(char letter)
    {
        string root = $"{char.ToUpperInvariant(letter)}:\\";
        try { if (new DriveInfo(root).DriveType != DriveType.Removable) return EjectResult.NotRemovable; }
        catch (ArgumentException) { return EjectResult.NotRemovable; }
        using var h = CreateFileW($@"\\.\{char.ToUpperInvariant(letter)}:", GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid) return EjectResult.Failed;
        bool locked = false;
        for (int i = 0; i < 10 && !locked; i++)
        {
            locked = DeviceIoControl(h, FsctlLockVolume, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            if (!locked) Thread.Sleep(200);
        }
        if (!locked) return EjectResult.InUse;
        if (!DeviceIoControl(h, FsctlDismountVolume, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)) return EjectResult.Failed;
        DeviceIoControl(h, IoctlMediaRemoval, [0], 1, IntPtr.Zero, 0, out _, IntPtr.Zero); // PREVENT_MEDIA_REMOVAL = falso
        return DeviceIoControl(h, IoctlEjectMedia, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero) ? EjectResult.Ejected : EjectResult.Failed;
    }
}
