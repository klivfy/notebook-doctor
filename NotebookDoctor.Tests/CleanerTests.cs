using System.Diagnostics;
using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

public sealed class CleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nd-cleaner-" + Guid.NewGuid().ToString("N"));

    public CleanerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Make(string rel, int ageDays, string content = "x")
    {
        string path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageDays));
        return path;
    }

    private CleanItem Item(bool ignoreAge = false, bool selected = true, bool canClean = true, string? dir = null) =>
        new() { Label = "t", Paths = [dir ?? _root], Selected = selected, IgnoreAge = ignoreAge, CanClean = canClean };

    [Fact]
    public void Deletes_OnlyFilesOlderThanCutoff()
    {
        string old = Make("old.tmp", 30), recent = Make("recent.tmp", 1);
        Cleaner.Clean([Item()], days: 7);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void IgnoreAge_DeletesRecentFilesToo()
    {
        string recent = Make("recent.tmp", 0);
        Cleaner.Clean([Item(ignoreAge: true)], days: 7);
        Assert.False(File.Exists(recent));
    }

    [Fact]
    public void ReturnsFreedBytes()
    {
        Make("a.tmp", 30, new string('a', 100));
        Make("sub/b.tmp", 30, new string('b', 50));
        Assert.Equal(150, Cleaner.Clean([Item()], days: 7));
    }

    [Fact]
    public void RemovesEmptyDirectories_ButKeepsOnesWithRecentFiles()
    {
        Make("gone/old.tmp", 30);
        string kept = Make("kept/recent.tmp", 0);
        Cleaner.Clean([Item()], days: 7);
        Assert.False(Directory.Exists(Path.Combine(_root, "gone")));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void Unselected_OrLocked_ItemsAreUntouched()
    {
        string a = Make("a.tmp", 30);
        Cleaner.Clean([Item(selected: false)], days: 7);
        Cleaner.Clean([Item(canClean: false)], days: 7);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void LockedFile_IsSkipped_WithoutThrowing()
    {
        string locked = Make("locked.tmp", 30), free = Make("free.tmp", 30);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Cleaner.Clean([Item()], days: 7);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public void MissingFolder_IsIgnored() =>
        Assert.Equal(0, Cleaner.Clean([Item(dir: Path.Combine(_root, "nao-existe"))], days: 7));

    [Fact]
    public void DoesNotFollowJunctions()
    {
        // Pasta que NÃO pode ser tocada, fora da raiz limpa, ligada por junction.
        string outside = Path.Combine(Path.GetTempPath(), "nd-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string precious = Path.Combine(outside, "precious.doc");
        File.WriteAllText(precious, "x");
        File.SetLastWriteTime(precious, DateTime.Now.AddDays(-365));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string link = Path.Combine(target, "link");
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            if (!Directory.Exists(link)) return; // sem permissão/SO para criar junction: nada a verificar

            Cleaner.Clean([Item(dir: target, ignoreAge: true)], days: 0);
            Assert.True(File.Exists(precious), "arquivo atrás da junction foi apagado");
        }
        catch (System.ComponentModel.Win32Exception) { }   // sem cmd.exe (fora do Windows): nada a verificar
        finally { try { Directory.Delete(link); } catch { } try { Directory.Delete(outside, true); } catch { } }
    }

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3,0 GB")]
    public void FormatBytes(long bytes, string expectedPt)
    {
        // O separador decimal depende da cultura do runner; compara ignorando vírgula/ponto.
        Assert.Equal(expectedPt.Replace(',', '.'), Cleaner.FormatBytes(bytes).Replace(',', '.'));
    }
}
