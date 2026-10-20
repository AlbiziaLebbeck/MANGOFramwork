param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory = Join-Path $root ".tools/bank-callback/Uat/$RunId"
$sshArguments = @('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=12','ntserver02')
if (Test-Path -LiteralPath $directory) { throw 'This preparation run already exists. Inspect state.json; do not rotate bank credentials after an uncertain result.' }
$existing = & ssh @sshArguments 'kubectl -n ttb-uat get secret ttb-bank --ignore-not-found -o name'
if ($LASTEXITCODE -ne 0 -or $existing) { throw 'An existing bank profile must be inspected rather than replaced.' }
$healthJson = & ssh @sshArguments 'kubectl -n ttb-uat exec deployment/ttb-commerce-api -- curl -fsS http://127.0.0.1:8080/health/ready'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect UAT readiness.' }
$health = $healthJson | ConvertFrom-Json
if ($health.paymentMode -ne 'Bank' -or $health.paymentsReady -or $health.recoveryFrozen) { throw 'Expected unfrozen UAT with checkout still closed.' }
New-Item -ItemType Directory -Path $directory | Out-Null
$username = 'ttb-uat-' + $RunId.Substring(0,12)
$password = [Convert]::ToHexStringLower([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$handoff = @{ environment='Uat'; url='https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification'; authentication='Basic over HTTPS'; username=$username; password=$password }
$bytes = [Text.Encoding]::UTF8.GetBytes(($handoff | ConvertTo-Json -Compress))
try {
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes,[Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/Callback/v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $directory 'handoff.dpapi'),$encrypted)
} finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes) }
$state = @{ runId=$RunId; phase='secret-create-attempted'; environment='Uat'; merchantId='PYA'; billerNo='075555900002101'; issuer='jwt-key-pya'; bankCredentialHandoffConfirmed=$false; actualBankSourceValidated=$false; productionChanged=$false }
$state | ConvertTo-Json | Set-Content (Join-Path $directory 'state.json') -Encoding utf8
$secret = @{ apiVersion='v1'; kind='Secret'; metadata=@{name='ttb-bank';namespace='ttb-uat';labels=@{'app.kubernetes.io/part-of'='ttb-commerce';'mangosgo.com/profile-run'=$RunId}};type='Opaque';stringData=@{
    Ttb__MerchantId='PYA'; Ttb__BillerNo='075555900002101'; Ttb__Issuer='jwt-key-pya'; Ttb__QrProfileApproved='true';
    Ttb__CallbackAuthentication='Basic'; Ttb__CallbackProfileApproved='true'; Ttb__CallbackUsername=$username; Ttb__CallbackPassword=$password;
    Ttb__CallbackSourceIpValidated='false'
} }
try {
    $created = $secret | ConvertTo-Json -Depth 8 -Compress | & ssh @sshArguments 'kubectl create -f - -o name'
    if ($LASTEXITCODE -ne 0 -or ($created -join '').Trim() -ne 'secret/ttb-bank') { throw 'Secret creation is unconfirmed. Preserve the encrypted handoff and inspect the named Secret before retrying.' }
    $state.phase='created'; $state | ConvertTo-Json | Set-Content (Join-Path $directory 'state.json') -Encoding utf8
} finally { $password=$null; $handoff=$null; $secret=$null }
Write-Output "UAT profile prepared. Credentials are DPAPI encrypted for this PC account: $directory/handoff.dpapi"
Write-Output 'No Production settings, source-acceptance flag, checkout gate or public ingress was changed. Roll out the tested UAT release separately.'
