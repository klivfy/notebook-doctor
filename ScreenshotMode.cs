using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// <c>NotebookDoctor.exe --screenshots=PASTA</c>: abre o app de verdade, percorre as nove abas nos temas claro e escuro,
/// salva um PNG de cada e encerra. O CI usa isso para publicar as telas em cada pull request; quem revisa design
/// vê o resultado sem precisar de um Windows. Combine com NOTEBOOKDOCTOR_DATA_DIR para não tocar nos dados do usuário.
/// </summary>
internal static class ScreenshotMode
{
    internal static readonly string[] TabNames =
        ["painel", "processos", "limpeza", "espaco", "inicializacao", "historico", "acoes", "ajustes", "configuracoes"];
    internal static readonly string[] Themes = ["light", "dark"];

    /// <summary>Retorna 0 se todas as telas foram salvas; 1 caso contrário.</summary>
    public static async Task<int> RunAsync(MainWindow win, string dir)
    {
        Directory.CreateDirectory(dir);
        win.WindowState = WindowState.Normal;
        win.Width = 1160; win.Height = 740;
        win.UpdateLayout();

        // Primeiros números do monitor e a leitura lenta de saúde (WMI/PowerShell); se demorar demais, segue sem eles
        var limit = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < limit && (App.Current.Latest is null || ReferenceEquals(App.Current.Health, HealthInfo.Empty)))
            await Task.Delay(250);
        await Task.Delay(2000);

        var log = new List<string>();
        int saved = 0, expected = Themes.Length * TabNames.Length;
        foreach (var theme in Themes)
        {
            ThemeManager.Apply(theme);
            for (int i = 0; i < TabNames.Length; i++)
            {
                win.SelectTab(i);
                await Task.Delay(1500); // as abas carregam os dados de forma assíncrona
                string file = $"{i:00}-{TabNames[i]}-{theme}.png";
                try { Save(win, Path.Combine(dir, file)); saved++; log.Add($"ok   {file}"); }
                catch (Exception ex) { log.Add($"ERRO {file}: {ex.Message}"); }
            }
        }

        log.Insert(0, $"{saved}/{expected} telas salvas ({win.ActualWidth:0}x{win.ActualHeight:0} px, texto {TextScale.Factor:0.##}x)");
        File.WriteAllLines(Path.Combine(dir, "resumo.txt"), log);
        return saved == expected ? 0 : 1;
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
