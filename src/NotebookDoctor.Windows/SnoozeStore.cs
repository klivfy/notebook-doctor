using System.IO;
using System.Text.Json;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 7.3 (C12): a lista de aplicativos em soneca, num arquivo JSON. É daqui que o "Acordar", o "Desfazer" de Ações e o
/// vigia sabem o que foi desligado. Gravação por arquivo temporário e troca: uma queda no meio não deixa a lista pela metade.
/// </summary>
public sealed class SnoozeStore(string file)
{
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string File { get; } = file;

    public List<SnoozedApp> Load()
    {
        lock (_lock) return Read();
    }

    private List<SnoozedApp> Read()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return [];
            return (JsonSerializer.Deserialize<List<SnoozedApp>>(System.IO.File.ReadAllText(File)) ?? [])
                .Where(a => a.Key.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    /// <summary>Muda a lista sob trava e grava. <paramref name="change"/> recebe a lista e devolve true se algo mudou.</summary>
    public void Update(Func<List<SnoozedApp>, bool> change)
    {
        lock (_lock)
        {
            var list = Read();
            if (!change(list)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            AtomicFile.WriteAllText(File, JsonSerializer.Serialize(list, Json));
        }
    }

    public void Save(SnoozedApp app) => Update(list =>
    {
        list.RemoveAll(a => a.Key == app.Key);
        list.Add(app);
        return true;
    });

    public void Remove(string key) => Update(list => list.RemoveAll(a => a.Key == key) > 0);
}
