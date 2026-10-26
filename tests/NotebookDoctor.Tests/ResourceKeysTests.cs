using System.Text.RegularExpressions;

namespace NotebookDoctor.Tests;

/// <summary>
/// Cor ou estilo com o nome errado no XAML não dá erro: o WPF só mostra o controle sem a cor. Este teste pega a chave que
/// ninguém declarou (por exemplo, depois de renomear uma cor).
/// </summary>
public class ResourceKeysTests
{
    [Fact]
    public void Toda_chave_de_recurso_usada_nas_telas_e_no_codigo_existe()
    {
        string app = RepoPaths.App;
        var xamls = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        var codes = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")).ToList();

        var defined = new HashSet<string>();
        foreach (var f in xamls) foreach (Match m in Regex.Matches(File.ReadAllText(f), @"x:Key=""(\w+)""")) defined.Add(m.Groups[1].Value);
        foreach (var f in codes)
        {
            string code = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(code, @"Resources\[""(\w+)""\]\s*=")) defined.Add(m.Groups[1].Value);
            if (f.EndsWith("TextScale.cs")) foreach (Match m in Regex.Matches(code, @"\(""(Fs\w+)"",")) defined.Add(m.Groups[1].Value);
        }

        var used = new List<(string File, string Key)>();
        foreach (var f in xamls) foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{(?:DynamicResource|StaticResource) (\w+)\}")) used.Add((f, m.Groups[1].Value));
        foreach (var f in codes)
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(?:SetResourceReference\([^,()]+,\s*|TryFindResource\(|FindResource\(|ThemeManager\.Get\()""(\w+)"""))
                used.Add((f, m.Groups[1].Value));

        // "On" + cor é montada em tempo de execução (MainWindow.Painel); as chaves completas (OnCritical…) estão nas contas dos temas
        var missing = used.Where(u => u.Key != "On" && !defined.Contains(u.Key)).Select(u => $"{Path.GetFileName(u.File)}: {u.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Chave de recurso que não existe:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Nomes_de_cor_antigos_nao_voltam_nas_telas()
    {
        string[] old = ["Surface", "SurfaceContainer", "CardBorder", "OnSurface", "OnSurfaceVariant", "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer", "Warning", "OnWarning", "WarningContainer", "Error", "OnError", "ErrorContainer"];
        var back = new List<string>();
        foreach (var f in Directory.EnumerateFiles(RepoPaths.App, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(?:(?:DynamicResource|StaticResource) |x:Key="")(\w+)"))
                if (old.Contains(m.Groups[1].Value)) back.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        Assert.True(back.Count == 0, "Nome de cor antigo:\n" + string.Join("\n", back.Distinct()));
    }
}
