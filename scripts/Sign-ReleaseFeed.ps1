# Signs exact feed bytes after verifying the only permitted stable asset.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$PublicKey,
      [switch]$VerifyOnly,[switch]$WindowsUserKey)
$ErrorActionPreference='Stop'
$index=Join-Path $Directory 'releases.stable.json'
$sig=$index+'.sig'
$raw=[IO.File]::ReadAllBytes($index)
$document=[Text.Encoding]::UTF8.GetString($raw) | ConvertFrom-Json
$items=@($document.Assets)
if ($items.Count -ne 1) {throw 'stable 인덱스는 현재 버전 하나여야 합니다.'}
$item=$items[0]
$name="SonoHUD.Core-$($item.Version)-stable-full.nupkg"
if ($item.PackageId -cne 'SonoHUD.Core' -or $item.Version -notmatch '^\d+\.\d+\.\d+$' -or
    $item.Type -cne 'Full' -or $item.FileName -cne $name) {throw '서명할 파일의 제품·버전·채널을 확인하세요.'}
$file=Get-Item -LiteralPath (Join-Path $Directory $name)
if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -ne $item.Size -or
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $item.SHA256) {throw 'full 패키지 무결성 오류'}
$ec=[Security.Cryptography.ECDsa]::Create()
$alg=[Security.Cryptography.HashAlgorithmName]::SHA256
$format=[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
try {
    $used=0; $public=[Convert]::FromBase64String($PublicKey); $ec.ImportSubjectPublicKeyInfo($public,[ref]$used)
    if ($used -ne $public.Length -or $ec.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') {throw 'P-256 공개키 오류'}
    if ($VerifyOnly) {
        if (!$ec.VerifyData($raw,[IO.File]::ReadAllBytes($sig),$alg,$format)) {throw '인덱스 서명이 공개키와 일치하지 않습니다.'}
    } else {
        if ($WindowsUserKey) {
            $info=& "$PSScriptRoot/Manage-ReleaseKey.ps1"
            if ($info.spki -cne $PublicKey) {throw '현재 Windows 운영 키가 소스의 공개키와 일치하지 않습니다.'}
            $keyFile=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SonoHUD.ReleaseSigning/stable-p256-v1.pk8.dpapi'
            $secret=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        } else {
            if (!$env:SONOHUD_UPDATE_PKCS8) {throw '보호된 서명 환경에 PKCS#8 키가 필요합니다.'}
            $secret=[Convert]::FromBase64String($env:SONOHUD_UPDATE_PKCS8)
        }
        try {
            $ec.ImportPkcs8PrivateKey($secret,[ref]$used)
            if ($used -ne $secret.Length -or [Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo()) -cne $PublicKey) {throw '서명키가 검증키와 다릅니다.'}
            $signature=$ec.SignData($raw,$alg,$format)
            if (!$ec.VerifyData($raw,$signature,$alg,$format)) {throw '서명 자체 확인 실패'}
            $out=[IO.File]::Open($sig,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try {$out.Write($signature,0,$signature.Length)} finally {$out.Dispose()}
        } finally {[Array]::Clear($secret,0,$secret.Length)}
    }
} finally {$ec.Dispose()}
