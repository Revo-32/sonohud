# SonoHUD 승인 로고 패키지

사용자가 최종 앱 적용을 승인한 `C-wave-refinement-v2.png`를 그대로 복사했다. 디자인 원본은 `docs/branding/concepts-01/C-wave-refinement-v2.png`에 계속 보존한다.

- 원본 크기: 1254 × 1254, RGBA PNG.
- 승인 원본 SHA-256: `2563E17F48DD126E1A172AB10DA4AEDDFDA68E8ABE4715A880E3ABEC44D9893B`.
- `SonoHUD.ico`: 16, 20, 24, 32, 40, 48, 64, 128, 256px의 9개 PNG 프레임을 담은 32비트 ICO.
- 변환은 전체 정사각형 원본의 비례 축소만 수행한다. 자르기, 배경 제거, 색 변경, 로고 재생성, 선명화 및 도형 재작성은 하지 않는다. 원본 알파를 보존한다.
- ICO를 실행 파일의 Win32 아이콘 및 WPF 창 아이콘에 동일하게 연결한다. 원본 PNG와 ICO는 앱 pack 리소스에도 포함한다.
- Windows 셸의 캐시나 기존 고정 바로가기는 갱신 시점에 영향을 줄 수 있다. 캐시 삭제나 사용자 작업 표시줄 변경은 수행하지 않는다.

재생성은 Windows PowerShell 또는 PowerShell에서 다음과 같이 실행한다. 앱 실행·빌드·시스템 설정 변경은 하지 않는 패키징 스크립트다.

```powershell
& .\Generate-Icons.ps1
```

스크립트는 승인 원본 해시를 확인한 뒤 `SonoHUD.ico`와 `branding-manifest.json`만 생성·갱신한다. 다른 폴더에서 시험하려면 `-DestinationDirectory`에 별도 폴더를 지정할 수 있다.
