$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$executable=Join-Path $repositoryRoot 'Builds/TTB/LocalWorld/Server/TtbLocalWorld.exe'
if(-not(Test-Path -LiteralPath $executable)){throw 'Build the local metaverse Server first.'}
if(Get-NetTCPConnection -LocalPort 7778 -State Listen -ErrorAction SilentlyContinue){throw 'Port 7778 is already occupied. Inspect the owner instead of stopping another service.'}
$settings=Get-Content -LiteralPath (Join-Path $repositoryRoot '.tools/local/game-development.local.json') -Raw | ConvertFrom-Json
if($settings.GameServerKey.Length -lt 32){throw 'Start the separate Local+Mock commerce API to provision its local game service profile first.'}
$log=Join-Path $repositoryRoot ('Builds/Logs/ttb-world-local-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')+'.log')
try{
  $env:MANGOS_TTB_GAME_LOCAL='true';$env:MANGOS_TTB_GAME_API='http://127.0.0.1:5286/ttb-commerce/v1'
  $env:MANGOS_TTB_GAME_ID=$settings.GameServerId;$env:MANGOS_TTB_GAME_KEY=$settings.GameServerKey
  $process=Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -ArgumentList @('-batchmode','-nographics','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
  @{processId=$process.Id;log=$log;environment='Local+Mock';gamePort=7778} | ConvertTo-Json
}finally{foreach($key in @('MANGOS_TTB_GAME_LOCAL','MANGOS_TTB_GAME_API','MANGOS_TTB_GAME_ID','MANGOS_TTB_GAME_KEY')){Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue}}
