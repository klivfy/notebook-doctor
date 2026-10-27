using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>
/// Fase 11, leva 11.1: bateria em watts (WMI de energia, o mesmo que o relatório de bateria do Windows usa), o turbo do
/// processador no plano de energia ativo (API documentada da powrprof.dll, sem driver) e a taxa de atualização da tela.
/// </summary>
public static class PowerProbe
{
    // ---------- bateria ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Leitura da bateria agora; null sem bateria (desktop) ou quando o Windows não respondeu.</summary>
    public static BatteryReading? ReadBattery()
    {
        if (!GetSystemPowerStatus(out var sps) || sps.BatteryFlag == 128 /* sem bateria */ || sps.BatteryFlag == 255 && sps.BatteryLifePercent == 255) return null;
        var status = WmiReader.Query("SELECT PowerOnline, Charging, Discharging, ChargeRate, DischargeRate, RemainingCapacity FROM BatteryStatus WHERE Active = TRUE", @"root\wmi");
        var full = WmiReader.Query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\wmi");
        var design = WmiReader.Query("SELECT DesignedCapacity FROM BatteryStaticData", @"root\wmi");
        double remaining = status.Sum(s => WmiReader.Num(s, "RemainingCapacity")) / 1000.0;
        double fullWh = full.Sum(s => WmiReader.Num(s, "FullChargedCapacity")) / 1000.0;
        double designWh = design.Sum(s => WmiReader.Num(s, "DesignedCapacity")) / 1000.0;
        double rate = status.Sum(s => WmiReader.Bool(s, "Charging") == true ? WmiReader.Num(s, "ChargeRate") : -WmiReader.Num(s, "DischargeRate")) / 1000.0;
        bool onAc = sps.AcLineStatus == 1 || status.Any(s => WmiReader.Bool(s, "PowerOnline") == true);
        bool charging = status.Any(s => WmiReader.Bool(s, "Charging") == true);
        int pct = sps.BatteryLifePercent <= 100 ? sps.BatteryLifePercent : fullWh > 0 ? (int)Math.Round(remaining * 100 / fullWh) : 0;
        // Alguns firmwares mandam taxa de descarga sem sinal na tomada: na tomada e sem carregar, 0 W é o honesto
        if (onAc && !charging && rate < 0 && status.All(s => WmiReader.Bool(s, "Discharging") != true)) rate = 0;
        return new BatteryReading(DateTime.Now, onAc, charging, Math.Round(rate, 1), remaining, fullWh, designWh, pct);
    }

    // ---------- turbo do processador (Modo Colo) ----------

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    private static readonly Guid SubEnergySaver = new("de830923-a562-41af-a086-e3a2c6bad2da");
    private static readonly Guid EnergySaverThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(p); }
        finally { LocalFree(p); }
    }

    private static (uint Ac, uint Dc)? Read(Guid scheme, Guid sub, Guid setting)
    {
        if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint ac) != 0) return null;
        if (PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint dc) != 0) return null;
        return (ac, dc);
    }

    private static bool Write(Guid scheme, Guid sub, Guid setting, uint ac, uint dc)
    {
        if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac) != 0) return false;
        if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc) != 0) return false;
        // O plano ativo só usa o valor novo depois de ser ativado de novo
        return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    /// <summary>Turbo do plano ativo (na tomada e na bateria); null quando o Windows não deixa ler.</summary>
    public static (Guid Scheme, uint Ac, uint Dc)? ReadBoost() =>
        ActiveScheme() is { } s && Read(s, SubProcessor, PerfBoostMode) is { } v ? (s, v.Ac, v.Dc) : null;

    public static bool WriteBoost(Guid scheme, uint ac, uint dc) => Write(scheme, SubProcessor, PerfBoostMode, ac, dc);

    /// <summary>Limite do economizador de bateria (%) no plano ativo: com 100, ele liga na hora na bateria.</summary>
    public static (Guid Scheme, uint Ac, uint Dc)? ReadSaverThreshold() =>
        ActiveScheme() is { } s && Read(s, SubEnergySaver, EnergySaverThreshold) is { } v ? (s, v.Ac, v.Dc) : null;

    public static bool WriteSaverThreshold(Guid scheme, uint ac, uint dc) => Write(scheme, SubEnergySaver, EnergySaverThreshold, ac, dc);

    // ---------- taxa de atualização da tela principal ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DevMode dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string? device, ref DevMode dm, IntPtr hwnd, uint flags, IntPtr param);

    private const int EnumCurrent = -1;
    private const uint DmDisplayFrequency = 0x400000;

    private static DevMode NewMode() => new() { DeviceName = "", FormName = "", Size = (ushort)Marshal.SizeOf<DevMode>() };

    /// <summary>Taxa atual da tela principal e as que ela aceita na mesma resolução; null quando não deu para ler.</summary>
    public static (int Current, IReadOnlyList<int> Supported)? RefreshRates()
    {
        var cur = NewMode();
        if (!EnumDisplaySettingsW(null, EnumCurrent, ref cur)) return null;
        var rates = new SortedSet<int>();
        var m = NewMode();
        for (int i = 0; EnumDisplaySettingsW(null, i, ref m) && i < 2000; i++)
        {
            if (m.PelsWidth == cur.PelsWidth && m.PelsHeight == cur.PelsHeight && m.BitsPerPel == cur.BitsPerPel && m.DisplayFrequency > 1)
                rates.Add((int)m.DisplayFrequency);
            m = NewMode();
        }
        return ((int)cur.DisplayFrequency, rates.ToList());
    }

    /// <summary>
    /// Muda a taxa da tela principal só para esta sessão (sem gravar no registro): reiniciar o computador também volta ao
    /// valor de antes.
    /// </summary>
    public static bool SetRefreshRate(int hz)
    {
        var dm = NewMode();
        if (!EnumDisplaySettingsW(null, EnumCurrent, ref dm)) return false;
        if (dm.DisplayFrequency == hz) return true;
        dm.DisplayFrequency = (uint)hz;
        dm.Fields = DmDisplayFrequency;
        return ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero) == 0;
    }

    // ---------- tema escuro dos aplicativos ----------

    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Apps e Windows no tema claro (1) ou escuro (0); null quando a chave não existe.</summary>
    public static (int? Apps, int? System) ReadLightTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Personalize);
        return (k?.GetValue("AppsUseLightTheme") as int?, k?.GetValue("SystemUsesLightTheme") as int?);
    }

    public static void WriteLightTheme(int? apps, int? system)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Personalize);
        if (apps is { } a) k.SetValue("AppsUseLightTheme", a, RegistryValueKind.DWord);
        if (system is { } s) k.SetValue("SystemUsesLightTheme", s, RegistryValueKind.DWord);
    }
}
