namespace NotebookDoctor.Services;

public enum StartMenuState { Missing, Ok, Stale }

/// <summary>
/// B21: atalho no Menu Iniciar. Ainda não há instalador, então o próprio app cria o atalho, a pedido, na pasta
/// Programas do usuário (sem administrador). "Stale" é um atalho que aponta para outro executável
/// (o app mudou de pasta, ex.: versão portátil copiada para outro lugar).
/// </summary>
public static class StartMenuLink
{
    public const string FileName = "Notebook Doctor.lnk";

    public static string PathIn(string programsDir) => programsDir.TrimEnd('\\', '/') + "\\" + FileName;

    public static StartMenuState State(bool exists, string? target, string currentExe)
    {
        if (!exists) return StartMenuState.Missing;
        return string.Equals(Normalize(target), Normalize(currentExe), StringComparison.OrdinalIgnoreCase)
            ? StartMenuState.Ok : StartMenuState.Stale;
    }

    public static string ButtonText(StartMenuState s) => s switch
    {
        StartMenuState.Ok => "Remover do Menu Iniciar",
        StartMenuState.Stale => "Atualizar atalho do Menu Iniciar",
        _ => "Adicionar ao Menu Iniciar",
    };

    public static string Describe(StartMenuState s) => s switch
    {
        StartMenuState.Ok => "O Notebook Doctor está no Menu Iniciar.",
        StartMenuState.Stale => "O atalho do Menu Iniciar aponta para outra pasta. Atualize para abrir esta cópia do app.",
        _ => "O Notebook Doctor ainda não está no Menu Iniciar.",
    };

    private static string Normalize(string? path) => (path ?? "").Trim().Trim('"').Replace('/', '\\');
}
