using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NotebookDoctor.Services;

public sealed record InfoLine(string Label, string Value);

/// <summary>Um bloco da aba Sistema: título e linhas "rótulo: valor". As mesmas linhas servem à tela e aos três formatos de exportação.</summary>
public sealed record InfoSection(string Key, string Title, IReadOnlyList<InfoLine> Lines);

// ---------- SMBIOS ----------

/// <summary>Um pente (ou slot vazio, <see cref="SizeBytes"/> = 0) da tabela SMBIOS tipo 17.</summary>
public sealed record SmbiosMemory(string Slot, string Bank, long SizeBytes, string FormFactor, string Type, int SpeedMHz, int ConfiguredMHz, string Manufacturer, string PartNumber);

/// <summary>Matriz de memória da SMBIOS tipo 16: quantos slots a placa tem e até quanto aceita.</summary>
public sealed record SmbiosArray(int Slots, long MaxCapacityBytes, string Ecc);

public sealed record SmbiosData(string Version, IReadOnlyList<SmbiosArray> Arrays, IReadOnlyList<SmbiosMemory> Devices);

/// <summary>
/// Leva 8.1 (S01): lê a tabela SMBIOS crua que o Windows entrega em <c>GetSystemFirmwareTable('RSMB')</c>. O WMI mostra só os pentes
/// presentes e some com os slots vazios; a SMBIOS diz quantos slots existem e quais estão livres.
/// </summary>
public static class SmbiosParser
{
    private static readonly Dictionary<int, string> MemoryTypes = new()
    {
        [0x03] = "DRAM", [0x0F] = "SDRAM", [0x12] = "DDR", [0x13] = "DDR2", [0x14] = "DDR2 FB-DIMM", [0x18] = "DDR3", [0x1A] = "DDR4",
        [0x1B] = "LPDDR", [0x1C] = "LPDDR2", [0x1D] = "LPDDR3", [0x1E] = "LPDDR4", [0x20] = "HBM", [0x21] = "HBM2", [0x22] = "DDR5", [0x23] = "LPDDR5", [0x24] = "HBM3",
    };

    private static readonly Dictionary<int, string> FormFactors = new()
    {
        [0x03] = "SIMM", [0x09] = "DIMM", [0x0D] = "SODIMM", [0x0F] = "FB-DIMM", [0x0C] = "RIMM", [0x10] = "Die", [0x0B] = "Chips soldados",
    };

    private static readonly string[] EccTypes = ["", "Outro", "Desconhecido", "Sem", "Paridade", "ECC de bit único", "ECC de vários bits", "CRC"];

    public static string TypeName(int code) => MemoryTypes.TryGetValue(code, out var n) ? n : "";
    public static string FormFactorName(int code) => FormFactors.TryGetValue(code, out var n) ? n : "";

    /// <summary>Lê o buffer inteiro devolvido pelo Windows (8 bytes de cabeçalho, depois as tabelas). Entrada estragada devolve o que deu para ler.</summary>
    public static SmbiosData Parse(byte[] raw)
    {
        var arrays = new List<SmbiosArray>();
        var devices = new List<SmbiosMemory>();
        if (raw.Length < 8) return new SmbiosData("", arrays, devices);
        string version = $"{raw[1]}.{raw[2]}";
        int total = (int)Math.Min((uint)(raw.Length - 8), BitConverter.ToUInt32(raw, 4));
        int pos = 8, end = 8 + total;

        while (pos + 4 <= end)
        {
            byte type = raw[pos], length = raw[pos + 1];
            if (length < 4 || pos + length > end) break;
            // Strings: depois da área formatada, textos terminados em zero; a lista acaba em dois zeros
            int strStart = pos + length, strEnd = strStart;
            while (strEnd + 1 < end && !(raw[strEnd] == 0 && raw[strEnd + 1] == 0)) strEnd++;
            var strings = new List<string>();
            if (strEnd > strStart)
                strings.AddRange(Encoding.UTF8.GetString(raw, strStart, strEnd - strStart).Split('\0'));
            string Str(int idx) => idx >= 1 && idx <= strings.Count ? strings[idx - 1].Trim() : "";
            int At(int off) => pos + off < pos + length ? raw[pos + off] : 0;
            int W(int off) => pos + off + 1 < pos + length ? BitConverter.ToUInt16(raw, pos + off) : 0;
            long D(int off) => pos + off + 3 < pos + length ? BitConverter.ToUInt32(raw, pos + off) : 0;

            if (type == 16 && length >= 0x0F)
            {
                long maxKb = D(0x07);
                long maxBytes = maxKb == 0x80000000 && length >= 0x17 ? BitConverter.ToInt64(raw, pos + 0x0F) : maxKb * 1024;
                int ecc = At(0x06);
                arrays.Add(new SmbiosArray(W(0x0D), maxBytes, ecc < EccTypes.Length ? EccTypes[ecc] : ""));
            }
            else if (type == 17 && length >= 0x15)
            {
                int sizeField = W(0x0C);
                long bytes = sizeField switch
                {
                    0 => 0,                       // slot vazio
                    0xFFFF => 0,                  // tamanho desconhecido
                    0x7FFF => D(0x1C) * 1024 * 1024,
                    _ => (sizeField & 0x8000) != 0 ? (long)(sizeField & 0x7FFF) * 1024 : (long)sizeField * 1024 * 1024,
                };
                int speed = W(0x15);
                if (speed == 0xFFFF && length > 0x56) speed = (int)D(0x54);
                int cfg = length >= 0x22 ? W(0x20) : 0;
                if (cfg == 0xFFFF && length > 0x5A) cfg = (int)D(0x58);
                devices.Add(new SmbiosMemory(Str(At(0x10)), Str(At(0x11)), bytes, FormFactorName(At(0x0E)), TypeName(At(0x12)),
                    speed is 0 or 0xFFFF ? 0 : speed, cfg is 0 or 0xFFFF ? 0 : cfg, bytes == 0 ? "" : Str(At(0x17)), bytes == 0 ? "" : Str(At(0x1A))));
            }
            else if (type == 127) break;

            pos = strEnd + 2;
            if (strEnd == strStart) pos = strStart + 2; // sem strings: dois zeros logo depois da área formatada
        }
        return new SmbiosData(version, arrays, devices);
    }
}

// ---------- o que o PowerShell e as APIs trazem ----------

public sealed record RawModule(string Slot, string Bank, long Bytes, int SpeedMHz, int ConfiguredMHz, string Manufacturer, string PartNumber, string FormFactor, string Type);
public sealed record RawCpu(string Name, int Cores, int Threads, int MaxMHz, int CurrentMHz, int L2Kb, int L3Kb, bool? Virtualization);
public sealed record RawGpu(string Name, string Driver, string DriverDate, long VramBytes, string Resolution);
public sealed record RawDisk(string Name, string Media, string Bus, long SizeBytes, string Health, double? TempC, double? WearPct);
public sealed record RawTpm(bool Present, string Version, bool? Enabled);
public sealed record RawTemp(string Source, string Name, double Celsius);

public sealed class RawSystem
{
    public string OsName { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string OsBuild { get; init; } = "";
    public string OsArch { get; init; } = "";
    public string Installed { get; init; } = "";
    public string Booted { get; init; } = "";
    /// <summary>1 = ativado; null = não deu para saber.</summary>
    public int? Activation { get; init; }
    public bool? SecureBoot { get; init; }
    public RawTpm? Tpm { get; init; }
    public List<RawCpu> Cpus { get; init; } = [];
    public int MemSlots { get; init; }
    public long MemMaxBytes { get; init; }
    public List<RawModule> Modules { get; init; } = [];
    public string BoardMaker { get; init; } = "";
    public string BoardProduct { get; init; } = "";
    public string BoardVersion { get; init; } = "";
    public string BiosVendor { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public string BiosDate { get; init; } = "";
    public List<RawGpu> Gpus { get; init; } = [];
    public List<RawDisk> Disks { get; init; } = [];
    /// <summary>Previsão de falha do SMART (root\wmi, exige administrador): true se algum disco prevê falha; null se não deu para ler.</summary>
    public bool? PredictFailure { get; init; }
    public List<string> Audio { get; init; } = [];
    public List<string> Optical { get; init; } = [];
    public List<string> Usb { get; init; } = [];
    public List<string> Bluetooth { get; init; } = [];
    public List<RawTemp> Temps { get; init; } = [];
}

public sealed record NetAdapter(string Name, string Description, string Kind, string Mac, long SpeedMbps, bool Up, IReadOnlyList<string> Ips, string Gateway, IReadOnlyList<string> Dns);
public sealed record WifiInfo(string Ssid, int SignalPct, int Channel, string Band, string Standard, int RxMbps, int TxMbps);

/// <summary>
/// Leva 9.3: o WMI de armazenamento (MSFT_PhysicalDisk) devolve números; o Get-PhysicalDisk do PowerShell mostrava os nomes.
/// A leitura direta usa os mesmos nomes para a aba Sistema não mudar.
/// </summary>
public static class StorageNames
{
    public static string Media(long code) => code switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified" };

    public static string Bus(long code) => code switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 9 => "iSCSI",
        10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual", 15 => "File Backed Virtual", 16 => "Storage Spaces",
        17 => "NVMe", 18 => "SCM", 19 => "UFS", _ => "Unknown",
    };

    public static string Health(long code) => code switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
}

public static class SystemInfoParser
{
    private static IEnumerable<JsonElement> Items(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];

    private static string S(JsonElement x, string p) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : v.ValueKind == JsonValueKind.Number ? v.ToString() : "") : "";

    private static long L(JsonElement x, string p) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l) ? l : 0;

    private static double? Dbl(JsonElement x, string p) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;

    private static bool? B(JsonElement x, string p) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    // O PowerShell manda texto solto, e não lista, quando só há um item
    private static List<string> Strings(JsonElement x, string p) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var v)
            ? (v.ValueKind == JsonValueKind.String ? [v] : Items(v)).Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!.Trim()).Where(s => s.Length > 0).Distinct().ToList() : [];

    public static RawSystem Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        JsonElement Get(string p) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(p, out var v) ? v : default;
        var os = Get("os"); var mem = Get("mem"); var board = Get("board"); var bios = Get("bios");
        var tpm = Get("tpm");
        return new RawSystem
        {
            OsName = S(os, "caption"), OsVersion = S(os, "version"), OsBuild = S(os, "build"), OsArch = S(os, "arch"),
            Installed = S(os, "install"), Booted = S(os, "boot"),
            Activation = os.ValueKind == JsonValueKind.Object && os.TryGetProperty("lic", out var lic) && lic.ValueKind == JsonValueKind.Number ? lic.GetInt32() : null,
            SecureBoot = B(os, "secureboot"),
            Tpm = tpm.ValueKind == JsonValueKind.Object ? new RawTpm(B(tpm, "present") == true, S(tpm, "version"), B(tpm, "enabled")) : null,
            Cpus = Items(Get("cpu")).Where(c => c.ValueKind == JsonValueKind.Object).Select(c => new RawCpu(S(c, "name"), (int)L(c, "cores"), (int)L(c, "threads"),
                (int)L(c, "mhz"), (int)L(c, "cur"), (int)L(c, "l2kb"), (int)L(c, "l3kb"), B(c, "virt"))).ToList(),
            MemSlots = (int)L(mem, "slots"), MemMaxBytes = L(mem, "maxkb") * 1024,
            Modules = Items(mem.ValueKind == JsonValueKind.Object && mem.TryGetProperty("modules", out var mods) ? mods : default).Where(m => m.ValueKind == JsonValueKind.Object)
                .Select(m => new RawModule(S(m, "loc"), S(m, "bank"), L(m, "cap"), (int)L(m, "speed"), (int)L(m, "cfg"), S(m, "mf"), S(m, "pn"), S(m, "ff"), S(m, "type"))).ToList(),
            BoardMaker = S(board, "mf"), BoardProduct = S(board, "product"), BoardVersion = S(board, "ver"),
            BiosVendor = S(bios, "vendor"), BiosVersion = S(bios, "ver"), BiosDate = S(bios, "date"),
            Gpus = Items(Get("gpu")).Where(g => g.ValueKind == JsonValueKind.Object).Select(g => new RawGpu(S(g, "name"), S(g, "drv"), S(g, "drvdate"), L(g, "vram"), S(g, "res"))).ToList(),
            Disks = Items(Get("disk")).Where(d => d.ValueKind == JsonValueKind.Object).Select(d => new RawDisk(S(d, "name"), S(d, "media"), S(d, "bus"), L(d, "size"), S(d, "health"),
                Dbl(d, "temp"), Dbl(d, "wear"))).ToList(),
            PredictFailure = B(r, "predict"),
            Audio = Strings(r, "audio"), Optical = Strings(r, "optical"), Usb = Strings(r, "usb"), Bluetooth = Strings(r, "bt"),
            Temps = Items(Get("thermal")).Where(t => t.ValueKind == JsonValueKind.Object && Dbl(t, "c") is not null)
                .Select(t => new RawTemp(S(t, "src"), S(t, "n"), Dbl(t, "c")!.Value)).ToList(),
        };
    }
}

// ---------- montagem das seções ----------

public static class SystemInfoBuilder
{
    private const int MaxListed = 12;

    private static InfoLine? Line(string label, string? value) => string.IsNullOrWhiteSpace(value) ? null : new InfoLine(label, value.Trim());

    private static InfoSection Section(string key, string title, params InfoLine?[] lines) =>
        new(key, title, lines.Where(l => l is not null).Select(l => l!).ToList());

    private static string Mhz(int mhz) => mhz >= 1000 ? $"{mhz / 1000.0:0.0#} GHz".Replace(',', '.') : $"{mhz} MHz";

    private static string Cache(int kb) => kb >= 1024 ? $"{kb / 1024.0:0.#} MB".Replace(',', '.') : $"{kb} KB";

    private static string Yes(bool? value, string yes, string no, string unknown) => value switch { true => yes, false => no, _ => unknown };

    /// <summary>Faixas que um sensor de verdade nunca sai: fora delas a leitura é lixo (sensor sem ligação, kelvin trocado por Celsius).</summary>
    public static bool PlausibleTemp(double celsius) => celsius is > 0 and < 125;

    public static string List(IReadOnlyList<string> items, int max = MaxListed)
    {
        if (items.Count == 0) return "";
        var shown = items.Take(max);
        return items.Count > max ? $"{Pt.N(items.Count, Loc.T("item"), Loc.T("itens"))}: {string.Join("; ", shown)}…" : string.Join("; ", shown);
    }

    public static List<InfoSection> Build(RawSystem raw, SmbiosData? smbios, string firmware, IReadOnlyList<NetAdapter> net, WifiInfo? wifi,
                                          IReadOnlyList<RawTemp> extraTemps, string machine)
    {
        var sections = new List<InfoSection>
        {
            Os(raw, firmware, machine), Cpu(raw), Memory(raw, smbios), Board(raw), Video(raw), Storage(raw), Devices(raw), Network(net, wifi),
        };
        var temps = Temperatures(raw, extraTemps);
        if (temps.Lines.Count > 0) sections.Add(temps);
        return sections.Where(s => s.Lines.Count > 0).ToList();
    }

    private static InfoSection Os(RawSystem r, string firmware, string machine)
    {
        string activation = r.Activation switch { 1 => Loc.T("Ativado"), null => "", _ => Loc.T("Não ativado") };
        string tpm = r.Tpm is null ? Loc.T("Não informado (exige administrador)")
            : !r.Tpm.Present ? Loc.T("Não encontrado")
            : $"{(r.Tpm.Version.Length > 0 ? r.Tpm.Version : Loc.T("Presente"))}{(r.Tpm.Enabled == false ? " · " + Loc.T("desligado") : "")}";
        return Section("os", Loc.T("Sistema operacional"),
            Line(Loc.T("Computador"), machine), Line(Loc.T("Windows"), r.OsName),
            Line(Loc.T("Versão"), r.OsVersion.Length > 0 ? $"{r.OsVersion} ({Loc.T("compilação")} {r.OsBuild})".Replace(" ()", "") : ""),
            Line(Loc.T("Arquitetura"), r.OsArch), Line(Loc.T("Instalado em"), r.Installed), Line(Loc.T("Ligado desde"), r.Booted),
            Line(Loc.T("Ativação"), activation), Line(Loc.T("Firmware"), firmware),
            Line(Loc.T("Inicialização segura"), Yes(r.SecureBoot, Loc.T("Ativada"), Loc.T("Desativada"), Loc.T("Não disponível"))),
            Line("TPM", tpm));
    }

    private static InfoSection Cpu(RawSystem r)
    {
        var lines = new List<InfoLine?>();
        for (int i = 0; i < r.Cpus.Count; i++)
        {
            var c = r.Cpus[i];
            string prefix = r.Cpus.Count > 1 ? $" {i + 1}" : "";
            lines.Add(Line(Loc.T("Processador") + prefix, c.Name));
            lines.Add(Line(Loc.T("Núcleos e threads") + prefix, c.Cores > 0 ? $"{c.Cores} / {c.Threads}" : ""));
            lines.Add(Line(Loc.T("Frequência") + prefix, c.MaxMHz > 0 ? $"{Loc.T("máxima")} {Mhz(c.MaxMHz)}" + (c.CurrentMHz > 0 ? $" · {Loc.T("agora")} {Mhz(c.CurrentMHz)}" : "") : ""));
            lines.Add(Line(Loc.T("Cache L2 / L3") + prefix, c.L2Kb > 0 || c.L3Kb > 0 ? $"{Cache(c.L2Kb)} / {Cache(c.L3Kb)}" : ""));
            lines.Add(Line(Loc.T("Virtualização") + prefix, Yes(c.Virtualization, Loc.T("Ativada"), Loc.T("Desativada"), "")));
        }
        return Section("cpu", Loc.T("Processador"), [.. lines]);
    }

    private static InfoSection Memory(RawSystem r, SmbiosData? smbios)
    {
        var lines = new List<InfoLine?>();
        // A SMBIOS enxerga os slots vazios; sem ela, vale o WMI (só os pentes presentes)
        if (smbios is { Devices.Count: > 0 })
        {
            var installed = smbios.Devices.Where(d => d.SizeBytes > 0).ToList();
            int slots = smbios.Arrays.Sum(a => a.Slots);
            if (slots < smbios.Devices.Count) slots = smbios.Devices.Count;
            lines.Add(Line(Loc.T("Instalada"), Units.Bytes(installed.Sum(d => d.SizeBytes))));
            lines.Add(Line(Loc.T("Slots"), Loc.F("{0} no total · {1} ocupados · {2} livres", slots, installed.Count, slots - installed.Count)));
            long max = smbios.Arrays.Sum(a => a.MaxCapacityBytes);
            lines.Add(Line(Loc.T("Capacidade máxima da placa"), max > 0 ? Units.Bytes(max) : ""));
            string ecc = smbios.Arrays.Select(a => a.Ecc).FirstOrDefault(e => e.Length > 0) ?? "";
            lines.Add(Line(Loc.T("Correção de erros"), ecc.Length > 0 ? Loc.T(ecc) : ""));
            foreach (var d in smbios.Devices)
            {
                string label = d.Slot.Length > 0 ? d.Slot : Loc.T("Slot");
                if (d.Bank.Length > 0 && !label.Contains(d.Bank, StringComparison.OrdinalIgnoreCase)) label += $" ({d.Bank})";
                lines.Add(new InfoLine(label, d.SizeBytes == 0 ? Loc.T("Vazio") : Module(d.SizeBytes, d.Type, d.FormFactor, d.SpeedMHz, d.ConfiguredMHz, d.Manufacturer, d.PartNumber)));
            }
        }
        else
        {
            lines.Add(Line(Loc.T("Instalada"), r.Modules.Count > 0 ? Units.Bytes(r.Modules.Sum(m => m.Bytes)) : ""));
            if (r.MemSlots > 0) lines.Add(Line(Loc.T("Slots"), Loc.F("{0} no total · {1} ocupados · {2} livres", r.MemSlots, r.Modules.Count, Math.Max(0, r.MemSlots - r.Modules.Count))));
            lines.Add(Line(Loc.T("Capacidade máxima da placa"), r.MemMaxBytes > 0 ? Units.Bytes(r.MemMaxBytes) : ""));
            foreach (var m in r.Modules)
                lines.Add(new InfoLine(m.Slot.Length > 0 ? m.Slot : Loc.T("Pente"), Module(m.Bytes, m.Type, m.FormFactor, m.SpeedMHz, m.ConfiguredMHz, m.Manufacturer, m.PartNumber)));
        }
        return Section("mem", Loc.T("Memória"), [.. lines]);
    }

    private static string Module(long bytes, string type, string form, int speed, int configured, string maker, string part)
    {
        var bits = new List<string> { Units.Bytes(bytes) };
        string kind = string.Join(" ", new[] { type, form.Length > 0 ? Loc.T(form) : "" }.Where(s => s.Length > 0));
        if (kind.Length > 0) bits.Add(kind);
        if (speed > 0) bits.Add(configured > 0 && configured != speed ? $"{speed} MHz ({Loc.T("em uso")} {configured})" : $"{speed} MHz");
        string who = string.Join(" ", new[] { maker, part }.Where(s => s.Length > 0 && !s.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) && s != "Undefined"));
        if (who.Length > 0) bits.Add(who);
        return string.Join(" · ", bits);
    }

    private static InfoSection Board(RawSystem r) =>
        Section("board", Loc.T("Placa-mãe e BIOS"),
            Line(Loc.T("Placa-mãe"), string.Join(" ", new[] { r.BoardMaker, r.BoardProduct }.Where(s => s.Length > 0))), Line(Loc.T("Revisão"), r.BoardVersion),
            Line("BIOS", string.Join(" ", new[] { r.BiosVendor, r.BiosVersion }.Where(s => s.Length > 0))), Line(Loc.T("Data da BIOS"), r.BiosDate));

    private static InfoSection Video(RawSystem r)
    {
        var lines = new List<InfoLine?>();
        for (int i = 0; i < r.Gpus.Count; i++)
        {
            var g = r.Gpus[i];
            string p = r.Gpus.Count > 1 ? $" {i + 1}" : "";
            lines.Add(Line(Loc.T("Placa de vídeo") + p, g.Name));
            lines.Add(Line(Loc.T("Memória de vídeo") + p, g.VramBytes > 0 ? Units.Bytes(g.VramBytes) : ""));
            lines.Add(Line(Loc.T("Driver") + p, string.Join(" · ", new[] { g.Driver, g.DriverDate }.Where(s => s.Length > 0))));
            lines.Add(Line(Loc.T("Resolução") + p, g.Resolution));
        }
        return Section("gpu", Loc.T("Vídeo"), [.. lines]);
    }

    private static InfoSection Storage(RawSystem r)
    {
        var lines = new List<InfoLine?>();
        foreach (var d in r.Disks)
        {
            var bits = new List<string>();
            if (d.SizeBytes > 0) bits.Add(Units.Bytes(d.SizeBytes));
            string kind = string.Join(" ", new[] { d.Media, d.Bus }.Where(s => s.Length > 0 && s != "Unspecified"));
            if (kind.Length > 0) bits.Add(kind);
            if (d.Health.Length > 0) bits.Add($"{Loc.T("saúde")}: {d.Health}");
            if (d.WearPct is { } w) bits.Add($"{Loc.T("desgaste")} {w:0}%");
            if (d.TempC is { } t && SystemInfoBuilder.PlausibleTemp(t)) bits.Add($"{t:0} °C");
            lines.Add(new InfoLine(d.Name.Length > 0 ? d.Name : Loc.T("Disco"), string.Join(" · ", bits)));
        }
        if (lines.Count > 0)
            lines.Add(Line(Loc.T("Previsão de falha (SMART)"), r.PredictFailure switch
            {
                true => Loc.T("Algum disco prevê falha: faça backup agora"), false => Loc.T("Nenhum disco prevê falha"), _ => Loc.T("Não informada (exige administrador)"),
            }));
        return Section("disk", Loc.T("Armazenamento"), [.. lines]);
    }

    private static InfoSection Devices(RawSystem r) =>
        Section("devices", Loc.T("Áudio, ópticos e periféricos"),
            Line(Loc.T("Áudio"), List(r.Audio)), Line(Loc.T("Unidades ópticas"), List(r.Optical)),
            Line("USB", List(r.Usb)), Line("Bluetooth", List(r.Bluetooth)));

    private static InfoSection Network(IReadOnlyList<NetAdapter> net, WifiInfo? wifi)
    {
        var lines = new List<InfoLine?>();
        foreach (var a in net)
        {
            var bits = new List<string>();
            bits.Add(a.Up ? Loc.T("conectado") : Loc.T("desconectado"));
            if (a.SpeedMbps > 0) bits.Add(a.SpeedMbps >= 1000 ? $"{a.SpeedMbps / 1000.0:0.#} Gbps" : $"{a.SpeedMbps} Mbps");
            if (a.Mac.Length > 0) bits.Add(a.Mac);
            lines.Add(new InfoLine(a.Name, $"{a.Description} · {string.Join(" · ", bits)}"));
            if (a.Ips.Count > 0) lines.Add(new InfoLine("  IP", string.Join(", ", a.Ips)));
            if (a.Gateway.Length > 0) lines.Add(new InfoLine("  " + Loc.T("Gateway"), a.Gateway));
            if (a.Dns.Count > 0) lines.Add(new InfoLine("  DNS", string.Join(", ", a.Dns)));
        }
        if (wifi is not null)
        {
            lines.Add(Line(Loc.T("Wi-Fi: rede"), wifi.Ssid));
            lines.Add(Line(Loc.T("Wi-Fi: sinal"), wifi.SignalPct > 0 ? $"{wifi.SignalPct}%" : ""));
            lines.Add(Line(Loc.T("Wi-Fi: faixa"), wifi.Channel > 0 ? $"{(wifi.Band.Length > 0 ? wifi.Band + " · " : "")}{Loc.T("canal")} {wifi.Channel}" : wifi.Band));
            lines.Add(Line(Loc.T("Wi-Fi: padrão"), wifi.Standard));
            lines.Add(Line(Loc.T("Wi-Fi: velocidade"), wifi.RxMbps > 0 ? $"{wifi.RxMbps} / {wifi.TxMbps} Mbps" : ""));
        }
        return Section("net", Loc.T("Rede"), [.. lines]);
    }

    /// <summary>Faixa do Wi-Fi pelo número do canal. Os canais do 6 GHz repetem os números dos outros, então só 2,4 e 5 GHz são afirmados.</summary>
    public static string BandOfChannel(int channel) => channel switch { >= 1 and <= 14 => "2,4 GHz", >= 36 and <= 177 => "5 GHz", _ => "" };

    /// <summary>Temperaturas que o Windows deixa ler sem driver de terceiros: zona térmica, discos e placa NVIDIA. Sem temperatura por núcleo da CPU.</summary>
    public static InfoSection Temperatures(RawSystem r, IReadOnlyList<RawTemp> extra)
    {
        var lines = new List<InfoLine>();
        foreach (var t in r.Temps.Concat(extra).Where(t => PlausibleTemp(t.Celsius)))
            lines.Add(new InfoLine($"{t.Name}{(t.Source.Length > 0 ? $" ({t.Source})" : "")}", $"{t.Celsius:0} °C"));
        foreach (var d in r.Disks.Where(d => d.TempC is { } x && PlausibleTemp(x)))
            lines.Add(new InfoLine(d.Name, $"{d.TempC!.Value:0} °C"));
        return new InfoSection("temp", Loc.T("Temperaturas"), lines);
    }
}

// ---------- exportação ----------

public static class SystemInfoExport
{
    public static string Text(IEnumerable<InfoSection> sections, string machine, DateTime when, string footer)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Loc.T("Informações do sistema")} · {machine} · {when:yyyy-MM-dd HH:mm}");
        foreach (var s in sections)
        {
            sb.AppendLine().AppendLine(s.Title).AppendLine(new string('-', s.Title.Length));
            foreach (var l in s.Lines) sb.AppendLine($"{l.Label}: {l.Value}");
        }
        sb.AppendLine().AppendLine(footer);
        return sb.ToString();
    }

    public static string Html(IEnumerable<InfoSection> sections, string machine, DateTime when, string footer)
    {
        // Só o que quebra o HTML; os acentos ficam legíveis (o arquivo é UTF-8)
        string E(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"" + E(Loc.Culture.Name) + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n<title>" + E(Loc.T("Informações do sistema")) + " · Notebook Doctor</title>\n");
        sb.Append("""
<style>
:root{--bg:#fff;--card:#f2f2f9;--on:#121212;--muted:#555;--primary:#00188f}
@media(prefers-color-scheme:dark){:root{--bg:#121212;--card:#1e1e1e;--on:#fff;--muted:#b8b8b8;--primary:#00bcf2}}
body{font:15px/1.5 "Segoe UI",system-ui,sans-serif;background:var(--bg);color:var(--on);margin:0;padding:24px 16px}
main{max-width:860px;margin:0 auto}
h1{font-size:24px;margin:0 0 4px} p.sub{color:var(--muted);margin:0 0 20px}
section{background:var(--card);border-radius:12px;padding:16px 20px;margin:0 0 16px}
h2{font-size:17px;margin:0 0 8px;color:var(--primary)}
dl{margin:0;display:grid;grid-template-columns:minmax(120px,max-content) 1fr;gap:4px 16px}
dt{color:var(--muted)} dd{margin:0;overflow-wrap:anywhere}
footer{color:var(--muted);font-size:13px;margin-top:24px}
</style></head><body><main>
""");
        sb.Append($"<h1>{E(Loc.T("Informações do sistema"))}</h1><p class=\"sub\">{E(machine)} · {when:yyyy-MM-dd HH:mm}</p>\n");
        foreach (var s in sections)
        {
            sb.Append($"<section><h2>{E(s.Title)}</h2><dl>");
            foreach (var l in s.Lines) sb.Append($"<dt>{E(l.Label)}</dt><dd>{E(l.Value)}</dd>");
            sb.Append("</dl></section>\n");
        }
        sb.Append($"<footer>{E(footer)}</footer></main></body></html>\n");
        return sb.ToString();
    }

    public static List<(PdfStyle Style, string Text)> PdfLines(IEnumerable<InfoSection> sections, string machine, DateTime when, string footer)
    {
        var lines = new List<(PdfStyle, string)> { (PdfStyle.Title, Loc.T("Informações do sistema")), (PdfStyle.Subtitle, $"{machine} · {when:yyyy-MM-dd HH:mm}") };
        foreach (var s in sections)
        {
            lines.Add((PdfStyle.Heading, s.Title));
            foreach (var l in s.Lines) lines.Add((PdfStyle.Text, $"{l.Label}: {l.Value}"));
        }
        lines.Add((PdfStyle.Small, footer));
        return lines;
    }

    /// <summary>Resumo curto para colar numa conversa ou chamado: sistema, processador, memória, vídeo e discos.</summary>
    public static string Summary(IEnumerable<InfoSection> sections, string machine)
    {
        var list = sections.ToList();
        string Value(string key, int line = 0) => list.FirstOrDefault(s => s.Key == key) is { } sec && sec.Lines.Count > line ? sec.Lines[line].Value : "";
        string Find(string key, string label) => list.FirstOrDefault(s => s.Key == key)?.Lines.FirstOrDefault(l => l.Label == label)?.Value ?? "";
        var sb = new StringBuilder();
        sb.AppendLine($"{machine} · {Find("os", Loc.T("Windows"))} {Find("os", Loc.T("Versão"))}".Trim());
        foreach (var (label, text) in new[]
        {
            (Loc.T("Processador"), Find("cpu", Loc.T("Processador"))),
            (Loc.T("Memória"), Find("mem", Loc.T("Instalada")) + (Find("mem", Loc.T("Slots")) is { Length: > 0 } sl ? $" ({sl})" : "")),
            (Loc.T("Placa-mãe"), Find("board", Loc.T("Placa-mãe"))),
            (Loc.T("Vídeo"), string.Join(" · ", new[] { Find("gpu", Loc.T("Placa de vídeo")), Find("gpu", Loc.T("Memória de vídeo")) }.Where(s => s.Length > 0))),
        })
            if (text.Length > 0) sb.AppendLine($"{label}: {text}");
        foreach (var d in list.FirstOrDefault(s => s.Key == "disk")?.Lines ?? []) sb.AppendLine($"{Loc.T("Disco")}: {d.Label} · {d.Value}");
        _ = Value("os");
        return sb.ToString().TrimEnd();
    }
}
