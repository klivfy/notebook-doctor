<#
.SYNOPSIS
  Constrói NotebookDoctor-Setup-<versão>.exe (Inno Setup) a partir dos executáveis já publicados para x64 e Arm64.
.DESCRIPTION
  Não assina nada. Em release, gerar-release.ps1 assina os executáveis ANTES de chamar este script (o instalador
  carrega os executáveis assinados) e assina o instalador depois.
  Precisa do Inno Setup 6.3 ou mais novo (ISCC.exe no PATH ou na pasta padrão); com -InstalarInno tenta instalá-lo pelo Chocolatey.
.EXAMPLE
  pwsh scripts/release/construir-instalador.ps1 -Versao 1.2.0 -OrigemX64 publish\x64 -OrigemArm64 publish\arm64
#>
param(
    [Parameter(Mandatory)][string]$Versao,
    [Parameter(Mandatory)][string]$OrigemX64,
    [Parameter(Mandatory)][string]$OrigemArm64,
    [string]$Saida = (Join-Path $PSScriptRoot '..\..\publish\instalador'),
    [switch]$InstalarInno
)

$ErrorActionPreference = 'Stop'
if ($Versao -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Versão inválida: $Versao (use 1.2.0)." }
foreach ($d in $OrigemX64, $OrigemArm64) {
    if (-not (Test-Path -LiteralPath (Join-Path $d 'NotebookDoctor.exe'))) { throw "NotebookDoctor.exe não encontrado em $d" }
}

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc -and $InstalarInno) {
    choco install innosetup -y --no-progress
    $iscc = Find-Iscc
}
if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) não encontrado. Instale em https://jrsoftware.org/isinfo.php ou use -InstalarInno.' }

New-Item -ItemType Directory -Force -Path $Saida | Out-Null
$script = Join-Path $PSScriptRoot '..\..\installer\NotebookDoctor.iss'
& $iscc "/DVersao=$Versao" "/DOrigemX64=$(Resolve-Path $OrigemX64)" "/DOrigemArm64=$(Resolve-Path $OrigemArm64)" "/DSaida=$(Resolve-Path $Saida)" $script
if ($LASTEXITCODE -ne 0) { throw 'O ISCC falhou.' }

$setup = Join-Path $Saida "NotebookDoctor-Setup-$Versao.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "O instalador não foi gerado: $setup" }
(Resolve-Path $setup).Path
