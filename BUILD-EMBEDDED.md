# SonoHUD Core 1.0.0 빌드

Windows x64, .NET SDK 10.0.401로 빌드한다. `global.json`과 프로젝트별 잠금 파일을 그대로 사용한다. 소스 ZIP을 영문 경로에 풀고 PowerShell에서 다음을 실행한다. CsWinRT 2.2.0 생성기가 한글 소스 경로에서 실패한 이력이 있다. 실제 프로그램은 사용자 한글 경로에서 설치할 수 있다.

```powershell
.\scripts\Build-Portable.ps1 -OutputPath ..\SonoHUD-Core-portable
```

`SonoHUD.slnx`와 제품 프로젝트는 App·Core·PluginContracts로 구성된다. 실행에는 x64 .NET 10 Desktop Runtime이 필요하다. 공개 소스는 제품·계약·빌드·고지·선별된 공급자 및 공개 사용자 문서를 담는다. 플러그인 설치 신뢰와 SDK는 docs/architecture를 참고한다. Windows API 메타데이터는 고정된 windows-rs 커밋의 MIT 선택 Windows.winmd이며, 실제 또는 대체 라이브러리가 필요한 내용을 임의로 지워 배포물을 만들지 않는다. 투영 입력 해시, 내장 런타임 원본과 재생성 경로는 third_party, EmbeddedWinRT.props 및 EmbeddedWinRT.targets에 있다. Microsoft.Windows.CsWinRT 2.2.0은 빌드 전용 도구다. .NET apphost 및 YamlDotNet 공급자 소스는 vendor-source에 있다.

## Windows 설치 프로그램 재생성

x64 설치기·업데이트·실행 도우미는 공식 Velopack 1.2.161 소스에서 재빌드한다. 공급 원본과 수정 내역, 라이선스 및 Windows 대상 종속성 고지는 licenses/Velopack-1.2.161에 있다. 일반 PowerShell에서 고정된 Rust 1.100.0 GNU/LLVM 호스트와 llvm-mingw 20261117 UCRT 도구 묶음을 사용한다. Windows SDK 개발자 환경이나 Visual Studio 설치는 필요하지 않다. 네이티브 빌드의 소스와 Cargo 캐시는 공백 없는 영문 경로를 사용한다. 전체 경로 대신 상대 위치의 새 폴더를 예시로 보인다.

```powershell
python .\scripts\Install-NativeToolchain.py ..\SonoHUD-open-tools
Invoke-WebRequest https://api.nuget.org/v3-flatcontainer/vpk/1.2.161/vpk.1.2.161.nupkg -OutFile ..\vpk.1.2.161.nupkg
.\scripts\Build-Velopack-Windows.ps1 -SourceDirectory .\vendor-source\Velopack -OutputDirectory ..\SonoHUD-vpk -ToolPackage ..\vpk.1.2.161.nupkg -RustBinDirectory ..\SonoHUD-open-tools\rust\bin -LlvmBinDirectory ..\SonoHUD-open-tools\llvm-mingw-20261117-ucrt-x86_64\bin
Get-Help .\scripts\New-CoreSetup.ps1 -Detailed
```

Cargo 빌드는 같은 LLVM/MinGW-w64 C 컴파일러로 `vendor-source/Velopack/src/bins/sonohud-cleanup.c`도 컴파일한다. Update.exe에 내장한 임시 DLL과 수정된 제거 코드의 원본은 `scripts/velopack-cleanup.c` 및 `scripts/velopack-uninstall.rs`에 있다. Windows 설치·제거 API, 분리된 네이티브 프로세스, 런타임 공급자 조건은 `licenses/Velopack-1.2.161/CHANGES.md`에서 확인할 수 있다.

`New-CoreSetup.ps1`에는 위에서 빌드한 x64 도우미가 있는 vpk.dll, 같은 버전의 Portable 출력 및 새 출력 디렉터리를 제공한다. 버전별 실행 파일에 대한 게시자 코드 서명은 별도 단계다. 공식 릴리스 서비스 주소는 Core 배포 정책에 따른다.

## 대응 소스와 확인

공개 소스와 실행 ZIP을 다시 만들려면 .NET SDK 외에 Python 3.11 이상이 필요하다. 소스 스냅숏 두 벌에서 각각 잠금 복원·publish한 뒤 전체 파일의 SHA-256과 생성된 WinRT 코드를 비교한다. 모든 작업/출력/증거 경로는 새 폴더여야 한다. 같은 소스 ZIP은 외부 공개 전용 소스 트리로 펼칠 수 있다. 이 작업은 원격 저장소에 접근하거나 업로드하지 않는다. 최초 복원 시 공급자 소스와 NuGet을 다운로드한다.

```powershell
.\scripts\New-ReleaseCandidate.ps1 -WorkDirectory ..\SonoHUD-build -OutputDirectory ..\SonoHUD-distribution -EvidenceDirectory ..\SonoHUD-build-evidence
```

소스 ZIP의 source-manifest.json은 자신을 제외한 파일별 SHA-256을 기록한다. 전달받은 ZIP은 공식 전달 채널을 통해 별도로 확인한 SHA-256과 비교한다. 자체 네이티브 재빌드 결과는 도구 버전과 경로에 따라 기계 코드·PE 메타데이터가 달라질 수 있다.

### 확장 API v1의 바이너리 호환성

제품 파일 버전은 1.0.0입니다. 이미 설치한 확장이 사용하는 공유 가사 자료형의 어셈블리 식별자는 SonoHUD.Core 0.7.3.0, 플러그인 계약은 1.0.0.0으로 유지합니다. 같은 API 세대에서 제품 버전만 올릴 때 이 식별자를 함께 올리지 마세요.
