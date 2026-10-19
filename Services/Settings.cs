using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

public static class AppPaths
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookDoctor");
    public static string Settings => Path.Combine(Dir, "settings.json");
    public static string History => Path.Combine(Dir, "history.jsonl");
    public static string Reports => Path.Combine(Dir, "relatorios");
    static AppPaths() => Directory.CreateDirectory(Dir);

    /// <summary>
    /// Onde salvar relatórios, CSV e pacotes: Documentos\Notebook Doctor. Se o Windows bloquear a gravação
    /// (ex.: "Acesso controlado a pastas" do Defender), usa a pasta de dados do app, que sempre permite.
    /// </summary>
    public static string ExportDir()
    {
        try
        {
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Notebook Doctor");
            Directory.CreateDirectory(docs);
            string probe = Path.Combine(docs, ".teste-gravacao");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return docs;
        }
        catch
        {
            string local = Path.Combine(Dir, "exportacoes");
            Directory.CreateDirectory(local);
            return local;
        }
    }
}

public sealed class AppSettings
{
    public string ThemeMode { get; set; } = "system"; // system | light | dark
    public int LastTab { get; set; }
    public string ActiveProfile { get; set; } = "work";
    public bool NativeNotifications { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public DateTime LastWeeklySummary { get; set; } = DateTime.MinValue;
    public bool RestorePointBeforeChanges { get; set; } = true;
    public DateTime LastRestorePoint { get; set; } = DateTime.MinValue;
    public bool StartWithWindows { get; set; }
    public bool AlertsEnabled { get; set; } = true;
    public bool WeeklyCleanup { get; set; }
    public int TempDays { get; set; } = 7;
    public int CpuAlertPct { get; set; } = 80;
    public int RamAlertPct { get; set; } = 90;
    public DateTime LastWeeklyRun { get; set; } = DateTime.MinValue;
    /// <summary>M20: programas que nunca geram aviso (ex.: renderizador que deve pesar mesmo).</summary>
    public List<string> AlertExceptions { get; set; } = new();
    /// <summary>M21: programas que o app nunca oferece encerrar nem tem a prioridade reduzida (além dos do sistema).</summary>
    public List<string> ExtraProtected { get; set; } = new();
    /// <summary>M02: mini-widget flutuante e a posição dele na tela.</summary>
    public bool MiniWidget { get; set; }
    public double? MiniLeft { get; set; } // null = ainda não foi movido (JSON não aceita NaN)
    public double? MiniTop { get; set; }
    /// <summary>A50: procurar versão nova uma vez por dia.</summary>
    public bool CheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
    /// <summary>A38: mostra o que cada correção faria, sem alterar nada.</summary>
    public bool SimulationMode { get; set; }
    /// <summary>M01: Ctrl+Alt+N abre ou esconde o painel de qualquer lugar.</summary>
    public bool GlobalHotkey { get; set; } = true;

    public bool IsAlertException(string name) => AlertExceptions.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static AppSettings Load()
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new(); }
        catch { s = new(); }
        ProcessTools.SetUserProtected(s.ExtraProtected);
        return s;
    }

    /// <summary>Lê uma lista digitada (um nome por linha, ou separados por vírgula), sem ".exe" e sem repetidos.</summary>
    public static List<string> ParseNames(string text) =>
        text.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public void Save()
    {
        try { File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex)
        {
            // Sem permissão de escrita (ou valor inválido): as configurações valem só nesta sessão; fica registrado
            try { File.AppendAllText(Path.Combine(AppPaths.Dir, "erros.log"), $"{DateTime.Now:s} Falha ao salvar configurações: {ex.Message}\n\n"); } catch { }
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "NotebookDoctor";

    public void ApplyStartWithWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (StartWithWindows && Environment.ProcessPath is { } exe)
            key.SetValue(RunName, $"\"{exe}\" --background");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }
}
