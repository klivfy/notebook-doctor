using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// <c>NotebookDoctor.exe --screenshots=PASTA [--high-contrast]</c>: abre o app de verdade, percorre as nove abas, salva um PNG
/// de cada, audita acessibilidade (ver <see cref="A11yAudit"/>) e encerra. O CI usa isso para publicar as telas e o relatório
/// em cada pull request; quem revisa design vê o resultado sem precisar de um Windows. Combine com NOTEBOOKDOCTOR_DATA_DIR
/// para não tocar nos dados do usuário.
/// <para>Sem <c>--high-contrast</c>: variantes "normal" (1160 px), "texto200" (texto a 200%) e "estreita" (720 px), em tema claro e escuro.
/// Com <c>--high-contrast</c>: aplica as paletas dos esquemas clássicos de Alto Contraste (preto e branco) ao tema do app e fotografa.</para>
/// </summary>
internal static class ScreenshotMode
{
    internal static readonly string[] TabNames =
        ["painel", "processos", "limpeza", "espaco", "inicializacao", "historico", "acoes", "ajustes", "configuracoes"];

    private sealed record Variant(string Name, double Width, double? TextScale, string[] Themes);

    private static readonly Variant[] Variants =
    [
        new("normal", 1160, null, ["light", "dark"]),
        new("texto200", 1160, 2.0, ["light", "dark"]),
        new("estreita", 720, null, ["light", "dark"]),
    ];

    /// <summary>Retorna 0 se todas as telas foram salvas; 1 caso contrário. Achados de acessibilidade não reprovam: ficam no relatório.</summary>
    public static async Task<int> RunAsync(MainWindow win, string dir, bool highContrast)
    {
        Directory.CreateDirectory(dir);
        win.WindowState = WindowState.Normal;
        win.Height = 740;
        win.Width = 1160;
        win.UpdateLayout();

        // Primeiros números do monitor e a leitura lenta de saúde (WMI/PowerShell); se demorar demais, segue sem eles
        var limit = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < limit && (App.Current.Latest is null || ReferenceEquals(App.Current.Health, HealthInfo.Empty)))
            await Task.Delay(250);
        await Task.Delay(2000);

        var log = new List<string>();
        var audit = new List<string>();
        int saved = 0, expected = 0;

        if (highContrast)
        {
            var schemes = new[]
            {
                ("hc-preto", (Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255), Color.FromRgb(0x1A, 0xEB, 0xFF), Color.FromRgb(0, 0, 0))),
                ("hc-branco", (Color.FromRgb(255, 255, 255), Color.FromRgb(0, 0, 0), Color.FromRgb(0x37, 0x00, 0x6E), Color.FromRgb(255, 255, 255))),
            };
            foreach (var (label, palette) in schemes)
            {
                ThemeManager.SimulatedHighContrast = palette;
                ThemeManager.Apply("system");
                log.Add($"{label}: Alto Contraste simulado (janela {palette.Item1}, texto {palette.Item2}, destaque {palette.Item3})");
                var v = new Variant(label, 1160, null, ["system"]);
                (int s2, int e2) = await CaptureAsync(win, dir, v, "system", log, audit);
                saved += s2; expected += e2;
            }
            ThemeManager.SimulatedHighContrast = null;
            ThemeManager.Apply("light");
        }
        else
        {
            foreach (var v in Variants)
            {
                win.Width = v.Width;
                TextScale.Override = v.TextScale;
                TextScale.Apply();
                foreach (var theme in v.Themes)
                {
                    ThemeManager.Apply(theme);
                    (int s, int e) = await CaptureAsync(win, dir, v, theme, log, audit);
                    saved += s; expected += e;
                }
            }
            TextScale.Override = null;
            TextScale.Apply();
        }

        log.Insert(0, $"{saved}/{expected} telas salvas ({(highContrast ? "alto contraste" : "normal, texto 200%, janela estreita")})");
        File.WriteAllLines(Path.Combine(dir, "resumo.txt"), log);
        File.WriteAllLines(Path.Combine(dir, "acessibilidade.txt"), Summarize(audit));
        return saved == expected && expected > 0 ? 0 : 1;
    }

    private static async Task<(int Saved, int Expected)> CaptureAsync(MainWindow win, string dir, Variant v, string theme,
                                                                      List<string> log, List<string> audit)
    {
        int saved = 0;
        await Task.Delay(800); // o layout reage ao novo tamanho de janela, de texto ou de tema
        for (int i = 0; i < TabNames.Length; i++)
        {
            win.SelectTab(i);
            await Task.Delay(v.Name == "normal" ? 1500 : 1000); // as abas carregam os dados de forma assíncrona
            string file = $"{v.Name}-{i:00}-{TabNames[i]}-{theme}.png";
            try
            {
                Save(win, Path.Combine(dir, file));
                saved++;
                log.Add($"ok   {file} ({win.ActualWidth:0}×{win.ActualHeight:0})");
            }
            catch (Exception ex) { log.Add($"ERRO {file}: {ex.Message}"); }
            try
            {
                foreach (var issue in A11yAudit.Run((FrameworkElement)win.Content, win.Background))
                    audit.Add($"[{v.Name}/{theme}/{TabNames[i]}] {issue}");
            }
            catch (Exception ex) { audit.Add($"[{v.Name}/{theme}/{TabNames[i]}] AUDITORIA FALHOU: {ex.Message}"); }
        }
        return (saved, TabNames.Length);
    }

    /// <summary>Contagem por tipo no topo; abaixo, cada achado uma vez só, com as variantes e abas em que aparece.</summary>
    private static IEnumerable<string> Summarize(List<string> audit)
    {
        // "[variante/tema/aba] TIPO: descrição"
        var rows = audit.Select(a =>
        {
            int close = a.IndexOf("] ", StringComparison.Ordinal);
            var where = a[1..close].Split('/');
            return (Variant: where[0], Theme: where[1], Tab: where[2], What: a[(close + 2)..]);
        }).ToList();
        yield return $"{rows.Count} ocorrências, {rows.Select(r => r.What).Distinct().Count()} achados distintos";
        foreach (var g in rows.GroupBy(r => r.What.Split(':')[0]).OrderByDescending(g => g.Select(r => r.What).Distinct().Count()))
            yield return $"  {g.Key}: {g.Select(r => r.What).Distinct().Count()} distintos";
        yield return "";
        foreach (var g in rows.GroupBy(r => r.What).OrderBy(g => g.Key.Split(':')[0]).ThenBy(g => g.Key))
        {
            string variants = string.Join(",", g.Select(r => r.Variant).Distinct());
            string tabs = string.Join(",", g.Select(r => r.Tab).Distinct());
            string themes = string.Join(",", g.Select(r => r.Theme).Distinct());
            yield return $"{g.Key}   <= {variants} | {themes} | {tabs}";
        }
    }

    /// <summary>Renderiza o conteúdo da janela (sem a moldura do Windows) sobre o fundo do tema.</summary>
    private static void Save(MainWindow win, string path)
    {
        win.UpdateLayout();
        var el = (FrameworkElement)win.Content;
        var dpi = VisualTreeHelper.GetDpi(el);
        double w = el.ActualWidth, h = el.ActualHeight;
        if (w < 1 || h < 1) throw new InvalidOperationException("janela sem tamanho");

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, w, h);
            ctx.DrawRectangle(win.Background ?? Brushes.White, null, rect);
            ctx.DrawRectangle(new VisualBrush(el), null, rect);
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                                         dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
