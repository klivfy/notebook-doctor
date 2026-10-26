using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>De quem é o suporte oficial: o fabricante do notebook ou o fabricante da peça.</summary>
public enum SupportVendor { Generic, Intel, Lenovo, Dell, Hp, Amd, Nvidia }

public enum DriverCategory { BiosFirmware, ChipsetAcpi, GpuDedicated, GpuIntegrated, Network, Audio, Peripherals }

/// <summary>De onde o driver deve vir.</summary>
public enum RecommendationAuthority
{
    /// <summary>Só do fabricante do notebook: ele ajusta o driver ao modelo (energia, ventoinhas, bateria).</summary>
    OemMandatory,
    /// <summary>Do fabricante da peça, pelo aplicativo oficial dele.</summary>
    ComponentPreferred,
    /// <summary>Os dois servem; o do notebook é o mais seguro, o da peça costuma ser mais novo.</summary>
    DualChoice,
}

public enum RiskLevel { Low, Medium, High, Critical }

/// <summary>Identificador PCI de um dispositivo, tirado do PNPDeviceID (<c>PCI\VEN_10DE&amp;DEV_25A2&amp;SUBSYS_0B291028…</c>).</summary>
public sealed record DeviceIdentifier(string VendorId, string DeviceId, string SubsystemId, string PnpDeviceId);

/// <summary>
/// O computador. <see cref="SerialNumber"/> fica vazio quando o fabricante não gravou um de verdade ("To be filled by O.E.M.").
/// O número de série só existe na memória: não vai para registro, relatório nem Ações.
/// </summary>
public sealed record ChassisInfo(string Manufacturer, string Model, string SerialNumber, string Sku, string ProductName, bool? Portable);

public sealed record BiosInfo(string Vendor, string Version, DateTime? ReleaseDate);

public sealed record GpuInfo(string Name, SupportVendor Vendor, string DriverVersion, DeviceIdentifier HardwareId, bool IsDedicated);

/// <summary>Placa de rede física. Sem endereço MAC: nenhuma regra precisa dele.</summary>
public sealed record NetworkAdapterInfo(string Name, SupportVendor Vendor, string DriverVersion, DeviceIdentifier HardwareId);

public sealed record HardwareInventory(
    ChassisInfo Chassis, BiosInfo Bios, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<NetworkAdapterInfo> NetworkAdapters, IReadOnlyList<string> Audio)
{
    public SupportVendor Oem => OemRules.VendorFromManufacturer(Chassis.Manufacturer);
}

/// <summary>Aplicativo oficial de drivers de um fabricante. <see cref="WingetId"/> vazio: não está no catálogo do WinGet.</summary>
public sealed record AssistantTool(string Key, string Name, string WingetId, string CliCommand, string PageUrl);

public sealed record OfficialSupportRoute(SupportVendor Vendor, string DisplayName, string PortalUrl, AssistantTool? Tool);

public sealed record DriverRecommendation(
    string ComponentName, DriverCategory Category, RecommendationAuthority Authority, RiskLevel Risk,
    SupportVendor Source, string DirectUrl, string Notes, bool RequiresRestorePoint);

/// <summary>Página do modelo no site do fabricante. <see cref="SendsSerial"/>: o endereço leva o número de série.</summary>
public sealed record ModelPage(string Url, bool SendsSerial);

/// <summary>Como está a rede de segurança antes de instalar algo arriscado.</summary>
public enum RestoreReadiness
{
    /// <summary>Há um ponto de restauração das últimas 24 horas.</summary>
    Ready,
    /// <summary>A Proteção do Sistema está ligada; falta criar o ponto.</summary>
    CreateFirst,
    /// <summary>A Proteção do Sistema está desligada: não dá para criar ponto.</summary>
    ProtectionOff,
    /// <summary>Não deu para ler o estado.</summary>
    Unknown,
}

/// <summary>
/// Drivers do fabricante: lê o inventário, reconhece o fabricante, monta os endereços oficiais e decide de onde cada
/// driver deve vir. Tudo local: nada aqui acessa a internet; os endereços só abrem no navegador quando a pessoa clica.
/// </summary>
public static class OemRules
{
    // ---------- fabricante ----------

    public static SupportVendor VendorFromManufacturer(string? manufacturer)
    {
        string m = (manufacturer ?? "").Trim().ToLowerInvariant();
        if (m.StartsWith("dell", StringComparison.Ordinal) || m.StartsWith("alienware", StringComparison.Ordinal)) return SupportVendor.Dell;
        if (m.StartsWith("lenovo", StringComparison.Ordinal)) return SupportVendor.Lenovo;
        if (m is "hp" or "hpi" || m.StartsWith("hp ", StringComparison.Ordinal) || m.StartsWith("hewlett", StringComparison.Ordinal)) return SupportVendor.Hp;
        if (m.StartsWith("intel", StringComparison.Ordinal)) return SupportVendor.Intel;
        if (m.StartsWith("advanced micro", StringComparison.Ordinal) || m.StartsWith("amd", StringComparison.Ordinal) || m.StartsWith("ati ", StringComparison.Ordinal)) return SupportVendor.Amd;
        if (m.StartsWith("nvidia", StringComparison.Ordinal)) return SupportVendor.Nvidia;
        return SupportVendor.Generic;
    }

    public static SupportVendor VendorFromPci(string? vendorId) => (vendorId ?? "").ToUpperInvariant() switch
    {
        "8086" => SupportVendor.Intel,
        "10DE" => SupportVendor.Nvidia,
        "1002" or "1022" => SupportVendor.Amd,
        _ => SupportVendor.Generic,
    };

    private static readonly Regex PciPattern = new(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{8}))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static DeviceIdentifier ParseDeviceId(string? pnpDeviceId)
    {
        string id = Clean(pnpDeviceId, 300);
        var m = PciPattern.Match(id);
        return m.Success
            ? new DeviceIdentifier(m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value.ToUpperInvariant(), id)
            : new DeviceIdentifier("", "", "", id);
    }

    // ---------- vídeo ----------

    private static readonly Regex VirtualVideo = new(@"Basic Display|Basic Render|VMware|VirtualBox|Hyper-V|Parallels|Citrix|Remote|Parsec|Mirage|Indirect Display|DisplayLink|Spacedesk|Virtual",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Adaptador sem driver de fabricante para recomendar (genérico do Windows, máquina virtual, tela remota).</summary>
    public static bool IsVirtualVideo(string name) => VirtualVideo.IsMatch(name);

    private static readonly Regex AmdDedicated = new(@"\bRX\s?\d|Radeon\s+Pro|FirePro|\bR[579]\s+\d{3}|\bHD\s+\d{4}M?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IntelDedicated = new(@"Arc\(?T?M?\)?\s+[AB]\d{3}|Iris\(?R?\)?\s+Xe\s+MAX", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Vídeo dedicado? NVIDIA é sempre dedicado; AMD só nas linhas RX, Pro e FirePro (o "Radeon Graphics" e o "Radeon 780M"
    /// são do processador); Intel só no Arc A/B e no Iris Xe MAX.
    /// </summary>
    public static bool IsDedicated(SupportVendor vendor, string name) => vendor switch
    {
        SupportVendor.Nvidia => true,
        SupportVendor.Amd => AmdDedicated.IsMatch(name),
        SupportVendor.Intel => IntelDedicated.IsMatch(name),
        _ => false,
    };

    // ---------- número de série ----------

    private static readonly HashSet<string> SerialPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Serial Number", "To be filled by O.E.M.", "To Be Filled By O.E.M.", "To be filled by OEM", "Default string", "Default",
        "Not Specified", "Not Applicable", "Not Available", "None", "N/A", "NA", "Unknown", "Invalid", "Serial", "Chassis Serial Number",
        "Base Board Serial Number", "OEM", "O.E.M.", "Empty", "Undefined", "123456789", "1234567890", "0123456789", "SerialNumber",
    };

    private static readonly Regex SerialShape = new(@"^[A-Za-z0-9-]{4,30}$", RegexOptions.CultureInvariant);

    /// <summary>Valor que o fabricante deixou de exemplo ("System Serial Number", "To be filled by O.E.M.", "00000000").</summary>
    public static bool IsPlaceholderSerial(string? serial)
    {
        string s = (serial ?? "").Trim();
        if (s.Length == 0 || SerialPlaceholders.Contains(s)) return true;
        string compact = s.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return compact.Length == 0 || compact.All(c => c == compact[0]);
    }

    /// <summary>
    /// O número de série pronto para entrar num endereço, ou vazio. Só letras, números e hífen: qualquer outra coisa
    /// (espaço, barra, "?", "&amp;") descarta o valor em vez de "consertá-lo".
    /// </summary>
    public static string SafeSerial(string? serial)
    {
        string s = Clean(serial, 64);
        if (IsPlaceholderSerial(s) || !SerialShape.IsMatch(s)) return "";
        return s.ToUpperInvariant();
    }

    /// <summary>Tira caracteres de controle e espaços repetidos e limita o tamanho. Vale para todo texto lido do WMI.</summary>
    public static string Clean(string? text, int max = 200)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        string s = Regex.Replace(new string(chars), @"\s+", " ").Trim();
        return s.Length > max ? s[..max] : s;
    }

    // ---------- leitura do inventário ----------

    private static readonly HashSet<int> PortableChassis = [8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32];

    /// <summary>Lê o JSON do script de inventário (NotebookDoctor.Windows.OemProbe). Campos que faltam ficam vazios.</summary>
    public static HardwareInventory ParseInventory(string json)
    {
        using var doc = JsonDocument.Parse(json.Length == 0 ? "{}" : json);
        var root = doc.RootElement;

        var cs = Obj(root, "cs");
        string manufacturer = Str(cs, "mf"), model = Str(cs, "model"), sku = Str(cs, "sku");
        string product = Str(cs, "product"), friendly = Str(cs, "version");
        // A Lenovo grava o nome comercial (ThinkPad T14 Gen 3) em Version e o tipo de máquina (21AH…) em Name e Model
        if (VendorFromManufacturer(manufacturer) == SupportVendor.Lenovo && friendly.Length > 0 && !IsPlaceholderSerial(friendly)) model = friendly;
        string serial = SafeSerial(Str(cs, "serial"));
        if (serial.Length == 0) serial = SafeSerial(Str(Obj(root, "bios"), "serial"));
        bool? portable = null;
        if (cs.ValueKind == JsonValueKind.Object && cs.TryGetProperty("chassis", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            var list = types.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Number).Select(t => t.GetInt32()).ToList();
            if (list.Count > 0) portable = list.Any(PortableChassis.Contains);
        }
        var chassis = new ChassisInfo(manufacturer, model, serial, sku, product, portable);

        var b = Obj(root, "bios");
        var bios = new BiosInfo(Str(b, "vendor"), Str(b, "ver"), DateTime.TryParse(Str(b, "date"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null);

        var gpus = new List<GpuInfo>();
        foreach (var g in Arr(root, "gpu"))
        {
            string name = Str(g, "name");
            if (name.Length == 0) continue;
            var id = ParseDeviceId(Str(g, "pnp"));
            var vendor = VendorFromPci(id.VendorId);
            if (vendor == SupportVendor.Generic) vendor = VendorFromManufacturer(Str(g, "mf"));
            gpus.Add(new GpuInfo(name, vendor, Str(g, "drv"), id, IsDedicated(vendor, name)));
        }

        var nets = new List<NetworkAdapterInfo>();
        foreach (var n in Arr(root, "net"))
        {
            string name = Str(n, "name");
            if (name.Length == 0) continue;
            var id = ParseDeviceId(Str(n, "pnp"));
            var vendor = VendorFromPci(id.VendorId);
            if (vendor == SupportVendor.Generic) vendor = VendorFromManufacturer(Str(n, "mf"));
            nets.Add(new NetworkAdapterInfo(name, vendor, Str(n, "drv"), id));
        }

        var audio = Arr(root, "audio").Where(a => a.ValueKind == JsonValueKind.String).Select(a => Clean(a.GetString())).Where(a => a.Length > 0).Distinct().ToList();
        return new HardwareInventory(chassis, bios, gpus, nets, audio);
    }

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return [];
        // ConvertTo-Json entrega um objeto sozinho quando a lista tem um item só
        return v.ValueKind switch { JsonValueKind.Array => v.EnumerateArray().ToList(), JsonValueKind.Object or JsonValueKind.String => [v], _ => [] };
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString()) : "";

    // ---------- proteção do sistema ----------

    /// <param name="protectionOn">Proteção do Sistema ligada (null: não deu para ler).</param>
    public static RestoreReadiness Readiness(bool? protectionOn, DateTime lastRestorePoint, DateTime now) => protectionOn switch
    {
        false => RestoreReadiness.ProtectionOff,
        null => RestoreReadiness.Unknown,
        _ => now - lastRestorePoint < TimeSpan.FromHours(24) && lastRestorePoint <= now ? RestoreReadiness.Ready : RestoreReadiness.CreateFirst,
    };
}

/// <summary>Endereços oficiais de suporte de cada fabricante.</summary>
public static class OemLinks
{
    public const string WindowsOptionalUpdates = "ms-settings:windowsupdate-optionalupdates";
    public const string LenovoVantageStore = "ms-windows-store://pdp/?productid=9WZDNCRFJ4MV";

    private static readonly AssistantTool DellCommandUpdate = new("dcu", "Dell Command | Update", "Dell.CommandUpdate", "dcu-cli.exe", "https://www.dell.com/support/home/pt-br?app=drivers");
    private static readonly AssistantTool LenovoSystemUpdate = new("lsu", "Lenovo System Update", "Lenovo.SystemUpdate", "tvsu.exe", "https://support.lenovo.com/br/pt");
    private static readonly AssistantTool LenovoVantage = new("vantage", "Lenovo Vantage", "", "", LenovoVantageStore);
    private static readonly AssistantTool HpImageAssistant = new("hpia", "HP Image Assistant", "HP.ImageAssistant", "HPImageAssistant.exe", "https://support.hp.com/br-pt/drivers");
    private static readonly AssistantTool HpSupportAssistant = new("hpsa", "HP Support Assistant", "HP.SupportAssistant", "", "https://support.hp.com/br-pt/drivers");
    private static readonly AssistantTool IntelDsa = new("dsa", "Intel Driver & Support Assistant", "Intel.IntelDriverAndSupportAssistant", "",
        "https://www.intel.com.br/content/www/br/pt/support/intel-driver-support-assistant.html");
    private static readonly AssistantTool AmdAdrenalin = new("adrenalin", "AMD Software: Adrenalin Edition", "", "", "https://www.amd.com/pt/products/software/adrenalin.html");
    private static readonly AssistantTool NvidiaApp = new("nvapp", "NVIDIA app", "", "", "https://www.nvidia.com/pt-br/software/nvidia-app/");

    /// <summary>Todos os aplicativos que o app sabe reconhecer, para procurar no computador.</summary>
    public static IReadOnlyList<AssistantTool> AllTools { get; } =
        [DellCommandUpdate, LenovoSystemUpdate, LenovoVantage, HpImageAssistant, HpSupportAssistant, IntelDsa, AmdAdrenalin, NvidiaApp];

    public static OfficialSupportRoute Route(SupportVendor vendor, string model = "") => vendor switch
    {
        SupportVendor.Dell => new(vendor, "Dell", "https://www.dell.com/support/home/pt-br?app=drivers", DellCommandUpdate),
        // ThinkPad, ThinkBook e ThinkCentre usam o System Update; as linhas de casa (IdeaPad, Yoga, Legion), o Vantage
        SupportVendor.Lenovo => new(vendor, "Lenovo", "https://support.lenovo.com/br/pt", IsLenovoBusiness(model) ? LenovoSystemUpdate : LenovoVantage),
        // O Image Assistant é das linhas corporativas (EliteBook, ProBook, ZBook); as de casa usam o Support Assistant
        SupportVendor.Hp => new(vendor, "HP", "https://support.hp.com/br-pt/drivers", IsHpBusiness(model) ? HpImageAssistant : HpSupportAssistant),
        SupportVendor.Intel => new(vendor, "Intel", "https://www.intel.com.br/content/www/br/pt/download-center/home.html", IntelDsa),
        SupportVendor.Amd => new(vendor, "AMD", "https://www.amd.com/pt/support/download/drivers.html", AmdAdrenalin),
        SupportVendor.Nvidia => new(vendor, "NVIDIA", "https://www.nvidia.com/pt-br/drivers/", NvidiaApp),
        _ => new(SupportVendor.Generic, "Windows Update", WindowsOptionalUpdates, null),
    };

    public static bool IsLenovoBusiness(string model) => Regex.IsMatch(model, @"\bThink(Pad|Book|Centre|Station)\b", RegexOptions.IgnoreCase);
    public static bool IsHpBusiness(string model) => Regex.IsMatch(model, @"\b(Elite|Pro|Z)Book\b|\bElite\s?Desk\b|\bPro\s?Desk\b|\bZ\d\b|\bEliteOne\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// A página de drivers deste computador. Com <paramref name="withSerial"/>, Dell e Lenovo recebem o número de série no
    /// endereço (a página já abre no modelo certo). Sem ele, a Lenovo ainda abre pelo tipo de máquina, que não identifica
    /// o aparelho. Null quando o fabricante não é Dell, Lenovo nem HP.
    /// </summary>
    public static ModelPage? ModelPageFor(ChassisInfo chassis, bool withSerial)
    {
        var vendor = OemRules.VendorFromManufacturer(chassis.Manufacturer);
        string serial = withSerial ? OemRules.SafeSerial(chassis.SerialNumber) : "";
        switch (vendor)
        {
            case SupportVendor.Dell:
                return serial.Length > 0
                    ? new ModelPage($"https://www.dell.com/support/home/pt-br/product-support/servicetag/{Uri.EscapeDataString(serial)}/drivers", true)
                    : new ModelPage(Route(vendor).PortalUrl, false);
            case SupportVendor.Lenovo:
                if (serial.Length > 0) return new ModelPage($"https://pcsupport.lenovo.com/br/pt/search?query={Uri.EscapeDataString(serial)}", true);
                string mtm = MachineType(chassis.ProductName);
                return mtm.Length > 0
                    ? new ModelPage($"https://pcsupport.lenovo.com/br/pt/search?query={Uri.EscapeDataString(mtm)}", false)
                    : new ModelPage(Route(vendor).PortalUrl, false);
            case SupportVendor.Hp:
                // A HP não tem endereço público com o número de série: a página pede o número, que o app copia
                return new ModelPage(Route(vendor).PortalUrl, false);
            default:
                return null;
        }
    }

    /// <summary>Os 4 primeiros caracteres do MTM da Lenovo (21AH00BBBR → 21AH) identificam o modelo, não o aparelho.</summary>
    public static string MachineType(string productName)
    {
        string p = OemRules.Clean(productName, 40).ToUpperInvariant();
        return Regex.IsMatch(p, @"^[0-9A-Z]{4}[0-9A-Z]{0,6}$") ? p[..4] : "";
    }

    /// <summary>Só endereços que o app mesmo montou podem ser abertos: https dos fabricantes e a página do Windows Update.</summary>
    public static bool IsOfficial(string url)
    {
        if (url == WindowsOptionalUpdates || url == LenovoVantageStore) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        string h = u.Host.ToLowerInvariant();
        string[] domains = ["dell.com", "lenovo.com", "hp.com", "intel.com", "intel.com.br", "amd.com", "nvidia.com"];
        return domains.Any(d => h == d || h.EndsWith("." + d, StringComparison.Ordinal));
    }
}

/// <summary>
/// As regras de recomendação. Firmware, chipset, energia e touchpad só do fabricante do notebook; vídeo dedicado da
/// NVIDIA e da AMD pelo aplicativo delas; o resto pode vir de qualquer um dos dois. Risco alto ou crítico exige
/// ponto de restauração antes.
/// </summary>
public static class OemAdvisor
{
    private static readonly Regex GpuAudio = new(@"High Definition Audio|Display Audio|HDMI|DisplayPort", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<DriverRecommendation> Recommend(HardwareInventory inv)
    {
        var oem = inv.Oem;
        // Fabricante desconhecido (montado, marca pequena): o caminho oficial que sobra é o próprio Windows Update
        var oemVendor = oem is SupportVendor.Dell or SupportVendor.Lenovo or SupportVendor.Hp ? oem : SupportVendor.Generic;
        string oemUrl = OemLinks.ModelPageFor(inv.Chassis, withSerial: false)?.Url ?? OemLinks.WindowsOptionalUpdates;
        string oemWarning = Loc.T("Use só o do fabricante do notebook: driver genérico pode descalibrar as ventoinhas, os perfis de energia (TDP) e o carregamento da bateria.");
        var list = new List<DriverRecommendation>();

        string biosName = inv.Bios.Version.Length > 0 ? Loc.F("BIOS/UEFI (versão {0})", inv.Bios.Version) : Loc.T("BIOS/UEFI");
        list.Add(Make(biosName, DriverCategory.BiosFirmware, RecommendationAuthority.OemMandatory, RiskLevel.Critical, oemVendor, oemUrl,
            Loc.T("Só do fabricante do notebook e só do modelo exato. Ligue na tomada e não desligue durante a atualização: interromper pode impedir o computador de ligar. O ponto de restauração não desfaz a BIOS, mas protege o resto.")));
        list.Add(Make(Loc.T("Chipset e gerenciamento de energia (ACPI)"), DriverCategory.ChipsetAcpi, RecommendationAuthority.OemMandatory, RiskLevel.High, oemVendor, oemUrl, oemWarning));
        if (inv.Chassis.Portable != false)
            list.Add(Make(Loc.T("Touchpad e teclas de função"), DriverCategory.Peripherals, RecommendationAuthority.OemMandatory, RiskLevel.High, oemVendor, oemUrl,
                Loc.T("Use só o do fabricante do notebook: o genérico costuma perder gestos, atalhos de brilho e volume e a tecla que desliga o touchpad.")));

        foreach (var g in inv.Gpus.Where(g => !OemRules.IsVirtualVideo(g.Name)).DistinctBy(g => g.Name))
            list.Add(Gpu(g, oemVendor, oemUrl));

        foreach (var n in inv.NetworkAdapters.DistinctBy(n => n.Name))
        {
            list.Add(n.Vendor == SupportVendor.Intel
                ? Make(n.Name, DriverCategory.Network, RecommendationAuthority.DualChoice, RiskLevel.Low, SupportVendor.Intel, OemLinks.Route(SupportVendor.Intel).PortalUrl,
                    Loc.T("Serve o da Intel (assistente oficial) ou o do fabricante do notebook. O da Intel costuma ser mais novo e corrige falhas de Wi-Fi."))
                : Make(n.Name, DriverCategory.Network, RecommendationAuthority.OemMandatory, RiskLevel.Medium, oemVendor, oemUrl,
                    Loc.T("Pegue no fabricante do notebook: o fabricante da placa nem sempre publica driver para o usuário final.")));
        }

        var audio = inv.Audio.Where(a => !GpuAudio.IsMatch(a)).ToList();
        if (audio.Count > 0)
            list.Add(Make(string.Join(", ", audio), DriverCategory.Audio, RecommendationAuthority.OemMandatory, RiskLevel.Medium, oemVendor, oemUrl,
                Loc.T("Use o do fabricante do notebook: ele traz os ajustes dos alto-falantes e microfones deste modelo. O genérico funciona, mas costuma soar mais baixo e sem os efeitos.")));

        return [.. list.OrderByDescending(r => r.Risk).ThenBy(r => r.Category)];
    }

    private static DriverRecommendation Gpu(GpuInfo g, SupportVendor oemVendor, string oemUrl)
    {
        var route = OemLinks.Route(g.Vendor);
        if (g.IsDedicated && g.Vendor is SupportVendor.Nvidia or SupportVendor.Amd or SupportVendor.Intel)
        {
            string notes = g.Vendor switch
            {
                SupportVendor.Nvidia => Loc.T("Prefira o NVIDIA app ou o site da NVIDIA: o driver sai antes e traz as correções para jogos. Num notebook com dois vídeos, o da NVIDIA convive com o integrado; não remova o integrado."),
                SupportVendor.Amd => Loc.T("Prefira o AMD Software: Adrenalin Edition. Se o instalador da AMD recusar este notebook, use o driver do fabricante do notebook."),
                _ => Loc.T("Prefira o assistente da Intel: as placas Arc recebem correções de desempenho com frequência."),
            };
            return Make(g.Name, DriverCategory.GpuDedicated, RecommendationAuthority.ComponentPreferred, RiskLevel.Medium, g.Vendor, route.PortalUrl, notes);
        }
        if (g.Vendor is SupportVendor.Intel or SupportVendor.Amd)
        {
            string notes = g.Vendor == SupportVendor.Intel
                ? Loc.T("Serve o da Intel (assistente oficial) ou o do fabricante do notebook. Se a tela tem ajustes do fabricante (economia de energia do painel, cores), prefira o do fabricante.")
                : Loc.T("Serve o da AMD ou o do fabricante do notebook. Muitos notebooks com AMD só aceitam o do fabricante; se o da AMD recusar, é esse o motivo.");
            return Make(g.Name, DriverCategory.GpuIntegrated, RecommendationAuthority.DualChoice, RiskLevel.Medium, g.Vendor, route.PortalUrl, notes);
        }
        return Make(g.Name, DriverCategory.GpuIntegrated, RecommendationAuthority.OemMandatory, RiskLevel.Medium, oemVendor, oemUrl,
            Loc.T("Pegue no fabricante do notebook: o fabricante deste vídeo não publica driver para o usuário final."));
    }

    private static DriverRecommendation Make(string name, DriverCategory category, RecommendationAuthority authority, RiskLevel risk,
        SupportVendor source, string url, string notes) =>
        new(name, category, authority, risk, source, url, notes, RequiresRestorePoint: risk >= RiskLevel.High);
}
