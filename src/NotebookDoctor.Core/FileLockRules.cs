namespace NotebookDoctor.Services;

/// <summary>O que a pessoa quer destravar: um arquivo, uma pasta ou uma unidade inteira (pendrive).</summary>
public enum LockTargetKind { File, Folder, Drive }

/// <summary>Tipo de programa, como o Gerenciador de Reinicialização do Windows (Restart Manager) informa.</summary>
public enum LockAppType { Unknown = 0, MainWindow = 1, OtherWindow = 2, Service = 3, Explorer = 4, Console = 5, Critical = 1000 }

/// <summary>Um programa que está segurando o arquivo, a pasta ou a unidade.</summary>
/// <param name="StartFileTime">Hora em que o processo começou (FILETIME UTC): impede fechar outro programa que herdou o mesmo PID.</param>
public sealed record LockingProcess(int Pid, string Name, string Title, LockAppType Type, string ServiceName = "", long StartFileTime = 0);

/// <summary>O que o app pode fazer com o programa que trava.</summary>
public enum LockVerdict
{
    /// <summary>Pode fechar (com confirmação; trabalho não salvo se perde).</summary>
    CanClose,
    /// <summary>É o Explorador de Arquivos: não se encerra, se reinicia (barra de tarefas e janelas de pasta voltam sozinhas).</summary>
    RestartExplorer,
    /// <summary>Serviço do Windows ou de um programa: parar pode tirar algo do ar; o app não para sozinho.</summary>
    Service,
    /// <summary>Do sistema ou protegido pela pessoa: nunca encerrado.</summary>
    Protected,
    /// <summary>O próprio Notebook Doctor.</summary>
    Self,
}

/// <summary>
/// Fase 10, leva 10.1: regras do destravador ("Quem está usando este arquivo?"). O executor (FileUnlocker) pergunta ao
/// Restart Manager do Windows quem segura cada arquivo; aqui fica o que pode ser registrado e o que fazer com cada programa.
/// </summary>
public static class FileLockRules
{
    /// <summary>
    /// Máximo de arquivos de uma pasta ou unidade perguntados ao Windows. O Restart Manager aceita mais, mas cada arquivo
    /// custa; numa unidade cheia, os primeiros milhares já mostram quem trava a ejeção na prática.
    /// </summary>
    public const int MaxFiles = 4000;

    public static LockVerdict Classify(LockingProcess p, int selfPid)
    {
        if (p.Pid == selfPid) return LockVerdict.Self;
        if (p.Type == LockAppType.Critical || p.Pid is 0 or 4) return LockVerdict.Protected;
        if (p.Type == LockAppType.Explorer || p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return LockVerdict.RestartExplorer;
        if (ProcessTools.IsProtected(p.Name)) return LockVerdict.Protected;
        if (p.Type == LockAppType.Service || p.ServiceName.Length > 0) return LockVerdict.Service;
        return LockVerdict.CanClose;
    }

    /// <summary>Nome sem ".exe", como a tabela de Processos e a lista de protegidos usam.</summary>
    public static string ProcessName(string imageName)
    {
        // Separadores do Windows à mão: o Core também roda (e é testado) no Linux
        string n = imageName.Trim();
        n = n[(n.LastIndexOfAny(['\\', '/']) + 1)..];
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    /// <summary>Texto da coluna "O que fazer", na língua do app.</summary>
    public static string VerdictText(LockVerdict v) => v switch
    {
        LockVerdict.CanClose => Loc.T("Pode fechar (salve antes o que estiver aberto nele)"),
        LockVerdict.RestartExplorer => Loc.T("Explorador de Arquivos: reiniciar libera; a barra de tarefas volta sozinha"),
        LockVerdict.Service => Loc.T("Serviço: feche o programa dono dele ou reinicie o computador"),
        LockVerdict.Protected => Loc.T("Do sistema: não é encerrado"),
        _ => Loc.T("O próprio Notebook Doctor"),
    };

    /// <summary>
    /// Tipo do alvo a partir do caminho: "E:", "E:\" é unidade; o resto é arquivo ou pasta conforme existe. Null quando o
    /// caminho é vazio, relativo ou não existe.
    /// </summary>
    public static LockTargetKind? KindOf(string path, Func<string, bool> fileExists, Func<string, bool> dirExists)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) return null;
        if (IsDriveRoot(path)) return dirExists(DriveRoot(path)) ? LockTargetKind.Drive : null;
        bool full = path.StartsWith(@"\\", StringComparison.Ordinal)
                    || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');
        if (!full) return null;
        if (fileExists(path)) return LockTargetKind.File;
        return dirExists(path) ? LockTargetKind.Folder : null;
    }

    public static bool IsDriveRoot(string path)
    {
        path = path.Trim();
        return path.Length is 2 or 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] is '\\' or '/');
    }

    /// <summary>"e:" vira "E:\".</summary>
    public static string DriveRoot(string path) => $"{char.ToUpperInvariant(path.Trim()[0])}:\\";

    /// <summary>Um por PID; primeiro os que podem ser fechados, depois por nome.</summary>
    public static List<LockingProcess> Order(IEnumerable<LockingProcess> found, int selfPid) =>
        found.DistinctBy(p => p.Pid)
            .OrderBy(p => Classify(p, selfPid) == LockVerdict.CanClose ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
}
