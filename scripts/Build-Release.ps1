[CmdletBinding()]
param(
    [string]$Tag = 'v1.1.0'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'KeyOverlay.sln'
$manifestPath = Join-Path $repositoryRoot 'KeyOverlay.Widget\Package.appxmanifest'
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$releaseName = "KeyOverlay-$Tag-x64"
$stagingDirectory = Join-Path $artifactRoot $releaseName

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
    throw 'Visual Studio MSBuild를 찾지 못했습니다.'
}

function Find-WindowsSdkTool([string]$name) {
    $windowsKitBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $windowsKitBin -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if (-not $tool) {
        throw "Windows SDK 도구를 찾지 못했습니다: $name"
    }
    return $tool.FullName
}

[xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
$identity = $manifest.Package.Identity
$packageVersion = [string]$identity.Version
$publisher = [string]$identity.Publisher
if ($Tag.TrimStart('v') -ne ([version]$packageVersion).ToString(3)) {
    throw "태그 $Tag 와 패키지 버전 $packageVersion 이 일치하지 않습니다."
}

$msbuildPath = Find-MSBuild
& $msbuildPath $solutionPath '/restore' '/m' '/p:Configuration=Release' '/p:Platform=x64' '/v:minimal'
if ($LASTEXITCODE -ne 0) {
    throw "Release 빌드에 실패했습니다. MSBuild 종료 코드: $LASTEXITCODE"
}

$packageDirectory = Join-Path $repositoryRoot "KeyOverlay.Widget\AppPackages\KeyOverlay.Widget_${packageVersion}_x64_Test"
$unsignedPackage = Join-Path $packageDirectory "KeyOverlay.Widget_${packageVersion}_x64.msix"
if (-not (Test-Path -LiteralPath $unsignedPackage)) {
    throw "Release MSIX를 찾지 못했습니다: $unsignedPackage"
}

$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
$resolvedStaging = [System.IO.Path]::GetFullPath($stagingDirectory)
if (-not $resolvedStaging.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '릴리즈 스테이징 경로가 artifacts 폴더 밖에 있습니다.'
}
if (Test-Path -LiteralPath $resolvedStaging) {
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedStaging | Out-Null

$signedPackage = Join-Path $resolvedStaging "KeyOverlay.Widget_${packageVersion}_x64.msix"
Copy-Item -LiteralPath $unsignedPackage -Destination $signedPackage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Release.ps1') -Destination (Join-Path $resolvedStaging 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination (Join-Path $resolvedStaging 'Uninstall.ps1')

$dependencySource = Join-Path $packageDirectory 'Dependencies\x64'
$dependencyDestination = Join-Path $resolvedStaging 'Dependencies\x64'
New-Item -ItemType Directory -Path $dependencyDestination -Force | Out-Null
Get-ChildItem -LiteralPath $dependencySource -File -Filter '*.appx' |
    Copy-Item -Destination $dependencyDestination

$signingCertificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' |
    Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddMonths(1) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $signingCertificate) {
    $certificateParameters = @{
        Type = 'Custom'
        Subject = $publisher
        FriendlyName = 'Key Overlay Release Signing'
        CertStoreLocation = 'Cert:\CurrentUser\My'
        KeyAlgorithm = 'RSA'
        KeyLength = 2048
        HashAlgorithm = 'SHA256'
        KeyUsage = 'DigitalSignature'
        KeyExportPolicy = 'Exportable'
        NotAfter = (Get-Date).AddYears(5)
        TextExtension = @(
            '2.5.29.37={text}1.3.6.1.5.5.7.3.3'
            '2.5.29.19={text}ca=FALSE'
        )
    }
    $signingCertificate = New-SelfSignedCertificate @certificateParameters
}

$certificatePath = Join-Path $resolvedStaging 'KeyOverlayDeveloper.cer'
Export-Certificate -Cert $signingCertificate -FilePath $certificatePath -Force | Out-Null

$signToolPath = Find-WindowsSdkTool 'signtool.exe'
& $signToolPath sign /fd SHA256 /sha1 $signingCertificate.Thumbprint /s My $signedPackage
if ($LASTEXITCODE -ne 0) {
    throw "MSIX 서명에 실패했습니다. SignTool 종료 코드: $LASTEXITCODE"
}
$signature = Get-AuthenticodeSignature -LiteralPath $signedPackage
$wrongSigner = $signature.SignerCertificate.Thumbprint -ne $signingCertificate.Thumbprint
$invalidSignatureStatus = $signature.Status -notin @('Valid', 'UnknownError')
if ($wrongSigner -or $invalidSignatureStatus) {
    throw "MSIX 서명자 검증에 실패했습니다. 상태: $($signature.Status)"
}

$releaseReadme = @"
Key Overlay $Tag (x64)

1. 이 ZIP을 완전히 압축 해제합니다.
2. PowerShell에서 다음 명령을 실행합니다.

   Set-ExecutionPolicy -Scope Process Bypass -Force
   .\Install.ps1

3. Win + G를 누르고 위젯 메뉴에서 Key Overlay를 연 뒤 핀을 켭니다.

Install.ps1은 UAC 관리자 권한을 요청하여 로컬 컴퓨터 TrustedPeople 저장소에
공개 서명 인증서를 등록하고,
Microsoft 서명 x64 런타임 종속성과 Key Overlay MSIX를 설치합니다.
제거하려면 Uninstall.ps1을 실행하세요.
"@
Set-Content -LiteralPath (Join-Path $resolvedStaging 'README.txt') -Value $releaseReadme -Encoding UTF8

$archivePath = Join-Path $artifactRoot "$releaseName.zip"
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $resolvedStaging '*') -DestinationPath $archivePath -CompressionLevel Optimal

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath
$hashPath = "$archivePath.sha256"
Set-Content -LiteralPath $hashPath -Value "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($archivePath))" -Encoding ASCII

Write-Host ''
Write-Host 'Release 빌드 생성 완료.' -ForegroundColor Green
Write-Host $archivePath
Write-Host $hashPath
