using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Fase 9: fundação (carregamento de DLLs, gravação atômica, listas grandes).</summary>
public class Fase9Tests
{
    // Leva 9.1, tarefa 05: o executável portátil roda de pastas onde qualquer um deixa arquivos (Downloads, pendrive)
    [Fact]
    public void Biblioteca_do_Windows_so_carrega_DLLs_da_System32()
    {
        var attr = typeof(SafeDelete).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(DllImportSearchPath.System32, attr!.Paths);
    }

    [Fact]
    public void App_so_carrega_DLLs_da_System32()
    {
        string info = File.ReadAllText(Path.Combine(RepoPaths.App, "AssemblyInfo.cs"));
        Assert.Contains("DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)", info);
    }

    [Fact]
    public void Nenhuma_DLL_e_carregada_pelo_nome_com_a_busca_padrao()
    {
        // NativeLibrary.TryLoad(x, out h) e Load(x) procuram primeiro na pasta do executável. Pelo nome, só com DllImportSearchPath;
        // sem ele, só com um caminho completo montado por Path.Combine no mesmo arquivo
        var call = new Regex(@"NativeLibrary\.(?:Try)?Load\(\s*([^,()]+?)\s*(?:,\s*out\b|\))");
        var hits = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            string code = File.ReadAllText(f);
            foreach (Match m in call.Matches(code))
            {
                string arg = m.Groups[1].Value;
                bool fullPath = Regex.IsMatch(arg, @"^\w+$") && Regex.IsMatch(code, $@"\b{arg}\s*=\s*Path\.Combine\(");
                if (!fullPath) hits.Add($"{Path.GetFileName(f)}: {m.Value}");
            }
        }
        Assert.Empty(hits);
    }
}
