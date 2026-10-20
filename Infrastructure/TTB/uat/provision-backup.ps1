param([switch]$StartDrill)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$recipientRoot = Join-Path $repositoryRoot '.tools/local/uat-backup-recipient'
$pemPath = Join-Path $recipientRoot 'recipient.pem'
$metadata = Get-Content -LiteralPath (Join-Path $recipientRoot 'recipient.json') -Raw | ConvertFrom-Json
$pem = Get-Content -LiteralPath $pemPath -Raw
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($pem)
try {
    $fingerprint = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($certificate.RawData))
    if ($fingerprint -ne $metadata.recipientSha256 -or $certificate.NotAfter.ToUniversalTime() -lt [DateTime]::UtcNow.AddDays(1)) { throw 'The UAT recipient identity/expiry is invalid.' }
} finally { $certificate.Dispose() }
$sshArgs = @('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$secretOutput = @(& ssh @sshArgs 'kubectl -n ttb-uat get secret ttb-backup-credentials --ignore-not-found -o name')
$secretName = ($secretOutput -join '').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the owned UAT backup secret.' }
$roleExists = ('SELECT EXISTS(SELECT FROM pg_roles WHERE rolname=''ttb_backup'');' | & ssh @sshArgs 'kubectl -n ttb-uat exec -i deployment/ttb-postgres -- psql -U ttb_owner -d ttb_uat -At -v ON_ERROR_STOP=1').Trim()
if ($LASTEXITCODE -ne 0 -or $roleExists -notin @('t','f')) { throw 'Cannot inspect the owned UAT backup role.' }
if ($roleExists -eq 't' -and -not $secretName) { throw 'Backup role exists without its credential secret. Preserve it and investigate; do not rotate blindly.' }
if (-not $secretName) {
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    @{ apiVersion='v1';kind='Secret';metadata=@{name='ttb-backup-credentials';namespace='ttb-uat'};type='Opaque';stringData=@{password=$password} } |
        ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl create -f -'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the isolated UAT backup credential.' }
} elseif ($roleExists -eq 'f') {
    # Recover an interrupted first provisioning from the existing owned secret without logging its contents.
    $encoded = (& ssh @sshArgs 'kubectl -n ttb-uat get secret ttb-backup-credentials -o jsonpath="{.data.password}"').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resume owned backup-role provisioning.' }
    $password = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))
}
if ($roleExists -eq 'f') {
    if ($password -notmatch '^[a-f0-9]{64}$') { throw 'Unexpected owned backup credential format; preserved.' }
    "CREATE ROLE ttb_backup LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '$password';" |
        & ssh @sshArgs 'kubectl -n ttb-uat exec -i deployment/ttb-postgres -- psql -U ttb_owner -d ttb_uat -v ON_ERROR_STOP=1'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the read-only UAT backup role.' }
}
$grants = @'
GRANT CONNECT ON DATABASE ttb_uat TO ttb_backup;
GRANT USAGE ON SCHEMA ttb, public TO ttb_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA ttb, public TO ttb_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA ttb, public TO ttb_backup;
ALTER DEFAULT PRIVILEGES FOR ROLE ttb_owner IN SCHEMA ttb, public GRANT SELECT ON TABLES TO ttb_backup;
ALTER DEFAULT PRIVILEGES FOR ROLE ttb_owner IN SCHEMA ttb, public GRANT SELECT ON SEQUENCES TO ttb_backup;
'@
$grants | & ssh @sshArgs 'kubectl -n ttb-uat exec -i deployment/ttb-postgres -- psql -U ttb_owner -d ttb_uat -v ON_ERROR_STOP=1'
if ($LASTEXITCODE -ne 0) { throw 'Cannot grant bounded UAT backup reads.' }
@{apiVersion='v1';kind='ConfigMap';metadata=@{name='ttb-backup-recipient';namespace='ttb-uat'};data=@{'recipient.pem'=$pem;sha256=$fingerprint}} |
    ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot install the public UAT backup recipient.' }
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backup.yaml') -Raw | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the suspended UAT backup job.' }
if ($StartDrill) {
    $jobName = 'ttb-uat-backup-drill-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss',[Globalization.CultureInfo]::InvariantCulture)
    & ssh @sshArgs "kubectl -n ttb-uat create job --from=cronjob/ttb-uat-backup '$jobName'"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start the isolated UAT backup producer drill.' }
    Write-Output ('Inspect producer job: ' + $jobName)
}
Write-Output 'UAT backup resources are prepared and recurring schedule remains suspended pending authenticated off-server restore verification.'
