namespace NotebookDoctor.Services;

public sealed record PreviewFile(string Path, long Bytes, DateTime Modified);

/// <summary>
/// B35: lista do que a limpeza apagaria num local, antes de apagar. Mostra os maiores primeiro, com o caminho
/// relativo à pasta do local para caber na tela, e diz quantos ficaram de fora da lista.
/// </summary>
public static class CleanPreview
{
    public const int MaxListed = 200;

    public static string Format(string label, IReadOnlyList<string> roots, IReadOnlyList<PreviewFile> files)
    {
        var sb = new System.Text.StringBuilder();
        long total = files.Sum(f => f.Bytes);
        sb.AppendLine($"{label}: {Pt.N(files.Count, "arquivo", "arquivos")}, {Units.Bytes(total)}.");
        if (files.Count == 0) return sb.Append("Nada a apagar aqui agora.").ToString();
        sb.AppendLine("Os maiores primeiro. Arquivos em uso no momento da limpeza são mantidos.");
        sb.AppendLine();
        foreach (var f in files.OrderByDescending(f => f.Bytes).Take(MaxListed))
            sb.AppendLine($"{Units.Bytes(f.Bytes),10}  {f.Modified:dd/MM/yyyy}  {Relative(f.Path, roots)}");
        if (files.Count > MaxListed)
            sb.AppendLine().Append($"… e mais {Pt.N(files.Count - MaxListed, "arquivo menor", "arquivos menores")}.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Caminho a partir da pasta do local ("Temp\abc.tmp" em vez do caminho inteiro).</summary>
    public static string Relative(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots.OrderByDescending(r => r.Length))
        {
            string r = root.TrimEnd('\\', '/');
            if (path.Length > r.Length && path.StartsWith(r, StringComparison.OrdinalIgnoreCase) && path[r.Length] is '\\' or '/')
            {
                string name = r[(r.LastIndexOfAny(['\\', '/']) + 1)..];
                return name + path[r.Length..];
            }
        }
        return path;
    }
}
