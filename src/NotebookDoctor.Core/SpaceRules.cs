namespace NotebookDoctor.Services;

public enum FileCategory { Images, Videos, Audio, Documents, Archives, Programs, Other }

/// <summary>C09: em que categoria cai um arquivo, pela extensão.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var d = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, string list) { foreach (var e in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) d["." + e] = c; }
        Add(FileCategory.Images, "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 nef arw dng svg ico psd ai");
        Add(FileCategory.Videos, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg 3gp ts vob");
        Add(FileCategory.Audio, "mp3 wav flac aac m4a ogg wma opus aiff mid midi");
        Add(FileCategory.Documents, "doc docx xls xlsx ppt pptx pdf txt rtf odt ods odp csv md epub mobi pst ost");
        Add(FileCategory.Archives, "zip rar 7z tar gz bz2 xz iso img vhd vhdx vmdk cab wim");
        Add(FileCategory.Programs, "exe dll msi sys msix appx jar apk");
        return d;
    }

    public static FileCategory Of(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOfAny(['\\', '/']);
        return dot > slash && dot >= 0 && Map.TryGetValue(path[dot..], out var c) ? c : FileCategory.Other;
    }

    public static string Name(FileCategory c) => c switch
    {
        FileCategory.Images => Loc.T("Imagens"),
        FileCategory.Videos => Loc.T("Vídeos"),
        FileCategory.Audio => Loc.T("Músicas e áudio"),
        FileCategory.Documents => Loc.T("Documentos"),
        FileCategory.Archives => Loc.T("Compactados e imagens de disco"),
        FileCategory.Programs => Loc.T("Programas e bibliotecas"),
        _ => Loc.T("Outros"),
    };
}

public sealed record TypeUsage(FileCategory Category, long Bytes, long Files, IReadOnlyList<(string Extension, long Bytes)> TopExtensions)
{
    public string Description
    {
        get
        {
            string ext = TopExtensions.Count == 0 ? "" : $" ({string.Join(", ", TopExtensions.Select(e => e.Extension))})";
            return $"{FileCategories.Name(Category)} · {Pt.N(Files, Loc.T("arquivo"), Loc.T("arquivos"))}{ext}";
        }
    }
}

/// <summary>Soma o tamanho dos arquivos por categoria e por extensão.</summary>
public sealed class SpaceByType
{
    private readonly Dictionary<FileCategory, (long Bytes, long Files)> _totals = new();
    private readonly Dictionary<(FileCategory, string), long> _ext = new();

    public void Add(string path, long bytes)
    {
        var c = FileCategories.Of(path);
        var t = _totals.GetValueOrDefault(c);
        _totals[c] = (t.Bytes + bytes, t.Files + 1);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length > 0) _ext[(c, ext)] = _ext.GetValueOrDefault((c, ext)) + bytes;
    }

    public List<TypeUsage> Result(int topExtensions = 3) =>
        _totals.Select(kv => new TypeUsage(kv.Key, kv.Value.Bytes, kv.Value.Files,
                _ext.Where(e => e.Key.Item1 == kv.Key).OrderByDescending(e => e.Value).Take(topExtensions).Select(e => (e.Key.Item2, e.Value)).ToList()))
            .OrderByDescending(t => t.Bytes).ToList();
}

public sealed record DuplicateFile(string Path, DateTime Modified, DateTime Created);

/// <summary>Arquivos idênticos (mesmo tamanho e mesmo conteúdo).</summary>
public sealed record DuplicateGroup(long Size, IReadOnlyList<DuplicateFile> Files)
{
    public long Wasted => Size * (Files.Count - 1);
}

public enum KeepRule { Oldest, Newest }

/// <summary>
/// C08: qual cópia fica. O grupo nunca fica sem nenhuma: se a escolha incluir todas as cópias, a que a regra manteria
/// sai da lista de apagar. Cópias dentro do Windows, de Program Files e de ProgramData nem entram nos grupos.
/// </summary>
public static class DuplicateRules
{
    /// <summary>A que fica: a mais antiga (ou mais nova) pela data de modificação; empate pela data de criação, depois o caminho mais curto e a ordem alfabética.</summary>
    public static DuplicateFile Keeper(DuplicateGroup group, KeepRule rule)
    {
        if (group.Files.Count == 0) throw new ArgumentException("Grupo vazio.", nameof(group));
        var ordered = rule == KeepRule.Oldest
            ? group.Files.OrderBy(f => f.Modified).ThenBy(f => f.Created)
            : group.Files.OrderByDescending(f => f.Modified).ThenByDescending(f => f.Created);
        ordered = ordered.ThenBy(f => f.Path.Length).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase);
        return ordered.First();
    }

    /// <summary>Todas as cópias que não são a que fica.</summary>
    public static List<DuplicateFile> Others(DuplicateGroup group, KeepRule rule)
    {
        var keep = Keeper(group, rule);
        return group.Files.Where(f => !ReferenceEquals(f, keep)).ToList();
    }

    /// <summary>
    /// O que de fato pode sair do que foi marcado: só caminhos que são do grupo e, se tudo estiver marcado, deixa
    /// uma cópia (a que <paramref name="fallback"/> manteria).
    /// </summary>
    public static List<string> ToDelete(DuplicateGroup group, IEnumerable<string> chosen, KeepRule fallback)
    {
        var inGroup = group.Files.Where(f => chosen.Any(c => Paths.Same(c, f.Path))).ToList();
        if (inGroup.Count >= group.Files.Count) inGroup.Remove(Keeper(group, fallback));
        return inGroup.Select(f => f.Path).ToList();
    }

    public static bool IsSystemPath(string path, KnownPlaces p) =>
        Paths.IsUnder(path, p.Windows) || Paths.IsUnder(path, p.ProgramFiles) || Paths.IsUnder(path, p.ProgramFilesX86) || Paths.IsUnder(path, p.ProgramData);

    /// <summary>Tira as cópias protegidas; um grupo que fica com menos de duas some.</summary>
    public static DuplicateGroup? WithoutSystem(DuplicateGroup group, KnownPlaces p)
    {
        var files = group.Files.Where(f => !IsSystemPath(f.Path, p)).ToList();
        return files.Count >= 2 ? group with { Files = files } : null;
    }

    public static string Describe(DuplicateGroup g) =>
        Loc.F("{0} cópias de {1}; as extras ocupam {2}", g.Files.Count, Paths.Leaf(g.Files[0].Path), Units.Bytes(g.Wasted));
}
