namespace NotebookDoctor.Services;

/// <summary>
/// B26: dados fictícios para o modo demonstração (--demo): apresentações, capturas de tela e treinamento,
/// sem mostrar programas nem números do computador real. Tudo é determinístico: o mesmo horário gera o mesmo dado.
/// </summary>
public static class DemoData
{
    public const double RamTotalGB = 16, DiskTotalGB = 476;

    // Nome, processos, CPU média (%), memória (MB)
    private static readonly (string Name, int Count, double Cpu, long Mem)[] Apps =
    [
        ("chrome", 18, 9, 1900), ("Teams", 6, 4, 950), ("Code", 9, 3, 780), ("Outlook", 1, 1.5, 520),
        ("WhatsApp", 3, 0.8, 420), ("Spotify", 4, 1.2, 310), ("explorer", 1, 0.6, 140), ("MsMpEng", 1, 1.0, 210),
        ("OneDrive", 1, 0.4, 120), ("SearchIndexer", 1, 0.3, 60), ("dwm", 1, 1.4, 90), ("svchost", 74, 1.8, 980),
    ];

    /// <summary>Uma leitura ao vivo. <paramref name="tick"/> conta as leituras desde que a demonstração abriu.</summary>
    public static Snapshot Sample(DateTime time, int tick)
    {
        var rnd = new Random(tick);
        double wave = Math.Sin(tick / 7.0);
        // De tempos em tempos o Chrome fica pesado por alguns minutos, para mostrar o aviso de programa pesado
        bool chromeBusy = tick % 160 is >= 40 and < 120;
        var groups = Apps.Select(a =>
        {
            double cpu = a.Cpu * (0.6 + rnd.NextDouble() * 0.8);
            if (a.Name == "chrome" && chromeBusy) cpu = 27 + rnd.NextDouble() * 6;
            long mem = a.Mem + (long)(rnd.NextDouble() * 60);
            if (a.Name == "Teams") mem += Math.Min(tick, 600) * 3L; // cresce por ~30 min: aparece como suspeita de vazamento de memória
            return new ProcGroup(a.Name, a.Count, Math.Round(cpu, 1), mem, Math.Round(rnd.NextDouble() * 400 * a.Cpu), rnd.Next(0, 12));
        }).ToList();

        double cpuTotal = Math.Clamp(groups.Sum(g => g.CpuPct) + 6 + 5 * wave, 3, 100);
        double ramPct = Math.Clamp(74 + 3 * Math.Sin(tick / 23.0) + Math.Min(tick, 600) * 0.01, 0, 92);
        return new Snapshot(time, Math.Round(cpuTotal, 1), Math.Round(ramPct, 1), Math.Round(RamTotalGB * ramPct / 100, 1), RamTotalGB,
            DiskBusy: Math.Round(4 + 6 * Math.Abs(wave) + rnd.NextDouble() * 3, 1), DiskFreePct: 18.4, DiskFreeGB: Math.Round(DiskTotalGB * 0.184),
            Groups: groups, CpuPerf: 88 + rnd.Next(0, 10), TempC: 58 + 6 * Math.Abs(wave), NetDownKBs: 120 + rnd.Next(0, 300), NetUpKBs: 20 + rnd.Next(0, 60));
    }

    public static HealthInfo Health() => new(
        "SSD NVMe 512 GB", "SSD", "Healthy", DiskWearPct: 7, DiskTempC: 41,
        BatteryHealthPct: 83, BatteryCycles: 312, BootSeconds: 38, BootAvgSeconds: 31,
        RebootPending: false, OnAc: true, ActivePlan: "Equilibrado", HighPlanGuid: null,
        StartupEnabled: 14, TempBytes: 2_400_000_000L, DriverErrors: [], Unsigned: [],
        Security: new SecurityInfo(true, 1, [], []),
        VirtualDisks: [new VirtualDisk("Ubuntu", @"C:\Users\demo\AppData\Local\Packages\Ubuntu\LocalState\ext4.vhdx", 26_000_000_000L)]);

    /// <summary>
    /// Sete dias de histórico, uma amostra a cada <paramref name="stepMinutes"/> minutos, só com o PC ligado
    /// (dias úteis 8h–19h, fim de semana 10h–14h). O disco enche devagar e tem uma limpeza no meio da semana.
    /// </summary>
    public static IEnumerable<(Snapshot Snap, int Score)> Week(DateTime now, int stepMinutes = 2)
    {
        var start = now.Date.AddDays(-7);
        int i = 0;
        for (var t = start; t < now.AddMinutes(-5); t = t.AddMinutes(stepMinutes), i++)
        {
            bool weekend = t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (t.Hour < (weekend ? 10 : 8) || t.Hour >= (weekend ? 14 : 19)) continue;

            var s = Sample(t, i);
            // Pico de reuniões às 10h e às 15h; manhã de segunda pesada (atualizações)
            double load = (t.Hour is 10 or 15 ? 22 : 0) + (t.DayOfWeek == DayOfWeek.Monday && t.Hour < 11 ? 18 : 0);
            double days = (t - start).TotalDays;
            double free = days < 3.5 ? 21 - days * 0.9 : 24 - (days - 3.5) * 0.9; // limpeza no meio da semana
            s = s with
            {
                Cpu = Math.Clamp(Math.Round(s.Cpu + load, 1), 0, 100),
                RamPct = Math.Clamp(Math.Round(s.RamPct - i * 0.01 + load / 3, 1), 0, 100),
                DiskFreePct = Math.Round(free, 1),
                DiskFreeGB = Math.Round(DiskTotalGB * free / 100),
            };
            int score = (int)Math.Clamp(Math.Round(92 - s.Cpu * 0.35 - Math.Max(0, s.RamPct - 70) * 0.8), 40, 98);
            yield return (s, score);
        }
    }

    /// <summary>Inventário de um notebook fictício para a aba "Do fabricante". O número de série é inventado.</summary>
    public static HardwareInventory OemInventory() => new(
        new ChassisInfo("Dell Inc.", "Latitude 5440", "DEMO123", "0C08", "Latitude 5440", true),
        new BiosInfo("Dell Inc.", "1.12.0", new DateTime(2024, 3, 12)),
        [
            new GpuInfo("Intel(R) Iris(R) Xe Graphics", SupportVendor.Intel, "31.0.101.4502", OemRules.ParseDeviceId(@"PCI\VEN_8086&DEV_A7A1&SUBSYS_0C081028&REV_04\3&11583659&0&10"), false),
            new GpuInfo("NVIDIA GeForce MX550", SupportVendor.Nvidia, "31.0.15.3640", OemRules.ParseDeviceId(@"PCI\VEN_10DE&DEV_1F9F&SUBSYS_0C081028&REV_A1\4&2B8260C3&0&0008"), true),
        ],
        [new NetworkAdapterInfo("Intel(R) Wi-Fi 6E AX211 160MHz", SupportVendor.Intel, "23.30.0.6", OemRules.ParseDeviceId(@"PCI\VEN_8086&DEV_51F1&SUBSYS_00948086&REV_01\3&11583659&0&A3"))],
        ["Realtek Audio", "Intel(R) Display Audio"]);
}
