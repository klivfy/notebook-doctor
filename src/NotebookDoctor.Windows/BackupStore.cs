using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 5: backups do que a remoção de sobras apaga. Cada sessão é uma pasta com manifest.json, um .zip por arquivo ou
/// pasta (os backups antigos têm um files.zip só), .reg (chaves exportadas pelo reg.exe) e .xml (tarefas). Ficam 30 dias
/// ou até 2 GB.
/// </summary>
public sealed class BackupStore(string root)
{
    public string Root { get; } = root;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public BackupSession Begin(string title)
    {
        string id = BackupRules.NewId(DateTime.Now, title);
        string dir = Path.Combine(Root, id);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(Root, $"{id}-{n}");
        return new BackupSession(dir, new BackupManifest { Id = Path.GetFileName(dir), Title = title, Created = DateTime.Now });
    }

    public List<BackupManifest> List()
    {
        if (!Directory.Exists(Root)) return [];
        var list = new List<BackupManifest>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
            if (Read(dir) is { } m) list.Add(m);
        return list.OrderByDescending(m => m.Created).ToList();
    }

    private static BackupManifest? Read(string dir)
    {
        try { return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json"))); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Pasta da sessão; null para ids que não são um nome simples (o id vem do registro de ações, que é um arquivo editável).</summary>
    private string? DirOf(string id) =>
        id.Length > 0 && Path.GetFileName(id) == id && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? Path.Combine(Root, id) : null;

    public void Delete(string id)
    {
        if (DirOf(id) is { } dir && Directory.Exists(dir) && SafeDelete.Root(dir) is not null)
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>Apaga as sessões vencidas (30 dias ou acima de 2 GB no total). Devolve quantas saíram.</summary>
    public int Prune(DateTime now)
    {
        var list = List();
        var expired = BackupRules.Expired(list.Select(m => (m.Id, m.Created, m.Bytes)).ToList(), now, BackupRules.MaxAge, BackupRules.MaxBytes);
        foreach (var id in expired)
            try { Delete(id); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return expired.Count;
    }

    /// <summary>Devolve tudo ao lugar. Pode ser repetido: o que já voltou é sobrescrito com o mesmo conteúdo.</summary>
    public string Restore(string id)
    {
        if (DirOf(id) is not { } dir || Read(dir) is not { } m)
            return Loc.T("Backup não encontrado: ele pode ter passado de 30 dias e sido apagado.");
        // Cada zip abre uma vez; um zip estragado (backup interrompido) só faz falhar os itens dele
        var zips = new Dictionary<string, ZipArchive?>(StringComparer.OrdinalIgnoreCase);
        ZipArchive Zip(BackupItem item)
        {
            string name = item.Zip.Length > 0 ? item.Zip : "files.zip";
            if (!zips.TryGetValue(name, out var z))
            {
                string path = Path.Combine(dir, Path.GetFileName(name));
                try { z = File.Exists(path) ? ZipFile.OpenRead(path) : null; }
                catch (InvalidDataException) { z = null; }
                zips[name] = z;
            }
            return z ?? throw new IOException("zip do backup ausente ou estragado");
        }
        int ok = 0, failed = 0;
        bool services = false, recycled = false;
        foreach (var item in m.Items)
        {
            if (item.Kind == BackupKind.Recycled) { recycled = true; continue; }
            try
            {
                switch (item.Kind)
                {
                    case BackupKind.RegistryKey or BackupKind.Service:
                        if (Run("reg.exe", "import", Archive(dir, item)) != 0) throw new IOException("reg import");
                        services |= item.Kind == BackupKind.Service;
                        break;
                    case BackupKind.RegistryValue:
                        using (var k = RegistryPaths.Create(item.OriginalPath))
                        {
                            var kind = Enum.Parse<RegistryValueKind>(item.ValueKind);
                            k.SetValue(item.ValueName, RegistryData.Decode(kind, item.ValueData), kind);
                        }
                        break;
                    case BackupKind.File:
                        Extract(Zip(item).GetEntry(item.Archive) ?? throw new IOException("arquivo fora do backup"), item.OriginalPath);
                        break;
                    case BackupKind.Folder:
                        var folderZip = Zip(item);
                        Directory.CreateDirectory(item.OriginalPath);
                        if (SafeDelete.Root(item.OriginalPath) is null) throw new IOException("pasta de destino passa por junção");
                        foreach (var entry in folderZip.Entries.Where(e => e.FullName.StartsWith(item.Archive + "/", StringComparison.Ordinal)))
                        {
                            // S3: entrada com ".." ou de outra pasta não escreve fora do lugar desta
                            string target = BackupRules.FolderTarget(entry.FullName, item.OriginalPath) ?? throw new IOException("entrada fora da pasta");
                            if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(target);
                            else Extract(entry, target);
                        }
                        break;
                    case BackupKind.Task:
                        if (Run("schtasks.exe", "/Create", "/TN", item.OriginalPath, "/XML", Archive(dir, item), "/F") != 0) throw new IOException("schtasks");
                        break;
                }
                ok++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException or InvalidDataException)
            {
                failed++;
            }
        }
        foreach (var z in zips.Values) z?.Dispose();
        m.Restored = failed == 0;
        Save(dir, m);

        var msg = new StringBuilder(Loc.F("{0} de {1} itens voltaram ao lugar.", ok, m.Items.Count(i => i.Kind != BackupKind.Recycled)));
        if (failed > 0) msg.Append(' ').Append(Loc.T("Alguns exigem administrador: abra o app como administrador e desfaça de novo."));
        if (services) msg.Append(' ').Append(Loc.T("Os serviços voltam depois de reiniciar o Windows."));
        if (recycled) msg.Append(' ').Append(Loc.T("Pastas grandes foram para a Lixeira: restaure por lá."));
        return msg.ToString();
    }

    private static void Extract(ZipArchiveEntry entry, string target)
    {
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        if (SafeDelete.Root(parent) is null) throw new IOException("pasta de destino passa por junção");
        if (File.Exists(target) && new FileInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("arquivo de destino é um link");
        entry.ExtractToFile(target, overwrite: true);
    }

    /// <summary>S3: o arquivo .reg ou .xml do item, sempre dentro da pasta da sessão (o manifesto é um arquivo do usuário).</summary>
    private static string Archive(string dir, BackupItem item)
    {
        string path = Path.Combine(dir, BackupRules.ArchiveFile(item.Archive) ?? throw new IOException("arquivo do backup fora da pasta"));
        return File.Exists(path) ? path : throw new IOException("arquivo do backup ausente");
    }

    internal static int Run(string exe, params string[] args) => Run(60_000, exe, args);

    /// <summary>
    /// Roda um programa do Windows em System32. S3/S4: caminho absoluto em System32 e cada argumento separado (ArgumentList),
    /// então nem um binário no diretório atual nem aspas num nome de chave ou de tarefa mudam o comando.
    /// </summary>
    internal static int Run(int timeoutMs, string exe, params string[] args)
    {
        string resolved = Path.IsPathRooted(exe) ? exe : SystemExe.System32(exe);
        var info = new ProcessStartInfo(resolved) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info);
        if (p is null) return -1;
        _ = p.StandardOutput.ReadToEndAsync(); // lê para o processo não travar com o buffer cheio
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (InvalidOperationException) { } return -1; }
        return p.ExitCode;
    }

    internal static void Save(string dir, BackupManifest m) => File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(m, Json));
}

/// <summary>
/// Uma sessão aberta: cada Add guarda o item, num zip próprio já fechado e gravado no disco, antes de quem chamou
/// apagar. Se o app cair no meio, tudo o que já saiu continua no backup.
/// </summary>
public sealed class BackupSession : IDisposable
{
    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
    private readonly string _dir;
    private int _n;

    public BackupManifest Manifest { get; }
    public string Id => Manifest.Id;

    internal BackupSession(string dir, BackupManifest manifest)
    {
        _dir = dir;
        Manifest = manifest;
        Directory.CreateDirectory(dir);
        BackupStore.Save(_dir, Manifest);
    }

    /// <summary>
    /// Grava um zip completo para um item: fecha o arquivo e força a gravação no disco antes de voltar. Se algo falhar
    /// no meio (um arquivo em uso), o zip parcial sai e a exceção segue, para quem chamou não apagar nada.
    /// </summary>
    private string WriteZip(Action<ZipArchive> fill)
    {
        string name = $"{++_n:000}.zip";
        string path = Path.Combine(_dir, name);
        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true)) fill(zip);
            fs.Flush(flushToDisk: true);
        }
        catch (Exception)
        {
            if (File.Exists(path)) { try { SafeDelete.FileAtExactPath(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            throw;
        }
        return name;
    }

    public void AddFile(string path)
    {
        string entry = BackupRules.EntryName(path);
        string zip = WriteZip(z => z.CreateEntryFromFile(path, entry, CompressionLevel.Fastest));
        Manifest.Items.Add(new BackupItem { Kind = BackupKind.File, OriginalPath = path, Archive = entry, Zip = zip, Bytes = new FileInfo(path).Length });
        BackupStore.Save(_dir, Manifest);
    }

    /// <summary>A pasta inteira vai para o zip; um arquivo que não dá para ler (em uso) cancela, e nada da pasta é apagado.</summary>
    public void AddFolder(string dir)
    {
        long total = 0;
        string zip = WriteZip(z =>
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", Recursive))
            {
                z.CreateEntryFromFile(f, BackupRules.EntryName(f), CompressionLevel.Fastest);
                total += new FileInfo(f).Length;
            }
            foreach (var d in Directory.EnumerateDirectories(dir, "*", Recursive))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) z.CreateEntry(BackupRules.EntryName(d) + "/");
        });
        Manifest.Items.Add(new BackupItem { Kind = BackupKind.Folder, OriginalPath = dir, Archive = BackupRules.EntryName(dir), Zip = zip, Bytes = total });
        BackupStore.Save(_dir, Manifest);
    }

    public void AddRecycled(string path, long bytes)
    {
        Manifest.Items.Add(new BackupItem { Kind = BackupKind.Recycled, OriginalPath = path, Bytes = 0 });
        BackupStore.Save(_dir, Manifest);
    }

    public void AddKey(string key, BackupKind kind = BackupKind.RegistryKey)
    {
        string file = $"{++_n:000}.reg";
        if (BackupStore.Run("reg.exe", "export", key, Path.Combine(_dir, file), "/y") != 0)
            throw new IOException(Loc.F("Não foi possível guardar a chave {0} antes de apagar.", key));
        Manifest.Items.Add(new BackupItem { Kind = kind, OriginalPath = key, Archive = file });
        BackupStore.Save(_dir, Manifest);
    }

    public void AddValue(string key, string name, RegistryValueKind kind, object data)
    {
        Manifest.Items.Add(new BackupItem
        {
            Kind = BackupKind.RegistryValue, OriginalPath = key, ValueName = name, ValueKind = kind.ToString(), ValueData = RegistryData.Encode(kind, data),
        });
        BackupStore.Save(_dir, Manifest);
    }

    public void AddTask(string path, string xml)
    {
        string file = $"{++_n:000}.xml";
        File.WriteAllText(Path.Combine(_dir, file), xml, Encoding.Unicode); // o XML do Agendador declara UTF-16
        Manifest.Items.Add(new BackupItem { Kind = BackupKind.Task, OriginalPath = path, Archive = file });
        BackupStore.Save(_dir, Manifest);
    }

    public void Dispose() => BackupStore.Save(_dir, Manifest);
}

/// <summary>Valores do registro em texto, para o manifesto: números em decimal, binário em base64, várias linhas separadas por \0.</summary>
public static class RegistryData
{
    public static string Encode(RegistryValueKind kind, object data) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])data),
        RegistryValueKind.MultiString => string.Join('\0', (string[])data),
        RegistryValueKind.DWord => Convert.ToUInt32(unchecked((uint)(int)data)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToUInt64(unchecked((ulong)(long)data)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => data.ToString() ?? "",
    };

    public static object Decode(RegistryValueKind kind, string text) => kind switch
    {
        RegistryValueKind.Binary => Convert.FromBase64String(text),
        RegistryValueKind.MultiString => text.Length == 0 ? Array.Empty<string>() : text.Split('\0'),
        RegistryValueKind.DWord => unchecked((int)uint.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryValueKind.QWord => unchecked((long)ulong.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
        _ => text,
    };
}
