param()
$ErrorActionPreference='Stop'
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$cron=(& ssh @sshArgs 'kubectl -n ttb-uat get cronjob ttb-uat-backup -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $cron.spec.suspend -ne $false -or $cron.spec.schedule -ne '*/15 * * * *') { throw 'Install this observer only for the explicitly approved, enabled UAT backup schedule.' }
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backup-metrics.py') -Raw
$helper=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'certificate_probe.py') -Raw
$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($source+[char]0+$helper)))
@{apiVersion='v1';kind='ConfigMap';metadata=@{name='ttb-backup-observer';namespace='ttb-uat'};data=@{'backup-metrics.py'=$source;'certificate_probe.py'=$helper}} | ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure the aggregate-only backup observer.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backup-metrics.yaml') -Raw).Replace('__SOURCE_SHA256__',$hash) | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot install the private read-only UAT backup observer.' }
& ssh @sshArgs 'kubectl -n ttb-uat rollout status deployment/ttb-backup-metrics --timeout=60s'
if ($LASTEXITCODE -ne 0) { throw 'Inspect the owned observer before retrying. No producer/database change was made.' }
Write-Output 'Private UAT backup observer installed: ciphertext metadata/completion marker only, read-only mount, no SQL/Kubernetes token or egress.'
