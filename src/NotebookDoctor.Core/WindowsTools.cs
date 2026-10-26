namespace NotebookDoctor.Services;

/// <summary>
/// Uma ferramenta que o Windows já traz. <see cref="File"/> é um nome fixo dentro de System32 (nunca vem de digitação); <see cref="Arguments"/>
/// também é fixo. <see cref="Optional"/>: algumas edições do Windows não a têm (a Política de Grupo, por exemplo, não vem no Home).
/// </summary>
public sealed record WindowsTool(string Id, string Name, string Description, string Group, string File, string Arguments = "", bool Optional = false);

/// <summary>Leva 8.4 (R16): atalhos para as ferramentas do Windows. O app só abre; quem pede permissão de administrador é a própria ferramenta.</summary>
public static class WindowsTools
{
    public static readonly IReadOnlyList<WindowsTool> All =
    [
        new("devices", "Gerenciador de Dispositivos", "Drivers e dispositivos instalados", "Sistema", "devmgmt.msc"),
        new("services", "Serviços", "Serviços do Windows e de programas", "Sistema", "services.msc"),
        new("tasks", "Agendador de Tarefas", "Tarefas que rodam sozinhas", "Sistema", "taskschd.msc"),
        new("events", "Visualizador de Eventos", "Registros de erros e avisos do Windows", "Sistema", "eventvwr.msc"),
        new("resmon", "Monitor de Recursos", "CPU, memória, disco e rede em detalhe", "Sistema", "resmon.exe"),
        new("msinfo", "Informações do Sistema", "Resumo técnico do computador", "Sistema", "msinfo32.exe"),
        new("msconfig", "Configuração do Sistema", "Inicialização e serviços (diagnóstico)", "Sistema", "msconfig.exe"),
        new("taskmgr", "Gerenciador de Tarefas", "Processos e desempenho", "Sistema", "taskmgr.exe"),
        new("env", "Variáveis de Ambiente", "Variáveis do usuário e do sistema", "Sistema", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
        new("power", "Opções de Energia", "Planos de energia e suspensão", "Sistema", "powercfg.cpl"),
        new("regedit", "Editor do Registro", "Cuidado: alterar o registro pode quebrar o Windows", "Avançado", "regedit.exe"),
        new("gpedit", "Política de Grupo", "Políticas locais (só em algumas edições)", "Avançado", "gpedit.msc", Optional: true),
        new("diskmgmt", "Gerenciamento de Disco", "Partições, unidades e volumes", "Discos", "diskmgmt.msc"),
        new("cleanmgr", "Limpeza de Disco do Windows", "A ferramenta de limpeza do próprio Windows", "Discos", "cleanmgr.exe", Optional: true),
        new("restore", "Restauração do Sistema", "Voltar o Windows a um ponto anterior", "Discos", "rstrui.exe"),
        new("firewall", "Firewall do Windows", "Regras de entrada e saída", "Rede e segurança", "wf.msc"),
        new("network", "Conexões de Rede", "Adaptadores de rede", "Rede e segurança", "ncpa.cpl"),
        new("accounts", "Contas de Usuário", "Contas e senhas do computador", "Rede e segurança", "netplwiz.exe"),
        new("programs", "Programas e Recursos", "Desinstalar programas (lista clássica)", "Programas", "appwiz.cpl"),
        new("features", "Recursos do Windows", "Ligar ou desligar recursos opcionais", "Programas", "OptionalFeatures.exe"),
        new("memtest", "Diagnóstico de Memória", "Testa a memória no próximo reinício", "Diagnóstico", "mdsched.exe"),
    ];

    private static readonly string[] AllowedExtensions = [".exe", ".msc", ".cpl"];

    /// <summary>O nome do arquivo é simples (sem caminho) e de um dos três tipos conhecidos: nada que a pessoa digitou chega aqui.</summary>
    public static bool IsSafe(WindowsTool t) =>
        t.File.Length > 0 && t.File.IndexOfAny(['\\', '/', ':', '"', ' ']) < 0 && AllowedExtensions.Contains(Path.GetExtension(t.File), StringComparer.OrdinalIgnoreCase)
        && (t.Arguments.Length == 0 || (t.File.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) && t.Arguments.StartsWith("sysdm.cpl,", StringComparison.Ordinal)));

    public static IEnumerable<WindowsTool> Available(Func<string, bool> exists) => All.Where(t => exists(t.File));

    public static IEnumerable<IGrouping<string, WindowsTool>> Grouped(IEnumerable<WindowsTool> tools) =>
        tools.GroupBy(t => t.Group).OrderBy(g => Array.IndexOf(GroupOrder, g.Key) is var i && i < 0 ? 99 : i);

    private static readonly string[] GroupOrder = ["Sistema", "Discos", "Rede e segurança", "Programas", "Diagnóstico", "Avançado"];
}
