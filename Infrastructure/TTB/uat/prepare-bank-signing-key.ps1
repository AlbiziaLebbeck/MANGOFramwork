param([Parameter(Mandatory)][ValidateSet('create','observe')][string]$Phase,[Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory=Join-Path $root ".tools/bank-signing/Uat/$RunId"
$publicDirectory=Join-Path $root 'Documentation/Provisioning'
$publicPath=Join-Path $publicDirectory ("ttb-uat-jwt-$RunId-public.pem")
$statePath=Join-Path $directory 'state.json'
$sshArguments=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
if($Phase -eq 'create'){
  if(Test-Path -LiteralPath $directory){throw 'This key run exists. Observe it; never regenerate or replace a key after a timeout.'}
  $existing=& ssh @sshArguments 'kubectl -n ttb-uat get secret ttb-signing-key --ignore-not-found -o name'
  if($LASTEXITCODE -ne 0 -or $existing){throw 'An existing UAT signing Secret must be inspected rather than replaced.'}
  $ready=& ssh @sshArguments 'kubectl -n ttb-uat exec deployment/ttb-commerce-api -- curl -fsS http://127.0.0.1:8080/health/ready'
  if($LASTEXITCODE -ne 0){throw 'Cannot inspect UAT readiness.'}
  $health=$ready|ConvertFrom-Json
  if($health.status -ne 'ready' -or $health.paymentMode -ne 'Bank' -or $health.paymentsReady){throw 'Expected ready UAT with bank payments closed.'}
  New-Item -ItemType Directory -Path $directory | Out-Null
  New-Item -ItemType Directory -Path $publicDirectory -Force | Out-Null
  $rsa=[Security.Cryptography.RSA]::Create(2048)
  try{
    $publicDer=$rsa.ExportSubjectPublicKeyInfo()
    $fingerprint=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($publicDer))
    $private=$rsa.ExportPkcs8PrivateKey()
    $entropy=[Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/BankSigning/v1')
    $encrypted=[Security.Cryptography.ProtectedData]::Protect($private,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $directory 'private.pkcs8.dpapi'),$encrypted)
    [IO.File]::WriteAllText($publicPath,$rsa.ExportSubjectPublicKeyInfoPem(),[Text.UTF8Encoding]::new($false))
    $challenge=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $signature=$rsa.SignData($challenge,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $verifier=[Security.Cryptography.RSA]::Create();try{$verifier.ImportSubjectPublicKeyInfo($publicDer,[ref]$null);if(-not $verifier.VerifyData($challenge,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'The prepared signing pair failed its offline signature proof.'}}finally{$verifier.Dispose()}
    $state=@{runId=$RunId;environment='Uat';phase='secret-create-attempted';publicKeySpkiSha256=$fingerprint;rsaBits=2048;offlineSignatureVerified=$true;publicFile=[IO.Path]::GetRelativePath($root,$publicPath).Replace('\','/');bankRegistrationConfirmed=$false;bankRequestMade=$false;bankPaymentsEnabled=$false}
    $state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding utf8
    $pem=$rsa.ExportPkcs8PrivateKeyPem()
    $secret=@{apiVersion='v1';kind='Secret';metadata=@{name='ttb-signing-key';namespace='ttb-uat';labels=@{'app.kubernetes.io/part-of'='ttb-commerce';'mangosgo.com/key-run'=$RunId};annotations=@{'mangosgo.com/public-key-spki-sha256'=$fingerprint;'mangosgo.com/bank-registration'='pending'}};type='Opaque';immutable=$true;data=@{'private.pem'=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pem))}}
    $created=$secret|ConvertTo-Json -Depth 6 -Compress|& ssh @sshArguments 'kubectl create -f - -o jsonpath="{.metadata.uid}"'
    if($LASTEXITCODE -ne 0){throw 'Secret creation was not confirmed. Inspect this named run and existing Secret before any action.'}
    $state.secretUid=($created -join '').Trim();$state.phase='created';$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding utf8
  }finally{if($null -ne $private){[Security.Cryptography.CryptographicOperations]::ZeroMemory($private)};$pem=$null;$secret=$null;$rsa.Dispose()}
}else{$state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json -AsHashtable}
$check=@'
import base64,hashlib,json,subprocess
secret=json.loads(subprocess.check_output(['kubectl','-n','ttb-uat','get','secret','ttb-signing-key','-o','json']))
key=subprocess.run(['openssl','pkey','-pubout','-outform','DER'],input=base64.b64decode(secret['data']['private.pem']),capture_output=True)
if key.returncode:raise SystemExit('The existing UAT Secret did not contain a usable signing key')
projected=subprocess.run(['kubectl','-n','ttb-uat','exec','deployment/ttb-commerce-api','--','openssl','pkey','-in','/run/ttb-key/private.pem','-pubout','-outform','DER'],capture_output=True)
print(json.dumps({'secretUid':secret['metadata']['uid'],'runId':secret['metadata']['labels']['mangosgo.com/key-run'],'publicKeySpkiSha256':hashlib.sha256(key.stdout).hexdigest(),'annotationMatches':secret['metadata']['annotations']['mangosgo.com/public-key-spki-sha256']==hashlib.sha256(key.stdout).hexdigest(),'immutable':secret['immutable'],'apiProjectedKeyMatches':projected.returncode==0 and projected.stdout==key.stdout}))
'@
$identity=$check|& ssh @sshArguments 'python3 -'
if($LASTEXITCODE -ne 0){throw 'Cannot observe the existing key identity.'}
$observed=$identity|ConvertFrom-Json
if($observed.runId -ne $RunId -or $observed.publicKeySpkiSha256 -ne $state.publicKeySpkiSha256 -or -not $observed.immutable -or -not $observed.annotationMatches){throw 'The existing Secret is not this prepared UAT signing key.'}
if($state.ContainsKey('secretUid') -and $state.secretUid -ne $observed.secretUid){throw 'The prepared Secret was replaced.'}
$state.secretUid=$observed.secretUid
$publicVerifier=[Security.Cryptography.RSA]::Create()
try{
  $publicVerifier.ImportFromPem([IO.File]::ReadAllText($publicPath))
  $publicFingerprint=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($publicVerifier.ExportSubjectPublicKeyInfo()))
  if($publicFingerprint -ne $state.publicKeySpkiSha256){throw 'Public registration file changed.'}
  $state.publicFileVerified=$true
}finally{$publicVerifier.Dispose()}
$recovered=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $directory 'private.pkcs8.dpapi')),[Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/BankSigning/v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
$recoveryVerifier=[Security.Cryptography.RSA]::Create()
try{
  $recoveryVerifier.ImportPkcs8PrivateKey($recovered,[ref]$null)
  if([Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($recoveryVerifier.ExportSubjectPublicKeyInfo())) -ne $state.publicKeySpkiSha256){throw 'The encrypted PC key copy did not match.'}
  $state.pcEncryptedRecoveryVerified=$true
}finally{[Security.Cryptography.CryptographicOperations]::ZeroMemory($recovered);$recoveryVerifier.Dispose()}
$state.apiProjectedKeyMatches=$observed.apiProjectedKeyMatches
$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding utf8
$state|ConvertTo-Json
