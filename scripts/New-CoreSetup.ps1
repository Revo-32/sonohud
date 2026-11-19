[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$VpkPath,
    [string]$Version,
    [string]$PackageId="SonoHUD.Core",
    [string]$DisplayName="SonoHUD"
)
$ErrorActionPreference="Stop"
if (-not $Version) {
    $properties=[xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)
    $Version=[string]$properties.Project.PropertyGroup.Version
}
$source=(Resolve-Path -LiteralPath $PublishDirectory).Path
$tool=(Resolve-Path -LiteralPath $VpkPath).Path
$icon=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../src/SonoHUD/SonoHUD.App/Assets/Branding/SonoHUD.ico')).Path
if ($PackageId -notmatch '^[a-zA-Z0-9.-]+$' -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw "패키지 식별자 또는 버전 형식 오류" }
foreach ($file in 'SonoHUD.exe','SonoHUD.dll','Velopack.dll','LICENSE','THIRD-PARTY-NOTICES.md') {
    if (-not (Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)) { throw "입력 파일이 없습니다: $file" }
}
$driver=$tool
$prefix=@()
if ([IO.Path]::GetExtension($tool) -eq '.dll') { $driver=(Get-Command dotnet.exe -CommandType Application).Source; $prefix=@($tool) }
$toolRoot=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($tool)) '../../..'))
$native=Get-Content -LiteralPath (Join-Path $toolRoot 'sonohud-native-build.json') -Raw | ConvertFrom-Json
if ($native.target -cne 'x86_64-pc-windows-gnullvm' -or $native.version -ne '1.2.161' -or $native.sourceCommit -ne '92d6a1c91716729d449034df5c50307dcce39493') { throw '검증한 Velopack 소스가 필요합니다.' }
foreach($name in 'setup','update','stub'){
    $expected=$native.helpers.$name
    $actual=(Get-FileHash -LiteralPath (Join-Path $toolRoot "vendor/${name}_x64.exe")).Hash
    if ($expected -ne $actual) {throw "Velopack 도구 파일을 확인하세요: $name"}
}
$pin=& $driver @prefix --help
if ($LASTEXITCODE -ne 0 -or ($pin -join ' ') -notmatch '1\.2\.161') { throw "이 소스는 vpk 1.2.161을 사용합니다." }
$dest=[IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $dest)) {New-Item -ItemType Directory -Path $dest | Out-Null}
if (Get-ChildItem -LiteralPath $dest -Filter "$PackageId-$Version-stable-full.nupkg") { throw "이 버전의 패키지가 이미 있습니다." }
& $driver @prefix pack --packId $PackageId --packVersion $Version --packDir $source --outputDir $dest --channel stable --runtime win-x64 --mainExe SonoHUD.exe --packAuthors SonoHUD --packTitle $DisplayName --icon $icon --framework net10-x64-desktop --shortcuts StartMenuRoot --instLocation PerUser --delta None
if ($LASTEXITCODE -ne 0) {throw "설치 프로그램 생성 실패"}
$setup=@(Get-ChildItem -LiteralPath $dest -Filter '*-Setup.exe')
if ($setup.Count -ne 1) {throw "설치 프로그램을 식별할 수 없습니다."}
$setup[0].FullName

