namespace NotebookDoctor.Services;

/// <summary>Quanto o app confia que o item é do programa desinstalado.</summary>
public enum LeftoverLevel { Safe, Moderate, Advanced }

public enum LeftoverKind { Folder, File, RegistryKey, RegistryValue, Service, Task }

/// <summary>Uma sobra encontrada depois da desinstalação. <see cref="Selected"/> é a marca na tela de revisão.</summary>
public sealed class Leftover
{
    public LeftoverKind Kind { get; init; }
    /// <summary>Pasta, arquivo, chave do registro ("HKEY_…"), chave do serviço ou caminho da tarefa no Agendador.</summary>
    public string Path { get; init; } = "";
    /// <summary>Só para valores do registro.</summary>
    public string ValueName { get; init; } = "";
    public LeftoverLevel Level { get; init; }
    public string Reason { get; init; } = "";
    public long Bytes { get; set; }
    /// <summary>Pasta de dados com arquivos que parecem ser da pessoa (documentos, fotos, saves): começa desmarcada.</summary>
    public bool MayHoldUserData { get; set; }
    public bool NeedsAdmin { get; set; }
    public bool Selected { get; set; }

    public string KindText => Kind switch
    {
        LeftoverKind.Folder => Loc.T("Pasta"),
        LeftoverKind.File => Loc.T("Arquivo"),
        LeftoverKind.RegistryKey => Loc.T("Chave do registro"),
        LeftoverKind.RegistryValue => Loc.T("Valor do registro"),
        LeftoverKind.Service => Loc.T("Serviço"),
        _ => Loc.T("Tarefa agendada"),
    };

    public string LevelText => Level switch
    {
        LeftoverLevel.Safe => Loc.T("Seguro"),
        LeftoverLevel.Moderate => Loc.T("Moderado"),
        _ => Loc.T("Avançado"),
    };

    public string Display => Kind switch
    {
        LeftoverKind.RegistryValue => $"{Path} › {ValueName}",
        LeftoverKind.Service => Paths.Leaf(Path),
        _ => Path,
    };

    public string SizeText => Bytes > 0 ? Units.Bytes(Bytes) : "";

    public string Notes => string.Join(" · ", new[]
    {
        MayHoldUserData ? Loc.T("Pode ter arquivos seus") : "",
        NeedsAdmin ? Loc.T("Exige administrador") : "",
    }.Where(n => n.Length > 0));

    /// <summary>Este item já inclui o outro: uma pasta inclui o que está dentro dela; uma chave, as subchaves e os valores.</summary>
    public bool Covers(Leftover other)
    {
        if (ReferenceEquals(this, other)) return false;
        bool fileLike = Kind is LeftoverKind.Folder or LeftoverKind.File, otherFileLike = other.Kind is LeftoverKind.Folder or LeftoverKind.File;
        if (fileLike && otherFileLike) return Kind == LeftoverKind.Folder ? Paths.IsUnder(other.Path, Path) : Paths.Same(Path, other.Path) && other.Kind == LeftoverKind.File;
        bool keyLike = Kind is LeftoverKind.RegistryKey or LeftoverKind.Service;
        if (keyLike && other.Kind is LeftoverKind.RegistryKey or LeftoverKind.RegistryValue or LeftoverKind.Service) return Paths.IsUnder(other.Path, Path);
        if (Kind == LeftoverKind.RegistryValue && other.Kind == LeftoverKind.RegistryValue)
            return Paths.Same(Path, other.Path) && string.Equals(ValueName, other.ValueName, StringComparison.OrdinalIgnoreCase);
        return Kind == LeftoverKind.Task && other.Kind == LeftoverKind.Task && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>O que a varredura consulta no Windows. A implementação real fica em NotebookDoctor.Windows; os testes usam uma falsa.</summary>
public interface ILeftoverSystem
{
    /// <summary>Nomes das subchaves; vazio quando a chave não existe ou não pode ser lida.</summary>
    IEnumerable<string> SubKeys(string key);
    bool KeyExists(string key);
    /// <summary>Valor de texto; <paramref name="name"/> null é o valor padrão da chave.</summary>
    string? StringValue(string key, string? name);
    IEnumerable<string> ValueNames(string key);
    /// <summary>Subpastas diretas (caminhos completos), sem seguir links.</summary>
    IEnumerable<string> Directories(string dir);
    bool DirectoryExists(string dir);
    /// <summary>Arquivos da pasta e das subpastas (caminhos completos).</summary>
    IEnumerable<string> Files(string dir);
    /// <summary>Tamanho do arquivo ou da pasta inteira.</summary>
    long Size(string path);
}

/// <summary>Pastas conhecidas do Windows deste usuário. <paramref name="UserContent"/>: Documentos, Imagens, OneDrive, a pasta do app etc.</summary>
public sealed record KnownPlaces(
    string Windows, string ProgramFiles, string ProgramFilesX86, string CommonFiles, string CommonFilesX86, string ProgramData,
    string UserProfile, string Roaming, string Local, string Desktop, string PublicDesktop, string StartMenu, string CommonStartMenu,
    IReadOnlyList<string> UserContent)
{
    public string LocalLow => Paths.Join(UserProfile, @"AppData\LocalLow");
    public string LocalPrograms => Paths.Join(Local, "Programs");
    public string StartMenuPrograms => Paths.Join(StartMenu, "Programs");
    public string CommonStartMenuPrograms => Paths.Join(CommonStartMenu, "Programs");

    /// <summary>Onde programas costumam criar pastas com o próprio nome (ou com o nome do editor).</summary>
    public IEnumerable<string> NameRoots => new[] { ProgramFiles, ProgramFilesX86, ProgramData, Roaming, Local, LocalLow, LocalPrograms, StartMenuPrograms, CommonStartMenuPrograms }
        .Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pastas que nunca são apagadas inteiras, mesmo vazias ou com nome igual ao do programa.</summary>
    public IEnumerable<string> Roots => new[]
    {
        Windows, ProgramFiles, ProgramFilesX86, CommonFiles, CommonFilesX86, ProgramData, UserProfile, Roaming, Local, LocalLow, LocalPrograms,
        Desktop, PublicDesktop, StartMenu, CommonStartMenu, StartMenuPrograms, CommonStartMenuPrograms,
        Paths.Join(Local, "Temp"), Paths.Join(Local, "Packages"), Paths.Join(ProgramFiles, "WindowsApps"),
    }.Concat(UserContent).Where(r => r.Length > 0);
}

public sealed record ShortcutInfo(string Path, string Target);

/// <summary>Contexto da varredura: as pastas do Windows, os outros programas instalados (para não apagar o que é deles), tarefas e atalhos.</summary>
public sealed record LeftoverContext(KnownPlaces Places, IReadOnlyList<InstalledProgram> Others, IReadOnlyList<ScheduledTaskInfo> Tasks, IReadOnlyList<ShortcutInfo> Shortcuts);

/// <summary>Proteções da varredura de sobras: o que nunca é apagado, em nenhum nível.</summary>
public static class LeftoverRules
{
    private static readonly string[] ReservedKeyNames = ["Microsoft", "Classes", "Policies", "Clients", "RegisteredApplications", "WOW6432Node", "ODBC", "Windows"];

    private static readonly string[] UserDataExtensions =
    [
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".pdf", ".rtf", ".jpg", ".jpeg", ".png", ".gif", ".bmp",
        ".tif", ".tiff", ".heic", ".raw", ".psd", ".ai", ".svg", ".mp3", ".wav", ".flac", ".m4a", ".mp4", ".mov", ".avi", ".mkv",
        ".sav", ".save", ".kdbx", ".pst", ".ost",
    ];

    private static readonly string[] UserDataFolderNames = ["save", "saves", "savegames", "projects", "projetos", "backup", "backups", "documents", "documentos"];

    /// <summary>
    /// Pasta que nunca sai: a raiz do disco, as pastas conhecidas do Windows (e as que contêm uma delas, como
    /// C:\Users), qualquer coisa dentro do Windows e o conteúdo da pessoa. Atalhos .lnk na Área de Trabalho
    /// e no Menu Iniciar podem sair.
    /// </summary>
    public static bool IsProtectedPath(string path, KnownPlaces places, bool shortcutFile = false)
    {
        string p = Paths.Clean(path);
        if (p.Length <= 3) return true;
        if (Paths.IsUnder(p, places.Windows)) return true;
        foreach (var root in places.Roots)
            if (Paths.Same(p, root) || Paths.IsUnder(root, p)) return true;
        if (places.UserContent.Any(c => Paths.IsUnder(p, c))) return true;
        bool onDesktop = Paths.IsUnder(p, places.Desktop) || Paths.IsUnder(p, places.PublicDesktop);
        return onDesktop && !(shortcutFile && p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Pasta de instalação de outro programa, ou pasta que contém ou está dentro de uma delas.</summary>
    public static bool SharedWithOther(string dir, IEnumerable<InstalledProgram> others, KnownPlaces places) =>
        others.Select(o => Paths.Clean(Environment.ExpandEnvironmentVariables(o.InstallLocation)))
              .Where(l => l.Length > 3 && !IsProtectedPath(l, places))
              .Any(l => Paths.IsUnder(l, dir) || Paths.IsUnder(dir, l));

    /// <summary>Pasta de instalação que pode guiar a varredura: nunca uma pasta protegida (um InstallLocation "C:\Program Files" acharia tudo).</summary>
    public static bool UsableOwnDir(string dir, KnownPlaces places, IEnumerable<InstalledProgram> others) =>
        !IsProtectedPath(dir, places) && !SharedWithOther(dir, others, places);

    /// <summary>Outro programa instalado é do mesmo editor: a pasta ou chave do editor é compartilhada.</summary>
    public static bool PublisherInUse(string name, IEnumerable<InstalledProgram> others) =>
        others.Any(o => ProgramNames.PublisherAliases(o.Publisher).Any(a => ProgramNames.Same(a, name)));

    /// <summary>Outro programa instalado tem esse nome.</summary>
    public static bool ProductInUse(string name, IEnumerable<InstalledProgram> others) =>
        others.Any(o => ProgramNames.Same(ProgramNames.Product(o.Name), name));

    /// <summary>
    /// Chave que nunca sai: os pontos de partida da varredura (Software, Classes, CLSID, Run, App Paths, Services),
    /// quem os contém e os nomes reservados logo abaixo de Software (Microsoft, Classes, Policies…).
    /// </summary>
    public static bool IsProtectedKey(string key, IEnumerable<string> containers, IEnumerable<string> softwareRoots)
    {
        string k = Paths.Clean(key);
        if (!k.Contains('\\')) return true;
        if (containers.Any(c => Paths.Same(k, c) || Paths.IsUnder(c, k))) return true;
        return softwareRoots.Any(r => Paths.Same(Paths.Parent(k), r) && ReservedKeyNames.Contains(Paths.Leaf(k), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Pasta de dados que parece guardar coisas da pessoa: nome de saves/projetos ou documentos, fotos, músicas dentro.</summary>
    public static bool LooksLikeUserData(string dir, IEnumerable<string> files)
    {
        if (UserDataFolderNames.Contains(Paths.Leaf(dir), StringComparer.OrdinalIgnoreCase)) return true;
        return files.Take(3000).Any(f =>
        {
            string name = Paths.Leaf(f);
            int dot = name.LastIndexOf('.');
            return dot > 0 && UserDataExtensions.Contains(name[dot..], StringComparer.OrdinalIgnoreCase)
                   || f.Split('\\').Any(part => UserDataFolderNames.Contains(part, StringComparer.OrdinalIgnoreCase));
        });
    }

    /// <summary>Apagar exige administrador: tudo fora da pasta do usuário, chaves de HKLM e serviços.</summary>
    public static bool NeedsAdmin(Leftover l, KnownPlaces places) => l.Kind switch
    {
        LeftoverKind.Folder or LeftoverKind.File => !Paths.IsUnder(l.Path, places.UserProfile),
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => l.Path.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase),
        LeftoverKind.Service => true,
        _ => false,
    };
}

/// <summary>
/// Procura o que o programa deixou para trás. Três níveis:
/// <list type="bullet">
/// <item><b>Seguro</b>: a pasta de instalação e tudo o que aponta para dentro dela (inicialização, App Paths, serviços,
/// componentes COM, tipos de arquivo, tarefas, atalhos) e a entrada de desinstalação que ficou.</item>
/// <item><b>Moderado</b>: pastas e chaves com o nome exato do programa, ou Editor\Programa.</item>
/// <item><b>Avançado</b>: a pasta ou chave do editor sem outro programa dele, e nomes parecidos. Começam desmarcados.</item>
/// </list>
/// Nenhum nível passa por cima das proteções de <see cref="LeftoverRules"/>.
/// </summary>
public static class LeftoverScanner
{
    public static readonly string[] SoftwareRoots = [@"HKEY_CURRENT_USER\Software", @"HKEY_LOCAL_MACHINE\SOFTWARE", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node"];

    private const string CurrentVersion = @"Microsoft\Windows\CurrentVersion";
    private static readonly string[] RunKeys = SoftwareRoots.SelectMany(r => new[] { $@"{r}\{CurrentVersion}\Run", $@"{r}\{CurrentVersion}\RunOnce" }).ToArray();
    private static readonly string[] AppPathsKeys = SoftwareRoots.Select(r => $@"{r}\{CurrentVersion}\App Paths").ToArray();
    private static readonly string[] ClassesRoots = SoftwareRoots.Select(r => $@"{r}\Classes").ToArray();
    private const string ServicesKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services";

    /// <summary>Nomes em Classes que não são tipos de arquivo de um programa.</summary>
    private static readonly HashSet<string> SpecialClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLSID", "Interface", "TypeLib", "AppID", "Applications", "SystemFileAssociations", "Directory", "Folder", "Drive", "*",
        "AllFilesystemObjects", "Local Settings", "MIME", "Protocols", "PackagedCom", "ActivatableClasses", "Extensions",
    };

    /// <summary>Containers que a varredura percorre: nunca podem sair inteiros.</summary>
    public static IEnumerable<string> Containers =>
        SoftwareRoots.Concat(RunKeys).Concat(AppPathsKeys).Concat(ClassesRoots).Concat(ClassesRoots.Select(c => c + @"\CLSID"))
                     .Concat(ClassesRoots.Select(c => c + @"\Applications")).Concat(SoftwareRoots.Select(r => $@"{r}\{CurrentVersion}\Uninstall"))
                     .Append(ServicesKey);

    public static List<Leftover> Scan(ProgramFingerprint fp, LeftoverContext ctx, ILeftoverSystem sys, CancellationToken ct = default)
    {
        var places = ctx.Places;
        var others = ctx.Others.Where(o => o.Id != fp.Id).ToList();
        var own = fp.Dirs.Where(d => LeftoverRules.UsableOwnDir(d, places, others)).ToList();
        var found = new List<Leftover>();

        bool Owned(string? command) => CommandLine.ExecutablePath(command) is { Length: > 0 } exe && own.Any(d => Paths.IsUnder(exe, d));
        void Add(LeftoverKind kind, string path, LeftoverLevel level, string reason, string valueName = "") =>
            found.Add(new Leftover { Kind = kind, Path = path, ValueName = valueName, Level = level, Reason = reason });

        // Seguro: a pasta de instalação
        foreach (var d in own)
            if (sys.DirectoryExists(d)) Add(LeftoverKind.Folder, d, LeftoverLevel.Safe, Loc.T("Pasta de instalação do programa"));

        // Moderado e avançado: pastas com o nome do programa ou do editor
        bool named = ProgramNames.Usable(fp.Product) && !LeftoverRules.ProductInUse(fp.Product, others);
        string? firstWord = ProgramNames.FirstWord(fp.Product);
        if (named)
            foreach (var root in places.NameRoots)
                foreach (var dir in sys.Directories(root))
                {
                    ct.ThrowIfCancellationRequested();
                    string leaf = Paths.Leaf(dir);
                    if (ProgramNames.Same(leaf, fp.Product)) Add(LeftoverKind.Folder, dir, LeftoverLevel.Moderate, Loc.T("Pasta com o nome do programa"));
                    else if (fp.Publishers.Any(p => ProgramNames.Same(leaf, p)))
                    {
                        foreach (var sub in sys.Directories(dir))
                            if (ProgramNames.Same(Paths.Leaf(sub), fp.Product)) Add(LeftoverKind.Folder, sub, LeftoverLevel.Moderate, Loc.T("Pasta do programa dentro da pasta do editor"));
                        if (!LeftoverRules.PublisherInUse(leaf, others)) Add(LeftoverKind.Folder, dir, LeftoverLevel.Advanced, Loc.T("Pasta do editor (nenhum outro programa dele está instalado)"));
                    }
                    else if (firstWord is not null && ProgramNames.Same(leaf, firstWord) && !LeftoverRules.ProductInUse(leaf, others))
                        Add(LeftoverKind.Folder, dir, LeftoverLevel.Advanced, Loc.T("Nome parecido com o do programa"));
                }

        // Moderado e avançado: chaves com o nome do programa ou do editor
        if (named)
            foreach (var root in SoftwareRoots)
                foreach (var name in sys.SubKeys(root))
                {
                    ct.ThrowIfCancellationRequested();
                    string key = $@"{root}\{name}";
                    if (ProgramNames.Same(name, fp.Product)) Add(LeftoverKind.RegistryKey, key, LeftoverLevel.Moderate, Loc.T("Chave com o nome do programa"));
                    else if (fp.Publishers.Any(p => ProgramNames.Same(name, p)))
                    {
                        foreach (var sub in sys.SubKeys(key))
                            if (ProgramNames.Same(sub, fp.Product)) Add(LeftoverKind.RegistryKey, $@"{key}\{sub}", LeftoverLevel.Moderate, Loc.T("Chave do programa dentro da chave do editor"));
                        if (!LeftoverRules.PublisherInUse(name, others)) Add(LeftoverKind.RegistryKey, key, LeftoverLevel.Advanced, Loc.T("Chave do editor (nenhum outro programa dele está instalado)"));
                    }
                    else if (firstWord is not null && ProgramNames.Same(name, firstWord) && !LeftoverRules.ProductInUse(name, others))
                        Add(LeftoverKind.RegistryKey, key, LeftoverLevel.Advanced, Loc.T("Nome parecido com o do programa"));
                }

        // Seguro: entrada de desinstalação que ficou
        if (fp.RegistryPath.Length > 0 && sys.KeyExists(fp.RegistryPath))
            Add(LeftoverKind.RegistryKey, fp.RegistryPath, LeftoverLevel.Safe, Loc.T("Entrada de desinstalação que ficou para trás"));

        if (own.Count > 0)
        {
            foreach (var key in AppPathsKeys)
                foreach (var sub in sys.SubKeys(key))
                    if (Owned(sys.StringValue($@"{key}\{sub}", null))) Add(LeftoverKind.RegistryKey, $@"{key}\{sub}", LeftoverLevel.Safe, Loc.T("Atalho de execução (App Paths) do programa"));

            foreach (var key in RunKeys)
                foreach (var value in sys.ValueNames(key))
                    if (Owned(sys.StringValue(key, value))) Add(LeftoverKind.RegistryValue, key, LeftoverLevel.Safe, Loc.T("Item de inicialização do programa"), value);

            foreach (var name in sys.SubKeys(ServicesKey))
            {
                ct.ThrowIfCancellationRequested();
                if (Owned(sys.StringValue($@"{ServicesKey}\{name}", "ImagePath"))) Add(LeftoverKind.Service, $@"{ServicesKey}\{name}", LeftoverLevel.Safe, Loc.T("Serviço do programa"));
            }

            foreach (var classes in ClassesRoots)
            {
                foreach (var id in sys.SubKeys(classes + @"\CLSID"))
                {
                    ct.ThrowIfCancellationRequested();
                    string key = $@"{classes}\CLSID\{id}";
                    if (Owned(sys.StringValue(key + @"\InprocServer32", null)) || Owned(sys.StringValue(key + @"\LocalServer32", null)))
                        Add(LeftoverKind.RegistryKey, key, LeftoverLevel.Safe, Loc.T("Componente COM do programa"));
                }
                foreach (var name in sys.SubKeys(classes))
                {
                    ct.ThrowIfCancellationRequested();
                    if (name.StartsWith('.') || SpecialClasses.Contains(name)) continue;
                    if (Owned(sys.StringValue($@"{classes}\{name}\shell\open\command", null)))
                        Add(LeftoverKind.RegistryKey, $@"{classes}\{name}", LeftoverLevel.Safe, Loc.T("Tipo de arquivo registrado pelo programa"));
                }
                foreach (var app in sys.SubKeys(classes + @"\Applications"))
                    if (Owned(sys.StringValue($@"{classes}\Applications\{app}\shell\open\command", null)))
                        Add(LeftoverKind.RegistryKey, $@"{classes}\Applications\{app}", LeftoverLevel.Safe, Loc.T("Registro do programa em \"Abrir com\""));
            }

            foreach (var t in ctx.Tasks)
                if (Owned(t.Command)) Add(LeftoverKind.Task, t.Path, LeftoverLevel.Safe, Loc.T("Tarefa agendada do programa"));
            foreach (var s in ctx.Shortcuts)
                if (own.Any(d => Paths.IsUnder(s.Target, d))) Add(LeftoverKind.File, s.Path, LeftoverLevel.Safe, Loc.T("Atalho para o programa"));
        }

        return Finish(found, ctx, sys, others);
    }

    /// <summary>Tira o que é protegido e o que já está incluído num item mais amplo, mede, marca e ordena.</summary>
    private static List<Leftover> Finish(List<Leftover> found, LeftoverContext ctx, ILeftoverSystem sys, List<InstalledProgram> others)
    {
        var places = ctx.Places;
        var containers = Containers.ToList();
        var result = new List<Leftover>();
        foreach (var l in found.OrderBy(l => l.Level).ThenBy(l => l.Path.Length))
        {
            bool isPath = l.Kind is LeftoverKind.Folder or LeftoverKind.File;
            if (isPath && LeftoverRules.IsProtectedPath(l.Path, places, shortcutFile: l.Kind == LeftoverKind.File)) continue;
            if (l.Kind == LeftoverKind.Folder && LeftoverRules.SharedWithOther(l.Path, others, places)) continue;
            if (l.Kind is LeftoverKind.RegistryKey && LeftoverRules.IsProtectedKey(l.Path, containers, SoftwareRoots)) continue;
            if (result.Any(r => r.Level <= l.Level && (r.Covers(l) || SameItem(r, l)))) continue;
            result.Add(l);
        }
        foreach (var l in result)
        {
            if (l.Kind is LeftoverKind.Folder or LeftoverKind.File) l.Bytes = sys.Size(l.Path);
            // Só pastas de dados achadas pelo nome: a de instalação tem ícones e imagens do próprio programa
            if (l.Kind == LeftoverKind.Folder && l.Level != LeftoverLevel.Safe && !Paths.IsUnder(l.Path, places.ProgramFiles) && !Paths.IsUnder(l.Path, places.ProgramFilesX86))
                l.MayHoldUserData = LeftoverRules.LooksLikeUserData(l.Path, sys.Files(l.Path));
            l.NeedsAdmin = LeftoverRules.NeedsAdmin(l, places);
            l.Selected = l.Level != LeftoverLevel.Advanced && !l.MayHoldUserData;
        }
        return result.OrderBy(l => l.Level).ThenBy(l => l.Kind).ThenBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool SameItem(Leftover a, Leftover b) =>
        a.Kind == b.Kind && Paths.Same(a.Path, b.Path) && string.Equals(a.ValueName, b.ValueName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Itens do nível escolhido na revisão: Seguro mostra só os seguros; Avançado mostra tudo.</summary>
    public static IEnumerable<Leftover> UpTo(IEnumerable<Leftover> items, LeftoverLevel level) => items.Where(l => l.Level <= level);

    /// <summary>
    /// Ordem para apagar: serviços e tarefas primeiro (param de usar os arquivos), depois valores, chaves (das mais
    /// fundas para cima), arquivos e pastas.
    /// </summary>
    public static List<Leftover> RemovalOrder(IEnumerable<Leftover> items) =>
        items.OrderBy(l => l.Kind switch
              {
                  LeftoverKind.Service => 0, LeftoverKind.Task => 1, LeftoverKind.RegistryValue => 2, LeftoverKind.RegistryKey => 3,
                  LeftoverKind.File => 4, _ => 5,
              })
             .ThenByDescending(l => l.Path.Count(c => c == '\\'))
             .ToList();
}
