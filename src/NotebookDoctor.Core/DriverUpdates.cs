using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Um driver que o Windows Update oferece (IWindowsDriverUpdate).</summary>
public sealed record OfferedDriver(
    string UpdateId, string Title, string Manufacturer, string Model, string DeviceClass, string HardwareId,
    DateTime? Date, string Version, long SizeBytes, bool Optional, bool EulaAccepted, string EulaText);

/// <summary>Um driver instalado, como o Windows o informa (Win32_PnPSignedDriver).</summary>
public sealed record InstalledDriver(
    string DeviceName, string DeviceId, IReadOnlyList<string> HardwareIds, string Version, DateTime? Date, string InfName, string Provider);

public enum DriverVerdict
{
    /// <summary>A versão oferecida é mais nova que a instalada.</summary>
    Newer,
    /// <summary>A oferecida é igual ou mais antiga: nada a ganhar.</summary>
    NotNewer,
    /// <summary>Não achei o dispositivo instalado que corresponde a esta oferta: não dá para comparar nem guardar o atual.</summary>
    NoDevice,
}

public sealed record DriverOffer(OfferedDriver Offered, InstalledDriver? Current, DriverVerdict Verdict)
{
    /// <summary>Driver de pacote de terceiros (oemNN.inf): dá para exportar uma cópia. Os do próprio Windows ficam no repositório dele.</summary>
    public bool CanBackup => Current is not null && DriverUpdateRules.IsExportable(Current.InfName);
    public bool CanInstall => Verdict == DriverVerdict.Newer;
}

/// <summary>O que a busca achou. <see cref="Error"/> vem preenchido quando o Windows Update não respondeu.</summary>
public sealed record DriverScan(IReadOnlyList<OfferedDriver> Offered, IReadOnlyList<InstalledDriver> Installed, string Error);

/// <summary>Como terminou a volta ao driver anterior.</summary>
public enum RestoreOutcome { Restored, CopyMissing, AddFailed, StillNewer }

/// <summary>Resultado do <c>IUpdateInstaller.Install</c>.</summary>
public sealed record DriverInstallResult(bool Ok, string Stage, int Code, int HResult, bool RebootRequired);

/// <summary>Cópia do driver que estava instalado antes da atualização.</summary>
public sealed class DriverBackup
{
    public string Id { get; set; } = "";
    public DateTime Created { get; set; }
    public string DeviceName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string InfName { get; set; } = "";
    public string Folder { get; set; } = "";
    /// <summary>O que foi instalado por cima (título da oferta), para a pessoa reconhecer.</summary>
    public string Replaced { get; set; } = "";
    public bool Restored { get; set; }
}

/// <summary>
/// Leva 7.2 (C13): decide o que vale recomendar, lê a saída dos scripts e monta os argumentos do <c>pnputil</c>.
/// Lógica pura; a busca e a instalação pelo Windows Update Agent estão em NotebookDoctor.Windows.
/// </summary>
public static class DriverUpdateRules
{
    private static readonly Regex TrailingVersion = new(@"(?<v>\d+(?:\.\d+){1,4})\s*$", RegexOptions.Compiled);
    private static readonly Regex OemInf = new(@"^oem\d{1,6}\.inf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LocalDrivePath = new(@"^[A-Za-z]:[\\/]", RegexOptions.Compiled); // sempre pasta local, nunca rede
    private static readonly Regex VersionText = new(@"^\d+(?:\.\d+){1,4}$", RegexOptions.Compiled);

    /// <summary>Os títulos do Windows Update são "Fabricante - Classe - 31.0.15.5222": a versão é o último pedaço.</summary>
    public static string VersionFromTitle(string title) => TrailingVersion.Match(title ?? "") is { Success: true } m ? m.Groups["v"].Value : "";

    public static bool IsExportable(string? infName) => infName is not null && OemInf.IsMatch(infName.Trim());

    /// <summary>
    /// Mais novo ou não, na ordem que o próprio Windows usa para escolher driver: primeiro a data, depois a versão.
    /// Sem data de um dos lados, vale só a versão; sem nenhuma das duas, não é mais novo (não recomenda no escuro).
    /// </summary>
    public static bool IsNewer(DateTime? offeredDate, string offeredVersion, DateTime? currentDate, string currentVersion)
    {
        if (offeredDate is { } od && currentDate is { } cd && od.Date != cd.Date) return od.Date > cd.Date;
        if (VersionText.IsMatch(offeredVersion.Trim()) && VersionText.IsMatch(currentVersion.Trim()))
            return VersionOrder.IsNewer(offeredVersion, currentVersion);
        return false;
    }

    /// <summary>O dispositivo instalado cujo ID de hardware é o da oferta (comparação sem diferenciar maiúsculas).</summary>
    public static InstalledDriver? Match(OfferedDriver offered, IEnumerable<InstalledDriver> installed)
    {
        if (offered.HardwareId.Length == 0) return null;
        return installed
            .Where(d => d.HardwareIds.Any(h => string.Equals(h.Trim(), offered.HardwareId.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.Date ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    public static DriverOffer Evaluate(OfferedDriver offered, IEnumerable<InstalledDriver> installed)
    {
        var current = Match(offered, installed);
        if (current is null) return new DriverOffer(offered, null, DriverVerdict.NoDevice);
        bool newer = IsNewer(offered.Date, offered.Version, current.Date, current.Version);
        return new DriverOffer(offered, current, newer ? DriverVerdict.Newer : DriverVerdict.NotNewer);
    }

    /// <summary>Recomendados primeiro; depois o resto, por classe e título.</summary>
    public static List<DriverOffer> Order(IEnumerable<OfferedDriver> offers, IReadOnlyList<InstalledDriver> installed) =>
        offers.Select(o => Evaluate(o, installed))
            .OrderBy(o => o.Verdict == DriverVerdict.Newer ? 0 : o.Verdict == DriverVerdict.NotNewer ? 1 : 2)
            .ThenBy(o => o.Offered.DeviceClass, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(o => o.Offered.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static bool IsGuid(string? id) => Guid.TryParse(id, out _);

    // ---------- argumentos do pnputil (sem shell; nomes e caminhos validados) ----------

    public static string[] ExportArgs(string infName, string folder) =>
        IsExportable(infName) && SafeFolder(folder) ? ["/export-driver", infName.Trim(), folder] : throw new ArgumentException("Driver ou pasta inválidos.");

    /// <summary>Instala os .inf da cópia (a pasta que o export-driver gerou).</summary>
    public static string[] AddArgs(string folder) =>
        SafeFolder(folder) ? ["/add-driver", folder.TrimEnd('\\', '/') + "\\*.inf", "/install"] : throw new ArgumentException("Pasta inválida.");

    /// <summary>Tira o pacote do repositório e do dispositivo, sem forçar: se estiver em uso, o pnputil recusa.</summary>
    public static string[] DeleteArgs(string infName) =>
        IsExportable(infName) ? ["/delete-driver", infName.Trim(), "/uninstall"] : throw new ArgumentException("Pacote inválido.");

    private static bool SafeFolder(string folder) =>
        !string.IsNullOrWhiteSpace(folder) && LocalDrivePath.IsMatch(folder) && folder.IndexOfAny(['"', '*', '?', '<', '>', '|', '\n', '\r']) < 0
        && !folder.Split('\\', '/').Contains("..");

    /// <summary>Nome de pasta para a cópia: data, hora e o dispositivo sem caracteres que o Windows recusa.</summary>
    public static string BackupId(DateTime now, string deviceName)
    {
        string safe = Regex.Replace(deviceName ?? "", @"[^A-Za-z0-9]+", "-").Trim('-');
        if (safe.Length > 40) safe = safe[..40].Trim('-');
        return $"{now:yyyyMMdd-HHmmss}-{(safe.Length == 0 ? "driver" : safe)}";
    }

    // ---------- leitura da saída dos scripts ----------

    private static IEnumerable<JsonElement> Items(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];

    private static string S(JsonElement x, string p) =>
        x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static DateTime? D(JsonElement x, string p) =>
        DateTime.TryParseExact(S(x, p), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static bool B(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Lê {"offered":[…],"installed":[…]}. Item sem identificador de atualização ou sem título é descartado.</summary>
    public static DriverScan ParseScan(string json)
    {
        var offered = new List<OfferedDriver>();
        var installed = new List<InstalledDriver>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("offered", out var o))
            foreach (var x in Items(o))
            {
                if (x.ValueKind != JsonValueKind.Object) continue;
                string id = S(x, "id"), title = S(x, "t");
                if (!IsGuid(id) || title.Length == 0) continue;
                long size = x.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out long l) ? l : 0;
                string version = S(x, "v");
                if (version.Length == 0) version = VersionFromTitle(title);
                offered.Add(new OfferedDriver(id, title, S(x, "mf"), S(x, "model"), S(x, "cls"), S(x, "hw"), D(x, "date"), version, size,
                    B(x, "opt"), B(x, "eula"), S(x, "eulaText")));
            }
        if (root.TryGetProperty("installed", out var i))
            foreach (var x in Items(i))
            {
                if (x.ValueKind != JsonValueKind.Object) continue;
                var hw = x.TryGetProperty("hw", out var h) ? Items(h).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!.Trim()).Where(v => v.Length > 0).ToList() : [];
                string dev = S(x, "dev");
                if (dev.Length == 0 || hw.Count == 0) continue;
                installed.Add(new InstalledDriver(S(x, "name"), dev, hw, S(x, "v"), D(x, "d"), S(x, "inf"), S(x, "prov")));
            }
        return new DriverScan(offered, installed, S(root, "error"));
    }

    /// <summary>Lê o resultado do script de instalação: {"ok":true,"stage":"install","code":2,"hr":0,"reboot":false}.</summary>
    public static DriverInstallResult ParseInstall(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var x = doc.RootElement;
        if (x.ValueKind != JsonValueKind.Object) return new DriverInstallResult(false, "parse", 0, 0, false);
        int N(string p) => x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
        // Só vale como sucesso o resultado "concluído" (2) do Windows Update Agent
        bool ok = B(x, "ok") && N("code") == 2;
        return new DriverInstallResult(ok, S(x, "stage"), N("code"), N("hr"), B(x, "reboot"));
    }

    /// <summary>Texto curto do tamanho: 12,4 MB.</summary>
    public static string SizeText(long bytes) => bytes <= 0 ? "" : Units.Bytes(bytes);
}

/// <summary>A lista de cópias (um JSON por cópia, na pasta da cópia). Só lógica de leitura e escrita do manifesto.</summary>
public static class DriverBackupIndex
{
    public const string FileName = "backup.json";

    public static string Serialize(DriverBackup b) => JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true });

    public static DriverBackup? Read(string json)
    {
        try
        {
            var b = JsonSerializer.Deserialize<DriverBackup>(json);
            // Sem identificador ou pasta, a cópia não serve para restaurar
            return b is { Id.Length: > 0, Folder.Length: > 0 } ? b : null;
        }
        catch (JsonException) { return null; }
    }
}
