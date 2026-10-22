using System.IO;
using System.Security;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>O registro de verdade, para o verificador: além de ler (<see cref="WindowsLeftoverSystem"/>), diz números e se a unidade está presente.</summary>
public sealed class WindowsRegistryCheckSystem : WindowsLeftoverSystem, IRegistryCheckSystem
{
    private readonly Dictionary<string, bool> _drives = new(StringComparer.OrdinalIgnoreCase);

    public int? IntValue(string key, string name)
    {
        try
        {
            using var k = RegistryPaths.Open(key);
            return k?.GetValue(name) is int i ? i : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>Só disco fixo ou removível pronto conta. Rede e unidades desconectadas nunca são prova: o arquivo pode estar só fora do alcance.</summary>
    public bool DriveReady(string root)
    {
        if (_drives.TryGetValue(root, out bool known)) return known;
        bool ready = false;
        try
        {
            var info = new DriveInfo(root);
            ready = info.DriveType == DriveType.Fixed || (info.DriveType == DriveType.Removable && info.IsReady);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        _drives[root] = ready;
        return ready;
    }
}

public sealed record RegistryFixResult(int Removed, int Skipped, int Failed, string? SessionId);

/// <summary>
/// Leva 7.4 (C04): apaga as entradas marcadas. Cada uma é conferida de novo na hora (a prova ainda vale e o lugar é um dos
/// conhecidos) e guardada num backup antes de sair: chave com <c>reg export</c>, valor no manifesto. Desfazer: aba Ações.
/// </summary>
public static class RegistryFixer
{
    public static RegistryFixResult Fix(IReadOnlyList<RegIssue> issues, IRegistryCheckSystem sys, BackupStore store, string systemRoot, string fontsDir)
    {
        int removed = 0, skipped = 0, failed = 0;
        string id;
        using (var session = store.Begin("Verificador de registro"))
        {
            id = session.Id;
            foreach (var issue in issues)
            {
                if (!RegistryChecker.StillBroken(sys, issue, systemRoot, fontsDir)) { skipped++; continue; }
                try
                {
                    if (issue.Value is null)
                    {
                        session.AddKey(issue.Key); // se o export falhar, lança antes de apagar
                        RegistryPaths.DeleteTree(issue.Key);
                    }
                    else
                    {
                        using var key = RegistryPaths.Open(issue.Key, writable: true);
                        if (key is null || !key.GetValueNames().Contains(issue.Value, StringComparer.OrdinalIgnoreCase)) { skipped++; continue; }
                        var kind = key.GetValueKind(issue.Value);
                        object? data = key.GetValue(issue.Value, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (data is null) { skipped++; continue; }
                        session.AddValue(issue.Key, issue.Value, kind, data);
                        key.DeleteValue(issue.Value, throwOnMissingValue: false);
                    }
                    removed++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException) { failed++; }
            }
        }
        if (removed == 0) { store.Delete(id); return new RegistryFixResult(0, skipped, failed, null); }
        return new RegistryFixResult(removed, skipped, failed, id);
    }
}
