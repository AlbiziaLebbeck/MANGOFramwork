param(
  [Parameter(Mandatory)][System.Diagnostics.Process]$Build,
  [Parameter(Mandatory)][string]$LogPath
)
$ErrorActionPreference='Stop'
# The caller passes the exact Start-Process object. Hold its OS handle; never find or kill Unity by name/PID.
$null=$Build.Handle
$lastRead=[DateTime]::MinValue
while(-not $Build.WaitForExit(1000)){
  if(([DateTime]::UtcNow-$lastRead).TotalSeconds -lt 10){continue}
  $lastRead=[DateTime]::UtcNow
  if(-not(Test-Path -LiteralPath $LogPath)){continue}
  $stream=$null;$reader=$null
  try{
    $stream=[IO.File]::Open($LogPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader=[IO.StreamReader]::new($stream);$logText=$reader.ReadToEnd()
  }finally{if($null -ne $reader){$reader.Dispose()}elseif($null -ne $stream){$stream.Dispose()}}
  $failure=$logText.IndexOf('Licensing initialization failed',[StringComparison]::Ordinal)
  if($failure -lt 0){continue}
  $reconnects=[regex]::Matches($logText.Substring($failure),'The connection with the Unity Licensing Client has been lost').Count
  if($reconnects -lt 2){continue}
  if(-not $Build.HasExited){$Build.Kill()}
  if(-not $Build.WaitForExit(15000)){throw 'The owned build has not exited; do not start another build in this workspace.'}
  throw "The owned Unity build stopped after an explicit Licensing initialization failure and repeated reconnects. Log: $LogPath. The user's Editor and Licensing Client were not stopped."
}
