using System.Diagnostics;
using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class FinalPathTests
{
    [Theory]
    [InlineData(@"\\?\C:\Windows\Temp\a.tmp", @"C:\Windows\Temp\a.tmp")]
    [InlineData(@"\\?\UNC\srv\share\x", @"\\srv\share\x")]
    [InlineData(@"\\?\unc\srv\share", @"\\srv\share")]
    [InlineData(@"C:\x", @"C:\x")]
    public void Caminho_final_volta_ao_formato_comum(string final, string expected) =>
        Assert.Equal(expected, Paths.FromFinalPath(final));
}

/// <summary>
/// S1: a limpeza apaga pelo identificador. Uma subpasta trocada por junção entre a lista e a exclusão não pode levar
/// a exclusão para fora da pasta limpa (com o app elevado, isso apagaria arquivos do sistema).
/// </summary>
[SupportedOSPlatform("windows")]
public class SafeDeleteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-safedelete-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _outside;

    public SafeDeleteTests()
    {
        _root = Path.Combine(_dir, "limpar");
        _outside = Path.Combine(_dir, "fora");
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(_dir)) return;
        foreach (var link in Directory.EnumerateDirectories(_dir, "*", SearchOption.AllDirectories)
                     .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
            Directory.Delete(link); // tira a junção sem entrar nela
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J não criou a junção");
    }

    [WindowsFact]
    public void Apaga_arquivo_dentro_da_pasta()
    {
        string f = Path.Combine(_root, "sub", "a.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, "x");
        string root = SafeDelete.Root(_root)!;
        Assert.True(SafeDelete.File(f, root, ignoreReadOnly: false));
        Assert.False(File.Exists(f));
    }

    [WindowsFact]
    public void Subpasta_trocada_por_juncao_nao_leva_a_exclusao_para_fora()
    {
        string victim = Path.Combine(_outside, "sistema.dll");
        File.WriteAllText(victim, "importante");
        string root = SafeDelete.Root(_root)!;
        // A lista viu limpar\sub\sistema.dll; depois alguém troca "sub" por uma junção para fora da pasta
        Junction(Path.Combine(_root, "sub"), _outside);
        Assert.False(SafeDelete.File(Path.Combine(_root, "sub", "sistema.dll"), root, ignoreReadOnly: true));
        Assert.True(File.Exists(victim));
    }

    [WindowsFact]
    public void Pasta_vazia_alcancada_por_juncao_fica()
    {
        string empty = Path.Combine(_outside, "vazia");
        Directory.CreateDirectory(empty);
        string root = SafeDelete.Root(_root)!;
        Junction(Path.Combine(_root, "sub"), _outside);
        Assert.False(SafeDelete.Directory(Path.Combine(_root, "sub", "vazia"), root));
        Assert.True(Directory.Exists(empty));
    }

    [WindowsFact]
    public void Raiz_que_passa_por_juncao_e_recusada()
    {
        Junction(Path.Combine(_root, "atalho"), _outside);
        Assert.Null(SafeDelete.Root(Path.Combine(_root, "atalho")));
        Assert.NotNull(SafeDelete.Root(_root));
    }

    [WindowsFact]
    public void Somente_leitura_so_sai_quando_pedido()
    {
        string f = Path.Combine(_root, "ro.tmp");
        File.WriteAllText(f, "x");
        File.SetAttributes(f, FileAttributes.ReadOnly);
        string root = SafeDelete.Root(_root)!;
        Assert.Throws<UnauthorizedAccessException>(() => SafeDelete.File(f, root, ignoreReadOnly: false));
        Assert.True(File.Exists(f));
        Assert.True(SafeDelete.File(f, root, ignoreReadOnly: true));
        Assert.False(File.Exists(f));
    }

    [WindowsFact]
    public void Pasta_vazia_dentro_da_raiz_sai_e_cheia_fica()
    {
        string empty = Path.Combine(_root, "vazia"), full = Path.Combine(_root, "cheia");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "a.txt"), "x");
        string root = SafeDelete.Root(_root)!;
        Assert.True(SafeDelete.Directory(empty, root));
        Assert.False(Directory.Exists(empty));
        Assert.ThrowsAny<IOException>(() => SafeDelete.Directory(full, root));
        Assert.True(Directory.Exists(full));
    }
}

/// <summary>C2: cada item do backup fica num zip já fechado; se o app cair no meio, o Desfazer ainda devolve o que saiu.</summary>
[SupportedOSPlatform("windows")]
public class BackupDurabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-backup-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [WindowsFact]
    public void Sessao_que_nao_fechou_ainda_restaura_o_que_saiu()
    {
        var store = new BackupStore(Path.Combine(_dir, "backups"));
        string file = Path.Combine(_dir, "dados", "a.txt"), folder = Path.Combine(_dir, "pasta");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(file, "conteúdo");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "b");

        var session = store.Begin("queda no meio"); // sem Dispose: o app caiu antes de fechar a sessão
        session.AddFile(file);
        File.Delete(file);
        session.AddFolder(folder);
        Directory.Delete(folder, recursive: true);

        string msg = store.Restore(session.Id);
        Assert.Matches(@"\b2 \w+ 2\b", msg); // "2 de 2" (ou "2 of 2" se outro teste trocou o idioma)
        Assert.Equal("conteúdo", File.ReadAllText(file));
        Assert.Equal("b", File.ReadAllText(Path.Combine(folder, "sub", "b.txt")));
    }

    [WindowsFact]
    public void Zip_estragado_falha_so_o_item_dele()
    {
        var store = new BackupStore(Path.Combine(_dir, "backups"));
        string a = Path.Combine(_dir, "a.txt"), b = Path.Combine(_dir, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        string id;
        using (var session = store.Begin("zip estragado"))
        {
            session.AddFile(a);
            session.AddFile(b);
            id = session.Id;
            File.WriteAllText(Path.Combine(store.Root, id, session.Manifest.Items[0].Zip), "não é um zip");
        }
        File.Delete(a);
        File.Delete(b);

        string msg = store.Restore(id); // antes: InvalidDataException derrubava o Desfazer inteiro
        Assert.Matches(@"\b1 \w+ 2\b", msg);
        Assert.False(File.Exists(a));
        Assert.Equal("b", File.ReadAllText(b));
    }
}
