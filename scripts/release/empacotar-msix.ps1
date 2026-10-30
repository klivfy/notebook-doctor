<#
.SYNOPSIS
    Compila o Notebook Doctor na edição da Microsoft Store (STORE) e gera os pacotes .msix / .msixbundle.

.PARAMETER Versao
    Versão semântica do pacote (padrão lida do .csproj).

.PARAMETER PublisherId
    Identificador do editor atribuído pelo Partner Center (ex: "CN=A1B2C3D4-E5F6-...").

.PARAMETER PackageIdentityName
    Nome da identidade do pacote reservado no Partner Center (padrão: "KlivfyTecnologia.NotebookDoctor").

.PARAMETER Saida
    Diretório de saída para os arquivos .msix e .msixbundle.

.PARAMETER Arquiteturas
    Lista de arquiteturas a compilar e empacotar (x64, arm64).

.PARAMETER PularCompilacao
    Pula o 'dotnet publish' e usa os binários existentes em publish-store/<arch>.
#>
[CmdletBinding()]
param(
    [string]$Versao,
    [string]$PublisherId = "CN=REPLACE_WITH_PARTNER_CENTER_PUBLISHER_ID",
    [string]$PackageIdentityName = "KlivfyTecnologia.NotebookDoctor",
    [string]$Saida = "msix-out",
    [string[]]$Arquiteturas = @("x64", "arm64"),
    [switch]$PularCompilacao
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Versao)) {
    $projXml = [xml](Get-Content "$repoRoot\src\NotebookDoctor\NotebookDoctor.csproj")
    $Versao = $projXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Versao) { $Versao = "1.1.0" }
}

$fourPartVersion = $Versao
if ($fourPartVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    $parts = $fourPartVersion.Split('.')
    while ($parts.Count -lt 4) { $parts += "0" }
    $fourPartVersion = [string]::Join('.', $parts[0..3])
}

Write-Host "=== Empacotamento Microsoft Store (MSIX) ===" -ForegroundColor Cyan
Write-Host "Versão do pacote: $fourPartVersion"
Write-Host "Publisher ID: $PublisherId"
Write-Host "Package Identity: $PackageIdentityName"
Write-Host "Arquiteturas: $([string]::Join(', ', $Arquiteturas))"

# Localizar MakeAppx.exe
$makeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    $cmd = Get-Command "makeappx.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $makeAppx = $cmd.Source }
}

if (-not $makeAppx -or -not (Test-Path $makeAppx)) {
    throw "MakeAppx.exe não foi encontrado nos Windows Kits. Instale o Windows 10/11 SDK."
}

Write-Host "MakeAppx: $makeAppx" -ForegroundColor Green

$saidaFull = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Saida))
if (Test-Path $saidaFull) { Remove-Item $saidaFull -Recurse -Force }
New-Item -ItemType Directory -Path $saidaFull -Force | Out-Null

$manifestTemplate = Get-Content "$repoRoot\packaging\store\AppxManifest.xml" -Raw -Encoding UTF8
$assetsSrc = "$repoRoot\packaging\store\Assets"

$generatedPackages = @()

foreach ($arch in $Arquiteturas) {
    Write-Host "`n--- Processando arquitetura: $arch ---" -ForegroundColor Yellow
    $publishDir = Join-Path $repoRoot "publish-store\$arch"

    if (-not $PularCompilacao) {
        Write-Host "Publicando NotebookDoctor para win-$arch (STORE)..."
        if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
        dotnet publish "$repoRoot\src\NotebookDoctor\NotebookDoctor.csproj" `
            -c Release `
            -r "win-$arch" `
            --self-contained true `
            -p:StoreBuild=true `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou para win-$arch" }
    }

    $layoutDir = Join-Path $saidaFull "layout-$arch"
    New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null
    Copy-Item "$publishDir\*" -Destination $layoutDir -Recurse -Force

    # Copiar Assets
    $layoutAssets = Join-Path $layoutDir "Assets"
    New-Item -ItemType Directory -Path $layoutAssets -Force | Out-Null
    Copy-Item "$assetsSrc\*" -Destination $layoutAssets -Recurse -Force

    # Customizar AppxManifest
    $archManifest = $manifestTemplate `
        -replace 'Name="KlivfyTecnologia\.NotebookDoctor"', "Name=`"$PackageIdentityName`"" `
        -replace 'Publisher="CN=REPLACE_WITH_PARTNER_CENTER_PUBLISHER_ID"', "Publisher=`"$PublisherId`"" `
        -replace 'Version="1\.1\.0\.0"', "Version=`"$fourPartVersion`"" `
        -replace 'ProcessorArchitecture="neutral"', "ProcessorArchitecture=`"$arch`""

    $manifestDest = Join-Path $layoutDir "AppxManifest.xml"
    [System.IO.File]::WriteAllText($manifestDest, $archManifest, [System.Text.UTF8Encoding]::new($false))

    $msixPath = Join-Path $saidaFull "NotebookDoctor-$arch.msix"
    Write-Host "Criando pacote MSIX em: $msixPath"
    & $makeAppx pack /d $layoutDir /p $msixPath /nv /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack falhou para $arch" }

    $generatedPackages += $msixPath
    # Limpa layout temporário
    Remove-Item $layoutDir -Recurse -Force
}

Write-Host "`nPacotes MSIX gerados com sucesso:" -ForegroundColor Green
foreach ($pkg in $generatedPackages) {
    $info = Get-Item $pkg
    Write-Host ("  {0} ({1:N2} MB)" -f $info.Name, ($info.Length / 1MB))
}

Write-Host "`nPara enviar ao Microsoft Partner Center, anexe os pacotes .msix gerados em '$Saida'." -ForegroundColor Cyan

