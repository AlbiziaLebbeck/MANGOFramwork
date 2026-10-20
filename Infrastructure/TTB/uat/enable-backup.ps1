param([Parameter(Mandatory)][string]$RestoreEvidenceDirectory,[Parameter(Mandatory)][string]$TransferDirectory)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
function Under-Root([string]$path,[string]$relativeRoot) {
    $resolved=[IO.Path]::GetFullPath($path); $root=[IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativeRoot))
    if (-not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must be in the owned local recovery/backup directory.' }
    return $resolved
}
$recovery=Under-Root $RestoreEvidenceDirectory '.artifacts/ttb-recovery'
$transferRoot=Under-Root $TransferDirectory '.artifacts/ttb-backups'
$proof=Get-Content -LiteralPath (Join-Path $recovery 'restore-drill.json') -Raw | ConvertFrom-Json
$transfer=Get-Content -LiteralPath (Join-Path $transferRoot 'transfer.json') -Raw | ConvertFrom-Json
if ($proof.status -ne 'verified' -or $proof.environment -ne 'Uat' -or $proof.databaseRestored -ne $true -or $proof.dataMatch -ne $true -or
    $proof.workersStarted -ne $false -or $proof.networkEgressAllowed -ne $false -or $proof.tcpListener -ne $false -or $proof.comparedTables -lt 43 -or
    $transfer.environment -ne 'Uat' -or $transfer.database -ne 'ttb_uat' -or $transfer.sourceFinalized -ne $true -or $transfer.offServerCiphertextVerified -ne $true -or
    $proof.archiveSha256 -ne $transfer.archiveSha256 -or $proof.archiveSha256 -notmatch '^[a-f0-9]{64}$' -or
    $transfer.job -notmatch '^ttb-uat-backup-[a-zA-Z0-9-]{1,100}$' -or $transfer.archive -notmatch '^ttb-uat-[a-zA-Z0-9-]+\.tar\.gz\.p7m$') { throw 'A completed matching isolated UAT restore and off-server ciphertext verification are required.' }
$archive=Join-Path $transferRoot $transfer.archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $proof.archiveSha256) { throw 'Verified off-server archive is missing or changed.' }
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$job=(& ssh @sshArgs "kubectl -n ttb-uat get job '$($transfer.job)' -o json") -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not @($job.status.conditions | Where-Object { $_.type -eq 'Complete' -and $_.status -eq 'True' }).Count) { throw 'Original UAT backup producer cannot be verified complete.' }
'{"spec":{"suspend":false}}' | & ssh @sshArgs 'kubectl -n ttb-uat patch cronjob ttb-uat-backup --type=merge --patch-file=/dev/stdin'
if ($LASTEXITCODE -ne 0) { throw 'Cannot enable the verified UAT schedule.' }
$cron=(& ssh @sshArgs 'kubectl -n ttb-uat get cronjob ttb-uat-backup -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $cron.spec.suspend -ne $false -or $cron.spec.schedule -ne '*/15 * * * *' -or $cron.spec.timeZone -ne 'Etc/UTC' -or $cron.spec.concurrencyPolicy -ne 'Forbid') { throw 'UAT schedule state differs; inspect without creating another producer.' }
@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');environment='Uat';schedule=$cron.spec.schedule;timeZone=$cron.spec.timeZone;suspended=$false;restoreArchiveSha256=$proof.archiveSha256;continuousOffServerRpoCertified=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $recovery 'schedule-enabled.json') -Encoding utf8
Write-Output 'Enabled only the verified UAT local encrypted backup every 15 minutes; off-server replication/retention and production recovery remain separate acceptance gates.'
