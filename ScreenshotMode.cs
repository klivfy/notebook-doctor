using System.IO;
using System.Runtime.InteropServices;
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
/// Com <c>--high-contrast</c>: liga de verdade o Alto Contraste do Windows (preto e branco) durante a execução e fotografa.</para>
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
            foreach (var (scheme, label) in new[] { ("High Contrast Black", "hc-preto"), ("High Contrast White", "hc-branco") })
            {
                bool on = await SetHighContrastAsync(true, scheme);
                log.Add($"{label}: Alto Contraste {(on ? "ligado" : "NÃO foi possível ligar")} ({scheme}); SystemParameters.HighContrast={SystemParameters.HighContrast}");
                if (!on) continue;
                ThemeManager.Apply("system");
                var v = new Variant(label, 1160, null, ["system"]);
                (int s, int e) = await CaptureAsync(win, dir, v, "system", log, audit);
                saved += s; expected += e;
            }
            await SetHighContrastAsync(false, "");
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

    /// <summary>Resumo por tipo de achado no topo, seguido da lista completa.</summary>
    private static IEnumerable<string> Summarize(List<string> audit)
    {
        yield return $"{audit.Count} achados";
        foreach (var g in audit.GroupBy(a => a[(a.IndexOf("] ") + 2)..].Split(':')[0]).OrderByDescending(g => g.Count()))
            yield return $"  {g.Key}: {g.Count()}";
        yield return "";
        foreach (var a in audit) yield return a;
    }

    // ---------- Alto Contraste de verdade (só no CI; a VM é descartada no fim) ----------
    private const uint SPI_SETHIGHCONTRAST = 0x0043, SPIF_SENDCHANGE = 0x0002, HCF_HIGHCONTRASTON = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HIGHCONTRAST
    {
        public uint cbSize, dwFlags;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszDefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref HIGHCONTRAST value, uint flags);

    private static async Task<bool> SetHighContrastAsync(bool on, string scheme)
    {
        var hc = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>(), dwFlags = on ? HCF_HIGHCONTRASTON : 0, lpszDefaultScheme = scheme };
        if (!SystemParametersInfo(SPI_SETHIGHCONTRAST, hc.cbSize, ref hc, SPIF_SENDCHANGE)) return false;
        for (int i = 0; i < 40; i++) // o Windows avisa por mensagem; espera o WPF perceber
        {
            if (SystemParameters.HighContrast == on) { await Task.Delay(1500); return true; }
            await Task.Delay(250);
        }
        return !on;
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
