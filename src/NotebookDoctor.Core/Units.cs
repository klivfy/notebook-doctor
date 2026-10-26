namespace NotebookDoctor.Services;

/// <summary>B16: "windows" = base 1024 com "GB" (como o Explorador), "decimal" = base 1000 (como na caixa do disco), "binary" = base 1024 com "GiB".</summary>
public enum SizeUnit { Windows, Decimal, Binary }

/// <summary>B17: Celsius ou Fahrenheit.</summary>
public enum TempUnit { Celsius, Fahrenheit }

/// <summary>
/// B16/B17: unidades escolhidas em Configurações, usadas em todo texto mostrado. As regras (limites de disco cheio,
/// temperatura alta) continuam em GiB e °C; só a apresentação muda.
/// </summary>
public static class Units
{
    public static SizeUnit Size { get; set; } = SizeUnit.Windows;
    public static TempUnit Temperature { get; set; } = TempUnit.Celsius;

    public static SizeUnit ParseSize(string? v) => v switch { "decimal" => SizeUnit.Decimal, "binary" => SizeUnit.Binary, _ => SizeUnit.Windows };
    public static TempUnit ParseTemp(string? v) => v == "f" ? TempUnit.Fahrenheit : TempUnit.Celsius;

    private static string Prefix(char p) => Size == SizeUnit.Binary ? $"{p}iB" : $"{p}B";

    /// <summary>"512 KB", "120 MB", "1,5 GB" (ou KiB/MiB/GiB, ou base 1000).</summary>
    public static string Bytes(long b)
    {
        double k = Size == SizeUnit.Decimal ? 1000 : 1024;
        return b >= k * k * k ? $"{b / (k * k * k):0.0} {Prefix('G')}"
             : b >= k * k ? $"{b / (k * k):0} {Prefix('M')}"
             : $"{b / k:0} {Prefix('K')}";
    }

    /// <summary>Valor medido em GiB (como o monitor mede RAM e disco) na unidade escolhida: "15,9 GB", "17,1 GB" (decimal), "15,9 GiB".</summary>
    public static string Gb(double gib, string format = "0.0") =>
        (Size == SizeUnit.Decimal ? gib * 1.073741824 : gib).ToString(format) + " " + Prefix('G');

    /// <summary>Par "usado de total", com a unidade uma vez só: "6,2 de 15,9 GB".</summary>
    public static string GbOf(double usedGib, double totalGib)
    {
        double f = Size == SizeUnit.Decimal ? 1.073741824 : 1;
        return Loc.F("{0:0.0} de {1:0.0} {2}", usedGib * f, totalGib * f, Prefix('G'));
    }

    /// <summary>"64 °C" ou "147 °F".</summary>
    public static string Temp(double celsius) =>
        Temperature == TempUnit.Fahrenheit ? $"{celsius * 9 / 5 + 32:0} °F" : $"{celsius:0} °C";
}
