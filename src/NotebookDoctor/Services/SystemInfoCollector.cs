namespace NotebookDoctor.Services;

/// <summary>
/// Leva 8.1 (S01–S03): junta tudo o que a aba Sistema mostra. O WMI e o registro são lidos direto (leva 9.3; o PowerShell fica de reserva); a tabela SMBIOS, a rede, o
/// Wi-Fi e a NVIDIA vêm de chamadas diretas ao Windows. As fontes rodam em paralelo e nenhuma derruba as outras.
/// </summary>
public static class SystemInfoCollector
{
    public static async Task<List<InfoSection>> CollectAsync(string machine, CancellationToken ct = default)
    {
        var raw = Task.Run(SystemInfoScript.Read, ct);
        var smbios = Task.Run(() => SystemProbe.ReadSmbios() is { } b ? SmbiosParser.Parse(b) : null, ct);
        var net = Task.Run(SystemProbe.Adapters, ct);
        var wifi = Task.Run(SystemProbe.Wifi, ct);
        var nvidia = Task.Run(SystemProbe.NvidiaTemperatures, ct);
        var firmware = Task.Run(SystemProbe.FirmwareKind, ct);
        await Task.WhenAll(raw, smbios, net, wifi, nvidia, firmware);
        return SystemInfoBuilder.Build(raw.Result, smbios.Result, Loc.T(firmware.Result), net.Result, wifi.Result, nvidia.Result, machine);
    }
}
