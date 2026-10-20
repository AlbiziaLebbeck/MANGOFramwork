param([string]$RunId = [Guid]::NewGuid().ToString('N'))
$ErrorActionPreference='Stop'
if($RunId -notmatch '^[a-f0-9]{32}$'){throw 'Use a new 32-character hexadecimal build run ID.'}
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$isolatedProject=Join-Path $repositoryRoot 'Builds/TTB/BuildWorkspace'
$runRoot=Join-Path $repositoryRoot "Builds/TTB/UatWorld/$RunId"
if(Test-Path -LiteralPath $runRoot){throw 'The UAT build run already exists. Inspect its result instead of overwriting it.'}
$unity='C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe'
$linuxModule='C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Data/PlaybackEngines/LinuxStandaloneSupport/Variations/linux64_server_nondevelopment_mono'
if(-not(Test-Path -LiteralPath $linuxModule)){throw 'The matching Linux Dedicated Server module is required.'}
$sceneRelative='Assets/_TtbCommerce/Generated/World/BaseWorld.unity'
$protected=@('Assets/DefaultPrefabObjects.asset','Assets/_Modules/PlayerController/Scripts/InputActions/PlayerActions.cs','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json','ProjectSettings/ShaderGraphSettings.asset','Assets/_Project/Scenes/BaseWorld.unity','Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab',$sceneRelative,($sceneRelative+'.meta'))
function Read-Protected { @($protected | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $repositoryRoot $_)).Hash.ToLowerInvariant()} }) }
$before=Read-Protected
$fingerprint=(Get-FileHash -LiteralPath (Join-Path $repositoryRoot $sceneRelative)).Hash+':'+(Get-FileHash -LiteralPath (Join-Path $repositoryRoot ($sceneRelative+'.meta'))).Hash
New-Item -ItemType Directory -Path $runRoot | Out-Null
$report=[ordered]@{runId=$RunId;environment='Uat';status='building';startedAt=[DateTimeOffset]::UtcNow.ToString('o');sceneFingerprint=$fingerprint;protectedBefore=$before;stages=@();bankAccepted=$false;deployed=$false}
try {
  Push-Location (Join-Path $repositoryRoot 'WebCommerce')
  try{& pnpm run build;if($LASTEXITCODE -ne 0){throw 'Frontend build failed.'}}finally{Pop-Location}
  foreach($folder in @('Assets','Packages','ProjectSettings')){
    & robocopy (Join-Path $repositoryRoot $folder) (Join-Path $isolatedProject $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if($LASTEXITCODE -gt 7){throw "Cannot copy $folder to the isolated project."}
  }
  $template=Join-Path $isolatedProject 'Assets/WebGLTemplates/MANGOsTTB'
  Copy-Item -Path (Join-Path $repositoryRoot 'WebCommerce/dist/*') -Destination (Join-Path $template 'commerce') -Recurse -Force
  $publicConfig=@{environment='Uat';experience='world';pwaEnabled=$true;apiBase='https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1';assetBase='./commerce/'} | ConvertTo-Json -Compress
  Set-Content -LiteralPath (Join-Path $template 'ttb-commerce-config.js') -Value "window.MANGOS_TTB_CONFIG=Object.freeze($publicConfig);" -Encoding utf8
  foreach($target in @('Server','Client')){
    $platform=if($target -eq 'Server'){'Linux64'}else{'WebGL'}
    $log=Join-Path $runRoot ($target.ToLowerInvariant()+'.log')
    $arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$isolatedProject+'"'),'-buildTarget',$platform)
    if($target -eq 'Server'){$arguments+=@('-standaloneBuildSubtarget','Server')}
    $arguments+=@('-ttbPreparedWorldFingerprint',$fingerprint,'-executeMethod',"Mangos.Ttb.Commerce.Editor.TtbUatWorldBuild.Build$target",'-logFile',('"'+$log+'"'))
    $process=Start-Process -FilePath $unity -ArgumentList $arguments -WorkingDirectory $isolatedProject -WindowStyle Hidden -PassThru
    Write-Output "TTB UAT $target build PID $($process.Id); log $log"
    & (Join-Path $PSScriptRoot 'wait-unity-build.ps1') -Build $process -LogPath $log
    $logText=Get-Content -LiteralPath $log -Raw
    if($process.ExitCode -ne 0 -or $logText -match '\berror CS\d{4}:' -or $logText -notmatch "TTB_UAT_WORLD_BUILD_PASSED: $target;"){throw "Unconfirmed UAT $target build."}
    $actual=(Get-FileHash -LiteralPath (Join-Path $isolatedProject $sceneRelative)).Hash+':'+(Get-FileHash -LiteralPath (Join-Path $isolatedProject ($sceneRelative+'.meta'))).Hash
    if($actual -ne $fingerprint){throw 'Paired prepared scene or FishNet IDs changed during the UAT build.'}
    & robocopy (Join-Path $isolatedProject "Builds/TTB/UatWorld/$target") (Join-Path $runRoot $target) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if($LASTEXITCODE -gt 7){throw "Cannot copy UAT $target artifacts."}
    if($target -eq 'Client'){& node (Join-Path $PSScriptRoot 'package-pwa.mjs') (Join-Path $runRoot $target);if($LASTEXITCODE -ne 0){throw 'UAT PWA packaging failed.'}}
    $report.stages+=@{target=$target;passed=$true;log=[IO.Path]::GetRelativePath($repositoryRoot,$log).Replace('\','/')}
  }
  $after=Read-Protected
  if(($before|ConvertTo-Json -Compress) -ne ($after|ConvertTo-Json -Compress)){throw 'A protected root asset changed. Preserve output for inspection.'}
  $report.protectedAfter=$after
  $report.artifacts=@(Get-ChildItem -LiteralPath (Join-Path $runRoot 'Server'),(Join-Path $runRoot 'Client') -File -Recurse | Sort-Object FullName | ForEach-Object {@{path=[IO.Path]::GetRelativePath($runRoot,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
  $report.status='passed'
} catch { $report.status='failed'; throw }
finally { $report.completedAt=[DateTimeOffset]::UtcNow.ToString('o');$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8 }
Write-Output "Paired Linux Server/WebGL UAT build passed: $runRoot. No deployment or bank request was made."
exit 0
