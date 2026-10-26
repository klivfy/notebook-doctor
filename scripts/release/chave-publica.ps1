<#
.SYNOPSIS
  Mostra o SHA-256 da chave pública de um certificado de assinatura de código, no formato de PublisherSignature.PinnedKeys.
.DESCRIPTION
  Aceita o certificado público (.cer, .crt) ou um arquivo já assinado (.exe). Não precisa da chave privada nem de senha.
  O valor é o mesmo que o app calcula em Updater.IsSignedByPublisher: SHA-256 do SubjectPublicKeyInfo. Requer PowerShell 7.
.EXAMPLE
  pwsh scripts/release/chave-publica.ps1 -Arquivo klivfy-assinatura.cer
#>
param([Parameter(Mandatory)][string]$Arquivo)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use o PowerShell 7 (pwsh).' }
if (-not (Test-Path -LiteralPath $Arquivo)) { throw "Arquivo não encontrado: $Arquivo" }

if ($Arquivo -match '\.(exe|dll|msi)$') {
    $sig = Get-AuthenticodeSignature -LiteralPath $Arquivo
    if ($sig.Status -ne 'Valid') { throw "A assinatura de $Arquivo não é válida: $($sig.Status)" }
    $cert = $sig.SignerCertificate
} else {
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $Arquivo).Path)
}

$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cert.PublicKey.ExportSubjectPublicKeyInfo()))
"Assunto : $($cert.Subject)"
"Validade: $($cert.NotBefore:yyyy-MM-dd) a $($cert.NotAfter:yyyy-MM-dd)"
"PinnedKeys: $hash"
