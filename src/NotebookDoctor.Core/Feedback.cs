using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotebookDoctor.Services;

/// <summary>
/// B25: avaliação do app dentro dele. Nota de 1 a 5 e um comentário opcional em texto puro. A avaliação fica no perfil
/// da pessoa e só vai para a Klivfy num clique em "Enviar avaliação", depois de mostrar exatamente o que será enviado.
/// </summary>
public static class Feedback
{
    public const int MaxComment = 500;

    public static bool ValidStars(int stars) => stars is >= 1 and <= 5;

    /// <summary>Comentário pronto para guardar e enviar: texto puro (sem controle nem quebra de linha solta), sem dado pessoal, até <see cref="MaxComment"/>.</summary>
    public static string CleanComment(string? comment, string home = "", string user = "", string machine = "", string domain = "", string serial = "")
    {
        if (string.IsNullOrWhiteSpace(comment)) return "";
        var text = new StringBuilder(comment.Length);
        foreach (char c in comment.Trim())
            text.Append(c is '\n' or '\r' or '\t' ? ' ' : char.IsControl(c) ? '\0' : c);
        string flat = System.Text.RegularExpressions.Regex.Replace(text.ToString().Replace("\0", ""), @"\s{2,}", " ").Trim();
        flat = Redaction.Anonymize(flat, home, user, machine, domain, serial);
        return flat.Length <= MaxComment ? flat : flat[..MaxComment].TrimEnd();
    }

    /// <summary>
    /// O corpo exato do envio, que também é o texto mostrado antes de enviar. Só estes campos existem: não há identificador da
    /// pessoa, do computador nem da instalação.
    /// </summary>
    public static string Payload(int stars, string comment, string appVersion, string language)
    {
        if (!ValidStars(stars)) throw new ArgumentOutOfRangeException(nameof(stars), "A nota vai de 1 a 5.");
        var obj = new JsonObject
        {
            ["app"] = "NotebookDoctor",
            ["versao"] = appVersion,
            ["idioma"] = language,
            ["nota"] = stars,
        };
        if (comment.Length > 0) obj["comentario"] = comment;
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

/// <summary>
/// M18: estatísticas de uso anônimas e opcionais. Desligadas por padrão. Só contagens por ferramenta (quantas vezes foi
/// aberta e quantas falhas o app teve), sem identificador de pessoa, de computador ou de instalação, sem nomes de arquivos,
/// programas ou endereços. O envio é no máximo semanal e a pessoa pode ver o texto exato antes.
/// </summary>
public static class Telemetry
{
    /// <summary>Intervalo mínimo entre dois envios.</summary>
    public static readonly TimeSpan SendEvery = TimeSpan.FromDays(7);

    /// <summary>
    /// Únicas chaves que entram na contagem: ferramentas do app, por nome fixo. Qualquer outra (nome de programa, pasta, texto
    /// da pessoa) é descartada na hora de contar e de montar o envio.
    /// </summary>
    public static readonly IReadOnlySet<string> Tools = new HashSet<string>(StringComparer.Ordinal)
    {
        "limpeza", "limpeza-avancada", "duplicados", "atualizacoes", "drivers", "inicializacao", "servicos", "tarefas", "extensoes",
        "programas", "energia", "backup", "seguranca", "recuperacao", "desfragmentar", "registro", "ferramentas-do-windows", "apagar-de-vez",
        "app", // falhas do próprio app
    };

    private const int MaxCount = 100_000;

    public static bool Known(string? tool) => tool is not null && Tools.Contains(tool);

    /// <summary>Soma um uso (ou uma falha) à contagem. Ferramenta desconhecida não conta.</summary>
    public static void Count(IDictionary<string, int> counts, string? tool)
    {
        if (!Known(tool)) return;
        counts[tool!] = Math.Min(MaxCount, (counts.TryGetValue(tool!, out int n) ? n : 0) + 1);
    }

    public static bool Empty(IReadOnlyDictionary<string, int> uses, IReadOnlyDictionary<string, int> failures) =>
        !uses.Any(p => Known(p.Key) && p.Value > 0) && !failures.Any(p => Known(p.Key) && p.Value > 0);

    /// <summary>Hora de enviar: ligado, com algo a enviar e passada uma semana desde o último envio.</summary>
    public static bool ShouldSend(bool enabled, DateTime lastSent, DateTime now, bool anything) =>
        enabled && anything && now - lastSent >= SendEvery;

    /// <summary>Semana do ano (ISO), como "2026-W43": a granularidade do envio. Não há data nem hora.</summary>
    public static string Week(DateTime date) =>
        $"{System.Globalization.ISOWeek.GetYear(date)}-W{System.Globalization.ISOWeek.GetWeekOfYear(date):00}";

    /// <summary>"11" ou "10": o suficiente para saber a família do Windows, sem número de compilação nem edição.</summary>
    public static string WindowsFamily(int build) => build >= 22000 ? "11" : "10";

    /// <summary>O corpo exato do envio, que também é o texto de "Ver o que seria enviado".</summary>
    public static string Payload(IReadOnlyDictionary<string, int> uses, IReadOnlyDictionary<string, int> failures, string appVersion, string language, string windows, DateTime now)
    {
        static JsonObject Clean(IReadOnlyDictionary<string, int> source)
        {
            var o = new JsonObject();
            foreach (var (tool, n) in source.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (Known(tool) && n > 0) o[tool] = Math.Min(n, MaxCount);
            return o;
        }
        var obj = new JsonObject
        {
            ["app"] = "NotebookDoctor",
            ["versao"] = appVersion,
            ["idioma"] = language,
            ["windows"] = windows,
            ["semana"] = Week(now),
            ["usos"] = Clean(uses),
            ["falhas"] = Clean(failures),
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
