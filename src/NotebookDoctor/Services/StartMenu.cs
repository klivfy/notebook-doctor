using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// B21: cria, confere e remove o atalho do app no Menu Iniciar do usuário. Usa a interface IShellLink do Windows,
/// grava só na pasta Programas do próprio usuário e não precisa de administrador.
/// </summary>
public static class StartMenu
{
    public static string LinkPath => StartMenuLink.PathIn(Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    public static StartMenuState Current()
    {
        try
        {
            string path = LinkPath;
            bool exists = File.Exists(path);
            return StartMenuLink.State(exists, exists ? ReadTarget(path) : null, Environment.ProcessPath ?? "");
        }
        catch { return StartMenuState.Missing; }
    }

    /// <summary>Cria ou substitui o atalho, apontando para o executável que está rodando.</summary>
    public static void Create()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Não foi possível descobrir onde está o executável.");
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? "");
            link.SetDescription("Monitor de desempenho e correções seguras");
            link.SetIconLocation(exe, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);
            ((IPersistFile)link).Save(LinkPath, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    public static void Remove()
    {
        if (File.Exists(LinkPath)) File.Delete(LinkPath);
    }

    /// <summary>Destino do atalho, mesmo quando o arquivo de destino já não existe.</summary>
    internal static string ReadTarget(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0); // STGM_READ
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // A ordem dos métodos tem que ser a da vtable de IShellLinkW (shobjidl_core.h)
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
