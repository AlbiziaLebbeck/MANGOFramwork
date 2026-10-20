$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$localSettings = Join-Path $repositoryRoot '.tools\local\postgres-development.local.json'
$sshArguments = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=12', '-o', 'StrictHostKeyChecking=yes', 'ntserver02')
$existingNamespace = & ssh @sshArguments 'kubectl get namespace ttb-commerce-dev --ignore-not-found -o name'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the TTB development namespace.' }
if (-not $existingNamespace) {
    & ssh @sshArguments 'kubectl create namespace ttb-commerce-dev'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the TTB development namespace.' }
}
$secretExists = & ssh @sshArguments 'kubectl -n ttb-commerce-dev get secret postgres-development --ignore-not-found -o name'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the development secret.' }
if (-not $secretExists) {
    $developmentPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $secret = @{
        apiVersion = 'v1'; kind = 'Secret'
        metadata = @{ name = 'postgres-development'; namespace = 'ttb-commerce-dev' }
        type = 'Opaque'; stringData = @{ password = $developmentPassword }
    }
    $secret | ConvertTo-Json -Depth 8 -Compress | & ssh @sshArguments 'kubectl create -f -'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the development secret.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $localSettings) | Out-Null
    @{ ConnectionString = "Host=127.0.0.1;Port=15439;Database=ttb_development;Username=ttb_development;Password=$developmentPassword;Include Error Detail=false" } |
        ConvertTo-Json | Set-Content -LiteralPath $localSettings -Encoding utf8
} elseif (-not (Test-Path -LiteralPath $localSettings)) {
    throw 'Development DB exists, but its local connection file is missing. Recover the local setting deliberately; do not rotate an initialized DB password.'
}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'postgres.yaml') -Raw | & ssh @sshArguments 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot apply the development DB manifest.' }
& ssh @sshArguments 'kubectl -n ttb-commerce-dev rollout status deployment/postgres --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'Development DB did not become ready.' }
Write-Output 'Development DB ready. Credentials retained only in ignored .tools/local and the isolated Kubernetes Secret.'
