namespace NotebookDoctor.Services;

public enum Safety { Safe, Careful, Never, Unknown }

public sealed record ProcessAbout(string Description, Safety Safety)
{
    public string SafetyText => Safety switch
    {
        Safety.Safe => "Sim, pode fechar",
        Safety.Careful => "Com cuidado",
        Safety.Never => "Não encerrar",
        _ => "Desconhecido",
    };
}

/// <summary>Descrição em português dos processos mais comuns e se é seguro encerrá-los.</summary>
public static class ProcessKnowledge
{
    private static readonly Dictionary<string, ProcessAbout> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navegadores e apps do dia a dia: fechar é seguro, só perde o que não foi salvo
        ["chrome"] = new("Navegador Google Chrome (cada aba e extensão é um processo)", Safety.Safe),
        ["msedge"] = new("Navegador Microsoft Edge", Safety.Safe),
        ["msedgewebview2"] = new("Componente web usado por vários aplicativos (Edge WebView2)", Safety.Careful),
        ["firefox"] = new("Navegador Mozilla Firefox", Safety.Safe),
        ["brave"] = new("Navegador Brave", Safety.Safe),
        ["spotify"] = new("Spotify (música)", Safety.Safe),
        ["discord"] = new("Discord (bate-papo e voz)", Safety.Safe),
        ["slack"] = new("Slack (mensagens de trabalho)", Safety.Safe),
        ["teams"] = new("Microsoft Teams", Safety.Safe),
        ["ms-teams"] = new("Microsoft Teams", Safety.Safe),
        ["zoom"] = new("Zoom (reuniões)", Safety.Safe),
        ["whatsapp"] = new("WhatsApp Desktop", Safety.Safe),
        ["telegram"] = new("Telegram Desktop", Safety.Safe),
        ["onedrive"] = new("OneDrive: sincroniza arquivos. Encerrar pausa a sincronização", Safety.Careful),
        ["dropbox"] = new("Dropbox: sincroniza arquivos", Safety.Careful),
        ["outlook"] = new("Microsoft Outlook (e-mail)", Safety.Safe),
        ["winword"] = new("Microsoft Word: salve o documento antes", Safety.Careful),
        ["excel"] = new("Microsoft Excel: salve a planilha antes", Safety.Careful),
        ["powerpnt"] = new("Microsoft PowerPoint: salve antes", Safety.Careful),
        ["code"] = new("Visual Studio Code (editor)", Safety.Safe),
        ["devenv"] = new("Visual Studio: salve o projeto antes", Safety.Careful),
        ["node"] = new("Node.js: servidor ou ferramenta de desenvolvimento", Safety.Careful),
        ["dotnet"] = new(".NET: aplicativo ou compilação em andamento", Safety.Careful),
        ["docker desktop"] = new("Docker Desktop (contêineres)", Safety.Careful),
        ["vmmem"] = new("Máquina virtual do WSL/Docker: usa a RAM reservada", Safety.Careful),
        ["vmmemwsl"] = new("Máquina virtual do WSL: usa a RAM reservada", Safety.Careful),
        ["claude"] = new("Claude (aplicativo de IA)", Safety.Safe),
        ["powershell"] = new("Janela do PowerShell", Safety.Careful),
        ["cmd"] = new("Prompt de comando", Safety.Careful),
        ["notepad"] = new("Bloco de Notas", Safety.Safe),
        ["steam"] = new("Steam (jogos)", Safety.Safe),
        ["epicgameslauncher"] = new("Epic Games Launcher", Safety.Safe),

        // Componentes do Windows: não encerrar
        ["system"] = new("Núcleo do Windows", Safety.Never),
        ["svchost"] = new("Hospeda serviços do Windows (vários processos são normais)", Safety.Never),
        ["explorer"] = new("Área de trabalho, barra de tarefas e Explorador de Arquivos", Safety.Never),
        ["dwm"] = new("Gerenciador de janelas do Windows (desenha a tela)", Safety.Never),
        ["csrss"] = new("Subsistema do Windows", Safety.Never),
        ["lsass"] = new("Segurança e logon do Windows", Safety.Never),
        ["winlogon"] = new("Logon do Windows", Safety.Never),
        ["services"] = new("Gerenciador de serviços do Windows", Safety.Never),
        ["smss"] = new("Gerenciador de sessões do Windows", Safety.Never),
        ["wininit"] = new("Inicialização do Windows", Safety.Never),
        ["memory compression"] = new("Compressão de memória do Windows: mantém mais coisas na RAM", Safety.Never),
        ["registry"] = new("Registro do Windows", Safety.Never),
        ["msmpeng"] = new("Windows Defender (antivírus em tempo real)", Safety.Never),
        ["securityhealthservice"] = new("Segurança do Windows", Safety.Never),
        ["searchindexer"] = new("Indexação de arquivos da busca do Windows: usa disco por um tempo e depois se acalma", Safety.Careful),
        ["searchhost"] = new("Busca do Windows", Safety.Careful),
        ["runtimebroker"] = new("Gerencia permissões de aplicativos da Microsoft Store", Safety.Never),
        ["textinputhost"] = new("Teclado de toque, emojis e ditado do Windows", Safety.Careful),
        ["ctfmon"] = new("Métodos de entrada de texto e idioma", Safety.Never),
        ["audiodg"] = new("Isolamento de áudio do Windows", Safety.Never),
        ["fontdrvhost"] = new("Gerenciador de fontes do Windows", Safety.Never),
        ["sihost"] = new("Interface do shell do Windows", Safety.Never),
        ["taskhostw"] = new("Tarefas em segundo plano do Windows", Safety.Never),
        ["wmiprvse"] = new("Provedor de informações do sistema (WMI)", Safety.Careful),
        ["dptf_helper"] = new("Controle térmico e de energia da Intel (Dynamic Tuning)", Safety.Never),
        ["notebookdoctor"] = new("Este aplicativo", Safety.Never),
    };

    public static ProcessAbout Describe(string name) =>
        ProcessTools.IsUserProtected(name) ? new((Known.TryGetValue(name, out var k) ? k.Description + ". " : "") + "Protegido por você em Configurações", Safety.Never)
        : Known.TryGetValue(name, out var a) ? a
        : ProcessTools.IsSystem(name) ? new("Componente do sistema", Safety.Never)
        : new("Sem descrição na base do app. Pesquise o nome antes de encerrar", Safety.Unknown);
}
