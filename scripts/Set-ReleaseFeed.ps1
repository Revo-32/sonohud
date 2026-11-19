# Generates the reviewed public trust policy in a clean release checkout, before building.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Owner, [Parameter(Mandatory)][string]$Repository,
      [Parameter(Mandatory)][string]$PublicKey, [string]$NextPublicKey)
$ErrorActionPreference='Stop'
if ($Owner -cnotmatch '^[A-Za-z0-9-]{1,39}$' -or $Repository -cnotmatch '^[A-Za-z0-9_.-]{1,100}$' -or $Repository -in '.', '..') {
    throw 'GitHub 소유자와 저장소 이름을 확인하세요.'
}
$keys = foreach ($value in @($PublicKey,$NextPublicKey)) {
    if (!$value) {continue}
    $bytes=[Convert]::FromBase64String($value)
    $ec=[Security.Cryptography.ECDsa]::Create()
    try {
        $used=0; $ec.ImportSubjectPublicKeyInfo($bytes,[ref]$used)
        if ($used -ne $bytes.Length -or $ec.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') {throw 'P-256 공개키가 필요합니다.'}
        [Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo())
    } finally {$ec.Dispose()}
}
$expressions=@($keys | ForEach-Object {'Convert.FromBase64String("' + $_ + '")'})
$source=@"
namespace SonoHUD.App.Deployment;

internal sealed partial class ReleasePolicy
{
    partial void ConfigureRelease()
    {
        Feed = StableFeed.FromGitHubReleases(PackageId, "$Owner", "$Repository", $($expressions -join ', '));
    }
}
"@
$path=Join-Path $PSScriptRoot '../src/SonoHUD/SonoHUD.App/Deployment/ReleaseChannel.g.cs'
if (Test-Path -LiteralPath $path) {
    if ([IO.File]::ReadAllText($path).TrimEnd() -cne $source.TrimEnd()) {throw '기존 신뢰 설정과 다릅니다. 키 교체는 소스 변경으로 검토하세요.'}
} else {[IO.File]::WriteAllText($path,$source+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
