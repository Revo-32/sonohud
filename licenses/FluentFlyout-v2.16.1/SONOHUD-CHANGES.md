# FluentFlyout 재사용 및 SonoHUD 수정 고지

- 원본: [FluentFlyout v2.16.1](https://github.com/unchihugo/FluentFlyout/tree/2d742fa4fc90a228803d90db2902740463113e77), commit `2d742fa4fc90a228803d90db2902740463113e77`.
- Copyright (c) 2024-2026 The FluentFlyout Authors. SPDX-License-Identifier: GPL-3.0-or-later.
- upstream LICENSE의 프로젝트 귀속문인 Copyright (C) 2025 Hugo Li <https://unchihugo.github.io/>를 보존합니다. 배포 루트 `LICENSE`는 공식 GNU GPL 전문입니다. 원본 파일의 위치와 해시는 같은 폴더의 `UPSTREAM-ATTRIBUTION.md`에 있습니다.
- 코드 수정일: **2026-11-15**, 이 고지 보완일: **2026-11-16**.
- 추가 코드 수정일: **2026-11-16**. `TaskbarWidgetWindow.cs`의 표면과 조작부를 A 레일 디자인으로 조정했습니다. 자체 WPF 리소스 `Views/TaskbarWidgetTheme.xaml`, 정사각 커버 프레임, 작은/세로 작업 표시줄의 여백을 적용했습니다. 기존 미디어 명령·설정·제목 스크롤·네이티브 연결 코드는 유지했습니다.
- 추가 코드 수정일: **2026-11-17 (M6)**. `TaskbarWidgetWindow.cs`에 공유 스펙트럼 표시 영역을 추가하고, 좁은 위젯에서는 이 영역만 숨깁니다. 긴 제목에 양끝 alpha mask와 여백을 적용했습니다. 기존 미디어 명령·자식 HWND 연결을 보존했습니다. 새 WASAPI 캡처·FFT 코드는 독립 작성했으며 원본 Visualizer.cs는 비교만 했습니다.

`TaskbarWindow.xaml.cs`, `NativeMethods.cs` 등의 작업 표시줄 HWND 검색·연결·배치 코드는 SonoHUD `Taskbar/TaskbarShell.cs`로 수정·통합했습니다. 모니터 device ID, 범위를 제한한 shell UIA 읽기, 점유 영역 배치, 작은 자식 창과 실패 처리를 사용합니다.

`TaskbarWidgetControl.xaml(.cs)` 및 `TaskbarWindow.xaml.cs`의 정보 배치·크기·자식 창 표현은 `Taskbar/TaskbarWidgetWindow.cs`에서 SonoHUD의 미디어 상태·명령·글꼴·제목 스크롤에 맞게 수정했습니다. 두 제품 소스 파일의 원저작권·SPDX·수정일·원본 commit 주석을 유지합니다.

기존 앱의 Flyout, 독립 미디어/볼륨 관리자, 오디오 캡처, Store/Premium, 업데이트, 브랜드 자산을 가져오지 않았습니다. SonoHUD의 설정·수명 관리 코드를 연결했습니다. 파일별 대응은 위 두 단락 및 같은 디렉터리의 원본 목록에서 확인할 수 있습니다.

비교용 원본 8개는 `original/`에, 출처와 SHA-256은 [source-manifest.json](source-manifest.json)에 있습니다. 이 원형 파일은 컴파일하지 않습니다. 감사에서 원격 고정 commit의 파일과 CRLF/LF만 정규화한 텍스트가 일치함을 확인했습니다. 역사적 원본은 프로젝트에 보존하고 공개용 ZIP에는 GNU 정본을 제공합니다. 특별한 GPL 연결 예외를 추가하지 않았습니다.

추가 코드 수정일은 2026-11-19입니다. TaskbarShell.cs는 선택한 모니터의 Explorer 핸들 검색을 재연결 검사에도 사용합니다. TaskbarWidgetWindow.cs는 차분한 바탕에 Prism 가장자리 애니메이션과 다색 주파수 막대를 적용했습니다.
