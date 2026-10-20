param([string]$RunId=[Guid]::NewGuid().ToString('N'))
$ErrorActionPreference='Stop'
if($RunId -notmatch '^[a-f0-9]{32}$'){throw 'Use a fresh 32-hex run ID.'}
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$isolatedProject=Join-Path $repositoryRoot 'Builds/TTB/BuildWorkspace'
$runRoot=Join-Path $repositoryRoot "Builds/TTB/Portal/$RunId"
if(Test-Path -LiteralPath $runRoot){throw 'Inspect the existing Portal build instead of overwriting it.'}
if(Test-Path -LiteralPath (Join-Path $isolatedProject 'Library/MangosBuildJob.json')){throw 'An isolated build job already exists. Inspect it before starting a new job.'}
$unity='C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe'
$protected=@('Assets/_Project/Scenes/BaseWorld.unity','Assets/_Project/Scenes/BaseWorld.unity.meta',
 'Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab','ProjectSettings/EditorBuildSettings.asset',
 'ProjectSettings/MangosBuildProfile.json','Assets/DefaultPrefabObjects.asset')
function Read-Protected {@($protected | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $repositoryRoot $_)).Hash.ToLowerInvariant()}})}
$before=Read-Protected
New-Item -ItemType Directory -Path $runRoot | Out-Null
$report=[ordered]@{runId=$RunId;status='building';startedAt=[DateTimeOffset]::UtcNow.ToString('o');protectedBefore=$before;stages=@();uploaded=$false;bankAccepted=$false}
try {
 foreach($folder in @('Assets','Packages','ProjectSettings')){
  & robocopy (Join-Path $repositoryRoot $folder) (Join-Path $isolatedProject $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy $folder to the isolated workspace."}
 }
 foreach($folder in @('Tools/TTB','WebCommerce/pwa')){
  & robocopy (Join-Path $repositoryRoot $folder) (Join-Path $isolatedProject $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy the reviewed PWA packager: $folder"}
 }
 $platform='Linux64';$method='MangosBuildPipeline.BuildBothBatch'
 for($phase=0;$phase -lt 3;$phase++){
  $log=Join-Path $runRoot ("phase-$phase-$platform.log")
  $arguments=@('-batchmode','-nographics','-projectPath',('"'+$isolatedProject+'"'),'-buildTarget',$platform,
   '-standaloneBuildSubtarget','Server','-executeMethod',$method,'-mangosExitWhenDone','-logFile',('"'+$log+'"'))
  $process=Start-Process -FilePath $unity -ArgumentList $arguments -WorkingDirectory $isolatedProject -WindowStyle Hidden -PassThru
  Write-Output "TTB Portal paired build PID $($process.Id); phase $phase; $platform; log $log"
  & (Join-Path $PSScriptRoot 'wait-unity-build.ps1') -Build $process -LogPath $log
  $text=Get-Content -LiteralPath $log -Raw
  if($text -match '\berror CS\d{4}:' -or $text -match '(?im)^Script updater for .+ failed with exitcode (?!0\b)\d+') {throw 'Compilation failed; preserve this run.'}
  $jobPath=Join-Path $isolatedProject 'Library/MangosBuildJob.json'
  $report.stages+=@{phase=$phase;platform=$platform;exitCode=$process.ExitCode;log=[IO.Path]::GetRelativePath($repositoryRoot,$log).Replace('\','/')}
  if($process.ExitCode -eq 2 -and (Test-Path -LiteralPath $jobPath)){
   $job=Get-Content -LiteralPath $jobPath -Raw | ConvertFrom-Json
   if($job.phase -ne 'switching'){throw 'Unexpected saved build phase.'}
   $platform=if($job.targets[$job.index] -eq 'Server'){'Linux64'}else{'WebGL'}
   $method='MangosBuildPipeline.ResumeBatch'
   continue
  }
  if($process.ExitCode -ne 0 -or (Test-Path -LiteralPath $jobPath) -or $text -notmatch 'MANGOS_BUILD_JOB Succeeded:'){throw 'The paired Portal build was not confirmed.'}
  $success=Get-Content -LiteralPath (Join-Path $isolatedProject 'Builds/last-successful.json') -Raw | ConvertFrom-Json
  $output=[IO.Path]::GetFullPath($success.output)
  $expected=[IO.Path]::GetFullPath((Join-Path $isolatedProject 'Builds'))+[IO.Path]::DirectorySeparatorChar
  if(-not $output.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected completed build location.'}
  & robocopy $output (Join-Path $runRoot 'Release') /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw 'Cannot copy the completed release.'}
  $report.buildId=$success.buildId
  break
 }
 if(-not $report.Contains('buildId')){throw 'The expected platform transitions did not complete.'}
 $after=Read-Protected
 if(($before | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)){throw 'A protected root file changed; preserve outputs for inspection.'}
 $report.protectedAfter=$after
 $report.artifacts=@(Get-ChildItem -LiteralPath (Join-Path $runRoot 'Release') -File -Recurse | Sort-Object FullName | ForEach-Object {@{path=[IO.Path]::GetRelativePath($runRoot,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
 $report.status='passed'
} catch {$report.status='failed';throw}
finally {$report.completedAt=[DateTimeOffset]::UtcNow.ToString('o');$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8}
Write-Output "Paired Linux Server/WebGL Portal release complete: $runRoot/Release/Upload. No upload or live deployment was made."
