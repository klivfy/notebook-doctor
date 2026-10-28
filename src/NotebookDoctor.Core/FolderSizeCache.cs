using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>Uma pasta medida: tamanho e a data de mudança da pasta na hora da medição.</summary>
public sealed record FolderSizeEntry(string Path, DateTime LastWriteUtc, long Bytes, DateTime MeasuredUtc);

/// <summary>
/// Leva 9.3: guarda o tamanho medido da pasta de cada programa. Na próxima abertura da aba Programas, a pasta cuja data
/// de mudança é a mesma da medição usa o valor guardado, e só as que mudaram são medidas de novo. A data da pasta só muda
/// quando algo muda direto nela (não num arquivo de uma subpasta), então o valor guardado também vence em 30 dias.
/// </summary>
public sealed class FolderSizeCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private const int MaxEntries = 2000;

    private readonly Dictionary<string, FolderSizeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    private static string Key(string path) => path.TrimEnd('\\', '/');

    /// <summary>Tamanho guardado, se a pasta não mudou desde a medição e a medição tem menos de 30 dias.</summary>
    public bool TryGet(string path, DateTime lastWriteUtc, DateTime nowUtc, out long bytes)
    {
        bytes = 0;
        if (!_entries.TryGetValue(Key(path), out var e) || e.LastWriteUtc != lastWriteUtc || nowUtc - e.MeasuredUtc > MaxAge || nowUtc < e.MeasuredUtc) return false;
        bytes = e.Bytes;
        return true;
    }

    public void Set(string path, DateTime lastWriteUtc, long bytes, DateTime nowUtc)
    {
        if (bytes <= 0) { _entries.Remove(Key(path)); return; }
        _entries[Key(path)] = new FolderSizeEntry(Key(path), lastWriteUtc, bytes, nowUtc);
    }

    /// <summary>Texto para gravar: sem as medições vencidas e no máximo 2000 pastas (as mais recentes).</summary>
    public string Serialize(DateTime nowUtc) => JsonSerializer.Serialize(
        _entries.Values.Where(e => nowUtc - e.MeasuredUtc <= MaxAge && e.MeasuredUtc <= nowUtc)
            .OrderByDescending(e => e.MeasuredUtc).Take(MaxEntries).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>Lê o que foi gravado. Arquivo vazio, quebrado ou com entradas inválidas vira um cache vazio (é só para acelerar).</summary>
    public static FolderSizeCache Parse(string? json)
    {
        var cache = new FolderSizeCache();
        if (string.IsNullOrWhiteSpace(json)) return cache;
        try
        {
            foreach (var e in JsonSerializer.Deserialize<List<FolderSizeEntry>>(json) ?? [])
                if (e is { Path.Length: > 0, Bytes: > 0 }) cache._entries[Key(e.Path)] = e with { Path = Key(e.Path) };
        }
        catch (JsonException) { }
        return cache;
    }
}
