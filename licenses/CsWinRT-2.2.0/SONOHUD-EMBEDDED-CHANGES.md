# SonoHUD Embedded 적용 — 2026-11-17

Microsoft.Windows.CsWinRT 2.2.0의 MIT LICENSE를 유지한다. NuGet 패키지의 `embedded/any`, `embedded/net6.0` 원문은 대응 소스의 `third_party/CsWinRT-2.2.0/embedded/`에 보존한다. `EmbeddedWinRT.targets`는 obj에 복사된 파일만 다음과 같이 정규화한다.

- 원본 런타임의 nullable 기본값을 유지하도록 파일 범위 지시문을 추가하고 `#nullable restore`를 disable로 바꾼다. SonoHUD 자체 코드는 nullable·경고 오류 처리를 유지한다.
- 구형 런타임 내부의 obsolete/ref 경고 CS0618·CS9191, nullable 문맥 CS8632만 복사/생성 파일에서 억제한다.
- Module.cs의 버전 없는 assembly SupportedOSPlatform("Windows") 고지를 제거한다. 호스트의 기존 net10.0-windows10.0.26100.0에서 정확한 최소 Windows 버전을 생성한다. 플랫폼 분석 자체는 끄지 않는다.

투영 입력은 Microsoft windows-rs의 MIT/Apache-2.0 명시 Windows.winmd이다. 배포에는 MIT 선택을 적용한다. 생성된 투영과 런타임은 SonoHUD.dll에 컴파일된다. WinRT.Runtime.dll/Microsoft.Windows.SDK.NET.dll을 합치거나 추출·변조한 구현이 아니다. Color는 이 입력의 A/R/G/B ABI 레이아웃을 독립 구현한 4바이트 구조체다.
