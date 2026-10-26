namespace NotebookDoctor.Tests;

/// <summary>Onde ficam os arquivos do repositório que alguns testes leem (telas, temas e textos embutidos).</summary>
internal static class RepoPaths
{
    /// <summary>Raiz do repositório: a pasta que tem src/NotebookDoctor/NotebookDoctor.csproj.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "NotebookDoctor", "NotebookDoctor.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
        }
    }

    /// <summary>Pasta do app WPF (telas, Themes, Textos).</summary>
    public static string App => Path.Combine(Root, "src", "NotebookDoctor");
}
