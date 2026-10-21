using System.Globalization;
using System.Text;

namespace NotebookDoctor.Services;

public enum PdfStyle { Title, Subtitle, Heading, Text, Small }

/// <summary>
/// B20: PDF de texto, sem bibliotecas externas: fontes padrão do leitor (Helvetica), página A4, quebra de linha
/// e de página automáticas. Acentos do português saem certos (codificação WinAnsi); símbolos fora dela viram "?".
/// </summary>
public static class SimplePdf
{
    private const double PageW = 595, PageH = 842, Margin = 50;

    private static (double Size, bool Bold, double Before) Font(PdfStyle s) => s switch
    {
        PdfStyle.Title => (22, true, 0),
        PdfStyle.Subtitle => (10, false, 4),
        PdfStyle.Heading => (14, true, 16),
        PdfStyle.Small => (9, false, 2),
        _ => (11, false, 2),
    };

    public static byte[] Build(IEnumerable<(PdfStyle Style, string Text)> lines, string title)
    {
        var pages = Layout(lines);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // 2: árvore de páginas, preenchida depois de saber os números das páginas
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            $"<< /Title ({Escape(title)}) /Producer (Notebook Doctor) >>",
        };
        var kids = new List<int>();
        foreach (var content in pages)
        {
            objects.Add($"<< /Length {Encode(content).Length} >>\nstream\n{content}\nendstream");
            int contentId = objects.Count;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW} {PageH}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            kids.Add(objects.Count);
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>";

        using var ms = new MemoryStream();
        void Write(string s) { var b = Encode(s); ms.Write(b, 0, b.Length); }
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>Distribui as linhas em páginas e devolve o fluxo de conteúdo de cada uma.</summary>
    private static List<string> Layout(IEnumerable<(PdfStyle Style, string Text)> lines)
    {
        var pages = new List<string>();
        var sb = new StringBuilder();
        double y = PageH - Margin;
        void NewPage() { if (sb.Length > 0) pages.Add(sb.ToString()); sb.Clear(); y = PageH - Margin; }
        foreach (var (style, text) in lines)
        {
            var (size, bold, before) = Font(style);
            double lead = size * 1.35;
            foreach (var part in Wrap(text, size))
            {
                y -= before + lead;
                before = 0;
                if (y < Margin) { NewPage(); y -= lead; }
                sb.Append(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size} Tf {Margin} {y:0.##} Td ({Escape(part)}) Tj ET\n");
            }
        }
        NewPage();
        if (pages.Count == 0) pages.Add("");
        return pages;
    }

    /// <summary>Quebra pela largura útil. A largura média da Helvetica é ~0,52 do tamanho: estimativa segura para texto comum.</summary>
    public static List<string> Wrap(string text, double size)
    {
        int max = Math.Max(10, (int)((PageW - 2 * Margin) / (size * 0.52)));
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > max) { result.Add(line.ToString()); line.Clear(); }
                for (string w = word; ; )
                {
                    if (w.Length <= max) { line.Append(line.Length > 0 ? " " : "").Append(w); break; }
                    if (line.Length > 0) { result.Add(line.ToString()); line.Clear(); }
                    result.Add(w[..max]); // palavra maior que a linha (ex.: caminho de arquivo): corta
                    w = w[max..];
                }
            }
            result.Add(line.ToString());
        }
        return result;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    // WinAnsi = Latin-1 nas faixas usadas pelo português, mais a pontuação tipográfica da faixa 0x80–0x9F
    private static readonly Dictionary<char, byte> WinAnsiExtra = new()
    {
        ['€'] = 0x80, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
    };

    public static byte[] Encode(string s)
    {
        var bytes = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bytes[i] = c < 0x80 || (c >= 0xA0 && c <= 0xFF) ? (byte)c : WinAnsiExtra.TryGetValue(c, out byte b) ? b : (byte)'?';
        }
        return bytes;
    }
}
