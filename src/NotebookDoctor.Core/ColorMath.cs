namespace NotebookDoctor.Services;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>DWORD do registro do Windows no formato 0xAABBGGRR (ex.: DWM\AccentColor).</summary>
    public static Rgb FromAbgr(uint abgr) => new((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>M09: contas de contraste (WCAG 2) para usar a cor de destaque do Windows sem perder legibilidade.</summary>
public static class ColorMath
{
    public static readonly Rgb White = new(255, 255, 255), Black = new(0, 0, 0);

    public static double Luminance(Rgb c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Mistura <paramref name="a"/> com <paramref name="b"/>; t = 0 é só a, t = 1 é só b.</summary>
    public static Rgb Mix(Rgb a, Rgb b, double t)
    {
        byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * Math.Clamp(t, 0, 1));
        return new(M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
    }

    /// <summary>
    /// Clareia (fundo escuro) ou escurece (fundo claro) a cor até ter o contraste mínimo com o fundo.
    /// Mantém a cor original quando ela já basta.
    /// </summary>
    public static Rgb Readable(Rgb color, Rgb background, double minContrast = 4.5)
    {
        var toward = Luminance(background) < 0.5 ? White : Black;
        for (int i = 0; i <= 20; i++)
        {
            var c = Mix(color, toward, i / 20.0);
            if (Contrast(c, background) >= minContrast) return c;
        }
        return toward;
    }

    /// <summary>Texto sobre a cor: preto ou branco, o que contrastar mais.</summary>
    public static Rgb OnColor(Rgb background) => Contrast(White, background) >= Contrast(Black, background) ? White : Black;
}
