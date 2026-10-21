namespace NotebookDoctor.Services;

/// <summary>
/// M03: overlay de desempenho por cima de jogos e apps em tela cheia. Só mostra; nunca recebe clique nem foco.
/// Fica fora do modo de apresentação (o PowerPoint em tela cheia não deve ganhar números no canto).
/// </summary>
public static class Overlay
{
    public const string Off = "off", Fullscreen = "fullscreen", Always = "always";
    public static readonly string[] Corners = ["top-left", "top-right", "bottom-left", "bottom-right"];

    /// <summary>
    /// <paramref name="notificationState"/> é o QUERY_USER_NOTIFICATION_STATE do Windows:
    /// 2 = app em tela cheia, 3 = jogo Direct3D em tela cheia, 4 = modo de apresentação.
    /// </summary>
    public static bool ShouldShow(string mode, int notificationState) => mode switch
    {
        Always => true,
        Fullscreen => notificationState is 2 or 3,
        _ => false,
    };

    /// <summary>Uma linha curta, legível de relance: "CPU 45% · RAM 72% · 64 °C".</summary>
    public static string Text(Snapshot s) =>
        $"CPU {s.Cpu:0}% · RAM {s.RamPct:0}%" + (s.TempC > 0 ? $" · {s.TempC:0} °C" : "") + (s.DiskBusy >= 50 ? $" · disco {s.DiskBusy:0}%" : "");

    /// <summary>Posição do canto escolhido dentro do monitor (em pixels), com margem.</summary>
    public static (double X, double Y) Place(string corner, Box monitor, double width, double height, double margin)
    {
        bool right = corner.EndsWith("right", StringComparison.Ordinal), bottom = corner.StartsWith("bottom", StringComparison.Ordinal);
        return (right ? monitor.Right - width - margin : monitor.X + margin,
                bottom ? monitor.Bottom - height - margin : monitor.Y + margin);
    }
}
