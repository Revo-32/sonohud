# SonoHUD 1.0.1의 실행 환경과 알려진 제한

- Windows 11 x64와 .NET 10 Desktop Runtime x64가 필요합니다. ARM64, 32비트 Windows, 모든 게임과 독점 전체 화면의 호환성은 확인하지 않았습니다.
- 설치 프로그램과 EXE에는 Authenticode 게시자 서명이 없습니다. SmartScreen이 경고하거나 Smart App Control 또는 조직 정책이 실행을 차단할 수 있습니다. 차단된 장치에서는 조직의 소프트웨어 배포 정책을 따르세요. [Microsoft SmartScreen 안내](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- Explorer 구성·Windows 빌드에 따라 작업 표시줄 위젯을 지원하는 모니터가 달라집니다. 실제 Windows 재로그인 직후의 트레이·위젯 복원과 Explorer 재시작은 v1.0.1 출시 시험에서 확인하지 못했습니다.
- 미디어 정보·재생 명령·탐색은 플레이어가 Windows에 제공하는 기능에 따라 달라집니다. 재생 위치 변경 요청을 수락해도 플레이어의 새 위치가 늦게 도착할 수 있습니다. 확장이 없는 Core에는 외부 가사 조회 기능이 없습니다.
- 설치형은 운영 배포 주소와 공개 검증키를 지정한 빌드에서만 새 버전을 확인합니다. v1.0.1에는 공식 GitHub 저장소 경로와 운영 공개키가 내장돼 있습니다. Portable은 새 폴더에 압축을 풀어 수동 업데이트합니다. [설치·업데이트·제거 안내](core-user-guide.md)
