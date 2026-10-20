param()
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$certificate=(& ssh @sshArgs 'kubectl -n ttb-uat get certificate ttb-api-test -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $certificate.spec.secretName -ne 'ttb-api-test-tls' -or @($certificate.spec.dnsNames).Count -ne 1 -or
    $certificate.spec.dnsNames[0] -ne 'ttb-api-test.mangosgo.com' -or
    -not @($certificate.status.conditions|Where-Object {$_.type -eq 'Ready' -and $_.status -eq 'True'}).Count) { throw 'Exact UAT certificate must be Ready.' }
$ingresses=(& ssh @sshArgs 'kubectl get ingress -A -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify hostname ownership before the transport preflight.' }
foreach($ingress in $ingresses.items) {
    if(@($ingress.spec.rules | Where-Object {$_.host -eq 'ttb-api-test.mangosgo.com'}).Count -eq 0){continue}
    if($ingress.metadata.namespace -ne 'ttb-uat' -or $ingress.metadata.name -ne 'ttb-uat-tls-check' -or
       $ingress.metadata.labels.'app.kubernetes.io/part-of' -ne 'ttb-commerce' -or
       @($ingress.spec.rules).Count -ne 1 -or @($ingress.spec.rules[0].http.paths).Count -ne 1 -or
       $ingress.spec.rules[0].http.paths[0].path -ne '/health/live' -or $ingress.spec.rules[0].http.paths[0].pathType -ne 'Exact') { throw 'An existing public route owns this UAT hostname; do not overwrite or add preflight routing.' }
}
# SSH_CONNECTION is established by the trusted sshd kernel peer, not by a caller's HTTP header.
$connection=(& ssh @sshArgs 'printenv SSH_CONNECTION') -join ''
$operatorAddress=($connection -split '\s+')[0]
$address=$null
if($LASTEXITCODE -ne 0 -or -not [Net.IPAddress]::TryParse($operatorAddress,[ref]$address) -or $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
   $operatorAddress -match '^(0\.|127\.|169\.254\.|10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)') { throw 'A verified public operator IPv4 is required; nothing exposed.' }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tls-health-check.yaml') -Raw).Replace('__OPERATOR_IPV4__',$operatorAddress) | & ssh @sshArgs 'kubectl apply -f -'
if($LASTEXITCODE -ne 0){throw 'Transport health-route apply failed. Inspect the named Ingress before retrying.'}
$output=Join-Path $repositoryRoot '.artifacts/tls-qa'; [IO.Directory]::CreateDirectory($output)|Out-Null
@{preparedAt=[DateTimeOffset]::UtcNow.ToString('o');environment='Uat';ingress='ttb-uat-tls-check';hostname='ttb-api-test.mangosgo.com';path='/health/live';pathType='Exact';operatorIpv4Sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($operatorAddress)));shopperExposed=$false;callbackExposed=$false;publicBankAcceptance=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'health-preflight-prepared.json') -Encoding utf8
Write-Output 'Prepared only the owned UAT /health/live transport route, allowlisted to the current trusted SSH operator IPv4. No shopper/callback route or shared controller setting was changed.'
