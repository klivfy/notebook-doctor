namespace NotebookDoctor.Services;

/// <param name="P">M05: CPU % e memória MB dos programas mais pesados nesta amostra ({"chrome":[12.5,1830]}).</param>
public sealed record HistSample(DateTime T, double Cpu, double Ram, double Disk, double FreePct, string[] Top, int Score = -1,
                                Dictionary<string, double[]>? P = null);
