[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [Parameter(Mandatory = $true)][string] $WorkDirectory,
    [Parameter(Mandatory = $true)][string] $EvidenceDirectory,
    [string] $UpstreamCacheDirectory
)

$ErrorActionPreference = 'Stop'
$python = (Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$arguments = @('-X', 'utf8', (Join-Path $PSScriptRoot 'package-release.py'),
    '--output', $OutputDirectory, '--work', $WorkDirectory, '--evidence', $EvidenceDirectory)
if ($UpstreamCacheDirectory) { $arguments += @('--upstream-cache', $UpstreamCacheDirectory) }
& $python @arguments
if ($LASTEXITCODE -ne 0) { throw '릴리스 후보 생성 또는 검증에 실패했습니다. 작업 폴더의 빌드 기록을 확인하세요.' }
