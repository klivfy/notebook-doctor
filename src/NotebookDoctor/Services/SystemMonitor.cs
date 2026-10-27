using System.Diagnostics;
using System.IO;

namespace NotebookDoctor.Services;

/// <summary>Coleta CPU, RAM, disco e processos sem depender de nomes de contadores localizados.</summary>
public sealed class SystemMonitor : IDisposable
{
    private readonly int _cores = Environment.ProcessorCount;
    private readonly string _systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
    private PerformanceCounter? _disk;
    private long _idle0, _kernel0, _user0;
    private Dictionary<int, TimeSpan> _prevCpu = new();
    private DateTime _prevTime = DateTime.UtcNow;

    private bool _diskInit;

    public SystemMonitor()
    {
        Native.GetSystemTimes(out _idle0, out _kernel0, out _user0);
    }

    /// <summary>Criar o contador de disco leva ~3 s no Windows; por isso é feito na primeira amostra (thread de fundo), não na abertura da janela.</summary>
    private void EnsureDiskCounter()
    {
        if (_diskInit) return;
        _diskInit = true;
        try { _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true); _disk.NextValue(); }
        catch { _disk = null; }
        try { _perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true); _perf.NextValue(); }
        catch { _perf = null; }
        try
        {
            // Muitos notebooks não expõem sensores ao Windows: nesse caso a categoria nem existe e a temperatura fica "indisponível"
            if (PerformanceCounterCategory.Exists("Thermal Zone Information"))
                foreach (var inst in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
                    _temps.Add(new PerformanceCounter("Thermal Zone Information", "Temperature", inst, readOnly: true));
        }
        catch { _temps.Clear(); }
    }

    private PerformanceCounter? _perf;
    private readonly List<PerformanceCounter> _temps = new();
    private Dictionary<int, ulong> _prevIo = new();
    private long _prevRx, _prevTx;

    public Snapshot Sample()
    {
        EnsureDiskCounter();
        // CPU total
        Native.GetSystemTimes(out var idle, out var kernel, out var user);
        double dTotal = (kernel - _kernel0) + (user - _user0), dIdle = idle - _idle0;
        double cpu = dTotal > 0 ? Math.Clamp((dTotal - dIdle) / dTotal * 100, 0, 100) : 0;
        (_idle0, _kernel0, _user0) = (idle, kernel, user);

        // RAM
        var mem = new Native.MEMORYSTATUSEX();
        Native.GlobalMemoryStatusEx(mem);
        double totalGB = mem.ullTotalPhys / 1073741824.0, usedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        // Disco
        double busy = 0;
        try { busy = Math.Clamp(_disk?.NextValue() ?? 0, 0, 100); } catch { }
        double freePct = 0, freeGB = 0;
        try
        {
            var d = new DriveInfo(_systemDrive);
            freeGB = d.AvailableFreeSpace / 1073741824.0;
            freePct = d.TotalSize > 0 ? d.AvailableFreeSpace * 100.0 / d.TotalSize : 0;
        }
        catch { }

        // Processos agrupados por nome
        var now = DateTime.UtcNow;
        double elapsed = Math.Max((now - _prevTime).TotalSeconds, 0.1);
        var newCpu = new Dictionary<int, TimeSpan>();
        var newIo = new Dictionary<int, ulong>();
        var conns = Native.TcpConnectionsByPid();
        var (windowed, hungPids) = Native.TopWindows();
        var groups = new Dictionary<string, (int n, double cpu, long mem, double io, int conns, bool hung, bool window)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;
                    double pct = 0;
                    try
                    {
                        var t = p.TotalProcessorTime;
                        newCpu[p.Id] = t;
                        if (_prevCpu.TryGetValue(p.Id, out var before))
                            pct = (t - before).TotalSeconds / elapsed / _cores * 100;
                    }
                    catch { /* processo protegido: sem tempo de CPU, mas ainda conta a memória */ }
                    double ioKBs = 0;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id); // 0 = sem acesso (processo protegido)
                    if (h != IntPtr.Zero)
                    {
                        try
                        {
                            if (Native.GetProcessIoCounters(h, out var io))
                            {
                                ulong total = io.ReadTransferCount + io.WriteTransferCount;
                                newIo[p.Id] = total;
                                if (_prevIo.TryGetValue(p.Id, out var before) && total >= before) ioKBs = (total - before) / 1024.0 / elapsed;
                            }
                        }
                        finally { Native.CloseHandle(h); }
                    }
                    var g = groups.GetValueOrDefault(p.ProcessName);
                    groups[p.ProcessName] = (g.n + 1, g.cpu + Math.Max(pct, 0), g.mem + p.WorkingSet64, g.io + ioKBs, g.conns + conns.GetValueOrDefault(p.Id),
                                             g.hung || hungPids.Contains(p.Id), g.window || windowed.Contains(p.Id));
                }
                catch { /* processo terminou durante a leitura */ }
            }
        }
        _prevCpu = newCpu;
        _prevIo = newIo;
        _prevTime = now;

        var list = groups.Select(kv => new ProcGroup(kv.Key, kv.Value.n, Math.Round(kv.Value.cpu, 1), kv.Value.mem / 1048576, Math.Round(kv.Value.io), kv.Value.conns,
                                                     kv.Value.hung, kv.Value.window)).ToList();

        // Rede total (todas as placas ativas)
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var st = ni.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
        }
        catch { }
        double down = _prevRx > 0 && rx >= _prevRx ? (rx - _prevRx) / 1024.0 / elapsed : 0;
        double up = _prevTx > 0 && tx >= _prevTx ? (tx - _prevTx) / 1024.0 / elapsed : 0;
        (_prevRx, _prevTx) = (rx, tx);
        double perf = -1;
        try { if (_perf is not null) perf = _perf.NextValue(); } catch { }
        double tempC = -1;
        try { if (_temps.Count > 0) tempC = _temps.Max(t => t.NextValue()) - 273.15; if (tempC is < 1 or > 130) tempC = -1; } catch { tempC = -1; }
        return new Snapshot(DateTime.Now, cpu, mem.dwMemoryLoad, usedGB, totalGB, busy, freePct, freeGB, list, perf, tempC, down, up);
    }

    public void Dispose() { _disk?.Dispose(); _perf?.Dispose(); foreach (var t in _temps) t.Dispose(); }
}
