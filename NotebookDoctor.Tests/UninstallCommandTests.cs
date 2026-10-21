using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

public class UninstallCommandTests
{
    private static Func<string, bool> Exists(params string[] files) =>
        p => files.Contains(p, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Quoted_WithArguments()
    {
        var c = UninstallCommand.Parse("\"C:\\Program Files\\App\\unins000.exe\" /SILENT /LOG=\"x y.txt\"", Exists(@"C:\Program Files\App\unins000.exe"));
        Assert.Equal(@"C:\Program Files\App\unins000.exe", c!.Executable);
        Assert.Equal("/SILENT /LOG=\"x y.txt\"", c.Arguments);
    }

    [Fact]
    public void Unquoted_PathWithSpaces()
    {
        var c = UninstallCommand.Parse(@"C:\Program Files\App\unins000.exe /S", Exists(@"C:\Program Files\App\unins000.exe"));
        Assert.Equal(@"C:\Program Files\App\unins000.exe", c!.Executable);
        Assert.Equal("/S", c.Arguments);
    }

    [Fact]
    public void Unquoted_NoArguments()
    {
        var c = UninstallCommand.Parse(@"C:\Apps\remove.exe", Exists(@"C:\Apps\remove.exe"));
        Assert.Equal("", c!.Arguments);
        Assert.Equal(@"C:\Apps\remove.exe", c.Display);
    }

    [Fact]
    public void BareName_ResolvesOnlyInSystemDirectory()
    {
        string msi = Path.Combine(Environment.SystemDirectory, "MsiExec.exe");
        var c = UninstallCommand.Parse("MsiExec.exe /X{ABC-123}", Exists(msi));
        Assert.Equal(msi, c!.Executable);
        Assert.Equal("/X{ABC-123}", c.Arguments);
    }

    [Fact]
    public void RelativePathWithDirectory_IsRejected() =>
        Assert.Null(UninstallCommand.Parse(@"..\evil.exe /S", _ => true));

    [Fact]
    public void ShellMetacharacters_StayInArguments()
    {
        var c = UninstallCommand.Parse(@"""C:\Apps\u.exe"" /S & del /q C:\*", Exists(@"C:\Apps\u.exe"));
        Assert.Equal(@"C:\Apps\u.exe", c!.Executable);
        Assert.Equal(@"/S & del /q C:\*", c.Arguments);   // nunca passa por cmd.exe, então "&" não executa nada
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ND_TEST_DIR", @"C:\Apps");
        var c = UninstallCommand.Parse(@"""%ND_TEST_DIR%\u.exe"" /S", Exists(@"C:\Apps\u.exe"));
        Assert.Equal(@"C:\Apps\u.exe", c!.Executable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\sem fecha.exe /S")]
    public void Invalid_ReturnsNull(string? raw) => Assert.Null(UninstallCommand.Parse(raw, _ => true));

    [Fact]
    public void MissingExecutable_ReturnsNull() =>
        Assert.Null(UninstallCommand.Parse(@"C:\Apps\gone.exe /S", Exists()));
}
