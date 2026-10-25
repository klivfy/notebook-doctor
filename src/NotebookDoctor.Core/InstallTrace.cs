using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>FNV-1a de 64 bits: rápido e sem dependência. Serve para saber se um valor mudou, não para segurança.</summary>
public static class Fnv
{
    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037;
        foreach (char c in s) { h ^= c; h *= 1099511628211; }
        return h;
    }
}

/// <summary>Valor do registro reduzido a dois carimbos: o nome (sem diferenciar maiúsculas) e o conteúdo (tipo e dados).</summary>
public readonly record struct RegValueStamp(ulong Name, ulong Data);

/// <summary>
/// Leva 5.4: fotografia de partes do registro. Guarda só carimbos (16 bytes por valor), para caber na memória
/// mesmo com centenas de milhares de chaves. As chaves de <see cref="TraceRules.WatchedKeys"/> guardam os dados
/// inteiros, para dar para voltar um PATH alterado pela instalação.
/// </summary>
public sealed class RegistrySnapshot
{
    public Dictionary<string, RegValueStamp[]> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Watched { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="values">Nome e conteúdo de cada valor ("tipo:dados").</param>
    public void Add(string key, IEnumerable<(string Name, string Data)> values)
    {
        var list = values.ToList();
        Keys[key] = list.Select(v => new RegValueStamp(Fnv.Hash(v.Name.ToLowerInvariant()), Fnv.Hash(v.Data))).OrderBy(s => s.Name).ToArray();
        if (TraceRules.WatchedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            Watched[key] = list.ToDictionary(v => v.Name, v => v.Data, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record TraceValue(string Key, string Name, string? Old = null);

public sealed record RegistryChanges(List<string> CreatedKeys, List<TraceValue> CreatedValues, List<TraceValue> ModifiedValues, int DeletedKeys);

/// <summary>Um arquivo ou pasta que o diário do NTFS mostrou mudando durante a instalação.</summary>
public sealed record UsnRecord(ulong FileRef, ulong ParentRef, long Usn, uint Reason, uint Attributes, string Name);

/// <summary>O que aconteceu com cada arquivo no fim: criado (e ainda existe) ou só alterado.</summary>
public sealed record UsnChange(ulong FileRef, string Name, bool Directory, bool Created);

/// <summary>
/// Leva 5.4: lê o diário de mudanças do NTFS (USN). Os registros vêm em ordem: o último de cada arquivo diz como ele
/// terminou (apagado, renomeado, só fechado).
/// </summary>
public static class UsnJournal
{
    public const uint DataOverwrite = 0x1, DataExtend = 0x2, DataTruncation = 0x4, FileCreate = 0x100, FileDelete = 0x200,
                      RenameOldName = 0x1000, RenameNewName = 0x2000, Close = 0x80000000;
    private const uint DirectoryAttribute = 0x10;
    private const uint Writes = DataOverwrite | DataExtend | DataTruncation | FileCreate | RenameNewName;

    /// <summary>Saída do FSCTL_READ_USN_JOURNAL: 8 bytes com o próximo USN e depois registros USN_RECORD_V2.</summary>
    public static (long Next, List<UsnRecord> Records) Parse(ReadOnlySpan<byte> buffer)
    {
        var list = new List<UsnRecord>();
        if (buffer.Length < 8) return (0, list);
        long next = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        int pos = 8;
        while (pos + 60 <= buffer.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[pos..]);
            if (length < 60 || pos + length > buffer.Length) break;
            var r = buffer.Slice(pos, length);
            if (BinaryPrimitives.ReadUInt16LittleEndian(r[4..]) == 2)
            {
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(r[56..]);
                int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(r[58..]);
                if (nameOffset + nameLength <= length)
                    list.Add(new UsnRecord(
                        BinaryPrimitives.ReadUInt64LittleEndian(r[8..]), BinaryPrimitives.ReadUInt64LittleEndian(r[16..]),
                        BinaryPrimitives.ReadInt64LittleEndian(r[24..]), BinaryPrimitives.ReadUInt32LittleEndian(r[40..]),
                        BinaryPrimitives.ReadUInt32LittleEndian(r[52..]), Encoding.Unicode.GetString(r.Slice(nameOffset, nameLength))));
            }
            pos += length;
        }
        return (next, list);
    }

    /// <summary>
    /// Um resultado por arquivo: o que foi apagado no fim (temporários do instalador) sai; o que foi criado e continua
    /// existindo é "criado"; o resto que recebeu escrita é "alterado".
    /// </summary>
    public static List<UsnChange> Summarize(IEnumerable<UsnRecord> records)
    {
        var result = new List<UsnChange>();
        foreach (var g in records.GroupBy(r => r.FileRef))
        {
            var ordered = g.OrderBy(r => r.Usn).ToList();
            var last = ordered[^1];
            if ((last.Reason & (FileDelete | RenameOldName)) != 0 && (last.Reason & RenameNewName) == 0) continue;
            uint all = ordered.Aggregate(0u, (acc, r) => acc | r.Reason);
            if ((all & Writes) == 0) continue;
            result.Add(new UsnChange(g.Key, last.Name, (last.Attributes & DirectoryAttribute) != 0, (all & FileCreate) != 0));
        }
        return result;
    }
}

/// <summary>Leva 5.4: o que uma instalação monitorada mudou, ligado ao programa pela chave de desinstalação que ela criou.</summary>
public sealed class InstallTrace
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Installer { get; set; } = "";
    public string UninstallKey { get; set; } = "";
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public List<string> CreatedKeys { get; set; } = new();
    public List<TraceValue> CreatedValues { get; set; } = new();
    public List<TraceValue> ModifiedValues { get; set; } = new();
    public List<string> CreatedFolders { get; set; } = new();
    public List<string> CreatedFiles { get; set; } = new();
    public List<string> ModifiedFiles { get; set; } = new();
    public int DeletedKeys { get; set; }
    /// <summary>Veio de outro computador: as sobras dele entram como Moderado (desmarcáveis), não como Seguro.</summary>
    public bool Imported { get; set; }

    public int Count => CreatedKeys.Count + CreatedValues.Count + CreatedFolders.Count + CreatedFiles.Count;
}

public static class TraceRules
{
    public const string ServicesKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services";

    /// <summary>Pontos de partida da fotografia do registro.</summary>
    public static readonly string[] Roots =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE", ServicesKey, @"HKEY_CURRENT_USER\Software", @"HKEY_CURRENT_USER\Environment",
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
    ];

    /// <summary>Chaves guardadas inteiras (o PATH): uma alteração delas pode ser desfeita.</summary>
    public static readonly string[] WatchedKeys = [@"HKEY_CURRENT_USER\Environment", @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"];

    /// <summary>Partes do registro que o Windows e outros programas mexem o tempo todo: ruído, nunca efeito da instalação.</summary>
    private static readonly string[] NoiseKeys =
    [
        @"\Microsoft\Windows\CurrentVersion\Explorer\", @"\Microsoft\Windows\CurrentVersion\Diagnostics", @"\Microsoft\Windows\CurrentVersion\Notifications",
        @"\Microsoft\Windows\CurrentVersion\CloudStore", @"\Microsoft\Windows\CurrentVersion\Search", @"\Microsoft\Windows\CurrentVersion\WindowsUpdate",
        @"\Microsoft\Windows\CurrentVersion\DeliveryOptimization", @"\Microsoft\Windows\CurrentVersion\Group Policy", @"\Microsoft\Windows\CurrentVersion\Installer",
        @"\Microsoft\Windows NT\CurrentVersion\Prefetcher", @"\Microsoft\Windows NT\CurrentVersion\AppCompatFlags", @"\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache",
        @"\Microsoft\Windows NT\CurrentVersion\NetworkList", @"\Microsoft\Windows Defender", @"\Microsoft\Cryptography\RNG", @"\Microsoft\SystemCertificates",
        @"\Microsoft\Tracing", @"\Microsoft\RADAR", @"\Microsoft\WBEM", @"\Microsoft\Windows Search", @"\Microsoft\Office\", @"\Microsoft\Edge",
        @"\Microsoft\OneDrive", @"\Microsoft\Windows\Shell", @"\Microsoft\Speech", @"\Microsoft\Input", @"\Microsoft\InputPersonalization",
        @"\Classes\Local Settings", @"\Microsoft\Windows\CurrentVersion\Shell Extensions\Cached", @"\NotebookDoctor",
    ];

    public static bool IsNoiseKey(string key) => NoiseKeys.Any(n => key.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pastas que mudam sozinhas (temporários, caches, logs do Windows, a pasta do próprio app).</summary>
    public static bool IsNoisePath(string path, KnownPlaces places, string appDataDir)
    {
        string p = Paths.Clean(path);
        if (p.Contains(@"\$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || p.Contains(@"\System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
        if (Paths.IsUnder(p, Paths.Join(places.Local, "Temp")) || Paths.IsUnder(p, appDataDir)) return true;
        string[] noise =
        [
            Paths.Join(places.Windows, "Prefetch"), Paths.Join(places.Windows, "Logs"), Paths.Join(places.Windows, "Temp"), Paths.Join(places.Windows, "SoftwareDistribution"),
            Paths.Join(places.Windows, "ServiceProfiles"), Paths.Join(places.Windows, @"System32\winevt"), Paths.Join(places.Windows, @"System32\config"),
            Paths.Join(places.Windows, @"System32\LogFiles"), Paths.Join(places.ProgramData, @"Microsoft\Windows Defender"), Paths.Join(places.ProgramData, @"Microsoft\Search"),
            Paths.Join(places.ProgramData, @"Microsoft\Windows\WER"), Paths.Join(places.Local, @"Microsoft\Windows"), Paths.Join(places.Local, @"Microsoft\Edge"),
            Paths.Join(places.Local, @"Google\Chrome"), Paths.Join(places.Local, @"Mozilla\Firefox"), Paths.Join(places.Local, "Packages"),
            Paths.Join(places.Roaming, @"Microsoft\Windows\Recent"), Paths.Join(places.Local, @"Microsoft\OneDrive"),
        ];
        return noise.Any(n => Paths.IsUnder(p, n));
    }

    /// <summary>
    /// Diferença entre as fotografias. <paramref name="currentNames"/> lê os nomes dos valores da chave agora, para
    /// dizer quais valores são novos (a fotografia só guarda carimbos).
    /// </summary>
    public static RegistryChanges Diff(RegistrySnapshot before, RegistrySnapshot after, Func<string, IReadOnlyList<string>> currentNames)
    {
        var createdKeys = new List<string>();
        var createdValues = new List<TraceValue>();
        var modified = new List<TraceValue>();
        foreach (var (key, stamps) in after.Keys)
        {
            if (IsNoiseKey(key)) continue;
            if (!before.Keys.TryGetValue(key, out var old)) { createdKeys.Add(key); continue; }
            if (stamps.AsSpan().SequenceEqual(old)) continue;
            var oldByName = old.ToDictionary(s => s.Name, s => s.Data);
            foreach (var name in currentNames(key))
            {
                var stamp = stamps.FirstOrDefault(s => s.Name == Fnv.Hash(name.ToLowerInvariant()));
                if (!oldByName.TryGetValue(stamp.Name, out ulong oldData)) createdValues.Add(new TraceValue(key, name));
                else if (oldData != stamp.Data)
                    modified.Add(new TraceValue(key, name, before.Watched.TryGetValue(key, out var w) && w.TryGetValue(name, out var o) ? o : null));
            }
        }
        // Só a chave mais alta criada: as de dentro saem junto com ela
        var created = Paths.TopLevel(createdKeys);
        int deleted = before.Keys.Keys.Count(k => !after.Keys.ContainsKey(k) && !IsNoiseKey(k));
        return new RegistryChanges(created, createdValues, modified, deleted);
    }

    /// <summary>Monta o trace: tira ruído, deixa só a pasta mais alta criada e liga ao programa pela chave de desinstalação nova.</summary>
    public static InstallTrace Build(string installer, DateTime started, DateTime finished, RegistryChanges registry,
                                     IEnumerable<(string Path, bool Directory, bool Created)> files, KnownPlaces places, string appDataDir,
                                     Func<string, string?> displayNameOf)
    {
        var list = files.Where(f => !IsNoisePath(f.Path, places, appDataDir)).ToList();
        var createdDirs = list.Where(f => f.Created && f.Directory).Select(f => Paths.Clean(f.Path)).ToList();
        var topDirs = Paths.TopLevel(createdDirs);
        bool InCreatedDir(string path) => topDirs.Any(d => Paths.IsUnder(path, d));
        string? uninstall = registry.CreatedKeys.FirstOrDefault(k => LeftoverScanner.SoftwareRoots.Any(r => Paths.Same(Paths.Parent(k), $@"{r}\Microsoft\Windows\CurrentVersion\Uninstall")));
        string name = (uninstall is null ? null : displayNameOf(uninstall)) ?? Path.GetFileNameWithoutExtension(installer.Replace('\\', '/'));
        return new InstallTrace
        {
            Id = BackupRules.NewId(started, name),
            Name = name,
            Installer = installer,
            UninstallKey = uninstall ?? "",
            Started = started,
            Finished = finished,
            CreatedKeys = registry.CreatedKeys,
            CreatedValues = registry.CreatedValues,
            ModifiedValues = registry.ModifiedValues,
            DeletedKeys = registry.DeletedKeys,
            CreatedFolders = topDirs,
            CreatedFiles = list.Where(f => f.Created && !f.Directory && !InCreatedDir(f.Path)).Select(f => Paths.Clean(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
            ModifiedFiles = list.Where(f => !f.Created && !f.Directory).Select(f => Paths.Clean(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>
    /// Leva 5.4b: o que a instalação criou e ainda está lá vira sobra de nível Seguro. Nunca entra: algo dentro do
    /// Windows (drivers, DLLs do sistema), pastas protegidas e os containers do registro. Valores alterados só são
    /// informados: voltar um valor alterado é escolha da pessoa, fora daqui.
    /// </summary>
    public static List<Leftover> ToLeftovers(InstallTrace trace, KnownPlaces places, IReadOnlyList<InstalledProgram> others, ILeftoverSystem sys)
    {
        var list = new List<Leftover>();
        var level = trace.Imported ? LeftoverLevel.Moderate : LeftoverLevel.Safe;
        string reason = trace.Imported ? Loc.T("Criado pela instalação monitorada em outro computador") : Loc.T("Criado pela instalação monitorada");
        void Add(LeftoverKind kind, string path, string valueName = "") =>
            list.Add(new Leftover { Kind = kind, Path = path, ValueName = valueName, Level = level, Reason = reason });
        foreach (var d in trace.CreatedFolders)
            if (sys.DirectoryExists(d) && !LeftoverRules.IsProtectedPath(d, places) && !LeftoverRules.SharedWithOther(d, others, places)) Add(LeftoverKind.Folder, d);
        foreach (var f in trace.CreatedFiles)
            if (sys.FileExists(f) && !LeftoverRules.IsProtectedPath(f, places, shortcutFile: true) && !Paths.IsUnder(f, places.Windows)
                && !LeftoverRules.SharedWithOther(Paths.Parent(f), others, places)) Add(LeftoverKind.File, f);
        var containers = LeftoverScanner.Containers.ToList();
        foreach (var k in trace.CreatedKeys.Where(sys.KeyExists))
        {
            bool service = Paths.Same(Paths.Parent(k), ServicesKey);
            if (!service && LeftoverRules.IsProtectedKey(k, containers, LeftoverScanner.SoftwareRoots)) continue;
            Add(service ? LeftoverKind.Service : LeftoverKind.RegistryKey, k);
        }
        foreach (var v in trace.CreatedValues.Where(v => sys.ValueNames(v.Key).Contains(v.Name, StringComparer.OrdinalIgnoreCase)))
            Add(LeftoverKind.RegistryValue, v.Key, v.Name);
        foreach (var l in list)
        {
            if (l.Kind is LeftoverKind.Folder or LeftoverKind.File) l.Bytes = sys.Size(l.Path);
            l.NeedsAdmin = LeftoverRules.NeedsAdmin(l, places);
        }
        return list;
    }

    /// <summary>Junta as sobras do trace com as da varredura comum, sem repetir o que um item já inclui.</summary>
    public static List<Leftover> Merge(IEnumerable<Leftover> fromTrace, IEnumerable<Leftover> fromScan)
    {
        var result = new List<Leftover>();
        foreach (var l in fromTrace.Concat(fromScan).OrderBy(l => l.Level).ThenBy(l => l.Path.Length))
        {
            if (result.Any(r => r.Level <= l.Level && (r.Covers(l) || (r.Kind == l.Kind && Paths.Same(r.Path, l.Path) && string.Equals(r.ValueName, l.ValueName, StringComparison.OrdinalIgnoreCase))))) continue;
            l.Selected = l.Level != LeftoverLevel.Advanced && !l.MayHoldUserData;
            result.Add(l);
        }
        return result;
    }

    /// <summary>Relatório em texto (exportar e "Ver detalhes").</summary>
    public static string Report(InstallTrace t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(t.Name);
        sb.AppendLine(Loc.F("Instalador: {0}", t.Installer));
        sb.AppendLine(Loc.F("Monitorado de {0} a {1}", t.Started.ToString("g"), t.Finished.ToString("t")));
        if (t.UninstallKey.Length > 0) sb.AppendLine(Loc.F("Entrada de desinstalação: {0}", t.UninstallKey));
        void Section(string title, IEnumerable<string> items)
        {
            var l = items.ToList();
            if (l.Count == 0) return;
            sb.AppendLine().AppendLine($"{title} ({l.Count:N0})");
            foreach (var i in l) sb.AppendLine("  " + i);
        }
        Section(Loc.T("Pastas criadas"), t.CreatedFolders);
        Section(Loc.T("Arquivos criados fora dessas pastas"), t.CreatedFiles);
        Section(Loc.T("Chaves do registro criadas"), t.CreatedKeys);
        Section(Loc.T("Valores do registro criados"), t.CreatedValues.Select(v => $"{v.Key} › {v.Name}"));
        Section(Loc.T("Valores do registro alterados (não são desfeitos sozinhos)"), t.ModifiedValues.Select(v => $"{v.Key} › {v.Name}"));
        Section(Loc.T("Arquivos alterados (não são desfeitos sozinhos)"), t.ModifiedFiles);
        if (t.DeletedKeys > 0) sb.AppendLine().AppendLine(Loc.F("Chaves do registro apagadas durante a instalação: {0}", t.DeletedKeys));
        return sb.ToString();
    }

    public static string Html(InstallTrace t) =>
        $"<!doctype html><html lang=\"{Loc.Culture.Name}\"><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(t.Name)} · Notebook Doctor</title>"
        + "<style>body{font:14px/1.5 \"Segoe UI\",sans-serif;margin:32px}pre{white-space:pre-wrap}</style></head><body><pre>"
        + WebUtility.HtmlEncode(Report(t)) + "</pre></body></html>";
}
