[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$ToolPackage,
    [Parameter(Mandatory=$true)][string]$RustBinDirectory,
    [Parameter(Mandatory=$true)][string]$LlvmBinDirectory
)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $SourceDirectory).Path
$cli=(Resolve-Path -LiteralPath $ToolPackage).Path
$out=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw '새 도구 폴더를 지정하세요.'}
$version='1.2.161'
$expectedCli='AC9BE73846D9A7A76483F8684ABD1503CD50703B992727429DCDC045589C5E08'
if((Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash -ne $expectedCli){throw 'vpk NuGet SHA-256이 다릅니다.'}
$rust=(Resolve-Path -LiteralPath $RustBinDirectory).Path
$llvm=(Resolve-Path -LiteralPath $LlvmBinDirectory).Path
$compiler=Join-Path $rust 'rustc.exe'
$versionText=& $compiler -vV
if ($LASTEXITCODE -or $versionText -notcontains 'host: x86_64-pc-windows-gnullvm' -or $versionText -notcontains 'release: 1.100.0') {
    throw 'Rust 1.100.0의 x86_64-pc-windows-gnullvm 호스트 도구가 필요합니다.'
}
foreach ($name in 'x86_64-w64-mingw32-clang.exe','x86_64-w64-mingw32-clang++.exe','llvm-ar.exe','windres.exe','llvm-readobj.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $llvm $name) -PathType Leaf)) {throw "llvm-mingw 컴파일 도구가 없습니다: $name"}
}
$manifest=Get-Content -Raw -LiteralPath (Join-Path $source 'src/bins/Cargo.toml')
if($manifest.Contains('webview2-com-sys') -or -not $manifest.Contains('Win32_System_Variant')){throw '변경된 공개 Velopack 소스가 필요합니다.'}
$cleanupSource=Join-Path $source 'src/bins/sonohud-cleanup.c'
$uninstallSource=Join-Path $source 'src/bins/src/commands/uninstall.rs'
foreach($pair in @(@($cleanupSource,'velopack-cleanup.c'),@($uninstallSource,'velopack-uninstall.rs'))){
    if((Get-FileHash -LiteralPath $pair[0]).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $pair[1])).Hash){throw '설치기 소스가 공개된 제거 코드와 다릅니다.'}
}
$cargoHome=if($env:CARGO_HOME){[IO.Path]::GetFullPath($env:CARGO_HOME)}else{Join-Path $env:USERPROFILE '.cargo'}
$target=if($env:CARGO_TARGET_DIR){[IO.Path]::GetFullPath($env:CARGO_TARGET_DIR)}else{Join-Path $source target}
$envChanges=@{
    PATH="$llvm;$rust;$env:SystemRoot/System32;$env:SystemRoot"
    CC='x86_64-w64-mingw32-clang'
    CXX='x86_64-w64-mingw32-clang++'
    AR='llvm-ar'
    CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER='x86_64-w64-mingw32-clang'
    CFLAGS="-ffile-prefix-map=$source=/_/velopack -ffile-prefix-map=$cargoHome=/_/cargo"
    CXXFLAGS="-ffile-prefix-map=$source=/_/velopack -ffile-prefix-map=$cargoHome=/_/cargo"
    CARGO_ENCODED_RUSTFLAGS=(@('-C','strip=symbols','-C','link-arg=-Wl,--no-insert-timestamp',"--remap-path-prefix=$source=/_/velopack","--remap-path-prefix=$cargoHome=/_/cargo") -join [char]31)
}
$previous=@{}
Push-Location -LiteralPath $source
try{
    foreach($key in $envChanges.Keys){
        $previous[$key]=[Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key,$envChanges[$key])
    }
    & (Join-Path $rust 'cargo.exe') build --locked --release --target x86_64-pc-windows-gnullvm -p velopack_bins --features windows --bin setup --bin update --bin stub
    if($LASTEXITCODE -ne 0){throw 'Velopack Windows 도구 빌드에 실패했습니다.'}
    $built=Join-Path $target 'x86_64-pc-windows-gnullvm/release'
    foreach($name in 'setup','update','stub'){
        $exe=Join-Path $built "$name.exe"
        $loadConfig=& (Join-Path $llvm 'llvm-readobj.exe') --coff-load-config $exe
        if($LASTEXITCODE -or !(($loadConfig -join [Environment]::NewLine) -match 'DependentLoadFlags: 0x800')){throw "시스템 DLL 검색 정책 확인 실패: $name"}
        if((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne $version){throw "실행 파일 버전 리소스 불일치: $name"}
        if(!([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($exe)).Contains('requestedExecutionLevel level="asInvoker"'))){throw "사용자 권한 매니페스트 누락: $name"}
    }
    [IO.Compression.ZipFile]::ExtractToDirectory($cli,$out)
    foreach($name in 'setup','update','stub'){
        Copy-Item -LiteralPath (Join-Path $built "$name.exe") -Destination (Join-Path $out "vendor/$($name)_x64.exe") -Force
    }
    $helpers = [ordered]@{}
    foreach($name in 'setup','update','stub'){
        $helpers[$name]=(Get-FileHash -LiteralPath (Join-Path $out "vendor/$($name)_x64.exe")).Hash
    }
    [ordered]@{version=$version;target='x86_64-pc-windows-gnullvm';toolchain='llvm-mingw-20261117-ucrt';sourceCommit='92d6a1c91716729d449034df5c50307dcce39493';cleanupSourceSha256=(Get-FileHash -LiteralPath $cleanupSource).Hash;uninstallSourceSha256=(Get-FileHash -LiteralPath $uninstallSource).Hash;helpers=$helpers} |
        ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8 (Join-Path $out 'sonohud-native-build.json')
    # The CLI finds the x64 helpers under vendor/ relative to its package root.
    Write-Output (Join-Path $out 'tools/net10.0/any/vpk.dll')
}finally{
    foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key])}
    Pop-Location
}
