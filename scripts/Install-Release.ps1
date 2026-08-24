[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$package = Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter 'KeyOverlay.Widget_*_x64.msix' |
    Select-Object -First 1
if (-not $package) {
    throw 'Key Overlay MSIX 파일을 찾지 못했습니다. 릴리즈 ZIP을 완전히 압축 해제했는지 확인하세요.'
}
if ($package.Name -notmatch '^KeyOverlay\.Widget_(?<version>\d+\.\d+\.\d+\.\d+)_x64\.msix$') {
    throw "MSIX 파일 이름에서 버전을 확인하지 못했습니다: $($package.Name)"
}
$releaseVersion = [version]$Matches.version

$certificatePath = Join-Path $PSScriptRoot 'KeyOverlayDeveloper.cer'
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw 'Key Overlay 서명 인증서를 찾지 못했습니다.'
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
$trustedCertificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\TrustedPeople' |
    Where-Object Thumbprint -eq $certificate.Thumbprint |
    Select-Object -First 1
if (-not $trustedCertificate) {
    Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
}

$dependencies = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Dependencies\x64') `
    -File -Filter '*.appx' -ErrorAction SilentlyContinue
if (-not $dependencies) {
    throw 'x64 런타임 종속성 패키지를 찾지 못했습니다.'
}

Get-Process -Name 'KeyOverlay.Widget', 'InputBridge' -ErrorAction SilentlyContinue |
    Stop-Process -Force

$existingPackages = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget'
foreach ($existingPackage in $existingPackages) {
    $isLoosePackage = -not $existingPackage.InstallLocation.StartsWith(
        (Join-Path $env:ProgramFiles 'WindowsApps'),
        [System.StringComparison]::OrdinalIgnoreCase)
    if ($isLoosePackage -or [version]$existingPackage.Version -eq $releaseVersion) {
        Remove-AppxPackage -Package $existingPackage.PackageFullName
    }
}

Add-AppxPackage -Path $package.FullName -DependencyPath $dependencies.FullName -ForceApplicationShutdown

$installedPackage = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget' | Select-Object -First 1
if (-not $installedPackage -or $installedPackage.Status -ne 'Ok') {
    throw 'Key Overlay 설치 상태를 확인하지 못했습니다.'
}

Get-Process -Name 'GameBar', 'GameBarFTServer' -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 500
Start-Process 'ms-gamebar:'

Write-Host ''
Write-Host "Key Overlay $($installedPackage.Version) 설치 완료." -ForegroundColor Green
Write-Host 'Win + G를 누르고 위젯 메뉴에서 Key Overlay를 연 뒤 핀을 켜세요.'
