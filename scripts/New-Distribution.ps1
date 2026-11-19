# Assemble public candidate files from the same reviewed source tree as the portable build.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$WorkDirectory,
      [Parameter(Mandatory)][string]$EvidenceDirectory,[string]$UpstreamCacheDirectory,
      [string]$ToolchainDirectory,[string]$VpkPackage,[string]$Exclusions)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dest=[IO.Path]::GetFullPath($OutputDirectory)
$work=[IO.Path]::GetFullPath($WorkDirectory)
$ev=[IO.Path]::GetFullPath($EvidenceDirectory)
foreach ($folder in @($dest,$work,$ev)) {
    if (Test-Path -LiteralPath $folder) {throw "새 디렉터리를 지정하세요: $folder"}
}
$pair=@{OutputDirectory=$dest;WorkDirectory=$work;EvidenceDirectory=$ev}
if ($UpstreamCacheDirectory) {$pair.UpstreamCacheDirectory=$UpstreamCacheDirectory}
& "$PSScriptRoot/New-ReleaseCandidate.ps1" @pair
if (!$?) {throw '독립 Release 빌드 실패'}
$srcZip=@(Get-ChildItem -LiteralPath $dest -Filter '*-source.zip')
if ($srcZip.Count -ne 1) {throw '대응 소스 입력이 하나여야 합니다.'}
$src=Join-Path $work 'native-input'
[IO.Compression.ZipFile]::ExtractToDirectory($srcZip[0].FullName,$src)
if (!$ToolchainDirectory) {
    $ToolchainDirectory=Join-Path $work 'toolchain'
    & python -B "$src/scripts/Install-NativeToolchain.py" $ToolchainDirectory
    if ($LASTEXITCODE) {throw '고정 빌드 도구를 준비하지 못했습니다.'}
}
if (!$VpkPackage) {
    $VpkPackage=Join-Path $work 'vpk.1.2.161.nupkg'
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/vpk/1.2.161/vpk.1.2.161.nupkg' -OutFile $VpkPackage
}
$vendorRoot=Join-Path $work 'vpk'
& "$src/scripts/Build-Velopack-Windows.ps1" -SourceDirectory "$src/vendor-source/Velopack" -OutputDirectory $vendorRoot -ToolPackage $VpkPackage -RustBinDirectory "$ToolchainDirectory/rust/bin" -LlvmBinDirectory "$ToolchainDirectory/llvm-mingw-20261117-ucrt-x86_64/bin"
if (!$?) {throw 'Windows 설치 도구 빌드 실패'}
$pack=Join-Path $work 'installer'
& "$src/scripts/New-CoreSetup.ps1" -PublishDirectory "$work/publish-1" -OutputDirectory $pack -VpkPath "$vendorRoot/tools/net10.0/any/vpk.dll"
if (!$?) {throw '설치 파일 생성 실패'}
$setup=@(Get-ChildItem -LiteralPath $pack -Filter '*-Setup.exe' -File)
$full=@(Get-ChildItem -LiteralPath $pack -Filter '*-stable-full.nupkg' -File)
if ($setup.Count -ne 1 -or $full.Count -ne 1) {throw 'Setup 또는 full 패키지가 하나여야 합니다.'}
Copy-Item -LiteralPath $setup[0].FullName -Destination (Join-Path $dest 'SonoHUD-Setup.exe')
Copy-Item -LiteralPath $full[0].FullName,(Join-Path $pack 'releases.stable.json') -Destination $dest
$inspect=@("$src/scripts/audit-release.py",'--directory',$dest,'--vendor',"$vendorRoot/vendor",'--source-tree',"$work/source-snapshot",'--result',"$ev/public-audit.json")
if ($Exclusions) {$inspect+=@('--exclusions',(Resolve-Path -LiteralPath $Exclusions).Path)}
& python -B @inspect
if ($LASTEXITCODE) {throw '공개 파일 및 대응 소스 검증 실패'}
$sum=Get-ChildItem -File -LiteralPath $dest | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$_.Name
}
[IO.File]::WriteAllLines((Join-Path $dest 'SHA256SUMS.txt'),[string[]]$sum,[Text.UTF8Encoding]::new($false))

& python -B "$src/scripts/check-manifest.py" $dest
if ($LASTEXITCODE) {throw '최종 업로드 목록 또는 해시 불일치'}
