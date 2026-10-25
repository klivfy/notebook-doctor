using System.Globalization;
using System.Text;

namespace NotebookDoctor.Services;

public enum BackupKind { File, Folder, RegistryKey, RegistryValue, Service, Task, Recycled }

/// <summary>Um item guardado antes de apagar. <see cref="Archive"/> é o arquivo .reg/.xml da sessão ou o nome dentro do zip; arquivos e pastas vão cada um para o seu <see cref="Zip"/>.</summary>
public sealed class BackupItem
{
    public BackupKind Kind { get; set; }
    public string OriginalPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    /// <summary>Tipo do valor do registro (String, ExpandString, MultiString, DWord, QWord, Binary).</summary>
    public string ValueKind { get; set; } = "";
    /// <summary>Dados do valor em texto: números em decimal, binário em base64, várias linhas separadas por \0.</summary>
    public string ValueData { get; set; } = "";
    public string Archive { get; set; } = "";
    /// <summary>Zip do item (arquivos e pastas), um por item; vazio nos backups antigos, que guardavam tudo em files.zip.</summary>
    public string Zip { get; set; } = "";
    public long Bytes { get; set; }
}

/// <summary>Uma sessão de backup: tudo o que uma remoção de sobras apagou, para desfazer pela aba Ações.</summary>
public sealed class BackupManifest
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Created { get; set; }
    public List<BackupItem> Items { get; set; } = new();
    public bool Restored { get; set; }
    public long Bytes => Items.Sum(i => i.Bytes);
}

public static class BackupRules
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public const long MaxBytes = 2L << 30;

    /// <summary>"20261022-041500-7-zip": ordena pela data e diz de onde veio.</summary>
    public static string NewId(DateTime now, string title)
    {
        var slug = new StringBuilder();
        foreach (char c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            if (slug.Length >= 40) break;
        }
        string s = slug.ToString().Trim('-');
        return now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (s.Length > 0 ? "-" + s : "");
    }

    /// <summary>Nome dentro do zip: "C:\Program Files\App\a.dll" → "C/Program Files/App/a.dll".</summary>
    public static string EntryName(string path)
    {
        string p = Paths.Clean(path);
        return p.Length >= 2 && p[1] == ':' ? p[0] + p[2..].Replace('\\', '/') : p.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>De volta ao caminho original: "C/Program Files/App/a.dll" → "C:\Program Files\App\a.dll".</summary>
    public static string PathOfEntry(string entry)
    {
        string e = entry.Replace('/', '\\').TrimEnd('\\');
        return e.Length >= 2 && char.IsAsciiLetter(e[0]) && e[1] == '\\' ? e[0] + ":" + e[1..] : e;
    }

    /// <summary>
    /// S3: onde uma entrada do zip de uma pasta volta, ou null se ela sairia da pasta. O Desfazer como administrador lê
    /// o backup de uma pasta do usuário; uma entrada com ".." ou de outra unidade não pode escrever fora do lugar dela.
    /// </summary>
    public static string? FolderTarget(string entry, string folder)
    {
        string target = PathOfEntry(entry);
        if (target.Split('\\').Any(part => part is "." or "..")) return null;
        return Paths.IsUnder(target, folder) ? target : null;
    }

    /// <summary>S3: o arquivo do backup (.reg, .xml) só pode ser um nome solto dentro da pasta da sessão.</summary>
    public static string? ArchiveFile(string archive) =>
        archive.Length > 0 && archive.IndexOfAny(['\\', '/', ':']) < 0 && archive is not ("." or "..") ? archive : null;

    /// <summary>
    /// Sessões a apagar: as com mais de 30 dias e, se o total passar de 2 GB, as mais antigas até caber. A mais nova
    /// sempre fica, para dar para desfazer a última remoção.
    /// </summary>
    public static List<string> Expired(IReadOnlyList<(string Id, DateTime Created, long Bytes)> sessions, DateTime now, TimeSpan maxAge, long maxBytes)
    {
        var ordered = sessions.OrderByDescending(s => s.Created).ToList();
        var expired = new List<string>();
        long total = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            if (i > 0 && (now - s.Created > maxAge || total + s.Bytes > maxBytes)) { expired.Add(s.Id); continue; }
            total += s.Bytes;
        }
        return expired;
    }

    public static string Describe(BackupManifest m) =>
        $"{m.Created.ToString("g", CultureInfo.CurrentCulture)} · {m.Title} · {Pt.N(m.Items.Count, Loc.T("item"), Loc.T("itens"))}"
        + (m.Bytes > 0 ? $" · {Units.Bytes(m.Bytes)}" : "") + (m.Restored ? $" · {Loc.T("restaurado")}" : "");
}
