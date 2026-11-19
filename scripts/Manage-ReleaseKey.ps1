#requires -Version 7.2
[CmdletBinding(DefaultParameterSetName='Read')]
param([Parameter(Mandatory,ParameterSetName='Create')][switch]$Create,
      [Parameter(Mandatory,ParameterSetName='Backup')][string]$BackupPath,
      [Parameter(Mandatory,ParameterSetName='Restore')][string]$RestorePath)
$ErrorActionPreference='Stop'
if (!$IsWindows) {throw '현재 Windows 계정이 필요합니다.'}
$folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SonoHUD.ReleaseSigning'
$file=Join-Path $folder 'stable-p256-v1.pk8.dpapi'
$owner=[Security.Principal.WindowsIdentity]::GetCurrent().User
$system=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
function Assert-PrivateDirectory {
    $node=Get-Item -LiteralPath $folder
    if($node.Attributes -band [IO.FileAttributes]::ReparsePoint){throw '키 저장 폴더는 실제 디렉터리여야 합니다.'}
    $acl=Get-Acl -LiteralPath $folder
    if(!$acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $owner.Value){throw '키 저장소 소유자 또는 상속 권한 오류'}
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.IdentityReference.Value -notin $owner.Value,$system.Value){throw '키 저장소에 허용하지 않은 주체가 있습니다.'}
    }
}
function Save-New([string]$path,[byte[]]$data){
    $f=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {$f.Write($data,0,$data.Length);$f.Flush($true)}finally{$f.Dispose()}
}
if(!(Test-Path -LiteralPath $folder)){
    if(!$Create -and !$RestorePath){throw '운영 키가 없습니다.'}
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($owner);$acl.SetAccessRuleProtection($true,$false)
    foreach($sid in $owner,$system){
        $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit, ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
    }
    [IO.FileSystemAclExtensions]::Create([IO.DirectoryInfo]::new($folder),$acl)
}
Assert-PrivateDirectory
$scope=[Security.Cryptography.DataProtectionScope]::CurrentUser
$ec=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
$raw=$null
try {
    if($RestorePath){
        if(Test-Path -LiteralPath $file){throw '기존 운영 키가 있어 복원하지 않았습니다.'}
        $pw=Read-Host '백업 암호' -AsSecureString
        try {
            $used=0;$blob=[IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $RestorePath))
            $ec.ImportEncryptedPkcs8PrivateKey([Net.NetworkCredential]::new('',$pw).Password,$blob,[ref]$used)
            if($used -ne $blob.Length -or $ec.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7'){throw 'P-256 백업 오류'}
        }finally{$pw.Dispose()}
    }
    if(!(Test-Path -LiteralPath $file)){
        if(!$Create -and !$RestorePath){throw '운영 키가 없습니다.'}
        $raw=$ec.ExportPkcs8PrivateKey()
        Save-New $file ([Security.Cryptography.ProtectedData]::Protect($raw,$null,$scope))
        [Array]::Clear($raw);$raw=$null
    }
    if((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '키 파일 유형 오류'}
    $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file),$null,$scope)
    $used=0;$ec.ImportPkcs8PrivateKey($raw,[ref]$used)
    if($used -ne $raw.Length -or $ec.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7'){throw '운영 키 형식 오류'}
    if($BackupPath){
        $pw=Read-Host '오프라인 키 백업의 긴 암호' -AsSecureString
        try {
            if($pw.Length -lt 16){throw '16자 이상의 암호를 지정하세요.'}
            $pbe=[Security.Cryptography.PbeParameters]::new('Aes256Cbc','SHA256',600000)
            $encrypted=$ec.ExportEncryptedPkcs8PrivateKey([Net.NetworkCredential]::new('',$pw).Password,$pbe)
            Save-New ([IO.Path]::GetFullPath($BackupPath)) $encrypted
        }finally{$pw.Dispose()}
    }
    $public=$ec.ExportSubjectPublicKeyInfo()
    [pscustomobject]@{algorithm='ECDSA-P256-SHA256';keyId=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($public)).ToLowerInvariant();spki=[Convert]::ToBase64String($public)}
}finally{if($raw){[Array]::Clear($raw)};$ec.Dispose()}
