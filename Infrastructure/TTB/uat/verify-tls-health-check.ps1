param()
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$hostname='ttb-api-test.mangosgo.com'
$addresses=@([Net.Dns]::GetHostAddresses($hostname))
if(-not $addresses.Count -or @($addresses|Where-Object {$_.ToString() -ne '209.15.122.115'}).Count){throw 'Local UAT DNS differs from the expected NT address; no HTTP test sent.'}
$connection=(& ssh @sshArgs 'printenv SSH_CONNECTION') -join ''
$operatorAddress=($connection -split '\s+')[0]
$ip=$null
if($LASTEXITCODE -ne 0 -or -not [Net.IPAddress]::TryParse($operatorAddress,[ref]$ip) -or $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork){throw 'Cannot identify the authoritative SSH operator peer.'}
$ingress=(& ssh @sshArgs 'kubectl -n ttb-uat get ingress ttb-uat-tls-check -o json') -join "`n" | ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or $ingress.metadata.labels.'app.kubernetes.io/part-of' -ne 'ttb-commerce' -or
 $ingress.metadata.annotations.'nginx.ingress.kubernetes.io/whitelist-source-range' -ne ($operatorAddress+'/32') -or
 @($ingress.spec.rules).Count -ne 1 -or @($ingress.spec.rules[0].http.paths).Count -ne 1 -or
 $ingress.spec.rules[0].http.paths[0].path -ne '/health/live' -or $ingress.spec.rules[0].http.paths[0].pathType -ne 'Exact'){throw 'Exact allowlisted health-only route is not the owned preflight.'}
$all=(& ssh @sshArgs 'kubectl get ingress -A -o json') -join "`n" | ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or @($all.items|Where-Object {@($_.spec.rules|Where-Object {$_.host -eq $hostname}).Count}).Count -ne 1){throw 'The UAT host has additional routes; preserve them and inspect before testing.'}
$nonce=[Guid]::NewGuid().ToString('N')
$handler=[Net.Http.HttpClientHandler]::new(); $handler.UseProxy=$false; $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(12)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('MANGOsTTB-UAT-Transport/1.0')
$observations=[ordered]@{}
try{
 foreach($path in @('/health/live','/ttb/v1/credit-notification','/ttb-commerce-test/v1/storefront/config','/internal/metrics')){
  $response=$client.GetAsync('https://'+$hostname+$path+'?transport_probe='+$nonce).GetAwaiter().GetResult()
  try{
   $status=[int]$response.StatusCode; $observations[$path]=$status
   if($path -eq '/health/live' -and $status -eq 200){
    $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    if($body.status -ne 'alive' -or $body.service -ne 'ttb-commerce'){throw 'Health route reached an unexpected service.'}
   }
  }finally{$response.Dispose()}
 }
 $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,'https://'+$hostname+'/health/live?transport_probe='+$nonce+'-forged')
 $request.Headers.Add('X-Forwarded-For','203.148.143.101'); $request.Headers.Add('X-Real-IP','203.148.143.101')
 try{$response=$client.SendAsync($request).GetAwaiter().GetResult(); try{$forgedOperatorStatus=[int]$response.StatusCode}finally{$response.Dispose()}}finally{$request.Dispose()}
}finally{$client.Dispose();$handler.Dispose()}
# A separate kernel peer (the NT node) must remain denied even when it forges the allowed operator's headers.
$nodeCommand="curl --noproxy '*' --silent --show-error --output /dev/null --write-out '%{http_code}' --connect-timeout 5 --max-time 10 --resolve '${hostname}:443:209.15.122.115' --header 'X-Forwarded-For: $operatorAddress' --header 'X-Real-IP: $operatorAddress' 'https://$hostname/health/live?transport_probe=$nonce-node'"
$nodeStatus=(& ssh @sshArgs $nodeCommand) -join ''
if($LASTEXITCODE -ne 0 -or $nodeStatus -notmatch '^\d{3}$'){throw 'The independently denied node probe could not be observed.'}
$closed=@('/ttb/v1/credit-notification','/ttb-commerce-test/v1/storefront/config','/internal/metrics'|Where-Object {$observations[$_] -notin @(403,404)}).Count -eq 0
$proof=@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');environment='Uat';probeRunId=$nonce;hostname=$hostname;systemCaAndHostnameVerified=$true;dnsMatchesExpectedNt=$true;observedHttpStatuses=$observations;
 operatorHealthAccepted=($observations['/health/live'] -eq 200);forgedBankHeadersFromOperatorStatus=$forgedOperatorStatus;
 unallowedNodeWithForgedOperatorHeadersStatus=[int]$nodeStatus;ingressSourceAllowlistVerified=($observations['/health/live'] -eq 200 -and $forgedOperatorStatus -eq 200 -and $nodeStatus -eq '403');
 shopperCallbackMetricsRoutesClosed=$closed;bankCallbackPeerAndAuthenticationAccepted=$false;bankPaymentsReady=$false;sharedControllerChanged=$false}
$output=Join-Path $repositoryRoot '.artifacts/tls-qa'; [IO.Directory]::CreateDirectory($output)|Out-Null
$proof | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'health-preflight-verification.json') -Encoding utf8
$proof | ConvertTo-Json -Depth 5
if(-not $proof.ingressSourceAllowlistVerified -or -not $closed){throw 'Transport evidence is incomplete. Preserve the result and keep bank routes closed; do not broaden the allowlist or trust caller headers.'}
