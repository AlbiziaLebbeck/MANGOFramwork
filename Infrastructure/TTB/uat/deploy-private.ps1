param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidatePattern('^[a-z0-9][a-z0-9-]{2,31}$')][string]$ReleaseId = ([DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '-api')
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.artifacts'))
$resolvedArtifact = [IO.Path]::GetFullPath($ArtifactDirectory)
if (-not $resolvedArtifact.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release artifacts must be inside the repository .artifacts directory.' }
if (-not (Test-Path -LiteralPath (Join-Path $resolvedArtifact 'TtbCommerce.Api'))) { throw 'A self-contained Linux API artifact is required.' }
$compatibilityPath = Join-Path $resolvedArtifact 'release-compatibility.json'
if (-not (Test-Path -LiteralPath $compatibilityPath)) { throw 'Release reader compatibility declaration is required. Pre-manual-refund readers cannot be rolled back into this database.' }
$readerContract = (Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json).readerContract
if ($readerContract -isnot [long] -and $readerContract -isnot [int]) { throw 'Reader contract must be an integer.' }
if ($readerContract -lt 2 -or $readerContract -gt 100) { throw 'Unsupported reader contract.' }
$archive = Join-Path $artifactRoot ($ReleaseId + '.tar.gz')
& tar -czf $archive -C $resolvedArtifact .
if ($LASTEXITCODE -ne 0) { throw 'Cannot package release.' }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$sshArguments = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=12', '-o', 'StrictHostKeyChecking=yes', 'ntserver02')
$readerFloor = (& ssh @sshArguments 'if test -f /opt/ttb-commerce/uat/minimum-reader-contract; then cat /opt/ttb-commerce/uat/minimum-reader-contract; else printf 1; fi').Trim()
if ($LASTEXITCODE -ne 0 -or $readerFloor -notmatch '^\d{1,3}$') { throw 'Cannot verify the UAT minimum reader contract.' }
if ($readerContract -lt [int]$readerFloor) { throw 'This release is too old to read existing financial/return records. Use a compatible forward release.' }
$remoteRelease = "/opt/ttb-commerce/uat/releases/$ReleaseId"
& ssh @sshArguments "test ! -e '$remoteRelease' && mkdir -p '$remoteRelease/api'"
if ($LASTEXITCODE -ne 0) { throw 'Release path exists or cannot be prepared. Use a new immutable release ID.' }
& scp -o BatchMode=yes -o ConnectTimeout=12 -o StrictHostKeyChecking=yes $archive "ntserver02:$remoteRelease/api.tar.gz"
if ($LASTEXITCODE -ne 0) { throw 'Release upload failed.' }
$prepare = @"
set -eu
printf '%s  %s\n' '$archiveHash' '$remoteRelease/api.tar.gz' | sha256sum -c -
tar -xzf '$remoteRelease/api.tar.gz' -C '$remoteRelease/api'
chmod -R a+rX '$remoteRelease/api'
chmod a+rx '$remoteRelease/api/TtbCommerce.Api'
if [ -f '$remoteRelease/api/.playwright/node/linux-x64/node' ]; then chmod a+rx '$remoteRelease/api/.playwright/node/linux-x64/node'; fi
if [ -f '$remoteRelease/api/ReceiptProbe' ]; then chmod a+rx '$remoteRelease/api/ReceiptProbe'; fi
"@
$prepare | & ssh @sshArguments 'tr -d "\r" | bash -s'
if ($LASTEXITCODE -ne 0) { throw 'Release verification/extraction failed.' }
# Fence incompatible rollback before any schema/new-reader rollout. Never lower this floor or erase financial evidence to make an older binary start.
& ssh @sshArguments "printf '%s\n' '$readerContract' > /opt/ttb-commerce/uat/minimum-reader-contract"
if ($LASTEXITCODE -ne 0) { throw 'Cannot record the minimum compatible reader contract.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'migrate.yaml') -Raw).Replace('__RELEASE__', $ReleaseId) | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot create migration job.' }
& ssh @sshArguments "kubectl -n ttb-uat wait --for=condition=complete 'job/ttb-migrate-$ReleaseId' --timeout=60s"
if ($LASTEXITCODE -ne 0) { throw 'Migration job failed. Inspect its safe logs before any retry/deployment.' }
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'api-network-policy.yaml') -Raw | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot constrain UAT API ingress traffic.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'api.yaml') -Raw).Replace('__RELEASE__', $ReleaseId) | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot deploy private UAT API.' }
& ssh @sshArguments 'kubectl -n ttb-uat rollout status deployment/ttb-commerce-api --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'Private UAT rollout failed.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'worker.yaml') -Raw).Replace('__RELEASE__', $ReleaseId) | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot deploy UAT commerce worker.' }
& ssh @sshArguments 'kubectl -n ttb-uat rollout status deployment/ttb-commerce-worker --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'UAT commerce worker rollout failed.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'receipt-worker.yaml') -Raw).Replace('__RELEASE__', $ReleaseId) | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot deploy UAT receipt worker.' }
& ssh @sshArguments 'kubectl -n ttb-uat rollout status deployment/ttb-receipt-worker --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'UAT receipt worker rollout failed.' }
Write-Output "Private UAT release $ReleaseId deployed. No ingress has been changed. Archive SHA256: $archiveHash"
