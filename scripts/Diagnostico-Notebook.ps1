<#
.SYNOPSIS
  Diagnostica lentidao no Windows e (opcionalmente) aplica correcoes seguras.

.EXAMPLE
  .\Diagnostico-Notebook.ps1                       # so le e gera relatorio (nada e alterado)
  .\Diagnostico-Notebook.ps1 -Corrigir             # relatorio + limpeza segura (pede confirmacao)
  .\Diagnostico-Notebook.ps1 -Corrigir -Silencioso # sem perguntas (usado no agendamento)
  .\Diagnostico-Notebook.ps1 -Profundo             # inclui DISM/SFC (admin, demora)
  .\Diagnostico-Notebook.ps1 -InstalarAgendamento  # cria tarefa semanal (relatorio + limpeza de temporarios)
  .\Diagnostico-Notebook.ps1 -RemoverAgendamento
#>
[CmdletBinding()]
param(
    [switch]$Corrigir,
    [switch]$Silencioso,
    [switch]$Profundo,
    [switch]$InstalarAgendamento,
    [switch]$RemoverAgendamento,
    [int]$DiasTemp = 7
)

$ErrorActionPreference = 'SilentlyContinue'
$TaskName  = 'Diagnostico-Notebook-Semanal'
$PastaRel  = Join-Path $PSScriptRoot 'relatorios'
$IsAdmin   = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# ---------- agendamento ----------
if ($RemoverAgendamento) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Tarefa '$TaskName' removida." ; return
}
if ($InstalarAgendamento) {
    $acao = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -Corrigir -Silencioso"
    $gat  = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At 12:00
    $cfg  = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName $TaskName -Action $acao -Trigger $gat -Settings $cfg -Description 'Relatorio semanal de desempenho + limpeza de temporarios' -Force | Out-Null
    Write-Host "Tarefa '$TaskName' criada (domingo 12:00, ou na proxima vez que ligar). Relatorios em: $PastaRel" ; return
}

# ---------- infra de relatorio ----------
$Achados = New-Object System.Collections.Generic.List[object]
function Add-Achado([string]$Nivel, [string]$Titulo, [string]$Detalhe, [string]$Solucao = '') {
    $Achados.Add([pscustomobject]@{ Nivel = $Nivel; Titulo = $Titulo; Detalhe = $Detalhe; Solucao = $Solucao })
}
function Secao($t) { Write-Host "`n== $t ==" -ForegroundColor Cyan }
function MB($b) { [math]::Round($b / 1MB) }

function Get-ArquivosAntigos([string]$Pasta, [int]$Dias) {
    $corte = (Get-Date).AddDays(-$Dias)
    Get-ChildItem -LiteralPath $Pasta -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt $corte }
}

# ---------- 1. sistema ----------
Secao 'Sistema'
$os  = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$cs  = Get-CimInstance Win32_ComputerSystem
$ramTotalGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$ramUsoPct  = [math]::Round(100 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize * 100))
$uptimeDias = [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalDays, 1)
Write-Host ("{0} {1} | {2} | RAM {3} GB ({4}% em uso) | Ligado ha {5} dias | Build {6}" -f $cs.Manufacturer, $cs.Model, $cpu.Name.Trim(), $ramTotalGB, $ramUsoPct, $uptimeDias, $os.BuildNumber)

if ($uptimeDias -ge 7)  { Add-Achado 'ATENCAO' 'Computador ligado ha muito tempo' "$uptimeDias dias sem reiniciar" 'Reinicie (nao use so "desligar" se o Inicio Rapido estiver ativo: use Reiniciar).' }
if ($ramUsoPct -ge 85)  { Add-Achado 'PROBLEMA' 'Memoria RAM quase cheia' "$ramUsoPct% em uso de $ramTotalGB GB" 'Feche abas/apps pesados (veja a lista abaixo) ou aumente a RAM.' }
elseif ($ramUsoPct -ge 75) { Add-Achado 'ATENCAO' 'Memoria RAM alta' "$ramUsoPct% em uso de $ramTotalGB GB" 'Reduza abas do navegador / apps em segundo plano.' }

$rebootPend = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')
if ($rebootPend) { Add-Achado 'ATENCAO' 'Reinicio pendente' 'O Windows Update esta aguardando reinicio' 'Reinicie o computador.' }

$self = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsSelfHost\Applicability'
if ($self.BranchName -and $self.BranchName -notmatch 'Release|^$') {
    Add-Achado 'ATENCAO' 'Windows em canal de pre-lancamento (Insider)' "Canal: $($self.BranchName) | Build $($os.BuildNumber)" 'Builds Insider podem ter bugs de desempenho. Considere sair do programa Insider (Configuracoes > Windows Update > Programa Windows Insider).'
}

# ---------- 2. disco ----------
Secao 'Discos'
Get-PhysicalDisk | ForEach-Object {
    Write-Host ("{0} | {1} | saude: {2}" -f $_.FriendlyName, $_.MediaType, $_.HealthStatus)
    if ($_.HealthStatus -ne 'Healthy') { Add-Achado 'PROBLEMA' "Disco com problema: $($_.FriendlyName)" "Saude: $($_.HealthStatus)" 'Faca backup AGORA e verifique o disco (chkdsk / fabricante).' }
    if ($_.MediaType -eq 'HDD') { Add-Achado 'ATENCAO' 'Disco rigido mecanico (HDD)' $_.FriendlyName 'Trocar por SSD e a melhoria de velocidade mais impactante possivel.' }
}
Get-Volume | Where-Object { $_.DriveLetter -and $_.Size -gt 0 } | ForEach-Object {
    $livrePct = [math]::Round($_.SizeRemaining / $_.Size * 100)
    Write-Host ("{0}: {1} GB livres de {2} GB ({3}%)" -f $_.DriveLetter, [math]::Round($_.SizeRemaining / 1GB, 1), [math]::Round($_.Size / 1GB, 1), $livrePct)
    if ($livrePct -lt 10)     { Add-Achado 'PROBLEMA' "Disco $($_.DriveLetter): quase cheio" "$livrePct% livre" 'Libere espaco (Configuracoes > Sistema > Armazenamento).' }
    elseif ($livrePct -lt 20) { Add-Achado 'ATENCAO'  "Pouco espaco em $($_.DriveLetter):" "$livrePct% livre" 'Mantenha ao menos 20% livre em SSD.' }
}

# ---------- 3. carga ao vivo ----------
Secao 'Carga atual (amostra de ~6s)'
$cpuS = 1..6 | ForEach-Object { Start-Sleep -Milliseconds 1000; (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime }
$dskS = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'"
$cpuMedia = [math]::Round(($cpuS | Measure-Object -Average).Average)
Write-Host "CPU media: $cpuMedia% | Disco ocupado: $($dskS.PercentDiskTime)% | Fila de disco: $($dskS.CurrentDiskQueueLength)"
if ($cpuMedia -ge 70)      { Add-Achado 'PROBLEMA' 'CPU sobrecarregada' "$cpuMedia% de uso medio" 'Veja os processos que mais consomem CPU abaixo.' }
elseif ($cpuMedia -ge 40)  { Add-Achado 'ATENCAO'  'CPU com carga alta em repouso' "$cpuMedia% de uso medio" 'Algo esta consumindo CPU em segundo plano (veja a lista).' }
if ($dskS.PercentDiskTime -ge 80) { Add-Achado 'PROBLEMA' 'Disco saturado' "$($dskS.PercentDiskTime)% ocupado" 'Veja quem esta lendo/gravando (Gerenciador de Tarefas > Disco). Pode ser antivirus, indexacao ou update.' }

# CPU por processo (amostra 5s) agrupado por nome
$n = [Environment]::ProcessorCount
$a = Get-Process | Select-Object Id, Name, @{n = 't'; e = { $_.TotalProcessorTime.TotalSeconds } }
Start-Sleep 5
$b = Get-Process | Select-Object Id, Name, @{n = 't'; e = { $_.TotalProcessorTime.TotalSeconds } }
$cpuProc = foreach ($p in $b) { $o = $a | Where-Object Id -eq $p.Id | Select-Object -First 1; if ($o) { [pscustomobject]@{ Name = $p.Name; Pct = ($p.t - $o.t) / 5 / $n * 100 } } }
$topCpu = $cpuProc | Group-Object Name | ForEach-Object { [pscustomobject]@{ Programa = $_.Name; 'CPU %' = [math]::Round(($_.Group | Measure-Object Pct -Sum).Sum, 1); Processos = $_.Count } } | Sort-Object 'CPU %' -Descending | Select-Object -First 6
Write-Host "`nTop CPU:"; $topCpu | Format-Table -AutoSize | Out-String | Write-Host
$topCpu | Where-Object { $_.'CPU %' -ge 10 } | ForEach-Object { Add-Achado 'ATENCAO' "Programa consumindo CPU: $($_.Programa)" "$($_.'CPU %')% da CPU ($($_.Processos) processos)" 'Feche ou reinicie esse programa; em navegadores, feche abas/extensoes pesadas.' }

$topMem = Get-Process | Group-Object Name | ForEach-Object { [pscustomobject]@{ Programa = $_.Name; MB = MB(($_.Group | Measure-Object WorkingSet64 -Sum).Sum); Processos = $_.Count } } | Sort-Object MB -Descending | Select-Object -First 8
Write-Host "Top memoria:"; $topMem | Format-Table -AutoSize | Out-String | Write-Host
$topMem | Where-Object { $_.MB -ge 1500 } | ForEach-Object { Add-Achado 'ATENCAO' "Programa usando muita RAM: $($_.Programa)" "$($_.MB) MB em $($_.Processos) processos" 'Feche abas/janelas que nao usa ou reinicie o programa.' }

# ---------- 4. energia ----------
Secao 'Energia'
$plano = (powercfg /getactivescheme) -replace '.*\((.*)\).*', '$1'
Write-Host "Plano ativo: $plano"
if ($plano -match 'conomia|saver') { Add-Achado 'PROBLEMA' 'Plano de energia em Economia' $plano 'Mude para Equilibrado/Alto desempenho (Configuracoes > Sistema > Energia > Modo de energia).' }
try {
    $full = (Get-CimInstance -Namespace root/wmi -ClassName BatteryFullChargedCapacity -ErrorAction Stop).FullChargedCapacity
    $proj = (Get-CimInstance -Namespace root/wmi -ClassName BatteryStaticData -ErrorAction Stop).DesignedCapacity
    if ($full -and $proj) {
        $saude = [math]::Round($full / $proj * 100)
        Write-Host "Saude da bateria: $saude% da capacidade original"
        if ($saude -lt 60) { Add-Achado 'ATENCAO' 'Bateria desgastada' "$saude% da capacidade original" 'Sem tomada o notebook reduz o desempenho. Considere trocar a bateria.' }
    }
} catch { }

# ---------- 5. inicializacao ----------
Secao 'Programas que iniciam com o Windows'
$startup = Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location
$startup | Format-Table Name, Location -AutoSize | Out-String | Write-Host
if (@($startup).Count -gt 10) { Add-Achado 'ATENCAO' 'Muitos programas na inicializacao' "$(@($startup).Count) itens" 'Gerenciador de Tarefas > Aplicativos de inicializacao: desative o que nao precisa.' }

# ---------- 6. lixo ----------
Secao 'Arquivos temporarios'
$pastasTemp = @($env:TEMP, "$env:LOCALAPPDATA\Temp", 'C:\Windows\Temp') | Select-Object -Unique | Where-Object { Test-Path $_ }
$antigos = $pastasTemp | ForEach-Object { Get-ArquivosAntigos $_ $DiasTemp }
$bytesLixo = ($antigos | Measure-Object Length -Sum).Sum
Write-Host ("Temporarios com mais de {0} dias: {1} MB em {2} arquivos" -f $DiasTemp, (MB $bytesLixo), @($antigos).Count)
if ($bytesLixo -gt 1GB) { Add-Achado 'ATENCAO' 'Muito lixo em pastas temporarias' "$(MB $bytesLixo) MB" 'Rode com -Corrigir para limpar com seguranca.' }
$lixeira = @((New-Object -ComObject Shell.Application).NameSpace(0xA).Items()).Count
Write-Host "Itens na Lixeira: $lixeira"

# ---------- 7. erros ----------
Secao 'Erros de hardware/disco nos ultimos 7 dias'
$erros = Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'disk', 'Ntfs', 'Microsoft-Windows-WHEA-Logger', 'stornvme', 'volmgr'; Level = 1, 2, 3; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 200
if ($erros) {
    $erros | Group-Object ProviderName | ForEach-Object { Write-Host ("{0}: {1} eventos" -f $_.Name, $_.Count) }
    Add-Achado 'PROBLEMA' 'Erros de disco/hardware registrados' "$(@($erros).Count) eventos (disk/Ntfs/WHEA)" 'Faca backup e rode "chkdsk /scan" e o diagnostico do fabricante.'
} else { Write-Host 'Nenhum erro de disco/hardware.' }

$defender = Get-MpComputerStatus
if ($defender -and -not $defender.RealTimeProtectionEnabled) { Add-Achado 'PROBLEMA' 'Protecao em tempo real desativada' 'Windows Defender' 'Reative em Seguranca do Windows.' }

# ---------- 8. correcoes ----------
if ($Corrigir) {
    Secao 'Aplicando correcoes seguras'
    $liberado = 0
    if ($antigos) {
        foreach ($f in $antigos) { try { $t = $f.Length; Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop; $liberado += $t } catch { } }
        Write-Host "Temporarios apagados: $(MB $liberado) MB (arquivos em uso foram ignorados)."
    }
    Clear-DnsClientCache; Write-Host 'Cache DNS limpo.'
    if ($IsAdmin) {
        Get-Volume | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' } | ForEach-Object {
            $d = Get-PhysicalDisk | Select-Object -First 1
            if ($d.MediaType -eq 'SSD') { Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim | Out-Null; Write-Host "TRIM executado em $($_.DriveLetter):" }
        }
    } else { Write-Host '(sem admin: TRIM e limpeza de C:\Windows\Temp parcial foram ignorados)' }
    if ($lixeira -gt 0 -and -not $Silencioso) {
        if ((Read-Host "Esvaziar a Lixeira ($lixeira itens)? Isso apaga definitivamente. [s/N]") -match '^[sSyY]') { Clear-RecycleBin -Force; Write-Host 'Lixeira esvaziada.' }
    }
}
if ($Profundo) {
    Secao 'Verificacao profunda (pode demorar)'
    if (-not $IsAdmin) { Write-Host 'Precisa de PowerShell como Administrador.' -ForegroundColor Yellow }
    else { DISM /Online /Cleanup-Image /ScanHealth; sfc /scannow }
}

# ---------- 9. resumo ----------
Secao 'RESUMO'
if ($Achados.Count -eq 0) { Write-Host 'Nenhum problema relevante encontrado.' -ForegroundColor Green }
$ordem = @{ PROBLEMA = 0; ATENCAO = 1; INFO = 2 }
$Achados | Sort-Object { $ordem[$_.Nivel] } | ForEach-Object {
    $cor = switch ($_.Nivel) { 'PROBLEMA' { 'Red' } 'ATENCAO' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0}] {1} - {2}" -f $_.Nivel, $_.Titulo, $_.Detalhe) -ForegroundColor $cor
    if ($_.Solucao) { Write-Host "    -> $($_.Solucao)" }
}

if (-not (Test-Path $PastaRel)) { New-Item -ItemType Directory -Path $PastaRel | Out-Null }
$arq = Join-Path $PastaRel ("relatorio-{0:yyyy-MM-dd_HHmm}.txt" -f (Get-Date))
$Achados | ForEach-Object { "[{0}] {1} - {2}`r`n    -> {3}" -f $_.Nivel, $_.Titulo, $_.Detalhe, $_.Solucao } | Set-Content -Path $arq -Encoding UTF8
Write-Host "`nRelatorio salvo em: $arq" -ForegroundColor DarkGray
