using System.Diagnostics;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Verificação de integridade do Windows com SFC e DISM. Exige administrador.
/// "Verificar" só lê (sfc /verifyonly e DISM /ScanHealth); "Reparar" altera arquivos do sistema (sfc /scannow e DISM /RestoreHealth).
/// </summary>
public static class SystemIntegrity
{
    /// <summary>Roda um comando e envia cada linha de saída para a tela. Retorna o código de saída (-1 se não iniciou).</summary>
    public static async Task<int> RunAsync(string exe, string args, Action<string> onLine, CancellationToken ct, Encoding? encoding = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = encoding ?? Encoding.UTF8,
                });
                if (p is null) return -1;
                using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
                string? line;
                while ((line = p.StandardOutput.ReadLine()) is not null)
                {
                    line = line.Replace("\0", "").Trim();
                    if (line.Length > 0) onLine(line);
                }
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception ex) { onLine("Falha ao iniciar: " + ex.Message); return -1; }
        }, ct);
    }

    // sfc escreve em UTF-16
    public static Task<int> Sfc(bool repair, Action<string> onLine, CancellationToken ct) =>
        RunAsync("sfc.exe", repair ? "/scannow" : "/verifyonly", onLine, ct, Encoding.Unicode);

    public static async Task<int> Dism(bool repair, Action<string> onLine, CancellationToken ct)
    {
        string oem = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage.ToString();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(int.Parse(oem));
        return await RunAsync("dism.exe", repair ? "/Online /Cleanup-Image /RestoreHealth" : "/Online /Cleanup-Image /ScanHealth", onLine, ct, enc);
    }
}
