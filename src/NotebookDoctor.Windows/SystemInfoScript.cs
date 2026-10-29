using System.Diagnostics;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 8.1: o PowerShell que lê o WMI (CIM) e o registro para a aba Sistema. Fica aqui, e não no app, para a CI do Windows
/// poder rodá-lo de verdade: um erro de sintaxe no script esvaziaria a aba sem aviso.
/// </summary>
public static class SystemInfoScript
{
    public const string Text = """
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$ErrorActionPreference='SilentlyContinue'
function D($v){ if($v){ try{ ([datetime]$v).ToString('yyyy-MM-dd') }catch{ '' } } else { '' } }
$o=[ordered]@{}

$os=Get-CimInstance Win32_OperatingSystem
$cv=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$lic=Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'" | Select-Object -First 1
$sb=$null; try{ $sb=[bool](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -ErrorAction Stop).UEFISecureBootEnabled }catch{}
$ver="$($cv.DisplayVersion)"; if(-not $ver){ $ver="$($cv.ReleaseId)" }
$o.os=[ordered]@{ caption="$($os.Caption)"; version=$ver; build="$($cv.CurrentBuild).$($cv.UBR)"; arch="$($os.OSArchitecture)"; install=(D $os.InstallDate); boot=$(if($os.LastBootUpTime){$os.LastBootUpTime.ToString('yyyy-MM-dd HH:mm')}else{''}); lic=$(if($lic){[int]$lic.LicenseStatus}else{$null}); secureboot=$sb }
try{ $t=Get-CimInstance -Namespace root/cimv2/security/microsofttpm -ClassName Win32_Tpm -ErrorAction Stop; if($t){ $o.tpm=[ordered]@{ present=$true; version="$((("$($t.SpecVersion)") -split ',')[0])"; enabled=[bool]$t.IsEnabled_InitialValue } } else { $o.tpm=[ordered]@{ present=$false; version=''; enabled=$null } } }catch{}

$o.cpu=@(Get-CimInstance Win32_Processor | ForEach-Object { [ordered]@{ name="$($_.Name)".Trim(); cores=[int]$_.NumberOfCores; threads=[int]$_.NumberOfLogicalProcessors; mhz=[int]$_.MaxClockSpeed; cur=[int]$_.CurrentClockSpeed; l2kb=[int]$_.L2CacheSize; l3kb=[int]$_.L3CacheSize; virt=$(if($_.VirtualizationFirmwareEnabled -ne $null){[bool]$_.VirtualizationFirmwareEnabled}else{$null}) } })

$arr=Get-CimInstance Win32_PhysicalMemoryArray | Select-Object -First 1
$o.mem=[ordered]@{ slots=[int]$arr.MemoryDevices; maxkb=[int64]$arr.MaxCapacity; modules=@(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [ordered]@{ loc="$($_.DeviceLocator)"; bank="$($_.BankLabel)"; cap=[int64]$_.Capacity; speed=[int]$_.Speed; cfg=[int]$_.ConfiguredClockSpeed; mf="$($_.Manufacturer)".Trim(); pn="$($_.PartNumber)".Trim(); ff=''; type='' } }) }

$bb=Get-CimInstance Win32_BaseBoard
$o.board=[ordered]@{ mf="$($bb.Manufacturer)"; product="$($bb.Product)"; ver="$($bb.Version)" }
$bi=Get-CimInstance Win32_BIOS
$o.bios=[ordered]@{ vendor="$($bi.Manufacturer)"; ver="$($bi.SMBIOSBIOSVersion)"; date=(D $bi.ReleaseDate) }

# Memória de vídeo de verdade: o WMI (AdapterRAM) para em 4 GB; o driver grava o tamanho real no registro
$vram=@{}
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' | Where-Object { $_.PSChildName -match '^\d{4}$' } | ForEach-Object {
  $p=Get-ItemProperty $_.PSPath; $n="$($p.DriverDesc)"
  if($n){ $q=$p.'HardwareInformation.qwMemorySize'; if($q -eq $null){ $m=$p.'HardwareInformation.MemorySize'; if($m -is [byte[]]){ $q=[BitConverter]::ToUInt32($m,0) } else { $q=$m } }; if($q){ $vram[$n]=[int64]$q } } }
$o.gpu=@(Get-CimInstance Win32_VideoController | ForEach-Object { $n="$($_.Name)"; $v=$vram[$n]; if(-not $v){ $v=[int64]$_.AdapterRAM }
  [ordered]@{ name=$n; drv="$($_.DriverVersion)"; drvdate=(D $_.DriverDate); vram=$v; res=$(if($_.CurrentHorizontalResolution){"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution) @ $($_.CurrentRefreshRate) Hz"}else{''}) } })

$o.disk=@(Get-PhysicalDisk | ForEach-Object { $rc=$_ | Get-StorageReliabilityCounter
  [ordered]@{ name="$($_.FriendlyName)"; media="$($_.MediaType)"; bus="$($_.BusType)"; size=[int64]$_.Size; health="$($_.HealthStatus)"; temp=$(if($rc -and $rc.Temperature){[int]$rc.Temperature}else{$null}); wear=$(if($rc -and $rc.Wear -ne $null){[int]$rc.Wear}else{$null}) } })
try{ $pf=@(Get-CimInstance -Namespace root/wmi -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction Stop); if($pf.Count -gt 0){ $o.predict=(@($pf | Where-Object { $_.PredictFailure }).Count -gt 0) } }catch{}

$o.audio=@(Get-CimInstance Win32_SoundDevice | ForEach-Object { "$($_.Name)" })
$o.optical=@(Get-CimInstance Win32_CDROMDrive | ForEach-Object { "$($_.Caption)" })
$pnp=@(Get-CimInstance Win32_PnPEntity | Where-Object { $_.Status -eq 'OK' -and $_.PNPDeviceID })
$o.usb=@($pnp | Where-Object { $_.PNPDeviceID -like 'USB\VID_*' -and $_.PNPClass -notin @('USB','System') } | ForEach-Object { "$($_.Name)" } | Select-Object -Unique)
$o.bt=@($pnp | Where-Object { $_.PNPClass -eq 'Bluetooth' -and $_.Name -notmatch 'Enumerator|Protocol|Service|Transport' } | ForEach-Object { "$($_.Name)" } | Select-Object -Unique)

$o.thermal=@()
try{ Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop | ForEach-Object { $o.thermal += [ordered]@{ src='ACPI'; n=("$($_.InstanceName)" -replace '^.*\\',''); c=[math]::Round(($_.CurrentTemperature/10)-273.15,1) } } }catch{}
$o | ConvertTo-Json -Compress -Depth 6
""";

    /// <summary>
    /// Leva 9.3: lê o WMI direto e só roda o script do PowerShell quando a leitura direta falha ou não traz nem o nome do
    /// Windows. A aba fica pronta alguns segundos antes, e funciona onde a empresa bloqueou o PowerShell.
    /// </summary>
    public static RawSystem Read()
    {
        var native = ReadNative();
        return native.OsName.Length > 0 ? native : Run();
    }

    /// <summary>O retrato pelo WMI, sem PowerShell (até 2 minutos). Falha devolve um retrato vazio.</summary>
    public static RawSystem ReadNative()
    {
        try
        {
            var task = Task.Run(WmiReader.SystemInfoJson);
            return task.Wait(TimeSpan.FromMinutes(2)) ? SystemInfoParser.Parse(task.GetAwaiter().GetResult()) : new RawSystem();
        }
        catch (AggregateException ex) when (ex.InnerException is PlatformNotSupportedException or DllNotFoundException or TypeInitializationException
                                            or System.Runtime.InteropServices.COMException) { return new RawSystem(); }
        catch (System.Text.Json.JsonException) { return new RawSystem(); }
    }

    /// <summary>Roda o script (até 2 minutos) e lê o JSON. Qualquer falha devolve um retrato vazio: a aba mostra o que as outras fontes deram.</summary>
    public static RawSystem Run()
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Text));
            using var p = Process.Start(new ProcessStartInfo(SystemExe.PowerShell, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 });
            if (p is null) return new RawSystem();
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(120_000)) { try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } return new RawSystem(); }
            p.WaitForExit();
            return SystemInfoParser.Parse(output.GetAwaiter().GetResult().Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Text.Json.JsonException or InvalidOperationException) { return new RawSystem(); }
    }
}
