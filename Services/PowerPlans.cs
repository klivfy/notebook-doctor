using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Lista e troca planos de energia via powercfg (não exige administrador para o usuário atual).</summary>
public static class PowerPlans
{
    private const string HighPerformanceTemplate = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private static readonly Regex Line = new(@"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s+\((.+?)\)\s*(\*)?", RegexOptions.Compiled);

    static PowerPlans() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    private static string Run(string args)
    {
        try
        {
            // powercfg escreve na página de código OEM do console (ex.: 850 no Brasil), não em UTF-8
            var oem = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", args)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = oem });
            string output = p!.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch { return ""; }
    }

    public static List<PowerPlan> List() =>
        Run("/list").Split('\n').Select(l => Line.Match(l)).Where(m => m.Success)
            .Select(m => new PowerPlan(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Success)).ToList();

    public static bool SetActive(string guid)
    {
        if (!Guid.TryParse(guid, out _)) return false; // nunca repassa texto livre ao powercfg
        Run($"/setactive {guid}");
        return List().Any(p => p.Active && p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Remove um plano (o Windows não deixa apagar o plano ativo).</summary>
    public static bool Delete(string guid)
    {
        if (!Guid.TryParse(guid, out _)) return false;
        Run($"/delete {guid}");
        return !List().Any(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cria o plano "Alto desempenho" (cópia do modelo do Windows) quando ele não existe.</summary>
    public static PowerPlan? CreateHighPerformance()
    {
        var before = List().Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Run($"/duplicatescheme {HighPerformanceTemplate}");
        return List().FirstOrDefault(p => !before.Contains(p.Guid));
    }
}
