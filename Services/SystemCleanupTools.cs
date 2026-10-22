using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>M37: lista e apaga pontos de restauração. Apagar exige administrador.</summary>
public static class RestorePoints
{
    private const string Script =
        "$p = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Select-Object SequenceNumber,Description,CreationTime); " +
        "$u = (Get-CimInstance Win32_ShadowStorage -ErrorAction SilentlyContinue | Measure-Object UsedSpace -Sum).Sum; " +
        "[pscustomobject]@{ points = $p; used = $u } | ConvertTo-Json -Depth 3 -Compress";

    public static (List<RestorePointInfo> Points, long UsedBytes) List() => RestorePointRules.ParseReport(PowerShell(Script));

    [DllImport("srclient.dll")]
    private static extern int SRRemoveRestorePoint(int sequenceNumber);

    /// <summary>Apaga um ponto pelo número de sequência. Devolve true se o Windows confirmou.</summary>
    public static bool Remove(int sequence)
    {
        try { return SRRemoveRestorePoint(sequence) == 0; }
        catch (DllNotFoundException) { return false; } // edições do Windows sem Restauração do Sistema
        catch (EntryPointNotFoundException) { return false; }
    }

    private static string PowerShell(string script)
    {
        try
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return ""; }
            return output.Result;
        }
        catch { return ""; }
    }
}

/// <summary>M36: tamanho dos logs de eventos e limpeza (wevtutil). Ver e limpar exigem administrador.</summary>
public static class EventLogs
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"winevt\Logs");

    /// <summary>Logs grandes que podem ser limpos; null quando o Windows não deixa ver a pasta (sem administrador).</summary>
    public static List<EventLogFile>? Large()
    {
        try
        {
            var files = new DirectoryInfo(Folder).EnumerateFiles("*.evtx").Select(f => (f.Name, f.Length)).ToList();
            return EventLogRules.Candidates(files);
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>Apaga os eventos do log. Devolve o código de saída do wevtutil (0 = limpo).</summary>
    public static Task<int> ClearAsync(string logName)
    {
        if (!EventLogRules.CanClear(logName)) throw new InvalidOperationException("Este log não é limpo pelo app.");
        return SystemIntegrity.RunAsync("wevtutil.exe", $"cl \"{logName.Replace("\"", "")}\"", _ => { }, CancellationToken.None);
    }
}
