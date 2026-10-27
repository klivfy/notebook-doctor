using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 9.3: o WMI lido direto, sem abrir um PowerShell (que levava de 2 a 6 segundos só para começar e some em
/// computadores onde a empresa o bloqueou). Monta o mesmo JSON dos scripts, e o Core lê os dois com o mesmo código; o
/// <c>Fase9IntegrationTests</c> compara as duas leituras na CI do Windows.
/// </summary>
public static class WmiReader
{
    private static readonly System.Management.EnumerationOptions Options = new() { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = true, Rewindable = false };

    /// <summary>Uma consulta. Classe ausente, sem permissão (TPM e SMART pedem administrador) ou tempo esgotado devolvem lista vazia.</summary>
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2") => TryQuery(wql, scope) ?? [];

    /// <summary>Como <see cref="Query"/>, mas null quando a consulta falhou (o PowerShell deixava o campo de fora nesse caso).</summary>
    public static List<ManagementBaseObject>? TryQuery(string wql, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), Options);
            var list = new List<ManagementBaseObject>();
            foreach (var o in searcher.Get()) list.Add(o);
            return list;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    /// <summary>Valor de uma propriedade; null quando a classe não a tem nesta versão do Windows.</summary>
    public static object? Get(ManagementBaseObject o, string property)
    {
        try { return o[property]; }
        catch (ManagementException) { return null; }
    }

    /// <summary>Texto como o "$($x.Prop)" do PowerShell: nulo vira vazio.</summary>
    public static string Str(ManagementBaseObject o, string property) => Get(o, property)?.ToString() ?? "";

    /// <summary>Número como o [int]/[int64] do PowerShell: nulo vira 0.</summary>
    public static long Num(ManagementBaseObject o, string property) => Get(o, property) is { } v && v is IConvertible ? Convert.ToInt64(v) : 0;

    public static bool? Bool(ManagementBaseObject o, string property) => Get(o, property) is bool b ? b : null;

    /// <summary>Data do WMI (texto DMTF) como yyyy-MM-dd, ou vazio.</summary>
    public static string Date(ManagementBaseObject o, string property, string format = "yyyy-MM-dd")
    {
        if (Get(o, property) is not string dmtf || dmtf.Length == 0) return "";
        try { return ManagementDateTimeConverter.ToDateTime(dmtf).ToString(format, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or FormatException) { return ""; }
    }

    private static JsonArray Strings(IEnumerable<string> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

    // ---------- Drivers do fabricante (mesmo JSON de OemProbe.Script) ----------

    public static string OemInventoryJson()
    {
        var o = new JsonObject();
        var c = Query("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem").FirstOrDefault();
        var p = Query("SELECT IdentifyingNumber, Name, Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var e = Query("SELECT ChassisTypes FROM Win32_SystemEnclosure").FirstOrDefault();
        var chassis = new JsonArray();
        if (e is not null && Get(e, "ChassisTypes") is ushort[] types)
            foreach (var t in types) chassis.Add((int)t);
        o["cs"] = new JsonObject
        {
            ["mf"] = c is null ? "" : Str(c, "Manufacturer"), ["model"] = c is null ? "" : Str(c, "Model"), ["sku"] = c is null ? "" : Str(c, "SystemSKUNumber"),
            ["serial"] = p is null ? "" : Str(p, "IdentifyingNumber"), ["product"] = p is null ? "" : Str(p, "Name"), ["version"] = p is null ? "" : Str(p, "Version"),
            ["chassis"] = chassis,
        };
        var b = Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS").FirstOrDefault();
        o["bios"] = new JsonObject
        {
            ["vendor"] = b is null ? "" : Str(b, "Manufacturer"), ["ver"] = b is null ? "" : Str(b, "SMBIOSBIOSVersion"),
            ["date"] = b is null ? "" : Date(b, "ReleaseDate"), ["serial"] = b is null ? "" : Str(b, "SerialNumber"),
        };
        var gpu = new JsonArray();
        foreach (var g in Query("SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController"))
            gpu.Add(new JsonObject { ["name"] = Str(g, "Name"), ["mf"] = Str(g, "AdapterCompatibility"), ["drv"] = Str(g, "DriverVersion"), ["pnp"] = Str(g, "PNPDeviceID") });
        o["gpu"] = gpu;
        // Como a tabela do PowerShell: chave sem diferença de maiúsculas, a última vence
        var drivers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Query("SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver"))
            if (Str(d, "DeviceID") is { Length: > 0 } id) drivers[id] = Str(d, "DriverVersion");
        var net = new JsonArray();
        foreach (var n in Query("SELECT Name, Manufacturer, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            string pnp = Str(n, "PNPDeviceID");
            if (!Regex.IsMatch(pnp, @"^(PCI|USB)\\", RegexOptions.IgnoreCase)) continue;
            net.Add(new JsonObject { ["name"] = Str(n, "Name"), ["mf"] = Str(n, "Manufacturer"), ["drv"] = drivers.GetValueOrDefault(pnp), ["pnp"] = pnp });
        }
        o["net"] = net;
        o["audio"] = Strings(Query("SELECT Name FROM Win32_SoundDevice").Select(a => Str(a, "Name")));
        return o.ToJsonString();
    }

    // ---------- Aba Sistema (mesmo JSON de SystemInfoScript.Text) ----------

    public static string SystemInfoJson()
    {
        var o = new JsonObject();
        o["os"] = Os();
        if (Tpm() is { } tpm) o["tpm"] = tpm;

        var cpu = new JsonArray();
        foreach (var c in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, L2CacheSize, L3CacheSize, VirtualizationFirmwareEnabled FROM Win32_Processor"))
            cpu.Add(new JsonObject
            {
                ["name"] = Str(c, "Name").Trim(), ["cores"] = Num(c, "NumberOfCores"), ["threads"] = Num(c, "NumberOfLogicalProcessors"),
                ["mhz"] = Num(c, "MaxClockSpeed"), ["cur"] = Num(c, "CurrentClockSpeed"), ["l2kb"] = Num(c, "L2CacheSize"), ["l3kb"] = Num(c, "L3CacheSize"),
                ["virt"] = Bool(c, "VirtualizationFirmwareEnabled"),
            });
        o["cpu"] = cpu;

        var arr = Query("SELECT MemoryDevices, MaxCapacity FROM Win32_PhysicalMemoryArray").FirstOrDefault();
        var modules = new JsonArray();
        foreach (var m in Query("SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
            modules.Add(new JsonObject
            {
                ["loc"] = Str(m, "DeviceLocator"), ["bank"] = Str(m, "BankLabel"), ["cap"] = Num(m, "Capacity"), ["speed"] = Num(m, "Speed"),
                ["cfg"] = Num(m, "ConfiguredClockSpeed"), ["mf"] = Str(m, "Manufacturer").Trim(), ["pn"] = Str(m, "PartNumber").Trim(), ["ff"] = "", ["type"] = "",
            });
        o["mem"] = new JsonObject { ["slots"] = arr is null ? 0 : Num(arr, "MemoryDevices"), ["maxkb"] = arr is null ? 0 : Num(arr, "MaxCapacity"), ["modules"] = modules };

        var bb = Query("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstOrDefault();
        o["board"] = new JsonObject { ["mf"] = bb is null ? "" : Str(bb, "Manufacturer"), ["product"] = bb is null ? "" : Str(bb, "Product"), ["ver"] = bb is null ? "" : Str(bb, "Version") };
        var bi = Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        o["bios"] = new JsonObject { ["vendor"] = bi is null ? "" : Str(bi, "Manufacturer"), ["ver"] = bi is null ? "" : Str(bi, "SMBIOSBIOSVersion"), ["date"] = bi is null ? "" : Date(bi, "ReleaseDate") };

        var vram = VideoMemory();
        var gpu = new JsonArray();
        foreach (var g in Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            string name = Str(g, "Name");
            long v = vram.GetValueOrDefault(name);
            if (v == 0) v = Num(g, "AdapterRAM");
            long h = Num(g, "CurrentHorizontalResolution");
            gpu.Add(new JsonObject
            {
                ["name"] = name, ["drv"] = Str(g, "DriverVersion"), ["drvdate"] = Date(g, "DriverDate"), ["vram"] = v,
                ["res"] = h > 0 ? $"{h}x{Num(g, "CurrentVerticalResolution")} @ {Num(g, "CurrentRefreshRate")} Hz" : "",
            });
        }
        o["gpu"] = gpu;

        o["disk"] = Disks();
        if (TryQuery("SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\wmi") is { Count: > 0 } pf)
            o["predict"] = pf.Any(x => Bool(x, "PredictFailure") == true);

        o["audio"] = Strings(Query("SELECT Name FROM Win32_SoundDevice").Select(a => Str(a, "Name")));
        o["optical"] = Strings(Query("SELECT Caption FROM Win32_CDROMDrive").Select(a => Str(a, "Caption")));
        var pnp = Query("SELECT Name, PNPDeviceID, PNPClass, Status FROM Win32_PnPEntity WHERE Status = 'OK'").Where(x => Str(x, "PNPDeviceID").Length > 0).ToList();
        o["usb"] = Strings(pnp.Where(x => Str(x, "PNPDeviceID").StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)
                                          && !new[] { "USB", "System" }.Contains(Str(x, "PNPClass"), StringComparer.OrdinalIgnoreCase))
                              .Select(x => Str(x, "Name")).Distinct());
        o["bt"] = Strings(pnp.Where(x => Str(x, "PNPClass").Equals("Bluetooth", StringComparison.OrdinalIgnoreCase)
                                         && !Regex.IsMatch(Str(x, "Name"), "Enumerator|Protocol|Service|Transport", RegexOptions.IgnoreCase))
                             .Select(x => Str(x, "Name")).Distinct());

        var thermal = new JsonArray();
        foreach (var t in Query("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi"))
            thermal.Add(new JsonObject
            {
                ["src"] = "ACPI", ["n"] = Regex.Replace(Str(t, "InstanceName"), @"^.*\\", ""),
                ["c"] = Math.Round(Num(t, "CurrentTemperature") / 10.0 - 273.15, 1, MidpointRounding.ToEven),
            });
        o["thermal"] = thermal;
        return o.ToJsonString();
    }

    private static JsonObject Os()
    {
        var os = Query("SELECT Caption, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault();
        string version = "", build = "";
        try
        {
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            version = cv?.GetValue("DisplayVersion")?.ToString() ?? "";
            if (version.Length == 0) version = cv?.GetValue("ReleaseId")?.ToString() ?? "";
            build = $"{cv?.GetValue("CurrentBuild")}.{cv?.GetValue("UBR")}";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        // A consulta de licença do Windows é a mais lenta de todas (alguns segundos), como já era no PowerShell
        var lic = Query("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'").FirstOrDefault();
        bool? secureBoot = null;
        try
        {
            using var sb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (sb?.GetValue("UEFISecureBootEnabled") is int enabled) secureBoot = enabled != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return new JsonObject
        {
            ["caption"] = os is null ? "" : Str(os, "Caption"), ["version"] = version, ["build"] = build, ["arch"] = os is null ? "" : Str(os, "OSArchitecture"),
            ["install"] = os is null ? "" : Date(os, "InstallDate"), ["boot"] = os is null ? "" : Date(os, "LastBootUpTime", "yyyy-MM-dd HH:mm"),
            ["lic"] = lic is null ? null : (int)Num(lic, "LicenseStatus"), ["secureboot"] = secureBoot,
        };
    }

    /// <summary>TPM: só com administrador. Sem permissão, nada (a aba diz "exige administrador"); sem chip, presente = falso.</summary>
    private static JsonObject? Tpm()
    {
        var list = TryQuery("SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm");
        if (list is null) return null;
        if (list.FirstOrDefault() is not { } t) return new JsonObject { ["present"] = false, ["version"] = "", ["enabled"] = null };
        return new JsonObject { ["present"] = true, ["version"] = Str(t, "SpecVersion").Split(',')[0], ["enabled"] = Bool(t, "IsEnabled_InitialValue") == true };
    }

    /// <summary>O WMI (AdapterRAM) para em 4 GB; o driver grava o tamanho real da memória de vídeo no registro.</summary>
    private static Dictionary<string, long> VideoMemory()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return result;
            foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc")?.ToString() is not { Length: > 0 } name) continue;
                    long size = k.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long q => q, byte[] { Length: >= 8 } q => BitConverter.ToInt64(q, 0), _ => k.GetValue("HardwareInformation.MemorySize") switch
                        {
                            int d => (uint)d, byte[] { Length: >= 4 } d => BitConverter.ToUInt32(d, 0), _ => 0,
                        },
                    };
                    if (size != 0) result[name] = size;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return result;
    }

    private static JsonArray Disks()
    {
        var disks = new JsonArray();
        foreach (var d in Query("SELECT * FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            JsonNode? temp = null, wear = null;
            try
            {
                if (d is ManagementObject mo)
                    foreach (var rc in mo.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        long t = Num(rc, "Temperature");
                        if (t != 0) temp = t;
                        if (Get(rc, "Wear") is { } w) wear = Convert.ToInt64(w);
                        break;
                    }
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
            disks.Add(new JsonObject
            {
                ["name"] = Str(d, "FriendlyName"), ["media"] = StorageNames.Media(Num(d, "MediaType")), ["bus"] = StorageNames.Bus(Num(d, "BusType")),
                ["size"] = Num(d, "Size"), ["health"] = StorageNames.Health(Get(d, "HealthStatus") is null ? 5 : Num(d, "HealthStatus")),
                ["temp"] = temp, ["wear"] = wear,
            });
        }
        return disks;
    }
}
