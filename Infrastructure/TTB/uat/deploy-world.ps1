param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId,[Parameter(Mandatory)][ValidateSet('create','observe')][string]$Phase,[ValidatePattern('^[a-f0-9]{32}$')][string]$PreviousRunId)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$run=Join-Path $root ".artifacts/uat-world/$RunId"
$package=Join-Path $run 'package'
$remote="/opt/ttb-commerce/uat/world-releases/$RunId"
$sshArguments=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
$stateFile=Join-Path $run 'deployment-state.json'
$operator=Join-Path $run 'deploy-world.py'
if($Phase -eq 'create'){
  if(Test-Path -LiteralPath $stateFile){throw 'This deployment was already attempted. Observe the existing run.'}
  $manifest=Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
  if($manifest.environment -ne 'Uat' -or $manifest.runId -ne $RunId){throw 'Wrong UAT package.'}
  foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $package $file.path)).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Package artifact changed.'}}
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'deploy-world.py') -Destination $operator
  $archive=Join-Path $run 'world.tar.gz'
  & tar -czf $archive -C $package .
  if($LASTEXITCODE -ne 0){throw 'Cannot archive the UAT world.'}
  $state=@{runId=$RunId;archiveSha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();operatorSha256=(Get-FileHash -LiteralPath $operator).Hash.ToLowerInvariant();phase='transfer-attempted';publicRouteCreated=$false}
  $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
  & ssh @sshArguments "test ! -e '$remote' && mkdir -p '$remote'"
  if($LASTEXITCODE -ne 0){throw 'Remote immutable run exists or could not be prepared. Inspect it.'}
  & scp -o BatchMode=yes -o ConnectTimeout=12 -o StrictHostKeyChecking=yes $archive "ntserver02:$remote/world.tar.gz"
  if($LASTEXITCODE -ne 0){throw 'Transfer failed. Inspect this existing run; do not recreate it.'}
}else{$state=Get-Content -LiteralPath $stateFile -Raw|ConvertFrom-Json}
if((Get-FileHash -LiteralPath $operator).Hash.ToLowerInvariant() -ne $state.operatorSha256){throw 'The pinned deployment operator changed.'}
$previous=if($PreviousRunId){" --previous-run-id '$PreviousRunId'"}else{''}
$output=Get-Content -LiteralPath $operator -Raw | & ssh @sshArguments "python3 - --run-id '$RunId' --archive-sha256 '$($state.archiveSha256)' --phase '$Phase'$previous"
if($LASTEXITCODE -ne 0){throw 'Private world operation did not confirm success. Inspect the existing remote state; never replay create on timeout.'}
$result=$output | ConvertFrom-Json
$output | Set-Content -LiteralPath (Join-Path $run ("deployment-$Phase.json")) -Encoding utf8
if($result.phase -eq 'verified'){$output | Set-Content -LiteralPath (Join-Path $run 'deployment-result.json') -Encoding utf8}
$output
