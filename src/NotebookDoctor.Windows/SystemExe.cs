using System.Diagnostics;

namespace NotebookDoctor.Services;

/// <summary>
/// S4 / SEC-04: resolve utilitários do próprio Windows sempre pelo caminho completo em System32, na pasta do Windows
/// ou em WindowsApps (aliases oficiais da Microsoft Store), impedindo que um binário com o mesmo nome no diretório
/// atual ou no PATH seja executado no lugar do original.
/// </summary>
public static class SystemExe
{
    /// <summary>Caminho absoluto em <c>%SystemRoot%\System32</c> para um utilitário nativo (ex.: <c>reg.exe</c>, <c>powercfg.exe</c>).</summary>
    public static string System32(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName) || exeName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(exeName))
            throw new ArgumentException("Informe apenas o nome relativo do utilitário do sistema.", nameof(exeName));
        string sys = Environment.SystemDirectory;
        return sys.Length > 0 ? Path.Combine(sys, exeName) : exeName;
    }

    /// <summary>Caminho absoluto na pasta raiz do Windows (ex.: <c>explorer.exe</c>, <c>regedit.exe</c>).</summary>
    public static string WindowsRoot(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName) || exeName.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new ArgumentException("Informe apenas o nome do executável.", nameof(exeName));
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return win.Length > 0 ? Path.Combine(win, exeName) : exeName;
    }

    /// <summary>Resolve uma ferramenta administrativa fixa em System32 ou na raiz do Windows.</summary>
    public static string ResolveWindowsTool(string fileName)
    {
        string sys = System32(fileName);
        if (File.Exists(sys)) return sys;
        return WindowsRoot(fileName);
    }

    /// <summary>
    /// Resolve um utilitário de linha de comando aceitando caminho absoluto explícito, binário em System32
    /// ou alias de execução oficial da Microsoft Store em <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>,
    /// sem recorrer ao diretório atual nem ao PATH arbitrário.
    /// </summary>
    public static string ResolveAppAliasOrSystem(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
            throw new ArgumentException("Informe o nome do executável.", nameof(exe));
        if (Path.IsPathRooted(exe)) return exe;
        if (exe.IndexOfAny(['\\', '/', ':']) >= 0 || exe.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Caminho relativo inválido para executável.", nameof(exe));

        string fileName = Path.HasExtension(exe) ? exe : exe + ".exe";
        string sys = System32(fileName);
        if (File.Exists(sys)) return sys;

        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps");
        if (Directory.Exists(windowsApps))
        {
            try
            {
                string? alias = Directory.EnumerateFileSystemEntries(windowsApps, fileName).FirstOrDefault();
                if (alias is not null) return alias;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return sys;
    }

    public static string Explorer => WindowsRoot("explorer.exe");

    public static string RegEdit => WindowsRoot("regedit.exe");

    public static string PowerShell
    {
        get
        {
            string v1 = System32(@"WindowsPowerShell\v1.0\powershell.exe");
            return File.Exists(v1) ? v1 : System32("powershell.exe");
        }
    }

    /// <summary>Abre o Explorador de Arquivos selecionando o caminho informado, sem passar por shell nem pelo PATH.</summary>
    public static void RevealInExplorer(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;
        string clean = targetPath.Replace("\"", "");
        if (clean.Length == 0) return;
        using var _ = Process.Start(new ProcessStartInfo(Explorer, $"/select,\"{clean}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.SystemDirectory,
        });
    }
}
