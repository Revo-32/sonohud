# SonoHUD에 포함된 글꼴

이 디렉터리의 글꼴은 각각의 공식 배포 압축 파일에서 가져온 **변경하지 않은 원본**입니다. 모든 가족의 Regular(400), Medium(500), SemiBold(600), Bold(700)를 앱의 WPF Resource로 포함합니다. 시스템 글꼴 설치나 외부 CDN 연결은 필요하지 않습니다.

| 글꼴 | 버전 | 공식 배포 |
|---|---|---|
| Wanted Sans | 1.0.3 | https://github.com/wanteddev/wanted-sans/releases/tag/v1.0.3 |
| Pretendard / Pretendard JP | 1.3.9 | https://github.com/orioncactus/pretendard/releases/tag/v1.3.9 |
| Inter | 4.1 | https://github.com/rsms/inter/releases/tag/v4.1 |
| JetBrains Mono | 2.304 | https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304 |

각 하위 디렉터리의 `OFL.txt`에 저작권 표시와 SIL Open Font License 1.1 원문이 있습니다. 이 안내와 라이선스·출처 manifest는 실행 파일의 배포 폴더에도 복사합니다. Pretendard의 Reserved Font Name 조건 등을 포함한 원문 조건을 유지하며, 글꼴 자체를 단독 판매하거나 SonoHUD의 자체 제작 글꼴로 표시하지 않습니다.

`font-manifest.json`은 출처 URL, 배포 버전, 원본 ZIP의 SHA-256, 추출 경로와 실제 배포 파일 SHA-256을 기록합니다. 폰트 파일의 이름/메타데이터/글리프를 수정하거나 subset·format 변환하지 않았습니다.

Wanted Sans를 기본으로 사용합니다. 선택한 가족에서 한글·일본어가 부족하면 함께 포함한 Wanted Sans, Pretendard JP 순서로 보완합니다. Pretendard를 명시 선택한 경우에는 해당 가족을 우선합니다. Inter와 JetBrains Mono 자체가 한글·일본어를 포함한다고 주장하지 않습니다. JetBrains Mono에서 보완된 CJK 글자는 라틴 문자와 같은 고정폭을 보장하지 않습니다. 이모지 및 모든 희귀 한자까지 번들만으로 지원하는 것은 보장하지 않습니다.
