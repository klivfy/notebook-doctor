using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

public static class AppPaths
{
    /// <summary>
    /// B43: com um arquivo "portatil.txt" (ou "portable.txt") ao lado do executável, os dados ficam na pasta "dados" junto do app,
    /// para rodar de um pendrive. O registro do Windows ainda é usado para iniciar com o Windows e para as notificações.
    /// </summary>
    public static readonly bool Portable = new[] { "portatil.txt", "portable.txt" }.Any(f => File.Exists(Path.Combine(AppContext.BaseDirectory, f)));

    /// <summary>B26: "--demo" abre com dados fictícios, numa pasta temporária própria, com a simulação sempre ligada.</summary>
    public static readonly bool Demo = Environment.GetCommandLineArgs().Contains("--demo");

    public static readonly string Dir = Demo
        ? Path.Combine(Path.GetTempPath(), "NotebookDoctor-demo")
        : Portable
        ? Path.Combine(AppContext.BaseDirectory, "dados")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookDoctor");
    public static string Settings => Path.Combine(Dir, "settings.json");
    public static string History => Path.Combine(Dir, "history.jsonl");
    public static string Reports => Path.Combine(Dir, "relatorios");
    /// <summary>Fase 5: backups da remoção de sobras (30 dias ou 2 GB).</summary>
    public static string Backups => Path.Combine(Dir, "backups");
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
    /// <summary>Leva 6.2: frequência da limpeza automática ("start", "daily", "weekly", "monthly"); só vale com <see cref="WeeklyCleanup"/> ligado.</summary>
    public string ScheduleKind { get; set; } = "weekly";
    /// <summary>A limpeza automática também limpa os navegadores (os tipos escolhidos em <see cref="BrowserKinds"/>).</summary>
    public bool ScheduleBrowser { get; set; }
    /// <summary>Tipos de dado de navegador que a limpeza apaga (nomes de BrowserDataKind). Vazio = nenhum.</summary>
    public List<string> BrowserKinds { get; set; } = new();
    /// <summary>Sites cujos cookies a limpeza mantém.</summary>
    public List<string> BrowserKeepSites { get; set; } = new();
    /// <summary>Limpa os dados escolhidos quando o navegador fecha.</summary>
    public bool BrowserCleanOnClose { get; set; }
    /// <summary>Avisa quando os temporários passam do limite (uma vez por dia).</summary>
    public bool JunkMonitor { get; set; }
    public int JunkMonitorLimitMb { get; set; } = 1024;
    public DateTime LastJunkNotice { get; set; } = DateTime.MinValue;
    /// <summary>Leva 6.1: pastas que a busca de arquivos inúteis nunca olha.</summary>
    public List<string> JunkExcluded { get; set; } = new();
    /// <summary>Leva 7.1: versões que a pessoa dispensou, no formato "id|versão". Uma versão mais nova volta a aparecer.</summary>
    public List<string> UpdateSkipped { get; set; } = new();
    /// <summary>Leva 7.1: programas cujas atualizações a pessoa pausou (ids do WinGet).</summary>
    public List<string> UpdatePaused { get; set; } = new();
    /// <summary>Leva 7.1: a pessoa leu e aceitou os termos da fonte do WinGet (nunca é aceito pelo app em nome dela).</summary>
    public bool WingetSourceTerms { get; set; }
    /// <summary>Leva 7.3: aplicativos que o modo soneca nunca adormece (chave = nome sem símbolos, minúsculo).</summary>
    public List<string> SnoozeNever { get; set; } = new();
    /// <summary>Leva 7.3: ao pôr um aplicativo para dormir, o padrão é acordá-lo quando a pessoa abrir.</summary>
    public bool SnoozeWakeOnOpen { get; set; } = true;
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
    public bool SimulationMode
    {
        get => _simulation || AppPaths.Demo; // B26: na demonstração nem importar ou redefinir as preferências desliga a simulação
        set => _simulation = value;
    }
    private bool _simulation;
    /// <summary>M01: Ctrl+Alt+N abre ou esconde o painel de qualquer lugar.</summary>
    public bool GlobalHotkey { get; set; } = true;

    /// <summary>M09: usa a cor de destaque do Windows nos botões e seleções (ajustada para manter o contraste).</summary>
    public bool UseAccentColor { get; set; }
    /// <summary>M44: horário sem notificações (os avisos continuam no histórico).</summary>
    public bool QuietHours { get; set; }
    public string QuietStart { get; set; } = "22:00";
    public string QuietEnd { get; set; } = "07:00";
    /// <summary>M43: sem notificações com app em tela cheia, jogo ou apresentação.</summary>
    public bool QuietFullscreen { get; set; } = true;
    /// <summary>M45: minutos seguidos acima do limite antes de avisar de CPU ou RAM alta.</summary>
    public int AlertMinutes { get; set; } = 3;
    /// <summary>M19: guardar relatórios de falha (sem dados pessoais) para o pacote de suporte. Desligado até a pessoa ligar.</summary>
    public bool CrashReports { get; set; }
    public DateTime LastCrashSeen { get; set; } = DateTime.MinValue;
    /// <summary>B07/B45: posição, tamanho e monitor da janela (área sem maximizar) para reabrir no mesmo lugar.</summary>
    public double? WinLeft { get; set; }
    public double? WinTop { get; set; }
    public double? WinWidth { get; set; }
    public double? WinHeight { get; set; }
    public bool WinMaximized { get; set; }
    /// <summary>M03: overlay de desempenho ("off", "fullscreen" ou "always") e o canto da tela.</summary>
    public string OverlayMode { get; set; } = Overlay.Off;
    public string OverlayCorner { get; set; } = "top-left";
    /// <summary>B16/B17: "windows", "decimal" ou "binary"; "c" ou "f".</summary>
    public string SizeUnit { get; set; } = "windows";
    public string TempUnit { get; set; } = "c";
    /// <summary>B09: ao abrir pelo atalho, a janela começa minimizada na barra de tarefas.</summary>
    public bool StartMinimized { get; set; }
    /// <summary>B10: o X da janela encerra o app em vez de deixá-lo na bandeja.</summary>
    public bool CloseExits { get; set; }
    /// <summary>B15: menu só com ícones, tabelas e cartões mais densos.</summary>
    public bool Compact { get; set; }
    /// <summary>Redesign V3: "rail" (trilho com ícone sobre o rótulo, padrão do Windows 11) ou "expanded" (lateral larga com ícone e texto).</summary>
    public string NavStyle { get; set; } = "rail";
    /// <summary>B29/B30/B31: ordenação, colunas ocultas e larguras de coluna de cada tabela.</summary>
    public Dictionary<string, GridLayout> Grids { get; set; } = new();
    /// <summary>B01: número da CPU no ícone da bandeja.</summary>
    public bool TrayCpu { get; set; }
    /// <summary>B37: com o tema "schedule", claro a partir de ThemeLightAt e escuro a partir de ThemeDarkAt.</summary>
    public string ThemeLightAt { get; set; } = "07:00";
    public string ThemeDarkAt { get; set; } = "19:00";
    /// <summary>B49: o aviso "continua na bandeja" já apareceu uma vez.</summary>
    public bool TrayHintShown { get; set; }
    /// <summary>B13: som discreto ao terminar uma limpeza.</summary>
    public bool PlaySounds { get; set; }
    /// <summary>B36: confirmações não destrutivas que a pessoa pediu para não repetir.</summary>
    public List<string> SkipConfirm { get; set; } = new();
    /// <summary>M13: "auto" (idioma do Windows), "pt", "en" ou "es". Vale ao reabrir o app.</summary>
    public string Language { get; set; } = Loc.Auto;
    /// <summary>M22: na aba Processos, junta processos do mesmo aplicativo (Steam + steamwebhelper).</summary>
    public bool GroupByApp { get; set; } = true;
    /// <summary>B38: hash da senha das configurações (PasswordHash); null = sem senha.</summary>
    public string? SettingsLock { get; set; }

    /// <summary>Tema que vale agora ("system", "light" ou "dark"), resolvendo o modo por horário.</summary>
    public string EffectiveTheme(DateTime now) => ThemeSchedule.Resolve(ThemeMode, TimeOnly.FromDateTime(now), ThemeLightAt, ThemeDarkAt);

    /// <summary>Aplica as unidades escolhidas aos textos (o Core lê de <see cref="Units"/>).</summary>
    public void ApplyUnits()
    {
        Units.Size = Units.ParseSize(SizeUnit);
        Units.Temperature = Units.ParseTemp(TempUnit);
    }

    public bool InQuietHours(DateTime now) =>
        QuietHours && AlertPolicy.TryParseClock(QuietStart, out var a) && AlertPolicy.TryParseClock(QuietEnd, out var b)
        && AlertPolicy.InQuietHours(TimeOnly.FromDateTime(now), a, b);

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
        if (AppPaths.Demo) return; // a demonstração não mexe no registro
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (StartWithWindows && Environment.ProcessPath is { } exe)
            key.SetValue(RunName, $"\"{exe}\" --background");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }
}
