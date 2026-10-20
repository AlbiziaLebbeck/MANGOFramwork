param()
$ErrorActionPreference = 'Stop'
$sshArgs = @('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$uatRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../uat'))
$floor = (& ssh @sshArgs 'cat /opt/ttb-commerce/uat/minimum-reader-contract').Trim()
if ($LASTEXITCODE -ne 0 -or $floor -notmatch '^\d{1,3}$' -or [int]$floor -lt 3) { throw 'Private metrics requires the compatible operations API/worker release.' }
$releasePaths = (& ssh @sshArgs 'kubectl -n ttb-uat get deployment ttb-commerce-api ttb-commerce-worker ttb-receipt-worker -o jsonpath="{range .items[*]}{.spec.template.spec.volumes[?(@.name==\"app\")].hostPath.path}{\"\n\"}{end}"')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect current owned UAT releases.' }
$releases = @($releasePaths | Where-Object { $_ -match '^/opt/ttb-commerce/uat/releases/([a-z0-9-]+)/api$' } | ForEach-Object { [regex]::Match($_,'/releases/([a-z0-9-]+)/api$').Groups[1].Value } | Select-Object -Unique)
if ($releases.Count -ne 1 -or @($releasePaths).Count -ne 3) { throw 'API and both workers must use the same verified immutable UAT release.' }
$release = $releases[0]
$existingOutput = @(& ssh @sshArgs 'kubectl -n ttb-uat get secret ttb-operations --ignore-not-found -o name')
$existing = ($existingOutput -join '').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the separate private metrics credential.' }
if (-not $existing) {
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    @{apiVersion='v1';kind='Secret';metadata=@{name='ttb-operations';namespace='ttb-uat'};type='Opaque';stringData=@{Operations__MetricsEnabled='true';Operations__MetricsToken=$token}} |
        ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl create -f -'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot provision the scoped aggregate-metrics collector token.' }
}
$configuration = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'prometheus.example.yml') -Raw).Replace('ttb-commerce-api.ttb-uat.svc.cluster.local:8080','ttb-commerce-metrics.ttb-uat.svc.cluster.local:8080')
$observerOutput=@(& ssh @sshArgs 'kubectl -n ttb-uat get deployment ttb-backup-metrics --ignore-not-found -o name')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the optional authorized UAT backup observer.' }
if (($observerOutput -join '').Trim()) {
    $configuration += @'

  - job_name: ttb-backups
    metrics_path: /metrics
    authorization:
      type: Bearer
      credentials_file: /run/ttb-metrics/token
    static_configs:
      - targets: [ttb-backup-metrics.ttb-uat.svc.cluster.local:8081]
        labels: { environment: Uat }
'@
}
$tlsOutput=@(& ssh @sshArgs 'kubectl -n ttb-uat get deployment ttb-tls-metrics --ignore-not-found -o name')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the optional UAT public-certificate observer.' }
if (($tlsOutput -join '').Trim()) {
    $configuration += @'

  - job_name: ttb-callback-tls
    metrics_path: /metrics
    scrape_timeout: 15s
    authorization:
      type: Bearer
      credentials_file: /run/ttb-metrics/token
    static_configs:
      - targets: [ttb-tls-metrics.ttb-uat.svc.cluster.local:8081]
        labels: { environment: Uat }
'@
}
$rules = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'alerts.yml') -Raw
@{apiVersion='v1';kind='ConfigMap';metadata=@{name='ttb-prometheus-config';namespace='ttb-uat'};data=@{'prometheus.yml'=$configuration;'alerts.yml'=$rules}} |
    ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the private collector configuration.' }
foreach ($file in @('api.yaml','worker.yaml','receipt-worker.yaml')) {
    (Get-Content -LiteralPath (Join-Path $uatRoot $file) -Raw).Replace('__RELEASE__',$release) | & ssh @sshArgs 'kubectl apply -f -'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot bind the existing UAT process to its separate operations credential.' }
}
$configHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($configuration + [char]0 + $rules)))
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'monitoring.yaml') -Raw).Replace('__CONFIG_SHA256__',$configHash) | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot install the isolated private collector.' }
foreach ($deployment in @('ttb-commerce-api','ttb-commerce-worker','ttb-receipt-worker','ttb-prometheus')) {
    & ssh @sshArgs "kubectl -n ttb-uat rollout status deployment/$deployment --timeout=60s"
    if ($LASTEXITCODE -ne 0) { throw ('Private rollout requires inspection: ' + $deployment) }
}
Write-Output 'Private aggregate-metrics collection configured. No backup DB access, public ingress, Alertmanager or external notification recipient was created.'
