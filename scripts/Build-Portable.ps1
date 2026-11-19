[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OutputPath,
    [switch] $SelfContained
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $destination) {
    throw '출력 폴더가 이미 있습니다. 기존 빌드를 보존하도록 새 경로를 지정하세요.'
}
$dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$configuration = Get-Content -Raw -LiteralPath (Join-Path $root 'global.json') | ConvertFrom-Json
$project = 'src/SonoHUD/SonoHUD.App/SonoHUD.App.csproj'
$standalone = if ($SelfContained) { 'true' } else { 'false' }

Push-Location -LiteralPath $root
try {
    $actual = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $configuration.sdk.version) {
        throw "global.json에 지정된 .NET SDK $($configuration.sdk.version) 버전이 필요합니다."
    }
    & $dotnet restore $project -r win-x64 --locked-mode "-p:SelfContained=$standalone"
    if ($LASTEXITCODE -ne 0) { throw '잠금 파일에 따른 복원에 실패했습니다.' }
    & $dotnet publish $project -c Release -r win-x64 --self-contained $standalone --no-restore "-p:PathMap=$root=/_/SonoHUD" '-p:DebugSymbols=false' '-p:DebugType=None' -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Release publish에 실패했습니다.' }
    foreach ($filename in 'Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll') {
        if (Get-ChildItem -LiteralPath $destination -Filter $filename -Recurse -File) {
            throw "외부 WinRT DLL이 출력에 포함됐습니다: $filename"
        }
    }
    if (Test-Path -LiteralPath (Join-Path $destination 'licenses/FluentFlyout-v2.16.1/LICENSE')) {
        throw '수정된 공급자 GPL 문서가 출력에 포함됐습니다. 배포본은 루트 LICENSE 정본과 UPSTREAM-ATTRIBUTION.md를 사용합니다.'
    }
    foreach ($item in @(
        @{ Source = 'docs/release/core-user-guide.md'; Target = '사용안내.md' },
        @{ Source = 'docs/release/M7.2-core-privacy.md'; Target = '개인정보와-보안.md' }
    )) {
        Copy-Item -LiteralPath (Join-Path $root $item.Source) -Destination (Join-Path $destination $item.Target)
    }
    Write-Output "빌드 완료: $destination"
} finally {
    Pop-Location
}
