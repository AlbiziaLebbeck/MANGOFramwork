param(
 [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,31}$')][string]$ReleaseId,
 [ValidateSet('create','observe')][string]$Phase='create'
)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory=Join-Path $repositoryRoot ('.artifacts/uat-catalog/'+$RunId)
if($Phase -eq 'create' -and (Test-Path -LiteralPath $directory)){throw 'This run already exists. Observe the same Job; do not create a replacement after an observation timeout.'}
[IO.Directory]::CreateDirectory($directory)|Out-Null
$script=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'prepare-test-catalog.py') -Raw
$json=$script | & ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=12 ntserver02 ('python3 -u - --run-id '+$RunId+' --release-id '+$ReleaseId+' --phase '+$Phase)
if($LASTEXITCODE -ne 0){throw 'The existing UAT catalog run needs inspection. No automatic retry or replacement was attempted.'}
$result=($json -join "`n")|ConvertFrom-Json -DateKind String
$file=if($result.phase -eq 'verified'){'result.json'}else{$Phase+'-'+[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()+'.json'}
$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $directory $file) -Encoding utf8
$result|ConvertTo-Json -Depth 8
