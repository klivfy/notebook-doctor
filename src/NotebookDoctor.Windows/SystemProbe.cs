using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 8.1 (S01, S02): as leituras de baixo nível da aba Sistema que o WMI não dá bem. Tudo só lê; cada função devolve vazio
/// em vez de lançar quando o Windows não oferece o dado (máquina virtual, sem Wi-Fi, sem placa NVIDIA).
/// </summary>
public static class SystemProbe
{
    // ---------- firmware ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint signature, uint tableId, IntPtr buffer, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out int type);

    private const uint Rsmb = 0x52534D42; // 'RSMB'

    /// <summary>A tabela SMBIOS crua, com o cabeçalho de 8 bytes do Windows. Null se o firmware não a entrega.</summary>
    public static byte[]? ReadSmbios()
    {
        try
        {
            uint size = GetSystemFirmwareTable(Rsmb, 0, IntPtr.Zero, 0);
            if (size == 0 || size > 4 * 1024 * 1024) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint got = GetSystemFirmwareTable(Rsmb, 0, buffer, size);
                if (got == 0 || got > size) return null;
                var bytes = new byte[got];
                Marshal.Copy(buffer, bytes, 0, (int)got);
                return bytes;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or OutOfMemoryException) { return null; }
    }

    /// <summary>"UEFI", "BIOS legado" ou "" quando o Windows não diz.</summary>
    public static string FirmwareKind()
    {
        try { return GetFirmwareType(out int t) ? t switch { 1 => "BIOS legado", 2 => "UEFI", _ => "" } : ""; }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return ""; }
    }

    // ---------- rede ----------

    public static List<NetAdapter> Adapters()
    {
        var list = new List<NetAdapter>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var ips = props.UnicastAddresses.Select(a => a.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork || (a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal))
                    .Select(a => a.ToString()).ToList();
                string gateway = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "";
                var dns = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
                long speed = nic.Speed > 0 && nic.Speed < 1_000_000_000_000 ? nic.Speed / 1_000_000 : 0;
                // O endereço MAC não entra: identifica a máquina e não ajuda a quem lê o relatório
                list.Add(new NetAdapter(nic.Name, nic.Description, nic.NetworkInterfaceType.ToString(), "", speed, nic.OperationalStatus == OperationalStatus.Up, ips, gateway, dns));
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { }
        return list.OrderByDescending(a => a.Up).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr reserved, out uint size, out IntPtr data, IntPtr type);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);

    private const int OpCurrentConnection = 7, OpChannelNumber = 8, StateConnected = 1;

    private static string Dot11Standard(int phy) => phy switch
    {
        4 => "802.11a", 5 => "802.11b", 6 => "802.11g", 7 => "802.11n (Wi-Fi 4)", 8 => "802.11ac (Wi-Fi 5)", 9 => "802.11ad", 10 => "802.11ax (Wi-Fi 6)", 11 => "802.11be (Wi-Fi 7)", _ => "",
    };

    /// <summary>
    /// A conexão Wi-Fi atual pela API Native Wifi (sem depender do texto do <c>netsh</c>, que muda com o idioma). Os deslocamentos dentro de
    /// WLAN_CONNECTION_ATTRIBUTES são fixos desde o Windows Vista; null se não há Wi-Fi ou ele está desconectado.
    /// </summary>
    public static WifiInfo? Wifi()
    {
        IntPtr handle = IntPtr.Zero, list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return null;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return null;
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                IntPtr entry = list + 8 + i * 532;                   // WLAN_INTERFACE_INFO: GUID (16) + descrição (512) + estado (4)
                var guid = Marshal.PtrToStructure<Guid>(entry);
                if (Marshal.ReadInt32(entry, 528) != StateConnected) continue;

                if (WlanQueryInterface(handle, ref guid, OpCurrentConnection, IntPtr.Zero, out _, out IntPtr data, IntPtr.Zero) != 0 || data == IntPtr.Zero) continue;
                try
                {
                    int ssidLen = Math.Clamp(Marshal.ReadInt32(data, 520), 0, 32);
                    var ssid = new byte[ssidLen];
                    Marshal.Copy(data + 524, ssid, 0, ssidLen);
                    int phy = Marshal.ReadInt32(data, 568), signal = Marshal.ReadInt32(data, 576);
                    int rx = Marshal.ReadInt32(data, 580) / 1000, tx = Marshal.ReadInt32(data, 584) / 1000;

                    int channel = 0;
                    if (WlanQueryInterface(handle, ref guid, OpChannelNumber, IntPtr.Zero, out _, out IntPtr ch, IntPtr.Zero) == 0 && ch != IntPtr.Zero)
                    {
                        channel = Marshal.ReadInt32(ch);
                        WlanFreeMemory(ch);
                    }
                    return new WifiInfo(Encoding.UTF8.GetString(ssid), Math.Clamp(signal, 0, 100), channel, SystemInfoBuilder.BandOfChannel(channel), Dot11Standard(phy), rx, tx);
                }
                finally { WlanFreeMemory(data); }
            }
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException) { return null; }
        finally
        {
            if (list != IntPtr.Zero) WlanFreeMemory(list);
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    // ---------- temperatura da placa NVIDIA ----------

    private delegate int NvmlInit();
    private delegate int NvmlShutdown();
    private delegate int NvmlCount(out uint count);
    private delegate int NvmlHandle(uint index, out IntPtr device);
    private delegate int NvmlName(IntPtr device, StringBuilder name, uint length);
    private delegate int NvmlTemp(IntPtr device, int sensor, out uint temp);

    /// <summary>
    /// Temperatura da GPU pela <c>nvml.dll</c> que o driver da NVIDIA instala (a mesma biblioteca do <c>nvidia-smi</c>). Sem a placa ou sem a
    /// biblioteca, lista vazia. Não há leitura da CPU por núcleo: exigiria um driver de kernel de terceiros.
    /// </summary>
    public static List<RawTemp> NvidiaTemperatures()
    {
        var result = new List<RawTemp>();
        IntPtr lib = IntPtr.Zero;
        bool started = false;
        NvmlShutdown? shutdown = null;
        try
        {
            string[] candidates = ["nvml.dll", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll")];
            if (!candidates.Any(c => NativeLibrary.TryLoad(c, out lib))) return result;
            T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));

            var init = Fn<NvmlInit>("nvmlInit_v2");
            shutdown = Fn<NvmlShutdown>("nvmlShutdown");
            if (init() != 0) return result;
            started = true;
            var count = Fn<NvmlCount>("nvmlDeviceGetCount_v2");
            var handle = Fn<NvmlHandle>("nvmlDeviceGetHandleByIndex_v2");
            var name = Fn<NvmlName>("nvmlDeviceGetName");
            var temp = Fn<NvmlTemp>("nvmlDeviceGetTemperature");
            if (count(out uint n) != 0) return result;
            for (uint i = 0; i < Math.Min(n, 4u); i++)
            {
                if (handle(i, out IntPtr dev) != 0) continue;
                var sb = new StringBuilder(96);
                if (name(dev, sb, 96) != 0) sb.Clear().Append("NVIDIA GPU");
                if (temp(dev, 0, out uint c) == 0) result.Add(new RawTemp("NVIDIA", sb.ToString(), c));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or ArgumentException or MarshalDirectiveException or AccessViolationException) { }
        finally
        {
            if (started) { try { shutdown?.Invoke(); } catch (Exception ex) when (ex is AccessViolationException or MarshalDirectiveException) { } }
            if (lib != IntPtr.Zero) NativeLibrary.Free(lib);
        }
        return result;
    }
}
