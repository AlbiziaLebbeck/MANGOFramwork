param()
$ErrorActionPreference='Stop'
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$certificate=(& ssh @sshArgs 'kubectl -n ttb-uat get certificate ttb-api-test -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $certificate.metadata.labels.'app.kubernetes.io/part-of' -ne 'ttb-commerce' -or $certificate.spec.secretName -ne 'ttb-api-test-tls' -or
    @($certificate.spec.dnsNames).Count -ne 1 -or $certificate.spec.dnsNames[0] -ne 'ttb-api-test.mangosgo.com' -or
    -not @($certificate.status.conditions | Where-Object {$_.type -eq 'Ready' -and $_.status -eq 'True'}).Count) { throw 'The exact owned UAT Certificate must be Ready before installing its public-certificate observer.' }
$files=[ordered]@{}
foreach($name in @('tls-metrics.py','backup-metrics.py','certificate_probe.py')) { $files[$name]=Get-Content -LiteralPath (Join-Path $PSScriptRoot $name) -Raw }
$hashText=($files.Values -join [char]0)
$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($hashText)))
@{apiVersion='v1';kind='ConfigMap';metadata=@{name='ttb-tls-observer';namespace='ttb-uat'};data=$files} | ConvertTo-Json -Depth 6 -Compress | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure the UAT certificate observer.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tls-metrics.yaml') -Raw).Replace('__SOURCE_SHA256__',$hash) | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'Cannot install the private UAT TLS observer.' }
& ssh @sshArgs 'kubectl -n ttb-uat rollout status deployment/ttb-tls-metrics --timeout=55s'
if ($LASTEXITCODE -ne 0) { throw 'Inspect the same observer deployment; no public API route was created.' }
Write-Output 'Private UAT TLS observer installed: public certificate only, pinned NT HTTPS handshake, no HTTP/payment data, public-serving expectation remains false.'
