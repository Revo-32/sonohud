# SonoHUD 제3자 소프트웨어 및 자산 고지

SonoHUD 및 수정된 FluentFlyout 코드는 GPL-3.0-or-later로 제공합니다. 라이선스 전문은 `LICENSE`에, 원저작자 및 수정일은 `NOTICE`와 `licenses/FluentFlyout-v2.16.1/SONOHUD-CHANGES.md`에 있습니다. 아래 구성 요소의 저작권과 고유 라이선스도 적용됩니다.

| 구성 요소 | 권리자·라이선스·사용 범위 | 동봉 고지와 공식 출처 |
|---|---|---|
| FluentFlyout v2.16.1 | Copyright (c) 2024-2026 The FluentFlyout Authors. GPL-3.0-or-later. 작업 표시줄 연결·배치와 위젯 창 구현에 사용 | `licenses/FluentFlyout-v2.16.1/`의 UPSTREAM-ATTRIBUTION.md, 수정 내역, 원본 파일 목록. 전체 GPL은 배포 루트 LICENSE에 포함. [고정 소스](https://github.com/unchihugo/FluentFlyout/tree/2d742fa4fc90a228803d90db2902740463113e77) |
| Microsoft.Windows.CsWinRT 2.2.0 | Copyright (c) Microsoft Corporation. MIT. 빌드 도구로 투영을 생성하고 런타임 소스를 프로그램에 컴파일 | `licenses/CsWinRT-2.2.0/`의 LICENSE, nuspec, 수정 내역. [패키지](https://www.nuget.org/packages/Microsoft.Windows.CsWinRT/2.2.0) |
| Windows.winmd | Copyright (c) Microsoft Corporation. windows-rs 메타데이터의 MIT 또는 Apache-2.0 중 MIT를 선택해 빌드에 사용 | `licenses/Windows-Metadata-MIT/`의 원본 안내와 라이선스. [메타데이터별 허가](https://github.com/microsoft/windows-rs/blob/4b8893b307719ec23cce698269ca572ef301dc5c/crates/libs/bindgen/default/readme.md) |
| YamlDotNet 18.1.0 | Antoine Aubry and contributors, MIT. LibYAML 유래 부분은 Copyright (c) 2006 Kirill Simonov, MIT. Lyricsfile 구문 처리에 사용 | `licenses/YamlDotNet-18.1.0-MIT.txt`, `licenses/YamlDotNet-18.1.0-libyaml.txt`, `licenses/YamlDotNet-18.1.0.nuspec`. [NuGet](https://www.nuget.org/packages/YamlDotNet/18.1.0), [LibYAML 고지](https://github.com/aaubry/YamlDotNet/blob/748334a8fa7c227740018b284b71ad95cc6b7fc7/LICENSE-libyaml) |
| .NET apphost 10.0.12 | .NET Foundation and Contributors, MIT와 공급자의 세부 제3자 고지. win-x64 템플릿에서 SonoHUD.exe 생성 | `licenses/DotNet-AppHost-10.0.12/`의 LICENSE.TXT, THIRD-PARTY-NOTICES.TXT, nuspec. [공급 패키지](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x64/10.0.12) |

실행하려면 사용자 PC에 Microsoft .NET 10 Desktop Runtime x64가 설치되어 있어야 합니다. Windows 미디어·합성·오디오 기능은 운영체제 API를 사용합니다. 이 Portable 배포에는 .NET 공유 런타임, 운영체제 DLL, Microsoft.Windows.SDK.NET.dll 또는 외부 WinRT.Runtime.dll이 포함되지 않습니다. [Microsoft 런타임 안내](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## 글꼴

다음 다섯 제품의 Regular, Medium, SemiBold, Bold 정적 파일 총 20개가 SonoHUD.dll의 리소스로 동봉됩니다. 각 글꼴은 원형과 SIL Open Font License 1.1을 유지합니다. 라이선스와 권리자 정보는 표의 파일에서 확인할 수 있습니다. 소스 ZIP의 해당 경로 앞에는 `src/SonoHUD/SonoHUD.App/`이 붙습니다. 파일별 출처와 SHA-256은 같은 위치의 `Assets/Fonts/font-manifest.json`에 있습니다. [OFL 동봉 안내](https://openfontlicense.org/ofl-faq/)

| 글꼴 | 권리자 및 고지 | 라이선스 파일 |
|---|---|---|
| [Wanted Sans 1.0.3](https://github.com/wanteddev/wanted-sans/releases/tag/v1.0.3) | Copyright 2024 The Wanted Sans Project Authors | `Assets/Fonts/WantedSans/OFL.txt` |
| [Pretendard 1.3.9](https://github.com/orioncactus/pretendard/releases/tag/v1.3.9) | Copyright (c) 2021, Kil Hyung-jin. Reserved Font Name Pretendard | `Assets/Fonts/Pretendard/OFL.txt` |
| [Pretendard JP 1.3.9](https://github.com/orioncactus/pretendard/releases/tag/v1.3.9) | Copyright (c) 2021, Kil Hyung-jin. Reserved Font Name Pretendard JP | `Assets/Fonts/PretendardJP/OFL.txt` |
| [Inter 4.1](https://github.com/rsms/inter/releases/tag/v4.1) | Copyright (c) 2016 The Inter Project Authors | `Assets/Fonts/Inter/OFL.txt` |
| [JetBrains Mono 2.304](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304) | Copyright 2020 The JetBrains Mono Project Authors | `Assets/Fonts/JetBrainsMono/OFL.txt` |

## 아이콘·이미지·미디어

SonoHUD 파형 로고 `C-wave-refinement-v2.png`와 9개 크기의 `SonoHUD.ico`는 프로젝트에서 생성하고 사용자가 채택했습니다. 이 자산은 프로젝트가 보유한 권리 범위에서 GPL-3.0-or-later로 제공합니다. 소스의 `src/SonoHUD/SonoHUD.App/Assets/Branding/branding-manifest.json`에 해시가 있습니다. UI 도형과 데모 자료는 프로젝트 자체 제작물입니다. FluentFlyout 로고, Stageshow 리소스, 외부 음악 파일, 상용 앨범 커버 및 외부 가사 원문을 패키지에 넣지 않습니다.

## 설치 및 업데이트 구성 요소

Velopack 1.2.161의 .NET 10용 클라이언트는 MIT로 제공됩니다. 기본 런타임에서 추가 NuGet 의존성을 요구하지 않습니다. 별도 프로세스로 실행하는 x64 설치기와 업데이트 도우미는 같은 고정 소스에서 빌드하며, Windows 런타임 확인 코드의 변경 사항은 `licenses/Velopack-1.2.161/CHANGES.md`에 있습니다. MIT 전문 및 의존성 고지는 같은 디렉터리에 있습니다. [공식 소스](https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493) · [클라이언트 패키지](https://www.nuget.org/packages/Velopack/1.2.161)

네이티브 설치 도우미에는 Rust 표준 라이브러리, MinGW-w64 시작 코드와 LLVM 런타임 일부가 링크됩니다. 배포된 성분과 고지 전문은 `licenses/Velopack-1.2.161/GNU-TOOLCHAIN-NOTICES.txt`, `RUST-STDLIB-COPYRIGHT.html`, `WINDOWS-NATIVE-NOTICES.txt`에 있습니다. [GNU/LLVM 도구 출처](https://github.com/mstorsjo/llvm-mingw) · [Rust 저작권 고지](https://github.com/rust-lang/rust/blob/main/COPYRIGHT)

## 대응 소스

배포자는 같은 버전의 Core 실행본·설치본 및 대응 소스 ZIP을 같은 배포 지점에서 추가 비용 없이 함께 제공합니다. 수정 가능한 소스, 프로젝트·빌드 스크립트, 고정 입력 및 권리 고지를 보존하십시오. [GNU GPLv3 §6](https://www.gnu.org/licenses/gpl-3.0.html#section6)
