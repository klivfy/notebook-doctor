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

    /// <summary>M13: o registro de Ações, a linha do tempo e as datas curtas saem no idioma do app.</summary>
    [Theory]
    [InlineData("en", "Program ended", "Permanent", "Can be undone", "Undone", "App action", "10/25 14:30")]
    [InlineData("es", "Programa cerrado", "Definitivo", "Se puede deshacer", "Deshecho", "Acción de la app", "25/10 14:30")]
    public void Registro_de_acoes_e_linha_do_tempo_saem_no_idioma_do_app(string lang, string title, string permanent, string canUndo, string undone, string kind, string stamp)
    {
        try
        {
            Loc.Use(lang);
            var fixedEntry = new ActionEntry { Title = "Programa encerrado", Time = new DateTime(2026, 10, 25, 14, 30, 0) };
            Assert.Equal(title, fixedEntry.TitleText);
            Assert.Equal(permanent, fixedEntry.StatusText);
            Assert.Equal(canUndo, new ActionEntry { UndoKind = "power" }.StatusText);
            Assert.Equal(undone, new ActionEntry { UndoKind = "power", Undone = true }.StatusText);
            Assert.Equal(stamp, fixedEntry.TimeText);
            var ev = new TimelineEvent(fixedEntry.Time, "Ação do app", "Programa encerrado", "");
            Assert.Equal(kind, ev.KindText);
            Assert.Equal(title, ev.TitleText);
        }
        finally { Loc.Use("pt"); }
        // Em português nada muda
        Assert.Equal("Definitivo", new ActionEntry().StatusText);
        Assert.Equal("25/10 14:30", new ActionEntry { Time = new DateTime(2026, 10, 25, 14, 30, 0) }.TimeText);
    }

    /// <summary>
    /// M13: Novidades, Privacidade e Licenças existem em inglês e espanhol, com a mesma estrutura do original: as mesmas
    /// linhas (em branco, título ou item de lista) e os mesmos números em cada linha. Assim, uma tradução não perde nem
    /// troca um dado (30 dias, 500 MB a 5 GB, 1366 × 768) e uma novidade nova não fica só em português.
    /// </summary>
    [Theory]
    [InlineData("Novidades")]
    [InlineData("Privacidade")]
    [InlineData("Licencas")]
    public void Textos_do_Sobre_tem_versao_em_cada_idioma_com_a_mesma_estrutura(string name)
    {
        static List<string> Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        // Só os dígitos, sem separador decimal ("11,8" e "11.8" valem o mesmo)
        static List<string> Numbers(string line) => Regex.Matches(Regex.Replace(line, @"(?<=\d)[.,](?=\d)", ""), @"\d+").Select(m => m.Value).ToList();

        string dir = Path.Combine(Root(), "Textos");
        string source = File.ReadAllText(Path.Combine(dir, name + ".txt"));
        var pt = Lines(source);
        foreach (var lang in new[] { "en", "es" })
        {
            string translated = File.ReadAllText(Path.Combine(dir, $"{name}.{lang}.txt"));
            Assert.NotEqual(source, translated);
            var t = Lines(translated);
            Assert.True(t.Count == pt.Count, $"{name}.{lang}: {t.Count} linhas, o original tem {pt.Count}");
            for (int i = 0; i < pt.Count; i++)
            {
                Assert.True(pt[i].Length == 0 == (t[i].Length == 0), $"{name}.{lang} linha {i + 1}: linha em branco diferente do original");
                Assert.True(pt[i].StartsWith("• ") == t[i].StartsWith("• "), $"{name}.{lang} linha {i + 1}: marcador de lista diferente do original");
                Assert.True(Numbers(pt[i]).SequenceEqual(Numbers(t[i])), $"{name}.{lang} linha {i + 1}: números diferentes do original ({string.Join(", ", Numbers(pt[i]))} × {string.Join(", ", Numbers(t[i]))})");
            }
        }
    }

    /// <summary>Todo texto escrito nas telas (XAML) precisa ter tradução, senão aparece em português no meio do inglês.</summary>
    [Fact]
    public void Todo_texto_das_telas_tem_traducao()
    {
        var en = Loc.Load("en");
        var missing = new List<string>();
        foreach (var file in new[] { "MainWindow.xaml", "MiniWidget.xaml", "AdvancedCleanWindow.xaml", "DuplicatesWindow.xaml", "UpdatesWindow.xaml", "DriversWindow.xaml", "ContextMenuWindow.xaml", "SnoozeWindow.xaml", "RegistryCheckWindow.xaml", "SecureEraseWindow.xaml", "RecoveryWindow.xaml", "DefragWindow.xaml", "NtfsRecoveryWindow.xaml", "WindowsToolsWindow.xaml" })
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

    /// <summary>
    /// U1: além do Loc.T, os textos que o app traduz por dentro também precisam estar no dicionário: as duas formas do
    /// Pt.N, os textos fixos do aviso flutuante (ShowSnack) e da simulação, e os dois lados de Loc.T(cond ? "a" : "b").
    /// </summary>
    [Fact]
    public void Plurais_avisos_e_simulacoes_tem_traducao()
    {
        var en = Loc.Load("en");
        const string Lit = @"""((?:[^""\\]|\\.)*)""";
        var keys = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains("Tests")))
        {
            string code = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(code, @"Pt\.N\([^,]+,\s*" + Lit + @"\s*,\s*" + Lit)) keys.AddRange([m.Groups[1].Value, m.Groups[2].Value]);
            foreach (Match m in Regex.Matches(code, @"Loc\.[TF]\([^;""]*?\?\s*" + Lit + @"\s*:\s*" + Lit)) keys.AddRange([m.Groups[1].Value, m.Groups[2].Value]);
            // Avisos, simulações, notificações, registro de Ações e diálogos recebem texto fixo e traduzem por dentro (M13).
            // Só vale texto de gente: com espaço ou começando em maiúscula (ids como "admin-restart" ou "startup" ficam de fora).
            foreach (Match call in Regex.Matches(code, @"(?:ShowSnack|Simulate|Notify|Alert|ActionLog\.Add|Dialogs\.(?:Confirm|ConfirmOnce|Info|ShowText))\(([^;]*)\);"))
                foreach (Match m in Regex.Matches(Regex.Replace(call.Groups[1].Value, @"\$""(?:[^""\\{]|\\.|\{[^}]*\})*""", "\"\""), @"(?<![$\w]|[=!]= )" + Lit)) // sem as interpoladas, que desalinham as aspas
                    if (Regex.IsMatch(m.Groups[1].Value, "[A-Za-zÀ-ú]{3}") && (m.Groups[1].Value.Contains(' ') || char.IsUpper(m.Groups[1].Value[0]))) keys.Add(m.Groups[1].Value);
        }
        var missing = keys.Select(Regex.Unescape).Where(k => !en.ContainsKey(k)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Sem tradução:\n" + string.Join("\n", missing));
    }
}
