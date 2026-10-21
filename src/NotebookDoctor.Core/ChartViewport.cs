namespace NotebookDoctor.Services;

/// <summary>
/// M04: zoom e arraste nos gráficos. Guarda qual trecho da série está à vista (início e quantidade de pontos).
/// Sem zoom, mostra tudo e acompanha os pontos novos; com zoom, o trecho fica parado até a pessoa voltar (Reset).
/// </summary>
public sealed class ChartViewport
{
    public const int MinPoints = 10;

    public int Total { get; private set; }
    public int Start { get; private set; }
    public int Count { get; private set; }
    public bool Zoomed => Total > 0 && Count < Total;

    public void SetTotal(int total)
    {
        bool follow = !Zoomed;
        Total = Math.Max(0, total);
        if (follow) { Start = 0; Count = Total; return; }
        Count = Math.Min(Count, Total);
        Start = Math.Clamp(Start, 0, Total - Count);
    }

    /// <summary>
    /// Aproxima (factor &lt; 1) ou afasta (factor &gt; 1) mantendo o ponto sob o cursor no mesmo lugar.
    /// <paramref name="anchor"/> é a posição do cursor de 0 (esquerda) a 1 (direita).
    /// </summary>
    public void Zoom(double factor, double anchor)
    {
        if (Total < MinPoints || factor <= 0) return;
        anchor = Math.Clamp(anchor, 0, 1);
        int count = Math.Clamp((int)Math.Round(Count * factor), Math.Min(MinPoints, Total), Total);
        if (count == Count) count = Math.Clamp(Count + (factor < 1 ? -1 : 1), Math.Min(MinPoints, Total), Total);
        double center = Start + anchor * (Count - 1);
        Start = Math.Clamp((int)Math.Round(center - anchor * (count - 1)), 0, Total - count);
        Count = count;
    }

    /// <summary>Move o trecho à vista; positivo vai para a direita (pontos mais novos).</summary>
    public void Pan(int points) => Start = Math.Clamp(Start + points, 0, Math.Max(0, Total - Count));

    public void Reset() { Start = 0; Count = Total; }

    /// <summary>Converte a posição do cursor (0..1) no índice da série completa.</summary>
    public int IndexAt(double fraction) =>
        Count == 0 ? -1 : Start + Math.Clamp((int)Math.Round(Math.Clamp(fraction, 0, 1) * (Count - 1)), 0, Count - 1);
}
