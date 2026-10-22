using System.Net;
using System.Text.RegularExpressions;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

public class LocTests
{
    [Theory]
    [InlineData("auto", "pt-BR", "pt")]
    [InlineData("auto", "pt-PT", "pt")]
    [InlineData("auto", "es-MX", "es")]
    [InlineData("auto", "en-US", "en")]
    [InlineData("auto", "fr-FR", "en")] // idioma sem tradução: inglês
    [InlineData("pt", "en-US", "pt")]    // escolha da pessoa vale mais que o Windows
    [InlineData("es", "pt-BR", "es")]
    [InlineData(null, "pt-BR", "pt")]
    public void Escolhe_o_idioma(string? setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, windows));

    [Fact]
    public void Traduz_e_volta_ao_portugues()
    {
        try
        {
            Loc.Use("en");
            Assert.Equal("Dashboard", Loc.T("Painel"));
            Assert.Equal("Texto sem tradução", Loc.T("Texto sem tradução")); // nunca some
            Assert.False(Loc.IsSource);
            Assert.Equal("en-US", Loc.Culture.Name);
            Loc.Use("es");
            Assert.Equal("Panel", Loc.T("Painel"));
            Loc.Use("xx");
            Assert.Equal("Painel", Loc.T("Painel"));
            Assert.True(Loc.IsSource);
        }
        finally { Loc.Use("pt"); }
    }

    [Fact]
    public void Inglês_e_espanhol_tem_as_mesmas_chaves_e_nada_vazio()
    {
        var en = Loc.Load("en");
        var es = Loc.Load("es");
        Assert.True(en.Count > 300);
        Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), es.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(en.Values.Concat(es.Values), v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Traducoes_mantem_marcadores_e_teclas_de_acesso()
    {
        static IEnumerable<string> Holes(string s) => Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x);
        foreach (var lang in new[] { "en", "es" })
            foreach (var (pt, t) in Loc.Load(lang))
            {
                Assert.True(Holes(pt).SequenceEqual(Holes(t)), $"{lang}: marcadores diferentes em \"{pt}\"");
                // "_Detalhes" → "_Details": a letra sublinhada do menu precisa continuar existindo
                Assert.True(pt.Count(c => c == '_') == t.Count(c => c == '_'), $"{lang}: tecla de acesso em \"{pt}\"");
            }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotebookDoctor.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }

    /// <summary>Todo texto escrito nas telas (XAML) precisa ter tradução, senão aparece em português no meio do inglês.</summary>
    [Fact]
    public void Todo_texto_das_telas_tem_traducao()
    {
        var en = Loc.Load("en");
        var missing = new List<string>();
        foreach (var file in new[] { "MainWindow.xaml", "MiniWidget.xaml", "AdvancedCleanWindow.xaml", "DuplicatesWindow.xaml", "UpdatesWindow.xaml" })
        {
            string xaml = File.ReadAllText(Path.Combine(Root(), file));
            foreach (Match m in Regex.Matches(xaml, @"\b(Content|Text|Header|ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText|Title)=""([^""]*)"""))
            {
                string v = WebUtility.HtmlDecode(m.Groups[2].Value);
                if (v.Length == 0 || v.StartsWith('{') || !Regex.IsMatch(v, "[A-Za-zÀ-ú]")) continue;
                if (!en.ContainsKey(v)) missing.Add($"{file}: {v}");
            }
        }
        Assert.True(missing.Count == 0, "Sem tradução:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void Todo_Loc_T_do_codigo_tem_traducao()
    {
        var en = Loc.Load("en");
        var missing = Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains("Tests"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Loc\.[TF]\(""((?:[^""\\]|\\.)*)""").Select(m => Regex.Unescape(m.Groups[1].Value)))
            .Where(k => !en.ContainsKey(k))
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Sem tradução:\n" + string.Join("\n", missing));
    }
}
