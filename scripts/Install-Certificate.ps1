[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedThumbprint
)

$ErrorActionPreference = 'Stop'

$resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $resolvedCertificatePath)
if ($certificate.Thumbprint -ne $ExpectedThumbprint) {
    throw '인증서 지문이 설치 패키지와 일치하지 않습니다.'
}

Import-Certificate -FilePath $resolvedCertificatePath `
    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

$installedCertificate = Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' |
    Where-Object Thumbprint -eq $ExpectedThumbprint |
    Select-Object -First 1
if (-not $installedCertificate) {
    throw '인증서를 신뢰 저장소에 등록하지 못했습니다.'
}

Write-Host 'Key Overlay 인증서 등록 완료.' -ForegroundColor Green
