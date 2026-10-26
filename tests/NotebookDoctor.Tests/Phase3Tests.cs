using System.Text.Json.Nodes;

namespace NotebookDoctor.Tests;

public class ColorMathTests
{
    [Fact]
    public void Contraste_de_preto_e_branco_e_21() =>
        Assert.Equal(21, ColorMath.Contrast(ColorMath.Black, ColorMath.White), 1);

    [Fact]
    public void Le_a_cor_do_registro_do_Windows() =>
        Assert.Equal(new Rgb(0x00, 0x78, 0xD4), Rgb.FromAbgr(0xFFD47800));

    [Theory]
    [InlineData(0x00, 0x78, 0xD4, 0x1F, 0x1F, 0x1F)] // azul do Windows sobre fundo escuro
    [InlineData(0xFF, 0xB9, 0x00, 0xFF, 0xFF, 0xFF)] // amarelo sobre fundo branco
    [InlineData(0x10, 0x10, 0x10, 0x12, 0x12, 0x12)] // quase preto sobre fundo escuro
    public void Cor_ajustada_sempre_e_legivel(byte r, byte g, byte b, byte br, byte bg, byte bb)
    {
        var bgColor = new Rgb(br, bg, bb);
        Assert.True(ColorMath.Contrast(ColorMath.Readable(new Rgb(r, g, b), bgColor), bgColor) >= 4.5);
    }

    [Fact]
    public void Cor_que_ja_contrasta_fica_igual()
    {
        var navy = new Rgb(0x00, 0x18, 0x8F);
        Assert.Equal(navy, ColorMath.Readable(navy, ColorMath.White));
    }

    [Theory]
    [InlineData(0x00, 0x18, 0x8F, 255)] // azul escuro: texto branco
    [InlineData(0xFF, 0xB9, 0x00, 0)]   // amarelo: texto preto
    public void Texto_sobre_a_cor(byte r, byte g, byte b, byte expected) =>
        Assert.Equal(expected, ColorMath.OnColor(new Rgb(r, g, b)).R);
}

public class SettingsTransferTests
{
    private const string Current = """{"ThemeMode":"dark","CpuAlertPct":80,"MiniLeft":120.5,"LastTab":3,"FirstRunDone":true,"AlertExceptions":["obs64"]}""";

    [Fact]
    public void Exporta_sem_dados_da_maquina()
    {
        var obj = JsonNode.Parse(SettingsTransfer.Export(Current, "1.2.0"))!.AsObject();
        Assert.Equal(SettingsTransfer.Marker, (string?)obj["_tipo"]);
        Assert.Equal("dark", (string?)obj["ThemeMode"]);
        Assert.Equal("obs64", (string?)obj["AlertExceptions"]![0]);
        Assert.False(obj.ContainsKey("MiniLeft"));
        Assert.False(obj.ContainsKey("LastTab"));
        Assert.False(obj.ContainsKey("FirstRunDone"));
    }

    [Fact]
    public void Importa_por_cima_mantendo_os_dados_da_maquina()
    {
        string imported = """{"_tipo":"NotebookDoctorSettings","_versao":"1.2.0","ThemeMode":"light","CpuAlertPct":70,"MiniLeft":999}""";
        var merged = JsonNode.Parse(SettingsTransfer.Merge(Current, imported))!.AsObject();
        Assert.Equal("light", (string?)merged["ThemeMode"]);
        Assert.Equal(70, (int?)merged["CpuAlertPct"]);
        Assert.Equal(120.5, (double?)merged["MiniLeft"]);  // posição do widget é desta máquina
        Assert.Equal("obs64", (string?)merged["AlertExceptions"]![0]); // o que não veio no arquivo fica
        Assert.False(merged.ContainsKey("_tipo"));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"ThemeMode":"light"}""")]
    [InlineData("""{"_tipo":"OutroApp"}""")]
    public void Recusa_arquivo_que_nao_e_do_app(string imported) =>
        Assert.Throws<FormatException>(() => SettingsTransfer.Merge(Current, imported));

    [Fact]
    public void Ida_e_volta_preserva_as_preferencias()
    {
        var back = JsonNode.Parse(SettingsTransfer.Merge("{}", SettingsTransfer.Export(Current, "1.2.0")))!.AsObject();
        Assert.Equal("dark", (string?)back["ThemeMode"]);
        Assert.Equal(80, (int?)back["CpuAlertPct"]);
    }

    [Fact]
    public void Redefinir_volta_ao_padrao_mas_mantem_dados_da_maquina()
    {
        var reset = JsonNode.Parse(SettingsTransfer.Reset(Current, """{"ThemeMode":"system","CpuAlertPct":80,"FirstRunDone":false,"AlertExceptions":[]}"""))!.AsObject();
        Assert.Equal("system", (string?)reset["ThemeMode"]);
        Assert.Empty(reset["AlertExceptions"]!.AsArray());
        Assert.True((bool?)reset["FirstRunDone"]); // não mostra as boas-vindas de novo
        Assert.Equal(120.5, (double?)reset["MiniLeft"]);
    }
}

public class SimplePdfTests
{
    private static string Ascii(byte[] b) => System.Text.Encoding.Latin1.GetString(b);

    [Fact]
    public void Gera_pdf_valido_com_xref_correto()
    {
        var pdf = SimplePdf.Build([(PdfStyle.Title, "Relatório"), (PdfStyle.Text, "Memória (RAM) em 90%")], "Teste");
        string text = Ascii(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        // startxref aponta para a tabela, e cada entrada aponta para o início do objeto
        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[startxref..]);
        var entries = text[startxref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
        Assert.NotEmpty(entries);
        for (int i = 0; i < entries.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[int.Parse(entries[i][..10])..]);
        Assert.Contains("\\(RAM\\)", text); // parênteses escapados
    }

    [Fact]
    public void Acentos_saem_em_WinAnsi()
    {
        var b = SimplePdf.Encode("ção – •");
        Assert.Equal(new byte[] { 0xE7, 0xE3, 0x6F, 0x20, 0x96, 0x20, 0x95 }, b);
        Assert.Equal((byte)'?', SimplePdf.Encode("▲")[0]);
    }

    [Fact]
    public void Muitas_linhas_viram_varias_paginas()
    {
        var lines = Enumerable.Range(0, 200).Select(i => (PdfStyle.Text, $"Linha {i}"));
        string text = Ascii(SimplePdf.Build(lines, "Teste"));
        var count = System.Text.RegularExpressions.Regex.Match(text, @"/Count (\d+)").Groups[1].Value;
        Assert.True(int.Parse(count) >= 4);
    }

    [Fact]
    public void Quebra_linhas_longas_e_palavras_enormes()
    {
        var parts = SimplePdf.Wrap(string.Join(" ", Enumerable.Repeat("palavra", 60)) + " " + new string('x', 300), 11);
        Assert.True(parts.Count > 3);
        Assert.All(parts, p => Assert.True(p.Length <= 90));
    }
}

public class ReportContentTests
{
    [Fact]
    public void Resumo_tem_nota_metricas_e_problemas()
    {
        var s = Make.Snap(cpu: 75, ram: 88, groups: [new ProcGroup("chrome", 10, 40, 2000)]);
        var findings = Diagnosis.Evaluate(Make.Minute(s), Make.Health(), uptime: Make.FreshBoot);
        string text = ReportContent.Summary(s, new HealthScoreResult(62, "Regular"), new BottleneckResult("Processador", ""), findings);
        Assert.Contains("Nota de saúde: 62 (Regular)", text);
        Assert.Contains("CPU 75%", text);
        Assert.Contains("[PROBLEMA] Memória RAM quase cheia", text);
        Assert.Contains("chrome", text);
    }

    /// <summary>M13: o relatório (PDF, HTML) e o resumo para colar saem no idioma do app, inclusive o texto dos achados.</summary>
    [Theory]
    [InlineData("en", "Diagnosis", "Report generated on", "Health score: 62 (", "Nota de saúde")]
    [InlineData("es", "Diagnóstico", "Informe generado el", "Nota de salud: 62 (", "Nota de saúde")]
    public void Relatorio_e_resumo_saem_no_idioma_do_app(string lang, string heading, string generated, string score, string notPortuguese)
    {
        try
        {
            Loc.Use(lang);
            var s = Make.Snap(cpu: 75, ram: 88, groups: [new ProcGroup("chrome", 10, 40, 2000)]);
            var findings = Diagnosis.Evaluate(Make.Minute(s), Make.Health(), uptime: Make.FreshBoot);
            var lines = ReportContent.Lines(s, findings, [], "PC", "1.2.0", "support");
            Assert.Contains(lines, l => l.Style == PdfStyle.Heading && l.Text == heading);
            Assert.Contains(lines, l => l.Text.StartsWith(generated));
            // O achado de RAM aparece com a tag e o título do idioma, nunca com a chave em português
            Assert.Contains(lines, l => l.Text.StartsWith("[") && !l.Text.Contains("Memória RAM quase cheia") && l.Text.Contains(Loc.T("Memória RAM quase cheia")));
            Assert.DoesNotContain(lines, l => l.Text.Contains("[ATENÇÃO]")); // "PROBLEMA" é a mesma palavra em espanhol
            if (lang == "en") Assert.DoesNotContain(lines, l => l.Text.Contains("[PROBLEMA]"));
            string summary = ReportContent.Summary(s, new HealthScoreResult(62, "Regular"), new BottleneckResult("Processador", ""), findings);
            Assert.Contains(score, summary);
            Assert.DoesNotContain(notPortuguese, summary);
        }
        finally { Loc.Use("pt"); }
    }

    [Fact]
    public void Relatorio_tem_secoes_principais()
    {
        var s = Make.Snap();
        var lines = ReportContent.Lines(s, [new Finding("OK", "Tudo certo", "Nada", "")], [], "PC", "1.2.0", "suporte");
        Assert.Contains(lines, l => l.Style == PdfStyle.Heading && l.Text == "Diagnóstico");
        Assert.Contains(lines, l => l.Text == "[OK] Tudo certo");
    }
}
