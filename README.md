# Key Overlay

게임 위에 고정해 사용하는 개인용 키 입력 그리드입니다. 일반 최상위 창이 아니라 Xbox Game Bar 위젯으로 표시되므로, Game Bar가 지원되는 독점 전체화면 게임에서도 오버레이가 유지됩니다. 버튼을 누르면 패키지 안의 Win32 브리지가 키보드 스캔 코드 기반 `SendInput`을 Windows 전역 입력 큐에 발생시킵니다.

## 기능

- 기본 키 `1`, `2`, `3`, `4`, `5`, `F4`, `F10`
- 게임 탐지나 특정 게임 창 핸들에 의존하지 않는 Windows 전역 키 입력
- `Ctrl` / `Shift` / `Alt` / `Win` 조합
- 키 추가·삭제, 2–8열 설정, 드래그 순서 변경
- 0–100% 배경 투명도와 25–100% 버튼 투명도 개별 조절
- Game Bar 제목 표시줄을 이용한 이동과 핀 고정
- 한 줄로 접기 및 펼치기
- 수동 중앙 정렬
- 해상도, DPI, 디스플레이 방향 변경 시 크기 재계산 후 자동 중앙 정렬
- 그리드와 접기 상태 자동 저장
- 입력 브리지 중단 시 자동 재시작

## 릴리즈 설치

Visual Studio 없이 설치하려면 [최신 GitHub Release](https://github.com/Leuconoe/KeyOveray/releases/latest)의 `KeyOverlay-v1.1.3-x64.zip`을 사용합니다.

1. ZIP을 완전히 압축 해제합니다.
2. 압축을 푼 폴더에서 PowerShell을 **관리자 권한으로** 엽니다.
3. 다음 명령을 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Install.ps1
```

`Install.ps1`은 관리자 권한이 아니면 즉시 중단됩니다. 관리자 PowerShell에서 로컬 컴퓨터의 `TrustedPeople` 인증서 저장소에 Key Overlay 공개 서명 인증서를 등록하고, Microsoft 서명 x64 런타임 종속성과 Key Overlay MSIX를 설치합니다. Visual Studio는 필요하지 않습니다.

설치 후 `Win + G`를 누르고 위젯 메뉴에서 **Key Overlay**를 연 뒤 핀을 켭니다. 제거하려면 릴리즈 폴더의 `Uninstall.ps1`을 실행합니다.

릴리즈 ZIP의 무결성은 함께 제공되는 `.sha256` 파일과 비교할 수 있습니다.

```powershell
Get-FileHash -Algorithm SHA256 .\KeyOverlay-v1.1.3-x64.zip
```

## 소스 빌드 설치 요구 사항

소스에서 직접 빌드하는 개발 배포도 `x64` Windows 전용입니다.

1. Windows 10 버전 2004 이상 또는 Windows 11 x64
2. 최신 Xbox Game Bar
3. Windows 개발자 모드
4. Visual Studio 2026 또는 호환되는 Visual Studio Build Tools
5. 다음 Visual Studio 구성요소
   - UWP 개발 도구
   - C++ 데스크톱 개발 도구
   - MSVC v145 x64/x86 빌드 도구
   - Windows 11 SDK `10.0.26100.0`

### 1. Xbox Game Bar 확인

`Win + G`를 눌러 Game Bar가 열리는지 확인합니다. 열리지 않으면 다음을 확인합니다.

- Windows 설정 → 게임 → Xbox Game Bar에서 Game Bar 활성화
- Microsoft Store → 라이브러리에서 Xbox Game Bar 설치 또는 업데이트

### 2. Windows 개발자 모드 활성화

로컬의 서명되지 않은 개발 레이아웃을 등록하기 위해 필요합니다.

- Windows 11 최신 버전: 설정 → 시스템 → 고급 → 개발자용 → 개발자 모드
- 이전 Windows 11/Windows 10: 설정에서 `개발자 설정`을 검색한 뒤 개발자 모드 활성화

이 설정을 변경할 때 관리자 승인이 필요할 수 있습니다.

### 3. Visual Studio 구성 확인

Visual Studio Installer에서 설치된 Visual Studio의 `수정`을 누른 뒤 UWP와 C++ 데스크톱 개발 구성요소를 설치합니다. 프로젝트는 `Debug|x64`와 `Release|x64` 구성을 제공합니다.

## 소스에서 개발 설치

PowerShell을 열고 저장소 루트로 이동합니다. 경로에 특수문자가 포함될 수 있으므로 `Set-Location -LiteralPath` 사용을 권장합니다.

```powershell
Set-Location -LiteralPath 'D:\workspace\____personal____\KeyOveray'
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\Install-Dev.ps1
```

관리자 PowerShell은 일반적으로 필요하지 않습니다. 개발자 모드를 처음 활성화하거나 회사 정책으로 제한된 PC에서는 관리자 권한이 요구될 수 있습니다.

설치 스크립트는 다음 작업을 자동으로 수행합니다.

1. Visual Studio MSBuild 검색
2. `Debug|x64` NuGet 복원 및 빌드
3. Windows SDK의 MakeAppx 검색
4. 생성된 MSIX를 `KeyOverlay.Widget\bin\x64\Debug\DevDeploy`에 완전한 개발 레이아웃으로 전개
5. 필요한 x64 UWP/C++ 디버그 런타임이 없을 때만 설치
6. 이전 Key Overlay 개발 프로세스 종료
7. 현재 사용자 계정에 `KeyOverlay.GameBarWidget` 패키지 등록

성공하면 다음 문구가 표시됩니다.

```text
Key Overlay 등록 완료.
```

`runFullTrust`를 사용하는 UWP 프로젝트라는 `APPX0006` 빌드 경고가 한 번 표시될 수 있습니다. 설치 스크립트가 완전한 개발 레이아웃을 별도로 생성하므로 이 프로젝트의 로컬 개발 설치에는 영향을 주지 않습니다.

### 이미 빌드한 패키지로 재등록

소스 변경 없이 등록만 다시 할 때 사용할 수 있습니다.

```powershell
.\scripts\Install-Dev.ps1 -SkipBuild
```

소스나 아이콘을 수정했다면 `-SkipBuild`를 사용하지 않아야 새 MSIX가 생성됩니다.

## 설치 확인

다음 명령으로 패키지 상태와 실제 등록 위치를 확인합니다.

```powershell
Get-AppxPackage -Name 'KeyOverlay.GameBarWidget' |
    Select-Object Name, Version, Status, InstallLocation
```

정상 상태는 다음과 같습니다.

- `Status`: `Ok`
- 릴리즈 설치의 `InstallLocation`: `C:\Program Files\WindowsApps` 아래
- 개발 설치의 `InstallLocation`: 저장소 아래 `KeyOverlay.Widget\bin\x64\Debug\DevDeploy`

Game Bar 전용 아이콘도 확인할 수 있습니다.

```powershell
$package = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget'
Test-Path (Join-Path $package.InstallLocation 'GameBar\Icons\icon.targetsize-44.png')
```

결과가 `True`면 정상입니다.

## 첫 실행과 고정

1. `Win + G`를 누릅니다.
2. 상단의 위젯 메뉴를 엽니다.
3. **Key Overlay**를 선택합니다.
4. 위젯 제목 표시줄의 핀을 켭니다.
5. 버튼을 직접 누르려면 **클릭 통과**를 끕니다.
6. Game Bar를 닫고 게임을 실행하거나 게임으로 돌아갑니다.

핀을 켜야 Game Bar를 닫은 뒤에도 위젯이 게임 위에 남습니다. 클릭 통과가 켜져 있으면 마우스 입력이 게임으로 통과하므로 Key Overlay 버튼을 누를 수 없습니다.

오버레이를 이동하려면 `Win + G`로 Game Bar 편집 상태를 연 뒤 위젯 제목 표시줄을 끕니다. `중앙` 버튼을 누르면 현재 화면 중앙으로 이동합니다.

## 키 배열 편집

1. `편집`을 누릅니다.
2. `키 추가`를 누릅니다.
3. 등록할 실제 키를 누릅니다.
4. 조합키가 필요하면 `Ctrl`, `Shift`, `Alt`, `Win`을 누른 상태에서 마지막 키를 누릅니다.
5. 타일을 끌어 순서를 변경합니다.
6. `−` 또는 `+`로 열 수를 2–8 사이에서 변경합니다.
7. 키를 선택한 뒤 `선택 삭제`로 제거합니다.
8. `버튼 투명도` 슬라이더로 버튼 배경의 투명도를 조정합니다.
9. `배경 투명도` 슬라이더로 위젯 바탕의 투명도를 별도로 조정합니다.
10. `완료`를 누릅니다.

설정은 현재 Windows 사용자 계정의 UWP 로컬 설정에 저장됩니다.

## 키 입력 방식

Key Overlay는 특정 게임 프로세스나 게임 창을 찾아 입력하지 않습니다. 각 버튼은 스캔 코드 기반 `SendInput`을 전역으로 발생시키며, 물리 키보드와 마찬가지로 그 순간 Windows가 전면으로 관리하는 애플리케이션이 입력을 받습니다.

Game Bar의 고정 위젯은 게임 위에 표시되면서도 게임 세션을 유지하도록 설계되어 있어 일반적인 사용에서는 게임이 입력을 받습니다. 다음 환경에서는 제한될 수 있습니다.

- 게임이 관리자 권한이고 입력 브리지는 일반 권한인 경우
- 게임이 주입된 Windows 입력을 무시하고 물리 HID만 직접 읽는 경우
- 게임 또는 보안 소프트웨어가 Game Bar나 `SendInput`을 차단하는 경우
- 클릭 통과가 켜져 있어 위젯 버튼 자체를 누를 수 없는 경우

보호 기능 우회나 프로세스 내부 입력 주입은 구현하지 않습니다.

## 업데이트와 재설치

릴리즈 버전을 업데이트할 때는 새 ZIP을 압축 해제하고 그 안의 `Install.ps1`을 실행합니다. 소스를 변경했거나 개발 버전을 다시 적용할 때는 다음 명령을 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\Install-Dev.ps1
```

스크립트가 실행 중인 Key Overlay와 InputBridge를 종료하므로 열린 위젯이 닫힐 수 있습니다. 완료 후 `Win + G`에서 다시 여세요.

등록된 개발 패키지는 저장소의 `DevDeploy` 폴더를 직접 참조합니다. 저장소를 이동하거나 삭제하기 전에 제거 스크립트를 실행하고, 이동한 새 경로에서 다시 설치해야 합니다.

## 제거

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\Uninstall.ps1
```

제거하면 Key Overlay 패키지와 저장된 그리드 설정이 현재 사용자 계정에서 삭제됩니다. Visual Studio와 공용 Microsoft 디버그 런타임은 제거하지 않습니다.

## 문제 해결

### 위젯 메뉴에 Key Overlay가 없음

먼저 설치 상태와 `PublicFolder`를 확인합니다.

```powershell
$package = Get-AppxPackage -Name 'KeyOverlay.GameBarWidget'
$package | Select-Object Name, Status, InstallLocation
Test-Path (Join-Path $package.InstallLocation 'GameBar')
```

패키지가 없거나 폴더 결과가 `False`면 전체 설치를 다시 실행합니다. 둘 다 정상이면 Game Bar 목록 캐시를 새로 고칩니다.

```powershell
Get-Process -Name 'GameBar', 'GameBarFTServer' -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process 'ms-gamebar:'
```

### `입력 실패`가 표시됨

1. 최신 소스로 `Install-Dev.ps1`을 다시 실행합니다.
2. Game Bar에서 Key Overlay 위젯을 닫았다가 다시 엽니다.
3. 브리지 프로세스를 확인합니다.

```powershell
Get-Process -Name 'InputBridge' -ErrorAction SilentlyContinue
```

프로세스가 없다면 위젯을 다시 열어 자동 실행을 유도합니다. 관리자 권한 게임에서는 권한 수준 차이로 `SendInput`이 차단될 수 있으므로 게임을 일반 권한으로 실행해 확인합니다.

버튼을 한 번 누른 뒤 내부 진단 결과를 확인할 수도 있습니다. 위젯 화면에는 짧게 `입력 실패`만 표시되고, 상세 단계와 Win32 오류 코드는 아래 파일에 기록됩니다.

```powershell
$family = (Get-AppxPackage -Name 'KeyOverlay.GameBarWidget').PackageFamilyName
Get-Content "$env:LOCALAPPDATA\Packages\$family\LocalState\input-diagnostic.txt"
```

정상 결과는 `OK`입니다.

### 빌드 도구를 찾지 못함

Visual Studio Installer에서 UWP, C++ 데스크톱 개발, Windows SDK `10.0.26100.0`, MSVC v145를 설치합니다. Community가 아닌 Professional 또는 Enterprise도 설치 스크립트가 자동 검색합니다.

### 완전 초기화

```powershell
.\scripts\Uninstall.ps1
.\scripts\Install-Dev.ps1
```

## 아이콘 자산

- 생성 원본: `KeyOverlay.Widget\Assets\AppIconSource.png`
- 앱 타일 300px: `Square150x150Logo.scale-200.png`
- 앱 타일 88px: `Square44x44Logo.scale-200.png`
- Store 로고 50px: `StoreLogo.png`
- Game Bar 메뉴 44px: `GameBar\Icons\icon.targetsize-44.png`

아이콘은 겹친 오버레이 패널과 세 개의 키를 결합한 형태이며, 44px 크기에서도 식별되도록 단순한 실루엣과 높은 명암 대비를 사용합니다.

## 구현 구조

- `KeyOverlay.Widget`: Xbox Game Bar UWP 위젯, 그리드 UI, 배치 및 상태 저장
- `InputBridge`: 스캔 코드 기반 전역 `SendInput` 키 누름/해제
- `LOCAL\\KeyOverlay.Input`: UWP 위젯이 사용하는 로컬 명명 파이프 이름

브리지는 실행 중인 위젯의 AppContainer SID를 확인해 같은 격리 네임스페이스에 파이프를 생성합니다. 파이프 명령은 매직 값, 가상 키 코드, 조합키 플래그를 하나의 12바이트 메시지로 전송합니다.

## 직접 빌드

Visual Studio에서 `KeyOverlay.sln`을 열고 `Debug|x64` 또는 `Release|x64`로 빌드합니다.

명령줄 빌드 예시는 다음과 같습니다.

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    .\KeyOverlay.sln `
    /restore /m `
    /p:Configuration=Release `
    /p:Platform=x64
```
