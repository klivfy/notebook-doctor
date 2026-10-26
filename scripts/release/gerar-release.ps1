<#
.SYNOPSIS
  Gera, assina e confere os arquivos de uma versão do Notebook Doctor e escreve o versao.json (A50).
.DESCRIPTION
  1. Publica o app autocontido (x64 e arm64).
  2. Assina os executáveis, constrói o instalador com eles (installer\NotebookDoctor.iss, Inno Setup) e assina o instalador com signtool
     (SHA-256, carimbo de tempo RFC 3161), conferindo cada assinatura. Com -Instalador, usa um instalador já pronto em vez de construir.
  3. Confere que a chave de quem assinou está em PublisherSignature.PinnedKeys (quando a lista não está vazia).
  4. Calcula o SHA-256 do instalador JÁ ASSINADO (assinar muda o arquivo, então o hash vem depois) e escreve o versao.json.

  O app só instala se o hash do arquivo baixado bater com o do versao.json, a assinatura for válida e o certificado for da Klivfy.
  Este script NÃO envia nada ao site: publicar o instalador e o versao.json é um passo manual da Klivfy.
  Precisa do Windows, do SDK do .NET 8, do signtool (Windows SDK) e do PowerShell 7. A chave privada nunca sai do repositório de certificados.
.EXAMPLE
  pwsh scripts/release/gerar-release.ps1 -Versao 1.2.0 -Impressao 0123ABCD...
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Versao,
    [string]$Instalador,                              # opcional: instalador já construído, sem assinatura; sem ele, o script constrói o de installer\NotebookDoctor.iss
    [Parameter(Mandatory)][string]$Impressao,         # impressão digital (SHA-1) do certificado no repositório do Windows
    [string]$CarimboDeTempo = 'http://timestamp.digicert.com',
    [string]$Saida = (Join-Path $PSScriptRoot "..\..\publish\release\$Versao"),
    [string]$Notas = ''
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use o PowerShell 7 (pwsh).' }
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source
if (-not $signtool) { throw 'signtool.exe não encontrado (instale o Windows SDK e abra o Prompt do Desenvolvedor).' }
if ($Instalador -and -not (Test-Path -LiteralPath $Instalador)) { throw "Instalador não encontrado: $Instalador" }
New-Item -ItemType Directory -Force -Path $Saida | Out-Null

foreach ($rid in 'x64', 'arm64') {
    dotnet publish "$raiz\src\NotebookDoctor\NotebookDoctor.csproj" -c Release -r "win-$rid" --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Versao -o "$Saida\$rid"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($rid) falhou." }
}

$setup = Join-Path $Saida "NotebookDoctor-Setup-$Versao.exe"

# O instalador carrega os executáveis, então eles são assinados antes de entrar nele; o instalador é assinado depois
function Assinar([string]$f) {
    & $signtool sign /sha1 $Impressao /fd SHA256 /tr $CarimboDeTempo /td SHA256 /d 'Notebook Doctor' $f
    if ($LASTEXITCODE -ne 0) { throw "Assinatura falhou: $f" }
    & $signtool verify /pa /v $f
    if ($LASTEXITCODE -ne 0) { throw "A assinatura não passou na conferência: $f" }
}
if ($Instalador) {
    Copy-Item -LiteralPath $Instalador -Destination $setup -Force
    $alvos = @("$Saida\x64\NotebookDoctor.exe", "$Saida\arm64\NotebookDoctor.exe", $setup)
} else {
    Assinar "$Saida\x64\NotebookDoctor.exe"
    Assinar "$Saida\arm64\NotebookDoctor.exe"
    & "$PSScriptRoot\construir-instalador.ps1" -Versao $Versao -OrigemX64 "$Saida\x64" -OrigemArm64 "$Saida\arm64" -Saida $Saida | Out-Null
    $alvos = @($setup)
}

foreach ($f in $alvos) { Assinar $f }

# A chave de quem assinou precisa ser uma das fixadas no código; senão o app recusaria a atualização
$fonte = Get-Content -Raw -LiteralPath "$raiz\src\NotebookDoctor.Core\PublisherSignature.cs"
$fixadas = [regex]::Matches(($fonte -replace '(?s)^.*PinnedKeys\s*=', ''), '"([0-9A-Fa-f]{64})"') | ForEach-Object { $_.Groups[1].Value.ToUpperInvariant() }
$cert = (Get-AuthenticodeSignature -LiteralPath $setup).SignerCertificate
$chave = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cert.PublicKey.ExportSubjectPublicKeyInfo()))
if ($fixadas.Count -eq 0) { Write-Warning "PublisherSignature.PinnedKeys está vazia. Preencha com: $chave" }
elseif ($fixadas -notcontains $chave) { throw "A chave do certificado ($chave) não está em PinnedKeys: o app recusaria esta atualização." }

$sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash
$manifesto = [ordered]@{
    version = $Versao
    url     = "https://klivfy.com/notebookdoctor/downloads/NotebookDoctor-Setup-$Versao.exe"
    sha256  = $sha
    notes   = $Notas
}
$json = $manifesto | ConvertTo-Json
[System.IO.File]::WriteAllText("$Saida\versao.json", $json, [System.Text.UTF8Encoding]::new($false))

"Pronto em $Saida"
"SHA-256 do instalador: $sha"
"Publique o instalador em ...\downloads\ e o versao.json em https://klivfy.com/notebookdoctor/versao.json"
