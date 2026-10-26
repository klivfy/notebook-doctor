using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 7.3 (C06): desativa e reativa itens do menu de contexto. Verbo estático: grava o valor <c>ProgrammaticAccessOnly</c>
/// (documentado; esconde do menu sem apagar nada). Manipulador: guarda a chave em backup (<c>reg export</c>) e a apaga;
/// reativar restaura o backup. Os backups ficam numa pasta própria, que o prazo de 30 dias dos outros não alcança.
/// </summary>
public static class ContextMenuEditor
{
    public static string SystemRoot => Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

    public static string BackupRoot(string appDir) => Path.Combine(appDir, "context-backups");
    private static string RecordFile(string appDir) => Path.Combine(appDir, "context-disabled.json");

    public static List<ContextMenuItem> Scan() => ContextMenuRules.Scan(new WindowsLeftoverSystem(), SystemRoot);

    // ---------- verbos ----------

    /// <exception cref="UnauthorizedAccessException">Chave de todos os usuários sem administrador.</exception>
    public static void SetVerbHidden(ContextMenuItem item, bool hidden)
    {
        Guard(item, MenuItemKind.Verb);
        try
        {
            using var key = RegistryPaths.Open(item.Key, writable: true) ?? throw new InvalidOperationException("O item não existe mais.");
            if (hidden) key.SetValue(ContextMenuRules.HideValue, "", RegistryValueKind.String);
            else key.DeleteValue(ContextMenuRules.HideValue, throwOnMissingValue: false);
        }
        catch (SecurityException ex) { throw new UnauthorizedAccessException(ex.Message, ex); }
    }

    // ---------- manipuladores ----------

    /// <summary>Guarda a chave e apaga. Devolve o registro do que foi desativado (já gravado na lista).</summary>
    public static DisabledHandler DisableHandler(ContextMenuItem item, string appDir)
    {
        Guard(item, MenuItemKind.Handler);
        var (protectedItem, reason) = ContextMenuRules.Protection(item, SystemRoot);
        if (protectedItem) throw new InvalidOperationException(reason);
        using (var probe = RegistryPaths.Open(item.Key)) if (probe is null) throw new InvalidOperationException("O item não existe mais.");

        var store = new BackupStore(BackupRoot(appDir));
        string id;
        using (var session = store.Begin($"Menu de contexto {item.Name}"))
        {
            session.AddKey(item.Key); // se o export falhar, lança antes de apagar
            id = session.Id;
        }
        try { RegistryPaths.DeleteTree(item.Key); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            store.Delete(id); // nada mudou: o backup não serve para nada
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
        var record = new DisabledHandler
        {
            SessionId = id, Hive = item.Hive, Location = item.Location, Name = item.Name, Display = item.Display, File = item.File, Disabled = DateTime.Now,
        };
        var all = Disabled(appDir);
        all.Add(record);
        SaveRecords(appDir, all);
        return record;
    }

    /// <summary>Restaura o backup do manipulador. Devolve a mensagem do restaurador; o registro só sai da lista se a chave voltou.</summary>
    public static string EnableHandler(DisabledHandler record, string appDir, out bool restored)
    {
        restored = false;
        if (!ContextMenuRules.Valid(record.Hive, record.Location, record.Name)) return "";
        string msg = new BackupStore(BackupRoot(appDir)).Restore(record.SessionId);
        string key = ContextMenuRules.ItemKey(record.Hive, record.Location, MenuItemKind.Handler, record.Name);
        using (var k = RegistryPaths.Open(key)) restored = k is not null;
        if (restored)
        {
            var all = Disabled(appDir);
            all.RemoveAll(r => r.SessionId == record.SessionId);
            SaveRecords(appDir, all);
        }
        return msg;
    }

    /// <summary>Restaura pelo id da sessão (o "Desfazer" de Ações). Devolve null se o id não é de um manipulador desativado.</summary>
    public static string? EnableBySession(string sessionId, string appDir, out bool restored)
    {
        restored = false;
        var record = Disabled(appDir).FirstOrDefault(r => r.SessionId == sessionId);
        return record is null ? null : EnableHandler(record, appDir, out restored);
    }

    public static List<DisabledHandler> Disabled(string appDir)
    {
        try
        {
            string file = RecordFile(appDir);
            if (!File.Exists(file)) return [];
            return (JsonSerializer.Deserialize<List<DisabledHandler>>(File.ReadAllText(file)) ?? [])
                .Where(r => ContextMenuRules.Valid(r.Hive, r.Location, r.Name) && r.SessionId.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static void SaveRecords(string appDir, List<DisabledHandler> all)
    {
        Directory.CreateDirectory(appDir);
        File.WriteAllText(RecordFile(appDir), JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Guard(ContextMenuItem item, MenuItemKind kind)
    {
        if (item.Kind != kind || !ContextMenuRules.Valid(item.Hive, item.Location, item.Name))
            throw new ArgumentException("Item do menu de contexto inválido.", nameof(item));
    }
}
