using System.Runtime.CompilerServices;

namespace NotebookDoctor.Services;

/// <summary>
/// Prioridade 5 (D2/C14): o motor de consulta ao catálogo do WinGet usado pelo app. COM primeiro; se o Instalador de
/// Aplicativo não tiver o servidor COM ou a ativação falhar, a linha de comando responde no lugar.
/// </summary>
public static class UpdateEngines
{
    public static FailoverUpdateEngine Shared { get; } = new(TryCreateCom(), new CliUpdateEngine());

    // Isolado: se a DLL da projeção COM faltar, a falha de carga acontece aqui dentro e não derruba o app
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IUpdateEngine? TryCreateCom()
    {
        try { return new ComUpdateEngine(); }
        catch (Exception) { return null; }
    }
}
