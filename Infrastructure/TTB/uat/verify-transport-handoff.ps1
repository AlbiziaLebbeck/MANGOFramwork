param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory=Join-Path $repositoryRoot ('.artifacts/tls-qa/transport-'+$RunId)
if(Test-Path -LiteralPath $directory){throw 'This run already exists locally. Inspect its live phase; do not recreate it after an observation timeout.'}
[IO.Directory]::CreateDirectory($directory)|Out-Null
$controller=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'transport-handoff.py') -Raw
$sshArgs=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=12','ntserver02')
$phase='preflight';$enabled=$false;$token=$null;$client=$null;$handler=$null
function Invoke-Phase([string]$Name) {
 $script:phase=$Name
 $json=$controller | & ssh @sshArgs ('python3 -u - --run-id '+$RunId+' --phase '+$Name)
 if($LASTEXITCODE -ne 0){throw "UAT transport phase failed: $Name. Preserve this run's identities."}
 $value=($json -join "`n")|ConvertFrom-Json -DateKind String
 $value|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $directory ($Name+'.json')) -Encoding utf8
 $value
}
function Get-Mac([string]$Nonce,[string]$Address) {
 $ip=[Net.IPAddress]::Parse($Address);if($ip.IsIPv4MappedToIPv6){$ip=$ip.MapToIPv4()}
 $bytes=[Text.Encoding]::UTF8.GetBytes("ttb-uat-transport-probe-v1`n"+$Nonce+"`n"+$ip.ToString())
 [Convert]::ToHexString([Security.Cryptography.HMACSHA256]::HashData([Text.Encoding]::UTF8.GetBytes($token),$bytes)).ToLowerInvariant()
}
function Read-Health([string]$Name,[bool]$Authenticated,[hashtable]$ForgedHeaders=@{}) {
 $nonce=[Guid]::NewGuid().ToString('N')
 $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,('https://ttb-api-test.mangosgo.com/health/live?transport_probe='+$nonce))
 if($Authenticated){$request.Headers.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$token)}
 foreach($key in $ForgedHeaders.Keys){[void]$request.Headers.TryAddWithoutValidation($key,[string]$ForgedHeaders[$key])}
 try{
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{
   $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
   if([int]$response.StatusCode -ne 200 -or $body.status -ne 'alive' -or $body.service -ne 'ttb-commerce'){throw 'The existing health-only route is not healthy; do not expand its allowlist.'}
   $socket=if($response.Headers.Contains('X-Ttb-Transport-Peer-Digest')){($response.Headers.GetValues('X-Ttb-Transport-Peer-Digest')|Select-Object -First 1)}else{$null}
   $effective=if($response.Headers.Contains('X-Ttb-Effective-Peer-Digest')){($response.Headers.GetValues('X-Ttb-Effective-Peer-Digest')|Select-Object -First 1)}else{$null}
   [pscustomobject]@{name=$Name;nonce=$nonce;status=200;socket=$socket;effective=$effective;systemCaAndHostnameVerified=$true}
  }finally{$response.Dispose()}
 }finally{$request.Dispose()}
}
try{
 # Capture the existing private metrics credential in memory only; it is never written to a file/argument/log.
 $encoded=(& ssh @sshArgs 'kubectl -n ttb-uat get secret ttb-operations -o jsonpath="{.data.Operations__MetricsToken}"') -join ''
 if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($encoded)){throw 'The existing private metrics credential is unavailable.'}
 $token=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded));$encoded=$null
 if($token.Length -lt 32 -or $token.Length -gt 512){throw 'The existing metrics credential has an invalid length.'}
 $connection=((& ssh @sshArgs 'printf "%s" "$SSH_CONNECTION"') -join '').Trim().Split(' ',[StringSplitOptions]::RemoveEmptyEntries)
 if($LASTEXITCODE -ne 0 -or $connection.Count -ne 4){throw 'Cannot determine the actual operator TCP peer.'}
 $operatorIp=[Net.IPAddress]::Parse($connection[0]).ToString()
 $handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=$false;$handler.UseCookies=$false
 $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(12)
 $before=Read-Health 'before-enabling' $true
 if($before.socket -or $before.effective){throw 'A diagnostic window is already active; inspect it instead of changing it.'}
 $enabled=$true;$start=Invoke-Phase 'enable';$proxyIp=$start.topology.controllerIp
 $noTrust=Read-Health 'no-trust-ignores-forged-bank' $true @{'X-Forwarded-For'='203.148.143.101';'X-Real-IP'='203.148.143.101'}
 if($noTrust.socket -ne (Get-Mac $noTrust.nonce $proxyIp) -or $noTrust.effective -ne $noTrust.socket){throw 'The actual application socket does not match the observed controller; refuse proxy trust.'}
 $noTrust|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory 'socket-before-trust.json') -Encoding utf8
 $trusted=Invoke-Phase 'trust'
 $checks=@()
 foreach($scenario in @(
  @{name='actual-operator';headers=@{}},
  @{name='forged-bank';headers=@{'X-Forwarded-For'='203.148.143.101';'X-Real-IP'='203.148.143.101'}},
  @{name='forged-multiple-hops';headers=@{'X-Forwarded-For'='203.148.143.101, 127.0.0.1';'X-Real-IP'='203.148.143.101'}}
 )){
  $row=Read-Health $scenario.name $true $scenario.headers
  if($row.socket -ne (Get-Mac $row.nonce $proxyIp) -or $row.effective -ne (Get-Mac $row.nonce $operatorIp)){throw 'The application did not retain the actual operator identity; refuse acceptance.'}
  $checks+=$row
 }
 $unauthenticated=Read-Health 'without-metrics-credential' $false @{'X-Ttb-Effective-Peer-Digest'='forged-response-digest'}
 if($unauthenticated.socket -or $unauthenticated.effective){throw 'Health observation leaked without the private credential.'}
 $checks+=$unauthenticated
 $checks|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $directory 'operator-health-proofs.json') -Encoding utf8
 $node=Invoke-Phase 'node-proof'
 $persisted=Invoke-Phase 'persist'
 $durable=Read-Health 'persisted-configmap-identity' $true @{'X-Forwarded-For'='203.148.143.101'}
 if($durable.socket -ne (Get-Mac $durable.nonce $proxyIp) -or $durable.effective -ne (Get-Mac $durable.nonce $operatorIp)){throw 'The persisted UAT transport configuration did not preserve the actual peer.'}
 $durable|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory 'persisted-configmap-proof.json') -Encoding utf8
 $finished=Invoke-Phase 'finish';$enabled=$false
 $after=Read-Health 'diagnostic-window-removed' $true
 if($after.socket -or $after.effective){throw 'The temporary diagnostic window was not removed.'}
 [pscustomobject]@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');runId=$RunId;applicationSocketMatchesController=$true;
  applicationEffectivePeerMatchesOperator=$true;forgedBankAndMultipleHopHeadersIgnored=$true;untrustedNodeKeepsSocketIdentity=$true;
  diagnosticWindowRemoved=$true;trustedProxyCidr=$trusted.topology.cidr;controllerUid=$trusted.topology.controllerUid;
  persistedInOptionalUatConfigMap=$true;callbackSourceIpValidationChanged=$false;actualBankNatAccepted=$false;
  bankAuthenticationAccepted=$false;bankCallbackPublicOpened=$false;sharedIngressChanged=$false;kkuChanged=$false;
  credentialWrittenToFileOrLog=$false;systemCaAndHostnameVerified=$true}
 |ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $directory 'result.json') -Encoding utf8
 Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw
}catch{
 [pscustomobject]@{failedPhase=$phase;failureType=$_.Exception.GetType().Name;runId=$RunId;observationIsNotTerminal=$true}
 |ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory 'failure.json') -Encoding utf8
 if($enabled){try{$null=Invoke-Phase 'abort'}catch{Write-Output 'Inspect the same UAT transport run; guarded cleanup did not confirm success. The diagnostic window expires automatically.'}}
 throw
}finally{
 $token=$null;$encoded=$null;$operatorIp=$null
 if($null -ne $client){$client.Dispose()};if($null -ne $handler){$handler.Dispose()}
}
