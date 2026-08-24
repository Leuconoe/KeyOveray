[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packages = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget'

if (-not $packages) {
    Write-Host '설치된 Key Overlay 패키지가 없습니다.'
    return
}

foreach ($package in $packages) {
    Remove-AppxPackage -Package $package.PackageFullName
}

Write-Host 'Key Overlay 제거 완료.' -ForegroundColor Green
