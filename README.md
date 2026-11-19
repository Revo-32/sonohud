![SonoHUD — Windows용 음악 플레이어와 데스크톱 오버레이](docs/branding/banners/sonohud-hero-dark-web.png)

한국어 · [English](README.en.md)

# SonoHUD

평소 쓰는 음악 앱은 그대로 두고, 듣고 있는 곡을 홈 플레이어와 데스크톱 오버레이, 작업 표시줄에서 확인하세요. Windows용 오픈소스 앱입니다.

**[Windows용 설치 파일 다운로드 ↓](https://github.com/Revo-32/sonohud/releases/download/v1.0.0/SonoHUD-Setup.exe)** · [Portable ZIP](https://github.com/Revo-32/sonohud/releases/download/v1.0.0/SonoHUD-v1.0.0-win-x64-portable.zip) · [대응 소스](https://github.com/Revo-32/sonohud/releases/download/v1.0.0/SonoHUD-v1.0.0-source.zip) · [최신 릴리스](https://github.com/Revo-32/sonohud/releases/latest)

v1.0.0 Stable · Windows 11 x64 · [.NET 10 Desktop Runtime x64 필요](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## 음악을 보는 세 가지 방법

- **홈 플레이어**에서 앨범 커버와 곡 정보를 보고, 재생하거나 원하는 구간으로 이동하세요. 다른 탭에서는 미니 플레이어가 같은 곡을 제어합니다. 지원되는 조작은 음악 앱에 따라 달라집니다.
- **데스크톱 오버레이**는 다른 창 위에 곡 정보를 띄웁니다. 디자인 7종에서 골라 위치와 크기를 바꾸세요. 편집을 마치면 클릭이 아래 창으로 통과하도록 설정할 수 있습니다.
- **작업 표시줄 위젯**에서는 창을 열지 않고 곡을 확인하거나 재생을 제어할 수 있습니다. 시스템 출력의 저음부터 고음까지 보여 주는 FFT 스펙트럼과 Prism RGB를 포함한 테마 6종을 지원합니다.

![Prism RGB 작업 표시줄 위젯에서 자체 데모 곡 Midnight Lines와 스펙트럼을 표시한 모습](docs/screenshots/taskbar-prism.png)

[홈 전체 화면](docs/screenshots/core-home.png) · [Compact 오버레이](docs/branding/sources/curated/compact.png) · [디자인 선택 화면](docs/screenshots/core-overlay.png) · [위젯 테마 6종 비교](docs/screenshots/widget-themes.png)

## 설치와 첫 재생

1. Windows에 **.NET Desktop Runtime 10의 x64 버전**을 설치합니다. [Microsoft 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
2. **설치형**은 `SonoHUD-Setup.exe`를 실행한 뒤 시작 메뉴에서 엽니다. **Portable**은 ZIP 전체를 새 폴더에 풀고 `SonoHUD.exe`를 실행합니다.
3. 평소 쓰는 음악 앱에서 곡을 재생합니다. 여러 플레이어가 열려 있다면 SonoHUD의 **오디오 → 음악 소스**에서 원하는 앱을 선택하세요.

설치형은 **설정 → 시작 및 업데이트**에서 Stable 업데이트를 확인하고 재시작해 적용합니다. Portable은 새 버전의 ZIP을 새 폴더에 풉니다. 설치기와 앱에는 Windows 게시자 서명이 없어 SmartScreen 경고가 표시되거나 조직 정책상 실행이 제한될 수 있습니다. [파일 지문(SHA-256)](https://github.com/Revo-32/sonohud/releases/download/v1.0.0/SHA256SUMS.txt) · [설치·제거 안내](docs/release/core-user-guide.md) · [알려진 제한](docs/release/known-limitations.md)

## 확장 기능

공개 Core에는 온라인 가사 공급자가 포함되어 있지 않습니다. 별도로 확보한 신뢰할 수 있는 확장은 **설정 → 확장 기능 → 확장 설치…**에서 설치하고 권한을 확인한 후 켤 수 있습니다. [확장 SDK](docs/architecture/M7.1-plugin-sdk.md) · [개인정보 안내](docs/release/M7.2-core-privacy.md)

## 개발과 라이선스

Windows x64와 `global.json`에 지정된 .NET SDK로 빌드합니다. 기능 제안이나 오류는 Issues에 남겨 주세요. 보안 문제는 비공개 보고 절차를 이용하세요. [빌드 방법](BUILD-EMBEDDED.md) · [Issues](https://github.com/Revo-32/sonohud/issues) · [보안 정책](SECURITY.md) · [v1.0.0 릴리스 노트](https://github.com/Revo-32/sonohud/releases/tag/v1.0.0)

SonoHUD Core는 GPL-3.0-or-later로 배포합니다. 작업 표시줄 위젯에는 FluentFlyout의 코드를 수정해 사용했습니다. 라이브러리와 글꼴의 MIT·OFL 등 개별 조건은 제3자 고지에서 확인할 수 있습니다. [라이선스](LICENSE) · [원저작자와 수정 내역](licenses/FluentFlyout-v2.16.1/SONOHUD-CHANGES.md) · [제3자 고지](THIRD-PARTY-NOTICES.md)
