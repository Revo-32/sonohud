# Velopack 1.2.161 · Windows 도우미 수정 기록

2026-11-19. Copyright © Velopack Ltd. 배포하는 관리 DLL은 공식 NuGet의 net10.0 바이너리다. 공급 패키지와 vpk 도구가 지시하는 같은 원본 커밋은 `92d6a1c91716729d449034df5c50307dcce39493`이다. 원본 MIT 전문은 LICENSE.txt, 도구와 라이브러리의 패키지 선언은 이 폴더의 두 nuspec에 있다. [원본 소스](https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493)

SonoHUD는 Windows x64 설치, 업데이트, 바로가기 실행용 도우미 3개를 이 원본에서 다시 빌드한다. 원본의 WebView2LoaderStatic 정적 SDK 호출을 Microsoft가 문서화한 설치 레지스트리의 pv 값 읽기로 교체했다. 해당 로더를 제공하는 crate를 Windows 빌드에서 제외하고 독립적으로 필요한 Windows Variant 바인딩을 지정했다. 레지스트리는 읽기만 하며 설치·제거·업데이트 및 데스크톱 런타임의 기존 처리 코드를 유지한다. SonoHUD는 WebView2 설치를 요구하지 않는다. [Microsoft의 런타임 감지 안내](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution#detect-if-a-suitable-webview2-runtime-is-already-installed)

이번 설치 도우미는 Rust 1.100.0의 x86_64-pc-windows-gnullvm 호스트와 동일 대상 표준 라이브러리, llvm-mingw 20261117 UCRT의 clang 23.1.6·LLD·windres로 생성했다. 대응 소스의 vendor-source/Velopack, 고정 Cargo.lock과 scripts/Build-Velopack-Windows.ps1에서 변경 소스와 빌드 방법을 확인할 수 있다. 독립 설치/업데이트 프로세스는 MIT 구성이다. 포함된 Rust 및 의존성 라이선스·저작권 표시는 WINDOWS-NATIVE-NOTICES.txt에, Windows 대상에서 해결한 228개 crate(빌드용 포함)의 정확한 버전·다운로드 해시는 WINDOWS-NATIVE-SOURCES.json에 있다. 대안 허가 `Apache-2.0 OR GPL-2.0-only`에서는 Apache-2.0을 적용한다.

## M7.3의 제거·재설치 변경

2026-11-19, Copyright © 2026 SonoHUD contributors. `scripts/velopack-cleanup.c`의 신규 C 코드와 `scripts/velopack-uninstall.rs`의 SonoHUD 변경분은 MIT 조건으로 제공한다. Rust 파일의 원본 Velopack 저작권과 MIT 전문도 유지한다. 설치기는 사용자가 보존한 파일과 패키지의 파일명을 대조한다. 제거 도구는 파일 내용을 배포 패키지와 비교하고 Windows의 열린 파일 핸들로 정리를 마친다. 이를 위해 Update.exe에 작은 x64 DLL을 내장한다. 이 DLL은 제거하는 동안 사용자 임시 폴더에 생성되고 Windows가 핸들을 닫을 때 삭제한다. Windows에 설치된 rundll32.exe를 절대 경로로 실행한다. 이 Windows 실행 파일은 배포물에 포함되지 않는다. 신규 소스와 빌드 명령은 대응 소스 ZIP의 scripts 및 vendor-source/Velopack/src/bins에 있다.

Velopack 1.2.161의 관리 SDK는 새 패키지를 다운로드할 때 적용 전에 Update.exe를 교체하고 이전 패키지를 정리할 수 있다. SonoHUD 제거기는 패키지 내부 명세의 실제 버전과 stable 피드 이름을 함께 확인한다. 이전 공개 후보 SonoHUD.Core v0.7.2에는 독립 소유 목록이 없어서 원본 전체 패키지 SHA-256 `6f6ac9f47137341f43c8eca6d5238a0c1619295dd940c4ed7934049a7cbe4321`에서 얻은 52개 설치 파일의 길이·SHA-256 목록을 새 도우미에 내장했다. `scripts/velopack-legacy-owned.tsv`와 vendor-source/Velopack/src/bins의 사본은 같은 바이트다. SHA-256 검증은 기존 잠금 파일에 있던 sha2 0.11.0을 사용한다. 패키지 버전·체크섬 집합은 그대로다. 네이티브 업데이터도 이 근거로 실제 current 파일을 교체 전과 백업 전환 직후에 확인한다. 사용자 파일 또는 내용 변경이 있으면 이전 버전을 유지한다. 기존 버전 백업은 확인된 원본 파일만 개별적으로 정리한다. Windows SDK가 생성하는 스테이징 ID는 Native GUID 형식과 관리 SDK의 UTF-8 BOM·32자리 형식을 확인한 경우 정리한다.

## M8의 네이티브 빌드 변경

2026-11-19, Copyright © 2026 SonoHUD contributors. 별도 프로세스로 실행되는 Velopack 도우미와 제거용 임시 DLL을 오픈소스 GNU/LLVM 도구로 재빌드했다. 빌드 스크립트는 공개된 버전의 LLVM 링커에서 시스템 DLL 검색 한정(PE DependentLoadFlags 0x800)과 Windows DLL 지연 로드를 설정하고, 동일 필드 순서의 Win32 버전·asInvoker 리소스 개체를 각 실행 파일에 연결한다. 제거 C 코드는 두 곳에서 와이드 문자열 인수를 명시했다. 원본 Velopack MIT와 SonoHUD가 추가한 설치기 소스의 MIT 고지는 앞 절에 있다.

도우미에는 MinGW-w64 CRT의 시작 개체, mingw32·mingwex·UCRT 보조 개체, Rust 표준 라이브러리와 Rust 의존성이 들어간다. 링크 맵에서 LLVM libunwind, 제거 DLL의 compiler-rt x64 스택 검사도 확인했다. 운영체제 API와 UCRT는 Windows 10/11이 제공한다. Visual Studio Build Tools 또는 Windows SDK의 C/C++ 라이브러리를 공급 입력으로 사용하지 않았다. MinGW-w64의 이진 재배포 고지(ZPL-2.1, 파일별 BSD·MIT 등), LLVM의 Apache-2.0 WITH LLVM-exception과 통합 구성 요소 원문, 도구 묶음의 추가 고지는 GNU-TOOLCHAIN-NOTICES.txt에 있다. GNU/LLVM에 동봉된 libc++/libc++abi 등의 고지도 포함했다. 표준 라이브러리의 파일별 저작권과 종속성 원문은 RUST-STDLIB-COPYRIGHT.html에 있다. [도구 배포 원본](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261117) · [MinGW-w64 소스](https://github.com/mingw-w64/mingw-w64/tree/41eb042358a52011b35350392130dcdc94a8ead8) · [LLVM 소스](https://github.com/llvm/llvm-project/tree/5b10d2d74929eae78912771222c9855af97496f4) · [Rust GNU/LLVM 대상](https://doc.rust-lang.org/rustc/platform-support/windows-gnullvm.html) · [Windows의 UCRT](https://learn.microsoft.com/en-us/cpp/windows/universal-crt-deployment?view=msvc-170)

BUILD-TOOLS-NOTICES.md는 vpk가 개발용 도구 폴더에 포함한 공급자 원문이다. SonoHUD의 산출물에는 WiX 빌드 도구, Windows SDK 서명 도구, zstd.exe, Linux AppImage 도구, 공급자 서명키 또는 vpk 전체를 넣지 않는다. Windows 빌드에 링크된 zstd 코드의 라이선스는 별도 네이티브 고지에 포함했다.
