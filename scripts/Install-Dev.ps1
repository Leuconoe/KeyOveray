[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'KeyOverlay.sln'
$packageOutputRoot = Join-Path $repositoryRoot 'KeyOverlay.Widget\AppPackages'
$debugOutputRoot = Join-Path $repositoryRoot 'KeyOverlay.Widget\bin\x64\Debug'
$stagingDirectory = Join-Path $debugOutputRoot 'DevDeploy'

function Find-MSBuild {
    $knownPaths = @(
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe')
    )

    foreach ($knownPath in $knownPaths) {
        if (Test-Path -LiteralPath $knownPath) {
            return $knownPath
        }
    }

    $vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vsWherePath) {
        $discoveredPath = & $vsWherePath -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if ($discoveredPath) {
            return $discoveredPath
        }
    }

    throw 'Visual Studio MSBuild를 찾지 못했습니다. UWP 및 C++ 데스크톱 개발 워크로드를 설치하세요.'
}

function Find-MakeAppx {
    $windowsKitBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $windowsKitBin -Recurse -File -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw 'Windows SDK의 MakeAppx 도구를 찾지 못했습니다.'
    }
    return $candidate.FullName
}

if (-not $SkipBuild) {
    $msbuildPath = Find-MSBuild
    & $msbuildPath $solutionPath '/restore' '/m' '/p:Configuration=Debug' '/p:Platform=x64' '/v:minimal'
    if ($LASTEXITCODE -ne 0) {
        throw "빌드에 실패했습니다. MSBuild 종료 코드: $LASTEXITCODE"
    }
}

$debugPackageDirectory = Get-ChildItem -LiteralPath $packageOutputRoot -Directory -Filter '*_x64_Debug_Test' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $debugPackageDirectory) {
    throw '디버그 종속성 패키지 폴더를 찾지 못했습니다. -SkipBuild 없이 다시 실행하세요.'
}

$debugPackage = Get-ChildItem -LiteralPath $debugPackageDirectory.FullName -File -Filter '*_x64_Debug.msix' |
    Select-Object -First 1
if (-not $debugPackage) {
    throw '디버그 MSIX 패키지를 찾지 못했습니다.'
}

$resolvedDebugOutput = [System.IO.Path]::GetFullPath($debugOutputRoot).TrimEnd('\') + '\'
$resolvedStaging = [System.IO.Path]::GetFullPath($stagingDirectory)
if (-not $resolvedStaging.StartsWith($resolvedDebugOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '개발 배포 폴더가 예상한 빌드 출력 경로 밖에 있습니다.'
}

$stagingProcesses = Get-Process -Name 'KeyOverlay.Widget', 'InputBridge' -ErrorAction SilentlyContinue |
    Where-Object {
        try { $_.Path.StartsWith($resolvedStaging, [System.StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    }
foreach ($stagingProcess in $stagingProcesses) {
    Stop-Process -Id $stagingProcess.Id -Force
}
if ($stagingProcesses) {
    Start-Sleep -Milliseconds 300
}

if (Test-Path -LiteralPath $resolvedStaging) {
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedStaging | Out-Null

$makeAppxPath = Find-MakeAppx
& $makeAppxPath unpack /p $debugPackage.FullName /d $resolvedStaging /o
if ($LASTEXITCODE -ne 0) {
    throw "MSIX 개발 배포 레이아웃 생성에 실패했습니다. MakeAppx 종료 코드: $LASTEXITCODE"
}

$manifestPath = Join-Path $resolvedStaging 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "개발 배포용 매니페스트가 없습니다: $manifestPath"
}

$dependencySpecs = @(
    @{
        Name = 'Microsoft.VCLibs.140.00.Debug'
        File = 'Dependencies\x64\Microsoft.VCLibs.x64.Debug.14.00.appx'
    },
    @{
        Name = 'Microsoft.NET.CoreRuntime.2.2'
        File = 'Dependencies\x64\Microsoft.NET.CoreRuntime.2.2.appx'
    },
    @{
        Name = 'Microsoft.NET.CoreFramework.Debug.2.2'
        File = 'Dependencies\x64\Microsoft.NET.CoreFramework.Debug.2.2.appx'
    }
)

foreach ($dependency in $dependencySpecs) {
    $installedDependency = Get-AppxPackage -Name $dependency.Name |
        Where-Object { $_.Architecture -eq 'X64' } |
        Select-Object -First 1
    if (-not $installedDependency) {
        $dependencyPath = Join-Path $debugPackageDirectory.FullName $dependency.File
        if (-not (Test-Path -LiteralPath $dependencyPath)) {
            throw "필수 패키지를 찾지 못했습니다: $dependencyPath"
        }
        Add-AppxPackage -Path $dependencyPath
    }
}

$existingPackage = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget' | Select-Object -First 1
if ($existingPackage) {
    $existingLocation = [System.IO.Path]::GetFullPath($existingPackage.InstallLocation).TrimEnd('\')
    if (-not $existingLocation.Equals($resolvedStaging.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-AppxPackage -Package $existingPackage.PackageFullName
    }
}

Add-AppxPackage -Register $manifestPath -ForceApplicationShutdown

$registeredPackage = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget'
if (-not $registeredPackage) {
    throw 'Key Overlay 패키지 등록을 확인하지 못했습니다.'
}

Write-Host ''
Write-Host 'Key Overlay 등록 완료.' -ForegroundColor Green
Write-Host 'Win + G를 누르고 위젯 메뉴에서 Key Overlay를 연 뒤 핀을 켜세요.'
Write-Host '게임 위에서 버튼을 누르려면 클릭 통과는 꺼진 상태여야 합니다.'
