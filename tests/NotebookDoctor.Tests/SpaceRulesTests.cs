using System.Runtime.Versioning;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class SpaceRulesTests
{
    [Theory]
    [InlineData(@"C:\Fotos\ferias.JPG", FileCategory.Images)]
    [InlineData(@"C:\v\filme.mkv", FileCategory.Videos)]
    [InlineData(@"C:\m\som.flac", FileCategory.Audio)]
    [InlineData(@"C:\d\relatorio.pdf", FileCategory.Documents)]
    [InlineData(@"C:\a\copia.7z", FileCategory.Archives)]
    [InlineData(@"C:\p\app.exe", FileCategory.Programs)]
    [InlineData(@"C:\x\semextensao", FileCategory.Other)]
    [InlineData(@"C:\pasta.jpg\arquivo", FileCategory.Other)] // o ponto está na pasta, não no arquivo
    [InlineData(@"C:\x\dados.zzz", FileCategory.Other)]
    public void Categoria_pela_extensao(string path, FileCategory expected) => Assert.Equal(expected, FileCategories.Of(path));

    [Fact]
    public void Soma_por_categoria_e_mostra_as_extensoes_maiores()
    {
        var s = new SpaceByType();
        s.Add(@"C:\a\1.jpg", 100); s.Add(@"C:\a\2.jpg", 200); s.Add(@"C:\a\3.png", 50); s.Add(@"C:\a\4.heic", 10); s.Add(@"C:\a\5.bmp", 5);
        s.Add(@"C:\v\1.mp4", 1000);
        s.Add(@"C:\x\sem", 7);
        var r = s.Result();
        Assert.Equal([FileCategory.Videos, FileCategory.Images, FileCategory.Other], r.Select(t => t.Category));
        var images = r[1];
        Assert.Equal(365, images.Bytes);
        Assert.Equal(5, images.Files);
        Assert.Equal([".jpg", ".png", ".heic"], images.TopExtensions.Select(e => e.Extension));
        Assert.Contains("Imagens · 5 arquivos", images.Description);
        Assert.Empty(r[2].TopExtensions); // sem extensão
    }
}

public class DuplicateRulesTests
{
    private static readonly DateTime T = new(2026, 1, 1);

    private static DuplicateGroup Group(params (string Path, int ModifiedDays, int CreatedDays)[] files) =>
        new(1000, files.Select(f => new DuplicateFile(f.Path, T.AddDays(f.ModifiedDays), T.AddDays(f.CreatedDays))).ToList());

    [Fact]
    public void Mais_antiga_e_mais_nova()
    {
        var g = Group((@"C:\a\foto.jpg", 10, 10), (@"C:\b\foto (1).jpg", 30, 30), (@"C:\c\foto.jpg", 20, 20));
        Assert.Equal(@"C:\a\foto.jpg", DuplicateRules.Keeper(g, KeepRule.Oldest).Path);
        Assert.Equal(@"C:\b\foto (1).jpg", DuplicateRules.Keeper(g, KeepRule.Newest).Path);
        Assert.Equal(2, DuplicateRules.Others(g, KeepRule.Oldest).Count);
        Assert.DoesNotContain(DuplicateRules.Others(g, KeepRule.Oldest), f => f.Path == @"C:\a\foto.jpg");
    }

    [Fact]
    public void Empate_de_data_decide_pela_criacao_depois_pelo_caminho_mais_curto()
    {
        var byCreated = Group((@"C:\a\x.bin", 5, 9), (@"C:\b\x.bin", 5, 2));
        Assert.Equal(@"C:\b\x.bin", DuplicateRules.Keeper(byCreated, KeepRule.Oldest).Path);
        var byPath = Group((@"C:\pasta longa\x.bin", 5, 5), (@"C:\p\x.bin", 5, 5));
        Assert.Equal(@"C:\p\x.bin", DuplicateRules.Keeper(byPath, KeepRule.Oldest).Path);
        Assert.Equal(@"C:\p\x.bin", DuplicateRules.Keeper(byPath, KeepRule.Newest).Path);
        var same = Group((@"C:\B\x.bin", 5, 5), (@"C:\A\x.bin", 5, 5));
        Assert.Equal(@"C:\A\x.bin", DuplicateRules.Keeper(same, KeepRule.Oldest).Path); // determinístico
    }

    [Fact]
    public void Marcar_todas_as_copias_sempre_deixa_uma()
    {
        var g = Group((@"C:\a\f.jpg", 10, 10), (@"C:\b\f.jpg", 20, 20), (@"C:\c\f.jpg", 30, 30));
        var all = g.Files.Select(f => f.Path).ToList();
        var doomed = DuplicateRules.ToDelete(g, all, KeepRule.Oldest);
        Assert.Equal(2, doomed.Count);
        Assert.DoesNotContain(@"C:\a\f.jpg", doomed);
        Assert.Equal(2, DuplicateRules.ToDelete(g, all, KeepRule.Newest).Count);
        Assert.DoesNotContain(@"C:\c\f.jpg", DuplicateRules.ToDelete(g, all, KeepRule.Newest));
    }

    [Fact]
    public void Ignora_o_que_nao_e_do_grupo_e_compara_sem_diferenciar_maiusculas()
    {
        var g = Group((@"C:\a\f.jpg", 10, 10), (@"C:\b\f.jpg", 20, 20));
        Assert.Equal([@"C:\b\f.jpg"], DuplicateRules.ToDelete(g, [@"c:\B\F.JPG", @"C:\outro\arquivo.docx"], KeepRule.Oldest));
        Assert.Empty(DuplicateRules.ToDelete(g, [@"C:\fora\x"], KeepRule.Oldest));
        Assert.Empty(DuplicateRules.ToDelete(g, [], KeepRule.Oldest));
    }

    [Fact]
    public void Qualquer_marcacao_deixa_pelo_menos_uma_copia()
    {
        var rng = new Random(42);
        for (int round = 0; round < 500; round++)
        {
            int n = rng.Next(2, 7);
            var g = new DuplicateGroup(10, Enumerable.Range(0, n).Select(i => new DuplicateFile($@"C:\d{i}\f.bin", T.AddDays(rng.Next(5)), T.AddDays(rng.Next(5)))).ToList());
            var chosen = g.Files.Where(_ => rng.Next(3) > 0).Select(f => f.Path).Concat(rng.Next(4) == 0 ? [@"C:\estranho\x"] : []).ToList();
            var doomed = DuplicateRules.ToDelete(g, chosen, rng.Next(2) == 0 ? KeepRule.Oldest : KeepRule.Newest);
            Assert.True(doomed.Count <= n - 1, $"rodada {round}: {doomed.Count} de {n}");
            Assert.All(doomed, d => Assert.Contains(g.Files, f => f.Path == d));
            Assert.Equal(doomed.Count, doomed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void Copias_do_sistema_nao_entram_nos_grupos()
    {
        var p = LeftoverScannerTests.Places;
        var g = Group((@"C:\Windows\System32\x.dll", 1, 1), (@"C:\Program Files\App\x.dll", 1, 1), (@"C:\Users\ana\Downloads\x.dll", 1, 1), (@"C:\Users\ana\Desktop\x.dll", 1, 1));
        var clean = DuplicateRules.WithoutSystem(g, p)!;
        Assert.Equal([@"C:\Users\ana\Downloads\x.dll", @"C:\Users\ana\Desktop\x.dll"], clean.Files.Select(f => f.Path));
        var onlyOneLeft = Group((@"C:\Windows\a", 1, 1), (@"C:\Users\ana\b", 1, 1));
        Assert.Null(DuplicateRules.WithoutSystem(onlyOneLeft, p));
        Assert.True(DuplicateRules.IsSystemPath(@"C:\ProgramData\x\y", p));
        Assert.False(DuplicateRules.IsSystemPath(@"C:\Windowsapps\y", p)); // nome parecido, outra pasta
    }

    [Fact]
    public void Desperdicio_do_grupo()
    {
        var g = Group((@"C:\a", 1, 1), (@"C:\b", 1, 1), (@"C:\c", 1, 1));
        Assert.Equal(2000, g.Wasted);
    }
}

[SupportedOSPlatform("windows")]
public class DuplicateRemoverIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NDDup" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly KnownPlaces Places = WindowsPlaces.Current();

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Make(string name, string content, int daysOld)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysOld));
        return path;
    }

    private static DuplicateGroup GroupOf(params string[] paths) =>
        new(new FileInfo(paths[0]).Length, paths.Select(p => new DuplicateFile(p, File.GetLastWriteTime(p), File.GetCreationTime(p))).ToList());

    [WindowsFact]
    public void Apaga_so_as_marcadas_e_confere_o_conteudo()
    {
        string a = Make(@"a\foto.dat", "conteudo igual", 30), b = Make(@"b\foto.dat", "conteudo igual", 10), c = Make(@"c\foto.dat", "conteudo igual", 5);
        var g = GroupOf(a, b, c);
        var result = DuplicateRemover.Remove([(g, [b])], Places, KeepRule.Oldest, permanent: true);
        Assert.Equal(1, result.Files);
        Assert.Equal(g.Size, result.Bytes);
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(File.Exists(c));
    }

    [WindowsFact]
    public void Marcar_todas_deixa_a_mais_antiga()
    {
        string a = Make(@"a\x.dat", "igual", 30), b = Make(@"b\x.dat", "igual", 10);
        var g = GroupOf(a, b);
        var result = DuplicateRemover.Remove([(g, [a, b])], Places, KeepRule.Oldest, permanent: true);
        Assert.Equal(1, result.Files);
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
    }

    [WindowsFact]
    public void Arquivo_que_mudou_depois_da_busca_nao_e_apagado()
    {
        string a = Make(@"a\x.dat", "original", 30), b = Make(@"b\x.dat", "original", 10);
        var g = GroupOf(a, b);
        File.WriteAllText(b, "ALTERADO"); // mesmo tamanho (8 bytes), conteúdo diferente
        var result = DuplicateRemover.Remove([(g, [b])], Places, KeepRule.Oldest, permanent: true);
        Assert.Equal(0, result.Files);
        Assert.Single(result.Skipped);
        Assert.True(File.Exists(b));
    }

    [WindowsFact]
    public void Sem_copia_que_fique_no_disco_nada_e_apagado()
    {
        string a = Make(@"a\x.dat", "igual", 30), b = Make(@"b\x.dat", "igual", 10);
        var g = GroupOf(a, b);
        File.Delete(a); // a cópia que ficaria sumiu: b passaria a ser a única
        var result = DuplicateRemover.Remove([(g, [b])], Places, KeepRule.Oldest, permanent: true);
        Assert.Equal(0, result.Files);
        Assert.True(File.Exists(b));
    }

    [WindowsFact]
    public void Arquivos_do_sistema_nunca_saem()
    {
        string a = Make(@"a\x.dat", "igual", 30);
        string inWindows = Path.Combine(Places.Windows, "nd-teste-nao-existe.dat");
        var g = new DuplicateGroup(new FileInfo(a).Length, [new DuplicateFile(a, DateTime.Now, DateTime.Now), new DuplicateFile(inWindows, DateTime.Now, DateTime.Now)]);
        var result = DuplicateRemover.Remove([(g, [inWindows])], Places, KeepRule.Oldest, permanent: true);
        Assert.Equal(0, result.Files);
        Assert.True(File.Exists(a));
    }
}
