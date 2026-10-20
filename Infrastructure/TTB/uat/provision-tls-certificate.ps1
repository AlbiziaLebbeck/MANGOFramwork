# Uses the existing ready DNS01 issuer for this exact UAT name; never applies an Ingress or edits the issuer.
param()
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$issuer=(& ssh @sshArgs 'kubectl get clusterissuer letsencrypt-production-cloudflare-dns01 -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $issuer.spec.acme.server -ne 'https://acme-v02.api.letsencrypt.org/directory' -or
    -not @($issuer.status.conditions | Where-Object {$_.type -eq 'Ready' -and $_.status -eq 'True'}).Count -or
    -not @($issuer.spec.acme.solvers | Where-Object {$_.dns01.cloudflare}).Count) { throw 'The existing approved-name DNS01 issuer is not ready; preserved all shared configuration.' }
$dns=@(& ssh @sshArgs 'getent ahostsv4 ttb-api-test.mangosgo.com')
if ($LASTEXITCODE -ne 0 -or -not $dns.Count -or @($dns | Where-Object {($_ -split '\s+')[0] -ne '209.15.122.115'}).Count) { throw 'UAT DNS does not resolve solely to the user-specified NT address.' }
$existingName=(& ssh @sshArgs 'kubectl -n ttb-uat get certificate ttb-api-test --ignore-not-found -o name') -join ''
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the owned UAT certificate name.' }
if ($existingName) {
    $existing=(& ssh @sshArgs 'kubectl -n ttb-uat get certificate ttb-api-test -o json') -join "`n" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $existing.metadata.labels.'app.kubernetes.io/part-of' -ne 'ttb-commerce' -or
        $existing.metadata.labels.'mangosgo.com/environment' -ne 'Uat' -or $existing.spec.secretName -ne 'ttb-api-test-tls' -or
        @($existing.spec.dnsNames).Count -ne 1 -or $existing.spec.dnsNames[0] -ne 'ttb-api-test.mangosgo.com') { throw 'An unrelated certificate occupies the UAT name; nothing adopted or rotated.' }
} else {
    $occupied=(& ssh @sshArgs 'kubectl -n ttb-uat get secret ttb-api-test-tls --ignore-not-found -o name') -join ''
    if ($LASTEXITCODE -ne 0 -or $occupied) { throw 'TLS Secret already exists without the owned certificate; do not overwrite it.' }
}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tls-certificate.yaml') -Raw | & ssh @sshArgs 'kubectl apply -f -'
if ($LASTEXITCODE -ne 0) { throw 'UAT certificate preparation failed; inspect its exact state before any retry.' }
Write-Output 'Prepared only Certificate/ttb-api-test using the existing DNS01 issuer. The issuer name refers to the trusted CA, not the payment Production environment. No Ingress/route/controller/bank profile was changed.'
& ssh @sshArgs 'kubectl -n ttb-uat wait --for=condition=Ready certificate/ttb-api-test --timeout=55s'
if ($LASTEXITCODE -ne 0) { throw 'Certificate readiness observation expired; inspect this same Certificate/Order/Challenge, do not recreate it.' }
$certificate=(& ssh @sshArgs 'kubectl -n ttb-uat get certificate ttb-api-test -o json') -join "`n" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot record UAT certificate readiness.' }
$output=Join-Path $repositoryRoot '.artifacts/tls-qa'; [IO.Directory]::CreateDirectory($output) | Out-Null
@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');environment='Uat';name=$certificate.metadata.name;dnsNames=$certificate.spec.dnsNames;ready=$true;revision=$certificate.status.revision;notBefore=$certificate.status.notBefore;notAfter=$certificate.status.notAfter;renewalTime=$certificate.status.renewalTime;publicRouteCreated=$false;bankProfileReady=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'certificate-ready.json') -Encoding utf8
Write-Output 'UAT certificate is Ready; public serving/hostname/source-IP and bank callback acceptance remain separate checks.'
