using System.IO;
using NotebookDoctor.Services;
using Drawing = System.Drawing;

namespace NotebookDoctor;

public static partial class Smoke
{
    /// <summary>Esquemas de Alto Contraste conferidos no ícone: o preto (fundo preto, texto branco) e o branco.</summary>
    private static readonly (string Name, Drawing.Color Window, Drawing.Color Text)[] TrayContrastSchemes =
    [
        ("preto", Drawing.Color.Black, Drawing.Color.White),
        ("branco", Drawing.Color.White, Drawing.Color.Black),
    ];

    /// <summary>Os desenhos do ícone: o logo com a bolinha e o número da CPU com um e com dois algarismos.</summary>
    private static readonly string?[] TrayLabels = [null, "7", "42"];

    private const int TrayZoom = 4, TrayCell = 32 * TrayZoom + 16;

    /// <summary>
    /// A CI não fotografa a bandeja e roda sem Alto Contraste. Aqui o ícone é desenhado em cada estado (bom, atenção, problema) e em cada
    /// forma (logo, número), no tema normal e nos esquemas de Alto Contraste preto e branco, numa folha <c>bandeja.png</c>: uma linha por
    /// esquema (normal, preto, branco), três grupos de colunas por estado (logo, 7, 42), cada ícone ampliado 4 × sem suavizar e, abaixo,
    /// em tamanho real. Em Alto Contraste, pixel com cor (fora da linha entre fundo e texto) vira falha: o estado tem de ir pela forma.
    /// </summary>
    private static void TraySheet(string folder)
    {
        var bands = new[] { TrayBand.Ok, TrayBand.Amber, TrayBand.Red };
        var rows = new List<(string Name, bool High, Drawing.Color Window, Drawing.Color Text)> { ("normal", false, Drawing.Color.White, Drawing.Color.Black) };
        rows.AddRange(TrayContrastSchemes.Select(s => ($"alto-contraste-{s.Name}", true, s.Window, s.Text)));
        int cols = bands.Length * TrayLabels.Length;
        using var sheet = new Drawing.Bitmap(cols * TrayCell, rows.Count * (TrayCell + 40));
        using var g = Drawing.Graphics.FromImage(sheet);
        g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.Half;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            int top = r * (TrayCell + 40);
            using (var back = new Drawing.SolidBrush(row.High ? row.Window : Drawing.Color.FromArgb(0xf3, 0xf3, 0xf3)))
                g.FillRectangle(back, 0, top, sheet.Width, TrayCell + 40);
            int c = 0;
            foreach (var band in bands)
                foreach (var label in TrayLabels)
                {
                    using var icon = TrayIcon.Render(band, label, row.High, row.Window, row.Text);
                    int left = c++ * TrayCell + 8;
                    g.DrawImage(icon, new Drawing.Rectangle(left, top + 8, 32 * TrayZoom, 32 * TrayZoom));
                    g.DrawImageUnscaled(icon, left + (32 * TrayZoom - 32) / 2, top + TrayCell);
                    if (row.High && ColoredPixels(icon, row.Window, row.Text) is var n and > 0)
                        Errors.Add($"Bandeja em Alto Contraste {row.Name}: ícone {band}/{label ?? "logo"} com {n} pixels coloridos (o estado deve ir pela forma)");
                }
            Log.AppendLine($"ok    bandeja linha {r + 1}: {row.Name} (colunas: bom, atenção, problema; em cada uma logo, 7, 42)");
        }
        sheet.Save(Path.Combine(folder, "bandeja.png"), Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Pixels visíveis cuja cor não fica entre o fundo e o texto (a suavização só mistura os dois).</summary>
    private static int ColoredPixels(Drawing.Bitmap bmp, Drawing.Color window, Drawing.Color text)
    {
        int count = 0;
        double dx = text.R - window.R, dy = text.G - window.G, dz = text.B - window.B;
        double len2 = dx * dx + dy * dy + dz * dz;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.A < 64) continue;
                // Distância do ponto à reta fundo→texto no espaço RGB; tolerância para o arredondamento da suavização
                double t = len2 == 0 ? 0 : Math.Clamp(((p.R - window.R) * dx + (p.G - window.G) * dy + (p.B - window.B) * dz) / len2, 0, 1);
                double ex = p.R - (window.R + t * dx), ey = p.G - (window.G + t * dy), ez = p.B - (window.B + t * dz);
                if (ex * ex + ey * ey + ez * ez > 24 * 24) count++;
            }
        return count;
    }
}
