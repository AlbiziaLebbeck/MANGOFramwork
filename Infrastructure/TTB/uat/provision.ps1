$ErrorActionPreference = 'Stop'
$sshArguments = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=12', '-o', 'StrictHostKeyChecking=yes', 'ntserver02')
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'namespace.yaml') -Raw | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the TTB UAT namespace.' }
$exists = & ssh @sshArguments 'kubectl -n ttb-uat get secret ttb-postgres-bootstrap --ignore-not-found -o name'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect UAT provisioning.' }
if (-not $exists) {
    $ownerPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $appPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $items = @(
        @{ apiVersion = 'v1'; kind = 'Secret'; metadata = @{ name = 'ttb-postgres-bootstrap'; namespace = 'ttb-uat' }; type = 'Opaque'; stringData = @{ 'owner-password' = $ownerPassword; 'app-password' = $appPassword } },
        @{ apiVersion = 'v1'; kind = 'Secret'; metadata = @{ name = 'ttb-database-owner'; namespace = 'ttb-uat' }; type = 'Opaque'; stringData = @{ connection = "Host=ttb-postgres;Database=ttb_uat;Username=ttb_owner;Password=$ownerPassword;Include Error Detail=false" } },
        @{ apiVersion = 'v1'; kind = 'Secret'; metadata = @{ name = 'ttb-commerce-runtime'; namespace = 'ttb-uat' }; type = 'Opaque'; stringData = @{ ConnectionStrings__Commerce = "Host=ttb-postgres;Database=ttb_uat;Username=ttb_app;Password=$appPassword;Include Error Detail=false" } }
    )
    @{ apiVersion = 'v1'; kind = 'List'; items = $items } | ConvertTo-Json -Depth 12 -Compress | & ssh @sshArguments 'kubectl create -f -'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated UAT DB secrets. Inspect named resources; do not rotate initialized passwords blindly.' }
}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'postgres.yaml') -Raw | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot apply UAT PostgreSQL resources.' }
& ssh @sshArguments 'kubectl -n ttb-uat rollout status deployment/ttb-postgres --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'UAT PostgreSQL did not become ready.' }
Write-Output 'UAT DB ready. Runtime has its own non-owner SQL role. No bank secrets or public ingress were created.'
