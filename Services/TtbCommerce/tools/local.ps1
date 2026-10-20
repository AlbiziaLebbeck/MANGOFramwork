param([ValidateRange(1024,65535)][int]$Port = 5286)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$settings=Get-Content -LiteralPath (Join-Path $repositoryRoot '.tools/local/postgres-development.local.json') -Raw | ConvertFrom-Json
$sshArgs=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=12','ntserver02')
$podIp=(& ssh @sshArgs 'kubectl -n ttb-commerce-dev get pods -l app=ttb-postgres -o jsonpath="{.items[0].status.podIP}"').Trim()
if($LASTEXITCODE -ne 0 -or $podIp -notmatch '^10\.42\.\d+\.\d+$'){throw 'Development database pod unavailable.'}
$sql=@'
SELECT 'CREATE DATABASE ttb_local_commerce' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname='ttb_local_commerce')\gexec
'@
$sql | & ssh @sshArgs 'kubectl -n ttb-commerce-dev exec -i deployment/postgres -- psql -U ttb_development -d ttb_development -v ON_ERROR_STOP=1'
if($LASTEXITCODE -ne 0){throw 'Cannot prepare the dedicated local-commerce database.'}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$tunnelPort=([Net.IPEndPoint]$listener.LocalEndpoint).Port;$listener.Stop()
function New-LocalDatabaseTunnel([int]$ListenerPort,[string]$DatabaseAddress) {
  $portCheck=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$ListenerPort)
  try{$portCheck.Start()}finally{$portCheck.Stop()}
  $logStem=Join-Path $repositoryRoot ('.tools/local/local-tunnel-'+[Guid]::NewGuid().ToString('N'))
  $process=Start-Process ssh -ArgumentList @('-N','-T','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=12','-o','ExitOnForwardFailure=yes','-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3','-L',"127.0.0.1:${ListenerPort}:${DatabaseAddress}:5432",'ntserver02') -WindowStyle Hidden -PassThru -RedirectStandardError ($logStem+'.err') -RedirectStandardOutput ($logStem+'.log')
  $deadline=[DateTime]::UtcNow.AddSeconds(15);$ready=$false
  while([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited){$probe=[Net.Sockets.TcpClient]::new();try{$probe.Connect('127.0.0.1',$ListenerPort);$ready=$true}catch{}finally{$probe.Dispose()};if($ready){break};Start-Sleep -Milliseconds 200}
  if(-not $ready){if(-not $process.HasExited){$process.Kill();[void]$process.WaitForExit(10000)};throw 'Local development tunnel failed.'}
  return $process
}
$tunnel=New-LocalDatabaseTunnel $tunnelPort $podIp
$apiProcess=$null;$apiOutput=$null;$apiError=$null;$outputCopy=$null;$errorCopy=$null
try{
  $env:ConnectionStrings__Commerce=$settings.ConnectionString -replace 'Database=ttb_development','Database=ttb_local_commerce' -replace 'Port=\d+',"Port=$tunnelPort"
  $env:ASPNETCORE_ENVIRONMENT='Development';$env:ASPNETCORE_URLS="http://127.0.0.1:$Port"
  $env:Commerce__Environment='Local';$env:Commerce__PaymentMode='Mock';$env:Commerce__Role='All';$env:Commerce__LocalTestIdentityEnabled='true'
  $env:Commerce__AdminSubjects__0='local:demo-admin'
  $env:Commerce__FinanceSubjects__0='local:demo-finance'
  # Synthetic Local/Mock workflows only; real environment policy remains closed by default.
  $env:ManualRefunds__LocalSimulationEnabled='true';$env:ManualRefunds__ExternalRefundAttestationApproved='true';$env:ManualRefunds__GoodsDispositionPolicyApproved='true'
  $env:Voids__Enabled='true';$env:Voids__LocalSimulationEnabled='true';$env:Voids__WindowPolicy='BankingDay2256';$env:Voids__FulfilledRefundPolicyApproved='true'
  $env:Commerce__TrustedOrigins__0='http://127.0.0.1:5176';$env:Commerce__TrustedOrigins__1='http://localhost:5176'
  $env:Commerce__TrustedOrigins__2='http://127.0.0.1:5186';$env:Commerce__TrustedOrigins__3='http://localhost:5186'
  $env:Commerce__TrustedOrigins__4='http://127.0.0.1:5188';$env:Commerce__TrustedOrigins__5='http://localhost:5188'
  $env:Storage__RootPath=Join-Path $repositoryRoot '.tools/local/private-commerce'
  $gameSettingsPath=Join-Path $repositoryRoot '.tools/local/game-development.local.json'
  if(-not(Test-Path -LiteralPath $gameSettingsPath)){
    $localGameSettings=@{GameServerId='mangos-ttb';GameServerKey=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()}
    $localGameSettings | ConvertTo-Json | Set-Content -LiteralPath $gameSettingsPath -Encoding utf8
  }
  $localGameSettings=Get-Content -LiteralPath $gameSettingsPath -Raw | ConvertFrom-Json
  if($localGameSettings.GameServerKey.Length -lt 32){throw 'Invalid local game service key.'}
  $env:Storage__GameServerId=$localGameSettings.GameServerId;$env:Storage__GameServerKey=$localGameSettings.GameServerKey
  # Run an isolated published copy so a local web session does not lock the source build output used by regression tests.
  $localRuntime=Join-Path $repositoryRoot ('.tools/local/runtime-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss',[Globalization.CultureInfo]::InvariantCulture)+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
  & (Join-Path $PSScriptRoot 'dotnet.ps1') publish src/TtbCommerce.Api --no-restore -o $localRuntime
  if($LASTEXITCODE -ne 0){throw 'Local runtime publish failed.'}
  & dotnet (Join-Path $localRuntime 'TtbCommerce.Api.dll') --contentRoot $localRuntime --migrate --seed-demo
  if($LASTEXITCODE -ne 0){throw 'Local migrations or explicit demo seeding failed.'}
  $start=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -ErrorAction Stop).Source)
  foreach($argument in @((Join-Path $localRuntime 'TtbCommerce.Api.dll'),'--contentRoot',$localRuntime)){$start.ArgumentList.Add($argument)}
  $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
  $apiOutput=[IO.File]::Create((Join-Path $localRuntime 'local-api.log'));$apiError=[IO.File]::Create((Join-Path $localRuntime 'local-api.err'))
  $apiProcess=[Diagnostics.Process]::new();$apiProcess.StartInfo=$start
  if(-not $apiProcess.Start()){throw 'Cannot start the owned Local API.'}
  $outputCopy=$apiProcess.StandardOutput.BaseStream.CopyToAsync($apiOutput);$errorCopy=$apiProcess.StandardError.BaseStream.CopyToAsync($apiError)
  $statePath=Join-Path $localRuntime 'local-process-state.json'
  function Save-LocalProcessState {
    [ordered]@{apiPid=$apiProcess.Id;apiStartedUtc=$apiProcess.StartTime.ToUniversalTime().ToString('o');runtime=$localRuntime;apiPort=$Port;
      tunnelPid=$tunnel.Id;tunnelStartedUtc=$tunnel.StartTime.ToUniversalTime().ToString('o');tunnelPort=$tunnelPort;databasePodIp=$podIp;
      environment='Local';paymentMode='Mock';automaticFinancialReplay=$false}|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding utf8
  }
  Save-LocalProcessState
  Write-Output ('Owned Local API running on loopback '+$Port+'; process state: '+$statePath)
  while(-not $apiProcess.HasExited){
    if($tunnel.HasExited){
      Write-Output 'The owned development SSH tunnel ended. Reconnecting the same loopback port; no HTTP/SQL mutation is replayed.'
      $reconnected=$false
      for($attempt=0;$attempt -lt 3 -and -not $apiProcess.HasExited;$attempt++){
        $nextIp=(& ssh @sshArgs 'kubectl -n ttb-commerce-dev get pods -l app=ttb-postgres -o jsonpath="{.items[0].status.podIP}"').Trim()
        if($LASTEXITCODE -eq 0 -and $nextIp -match '^10\.42\.\d+\.\d+$'){
          try{$replacement=New-LocalDatabaseTunnel $tunnelPort $nextIp;$tunnel=$replacement;$podIp=$nextIp;$reconnected=$true;Save-LocalProcessState;break}catch{if($attempt -eq 2){throw}}
        }
        Start-Sleep -Seconds 2
      }
      if(-not $reconnected -and -not $apiProcess.HasExited){throw 'The Local tunnel could not be restored; stopping only this owned Local API.'}
      if($reconnected){Write-Output 'Development tunnel restored on the same port; the Local API process and database snapshots were retained.'}
    }
    Start-Sleep -Milliseconds 300
  }
  $apiProcess.WaitForExit();exit $apiProcess.ExitCode
}finally{
  if($null -ne $apiProcess -and -not $apiProcess.HasExited){$apiProcess.Kill();[void]$apiProcess.WaitForExit(10000)}
  foreach($copy in @($outputCopy,$errorCopy)){if($null -ne $copy){[void]$copy.GetAwaiter().GetResult()}}
  if($null -ne $apiOutput){$apiOutput.Dispose()};if($null -ne $apiError){$apiError.Dispose()}
  foreach($key in @('Commerce__FinanceSubjects__0','ManualRefunds__LocalSimulationEnabled','ManualRefunds__ExternalRefundAttestationApproved','ManualRefunds__GoodsDispositionPolicyApproved')){Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue}
  if(-not $tunnel.HasExited){$tunnel.Kill();[void]$tunnel.WaitForExit(10000)}
  foreach($key in @('ConnectionStrings__Commerce','ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','Commerce__Environment','Commerce__PaymentMode','Commerce__Role','Commerce__LocalTestIdentityEnabled','Commerce__AdminSubjects__0','Commerce__TrustedOrigins__0','Commerce__TrustedOrigins__1','Commerce__TrustedOrigins__2','Commerce__TrustedOrigins__3','Commerce__TrustedOrigins__4','Commerce__TrustedOrigins__5','Storage__RootPath','Storage__GameServerId','Storage__GameServerKey','Voids__Enabled','Voids__LocalSimulationEnabled','Voids__WindowPolicy','Voids__FulfilledRefundPolicyApproved')){Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue}
}
