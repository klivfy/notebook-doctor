using System.Globalization;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

public enum RecoveryMode { Regular, Extensive, Signature }

public enum RecoveryType { Documents, Photos, Videos, Audio, Archives }

/// <param name="Source">Letra da unidade de onde recuperar.</param>
/// <param name="DestFolder">Pasta onde o winfr cria a pasta "Recovery_data_hora". Tem de estar em OUTRA unidade.</param>
/// <param name="Filters">Filtros do usuário além dos tipos: um nome, um curinga (<c>*.docx</c>) ou uma pasta terminada em barra (<c>\Users\ana\Documents\</c>).</param>
public sealed record RecoveryRequest(string Source, string DestFolder, RecoveryMode Mode, IReadOnlyList<RecoveryType> Types, IReadOnlyList<string> Filters);

/// <summary>
/// Leva 8.3 (V01, opção A): monta a linha de comando do <b>Windows File Recovery</b> (<c>winfr</c>), da Microsoft, e confere o pedido antes.
/// O app não recupera nada sozinho: o winfr lê a unidade e grava os arquivos numa pasta de outra unidade, e o app só acompanha.
/// </summary>
public static class RecoveryRules
{
    // Letras do plano: o que a pessoa escolhe em linguagem comum vira filtros de nome para os modos normal e extensivo
    private static readonly Dictionary<RecoveryType, string[]> Extensions = new()
    {
        [RecoveryType.Documents] = ["*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx", "*.pdf", "*.txt", "*.odt", "*.ods", "*.rtf", "*.csv"],
        [RecoveryType.Photos] = ["*.jpg", "*.jpeg", "*.png", "*.heic", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.cr2", "*.nef", "*.arw", "*.dng"],
        [RecoveryType.Videos] = ["*.mp4", "*.mov", "*.avi", "*.mkv", "*.wmv", "*.m4v"],
        [RecoveryType.Audio] = ["*.mp3", "*.wav", "*.flac", "*.m4a", "*.wma"],
        [RecoveryType.Archives] = ["*.zip", "*.rar", "*.7z"],
    };

    // Grupos de assinatura que o modo /x entende (pelo guia do winfr): ASF, JPEG, MP3, MPEG, PDF, PNG, ZIP
    private static readonly Dictionary<RecoveryType, string[]> Signatures = new()
    {
        [RecoveryType.Documents] = ["PDF", "ZIP"],      // ZIP inclui docx, xlsx e pptx
        [RecoveryType.Photos] = ["JPEG", "PNG"],
        [RecoveryType.Videos] = ["MPEG", "ASF"],
        [RecoveryType.Audio] = ["MP3", "ASF"],
        [RecoveryType.Archives] = ["ZIP"],
    };

    private static readonly Regex Letter = new("^[A-Za-z]$", RegexOptions.Compiled);
    private static readonly Regex SafeFilter = new(@"^[\p{L}\p{N} _\-\.\*\?\\()~!@#$%&+=,;'\[\]]{1,200}$", RegexOptions.Compiled);
    private static readonly Regex DestPath = new(@"^[A-Za-z]:\\[^""<>|*?\r\n]*$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Filters(RecoveryType type) => Extensions[type];

    public static IReadOnlyList<string> SignatureGroups(IEnumerable<RecoveryType> types) =>
        types.SelectMany(t => Signatures[t]).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();

    /// <summary>O modo mais indicado para o sistema de arquivos: normal só funciona em NTFS; nos demais, o extensivo.</summary>
    public static RecoveryMode Suggest(string fileSystem) =>
        fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) ? RecoveryMode.Regular : RecoveryMode.Extensive;

    public static bool ModeWorksOn(RecoveryMode mode, string fileSystem) => mode switch
    {
        RecoveryMode.Regular => fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase),
        _ => fileSystem is { Length: > 0 },
    };

    /// <summary>Erros do pedido (vazio = pode seguir). <paramref name="sourceFs"/> é o sistema de arquivos da origem.</summary>
    public static List<string> Validate(RecoveryRequest r, string sourceFs)
    {
        var errors = new List<string>();
        if (!Letter.IsMatch(r.Source)) { errors.Add("Escolha a unidade de onde recuperar"); return errors; }
        if (!DestPath.IsMatch(r.DestFolder) || r.DestFolder.Contains("..")) errors.Add("Escolha uma pasta de destino válida (com a letra da unidade)");
        else if (char.ToUpperInvariant(r.DestFolder[0]) == char.ToUpperInvariant(r.Source[0]))
            errors.Add("O destino tem de ficar em OUTRA unidade: gravar na mesma apagaria de vez o que se quer recuperar");
        if (!ModeWorksOn(r.Mode, sourceFs))
            errors.Add(r.Mode == RecoveryMode.Regular ? "O modo rápido só funciona em NTFS. Use o profundo" : "Sistema de arquivos desconhecido");
        if (r.Mode == RecoveryMode.Signature && r.Types.Count == 0) errors.Add("O modo por assinatura precisa de pelo menos um tipo de arquivo");
        if (r.Mode != RecoveryMode.Signature && r.Types.Count == 0 && r.Filters.Count == 0) errors.Add("Escolha pelo menos um tipo de arquivo ou escreva um filtro");
        foreach (var f in r.Filters)
            if (!SafeFilter.IsMatch(f.Trim()) || f.Contains("..")) errors.Add("Um dos filtros tem caracteres que não pode ter");
        return errors;
    }

    /// <summary>
    /// Argumentos do <c>winfr</c>, em lista (sem shell). Modo normal e extensivo filtram por nome (<c>/n</c>); o de assinatura procura pelo
    /// conteúdo (<c>/x /y:</c>). Falha se o pedido não passa em <see cref="Validate"/>.
    /// </summary>
    public static string[] Args(RecoveryRequest r, string sourceFs)
    {
        var errors = Validate(r, sourceFs);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
        var args = new List<string> { char.ToUpperInvariant(r.Source[0]) + ":", r.DestFolder.TrimEnd('\\') };
        if (r.Mode == RecoveryMode.Signature)
        {
            args.Add("/x");
            args.Add("/y:" + string.Join(",", SignatureGroups(r.Types)));
            return [.. args];
        }
        args.Add(r.Mode == RecoveryMode.Regular ? "/regular" : "/extensive");
        foreach (var f in r.Types.SelectMany(Filters).Concat(r.Filters.Select(f => f.Trim())).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            args.Add("/n");
            args.Add(f);
        }
        return [.. args];
    }

    /// <summary>Porcentagem da linha de saída ("Pass 1: Scanning disk 12%"), ou null. O texto muda com o idioma; o número com % não.</summary>
    public static int? ParsePercent(string line)
    {
        var m = Regex.Match(line, @"(\d{1,3})\s*%");
        return m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n and >= 0 and <= 100 ? n : null;
    }

    private static readonly Regex RecoveryFolder = new(@"^Recovery_(\d{8})_(\d{6})$", RegexOptions.Compiled);

    /// <summary>
    /// A pasta que o winfr criou nesta rodada: "Recovery_aaaammdd_hhmmss", a mais nova que não seja anterior ao início (com 2 minutos de
    /// folga entre o relógio do app e o do processo).
    /// </summary>
    public static string? FindOutput(IEnumerable<string> folderNames, DateTime startedLocal)
    {
        string? best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (var name in folderNames)
        {
            var m = RecoveryFolder.Match(name);
            if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
            if (t < startedLocal.AddMinutes(-2) || t <= bestTime) continue;
            best = name;
            bestTime = t;
        }
        return best;
    }

    public static List<string> Warnings(bool sourceIsSystem, bool? solidState, RecoveryMode mode)
    {
        var list = new List<string>
        {
            "Pare de usar a unidade de origem agora: tudo o que for gravado nela pode apagar de vez o que você quer recuperar.",
            "Nem sempre dá para recuperar tudo, e os nomes e pastas podem não voltar (em NTFS voltam mais vezes).",
        };
        if (sourceIsSystem) list.Add("Esta é a unidade do Windows, que grava o tempo todo: quanto mais o PC for usado, menores as chances. O ideal é recuperar de outro PC ou de um pen drive de boot.");
        if (solidState == true) list.Add("Em SSD, o Windows costuma apagar os blocos de arquivos excluídos (TRIM) pouco depois: a chance de recuperar é baixa.");
        if (mode == RecoveryMode.Extensive) list.Add("O modo profundo pode levar muitas horas em disco grande.");
        if (mode == RecoveryMode.Signature) list.Add("O modo por assinatura acha o conteúdo, mas não o nome nem a pasta: os arquivos voltam com nomes genéricos.");
        return list;
    }
}
