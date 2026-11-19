# 검토한 Windows 공개 후보 만들기

공개 저장소의 입력은 검증된 대응 소스 ZIP입니다. 개인 확장이나 내부 시험 자료가 섞인 작업 폴더를 업로드하면 안 됩니다. 해당 ZIP에는 앱·계약·Core 회귀 시험, 수정 가능한 의존 소스, 라이선스, 도구 입력과 GitHub Actions 정의가 있습니다. 공식 Stable 채널의 설치 파일, 패키지, 업데이트 목록과 서명은 [GitHub Releases](https://github.com/Revo-32/sonohud/releases/latest)에 게시합니다.

## 로컬 후보

Windows x64, PowerShell 7, Python 3.11 이상, global.json에 고정한 .NET 10 SDK를 사용합니다. 경로가 짧고 넉넉한 디스크에 서로 독립된 세 개의 새 폴더를 지정합니다. 잠금 파일을 커밋하고 네이티브 도구 다운로드의 SHA-256을 확인합니다.

```powershell
dotnet restore src/SonoHUD/SonoHUD.Tests/SonoHUD.Tests.csproj -r win-x64 --locked-mode -p:SelfContained=false
dotnet run -c Release --project src/SonoHUD/SonoHUD.Tests -r win-x64 --no-restore -- D:/rc-tests
./scripts/New-Distribution.ps1 -OutputDirectory D:/rc-out -WorkDirectory D:/rc-build -EvidenceDirectory D:/rc-evidence
python -B scripts/check-manifest.py D:/rc-out
```

`New-Distribution.ps1`은 두 폴더에서 대응 소스로 재빌드하고, Setup에 든 업데이트 패키지와 Portable 파일을 대조합니다. 내부 관리자에게 이미 알려진 비공개 지문은 저장소 밖 JSON(`words`, `sha256` 배열)을 `-Exclusions`로 지정해 추가 검사할 수 있습니다. 설치기의 GNU/LLVM 빌드와 도구 고지는 BUILD-EMBEDDED.md에 있습니다. 소스·해시를 포함한 정확한 업로드 목록은 SHA256SUMS.txt로 검증합니다.

## 업데이트 신뢰 설정

v1.0.0의 공식 대상은 `Revo-32/sonohud`이며, 앱에 내장할 공개키와 키 ID를 `docs/release/update-trust.json`에서 관리합니다. 개발자 Windows 계정의 로컬 개인키는 전용 DPAPI 저장소에 보관합니다. 기존 키로 다시 서명할 때는 새로운 키를 생성할 필요가 없습니다. 최초 서명·오프라인 백업·교체는 [운영 키 정책](M8.1-signing-key-policy.md)에 따릅니다.

로컬에서 완성한 서명 전 5개 파일의 해시를 확인한 다음 아래 순서로 업데이트 인덱스를 서명하고 최종 7개 파일을 대조합니다. 서명 도구가 패키지 이름·버전·길이·SHA-256을 검사합니다.

```powershell
$trust = Get-Content docs/release/update-trust.json -Raw | ConvertFrom-Json
./scripts/Sign-ReleaseFeed.ps1 -Directory D:/rc-out -PublicKey $trust.publicKey -WindowsUserKey
./scripts/Sign-ReleaseFeed.ps1 -Directory D:/rc-out -PublicKey $trust.publicKey -VerifyOnly
python -B scripts/check-manifest.py D:/rc-out --add-verified-signature
python -B scripts/check-manifest.py D:/rc-out --signed
```

## 승인 후 GitHub Actions 실행

저장소 소유자가 보호된 기본 브랜치에서 수동으로 실행합니다. 빌드 작업은 읽기 권한으로 테스트·패키징·감사를 수행합니다. 서명을 요청한 경우에만 별도 `release-signing` 환경에서 승인된 비밀 저장소를 사용합니다. 환경의 검토자, 브랜치 제한, 비밀 이전은 소유자가 승인한 뒤 설정합니다. 첫 배포에 이미 로컬 검증한 자산을 사용한다면 CI로 개인키를 이전할 필요가 없습니다.

워크플로 결과는 기한이 있는 검토용 아티팩트입니다. 게시할 때는 먼저 태그와 초안 충돌을 확인하고, 초안의 파일을 다시 받아 지문과 서명을 대조합니다. 공개 승인 후 실제 익명 GitHub 다운로드를 확인합니다. 최초와 동일한 설치본에는 ‘최신’이 정상입니다. 다음 정식 버전의 실제 업데이트 적용은 해당 버전이 발행된 뒤 검증합니다. [릴리스 절차 및 승인 체크리스트](M8.1-github-release-playbook.md) · [GitHub 수동 실행](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
