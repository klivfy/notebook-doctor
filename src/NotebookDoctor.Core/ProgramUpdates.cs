using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Um programa com versão nova no catálogo do WinGet.</summary>
public sealed record UpdateCandidate(string Name, string Id, string Installed, string Available, string Source);

public enum UpdateState { Available, Skipped, Paused }

public enum ExitKind { Done, NotApplicable, Cancelled, Other }

/// <summary>Resultado da leitura da tabela de <c>winget upgrade</c>.</summary>
public sealed record WingetList(IReadOnlyList<UpdateCandidate> Items, int Truncated);

/// <summary>
/// Leva 7.1 (C14): comparação de versões, "pular esta versão", "pausar este app", ordenação e leitura da saída do
/// <c>winget</c>. Tudo aqui é lógica pura; quem executa o <c>winget</c> está em NotebookDoctor.Windows.
/// </summary>
public static class VersionOrder
{
    private static readonly string[] PreRelease = ["alpha", "beta", "rc", "pre", "preview", "dev", "nightly", "canary", "snapshot"];

    /// <summary>Menor que zero se <paramref name="a"/> é anterior a <paramref name="b"/>. "1.2" e "1.2.0" são iguais; "1.0-beta" vem antes de "1.0".</summary>
    public static int Compare(string? a, string? b)
    {
        var x = Parts(a);
        var y = Parts(b);
        for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
        {
            int c = ComparePart(i < x.Count ? x[i] : null, i < y.Count ? y[i] : null);
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool IsNewer(string? candidate, string? than) => Compare(candidate, than) > 0;

    private static List<string> Parts(string? version)
    {
        var text = (version ?? "").Trim().TrimStart('v', 'V');
        var list = new List<string>();
        foreach (Match m in Regex.Matches(text, @"[0-9]+|[A-Za-z]+")) list.Add(m.Value);
        return list;
    }

    // Parte ausente vale 0. Número ganha de texto na mesma posição; texto de pré-lançamento perde para a ausência.
    private static int ComparePart(string? a, string? b)
    {
        bool na = a is not null && char.IsDigit(a[0]);
        bool nb = b is not null && char.IsDigit(b[0]);
        if (a is null) return nb ? CompareNumbers("0", b!) : IsPre(b!) ? 1 : -1;
        if (b is null) return na ? CompareNumbers(a, "0") : IsPre(a) ? -1 : 1;
        if (na && nb) return CompareNumbers(a, b);
        if (na != nb) return na ? 1 : -1;
        return Math.Sign(string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPre(string text) => PreRelease.Contains(text.ToLowerInvariant());

    // Sem converter para número: versões como 20240101123456 ou calendários longos não estouram.
    private static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
        return Math.Sign(string.CompareOrdinal(a, b));
    }
}

public static class ProgramUpdateRules
{
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}$", RegexOptions.Compiled);

    /// <summary>
    /// Só ids do catálogo (Editor.Programa). Ids de "programas não reconhecidos" começam com ARP\ ou MSIX\ e ficam de fora,
    /// o que também impede qualquer caractere de comando de chegar à linha de argumentos.
    /// </summary>
    public static bool IdOk(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>Chave guardada em "pular esta versão": o app e a versão que a pessoa dispensou.</summary>
    public static string SkipKey(string id, string version) => $"{id.Trim()}|{version.Trim()}";

    public static string? SkippedVersion(IEnumerable<string> skipped, string id)
    {
        string? best = null;
        foreach (var entry in skipped)
        {
            int bar = entry.IndexOf('|');
            if (bar <= 0 || !string.Equals(entry[..bar], id, StringComparison.OrdinalIgnoreCase)) continue;
            string v = entry[(bar + 1)..];
            if (best is null || VersionOrder.Compare(v, best) > 0) best = v;
        }
        return best;
    }

    /// <summary>Pausado ganha de pulado. Pulado só vale até a versão dispensada: uma versão mais nova volta a aparecer.</summary>
    public static UpdateState StateOf(UpdateCandidate c, IEnumerable<string> skipped, IEnumerable<string> paused)
    {
        if (paused.Any(p => string.Equals(p.Trim(), c.Id, StringComparison.OrdinalIgnoreCase))) return UpdateState.Paused;
        string? skip = SkippedVersion(skipped, c.Id);
        return skip is not null && VersionOrder.Compare(c.Available, skip) <= 0 ? UpdateState.Skipped : UpdateState.Available;
    }

    /// <summary>Disponíveis primeiro, depois pulados e pausados; dentro de cada grupo, por nome.</summary>
    public static List<(UpdateCandidate Item, UpdateState State)> Order(IEnumerable<UpdateCandidate> items, IEnumerable<string> skipped, IEnumerable<string> paused)
    {
        var skipList = skipped.ToList();
        var pauseList = paused.ToList();
        return items.Select(i => (Item: i, State: StateOf(i, skipList, pauseList)))
            .OrderBy(t => t.State)
            .ThenBy(t => t.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Uma entrada por programa: dispensar uma versão nova troca a anterior.</summary>
    public static List<string> WithSkip(IEnumerable<string> skipped, string id, string version)
    {
        var list = skipped.Where(e => !e.StartsWith(id.Trim() + "|", StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(SkipKey(id, version));
        return list;
    }

    public static List<string> WithPause(IEnumerable<string> paused, string id, bool pause)
    {
        var list = paused.Where(p => !string.Equals(p.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (pause) list.Add(id.Trim());
        return list;
    }
}

/// <summary>
/// Argumentos e leitura de saída do <c>winget</c>. A fonte é sempre "winget" (o catálogo público): a fonte da Store
/// tem termos próprios e fica de fora. Os argumentos de aceite só entram quando a pessoa aceitou na tela do app.
/// </summary>
public static class WingetCommands
{
    public const string Source = "winget";

    public static string[] ListUpgrades(bool sourceTermsAccepted) =>
        With(["upgrade", "--source", Source, "--disable-interactivity"], sourceTermsAccepted ? ["--accept-source-agreements"] : []);

    public static string[] Show(string id, bool sourceTermsAccepted) =>
        With(["show", "--id", Checked(id), "--exact", "--source", Source, "--disable-interactivity"], sourceTermsAccepted ? ["--accept-source-agreements"] : []);

    /// <param name="packageTermsAccepted">A pessoa leu e aceitou os termos dos pacotes na tela. Sem isso, o argumento não vai.</param>
    public static string[] Upgrade(string id, bool sourceTermsAccepted, bool packageTermsAccepted)
    {
        var args = new List<string> { "upgrade", "--id", Checked(id), "--exact", "--source", Source, "--silent", "--disable-interactivity" };
        if (sourceTermsAccepted) args.Add("--accept-source-agreements");
        if (packageTermsAccepted) args.Add("--accept-package-agreements");
        return [.. args];
    }

    /// <summary>Instala um pacote novo (os aplicativos de drivers dos fabricantes). Mesmas regras de aceite do <see cref="Upgrade"/>.</summary>
    public static string[] Install(string id, bool sourceTermsAccepted, bool packageTermsAccepted)
    {
        var args = new List<string> { "install", "--id", Checked(id), "--exact", "--source", Source, "--silent", "--disable-interactivity" };
        if (sourceTermsAccepted) args.Add("--accept-source-agreements");
        if (packageTermsAccepted) args.Add("--accept-package-agreements");
        return [.. args];
    }

    private static string[] With(string[] a, string[] b) => [.. a, .. b];

    private static string Checked(string id) =>
        ProgramUpdateRules.IdOk(id) ? id : throw new ArgumentException("Identificador de pacote inválido.", nameof(id));

    /// <summary>
    /// Lê a tabela de <c>winget upgrade</c> sem depender do idioma: acha a linha de traços, pula o cabeçalho e separa
    /// as colunas por 2 ou mais espaços (nomes têm espaço simples; id e versão nunca têm). Só a primeira tabela conta;
    /// a segunda lista pacotes que exigem alvo explícito. Linhas cortadas pelo terminal ("…") são contadas e descartadas.
    /// </summary>
    public static WingetList ParseUpgrades(string output)
    {
        var items = new List<UpdateCandidate>();
        int truncated = 0;
        bool inTable = false;
        foreach (var raw in output.Split('\n'))
        {
            // O indicador de progresso reescreve a linha com \r: vale o que vem depois do último
            string line = raw.TrimEnd('\r');
            int cr = line.LastIndexOf('\r');
            if (cr >= 0) line = line[(cr + 1)..];
            line = line.TrimEnd();

            if (!inTable)
            {
                if (Regex.IsMatch(line, @"^-{8,}$")) inTable = true;
                continue;
            }
            if (line.Length == 0) break;
            if (Regex.IsMatch(line, @"^-{8,}$")) continue;

            var cols = Regex.Split(line.Trim(), @"\s{2,}");
            if (cols.Length is not (4 or 5)) continue; // rodapé ("2 atualizações disponíveis.") e avisos
            if (cols.Any(c => c.Contains('…'))) { truncated++; continue; }
            string id = cols[1];
            if (!ProgramUpdateRules.IdOk(id)) continue; // ARP\… e MSIX\…: fora do catálogo
            string installed = CleanVersion(cols[2]);
            string available = CleanVersion(cols[3]);
            if (available.Length == 0 || !VersionOrder.IsNewer(available, installed)) continue;
            items.Add(new UpdateCandidate(cols[0], id, installed, available, cols.Length == 5 ? cols[4] : Source));
        }
        return new WingetList(items, truncated);
    }

    private static string CleanVersion(string v) => v.Trim().TrimStart('<', '>', '~', '=').Trim();

    /// <summary>Classe do código de saída do <c>winget</c>. Só os códigos que se conhece; o resto é <see cref="ExitKind.Other"/>.</summary>
    public static ExitKind Classify(int exit) => exit switch
    {
        0 => ExitKind.Done,
        unchecked((int)0x8A15002B) => ExitKind.NotApplicable,
        1223 or unchecked((int)0x800704C7) => ExitKind.Cancelled,
        _ => ExitKind.Other,
    };

    public static bool Succeeded(int exit) => Classify(exit) is ExitKind.Done or ExitKind.NotApplicable;
}

/// <summary>
/// Texto dos termos de um pacote, tirado de <c>winget show</c> (que é o que a pessoa lê antes de aceitar).
/// </summary>
public static class WingetShowText
{
    /// <summary>Tira o indicador de progresso e as linhas em branco do começo e do fim.</summary>
    public static string Clean(string output)
    {
        var lines = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int cr = line.LastIndexOf('\r');
            if (cr >= 0) line = line[(cr + 1)..];
            line = line.TrimEnd();
            if (line.Trim() is "-" or "\\" or "|" or "/") continue; // quadro do indicador de progresso
            lines.Add(line);
        }
        return string.Join("\n", lines).Trim();
    }
}
