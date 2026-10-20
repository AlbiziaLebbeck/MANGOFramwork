param([ValidateSet('Server','Client','Both')][string]$Target='Both')
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$originalBaseWorld=Join-Path $repositoryRoot 'Assets/_Project/Scenes/BaseWorld.unity'
$originalBaseWorldHash=(Get-FileHash -LiteralPath $originalBaseWorld -Algorithm SHA256).Hash
$originalWorldManager=Join-Path $repositoryRoot 'Assets/_Modules/Networking/Prefabs/WorldManagerPrefab.prefab'
$originalWorldManagerHash=(Get-FileHash -LiteralPath $originalWorldManager -Algorithm SHA256).Hash
$isolatedProject=[IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds/TTB/BuildWorkspace'))
$expectedParent=[IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds/TTB'))+[IO.Path]::DirectorySeparatorChar
if(-not $isolatedProject.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid isolated workspace.'}
if(-not(Test-Path -LiteralPath (Join-Path $repositoryRoot 'Assets/_TtbCommerce/Resources/TtbGameModels.asset'))){throw 'Build the isolated gallery first to generate and validate model resources/player integrations.'}
& (Join-Path $PSScriptRoot 'prepare-overlay.ps1') -Environment Local -Experience world
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
foreach($folder in @('Assets','Packages','ProjectSettings')){
  & robocopy (Join-Path $repositoryRoot $folder) (Join-Path $isolatedProject $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy $folder to the isolated project."}
}
$unity='C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$targets=if($Target -eq 'Both'){@('Server','Client')}else{@($Target)}
$pairedFingerprint=$null
foreach($buildTarget in $targets){
  $platform=if($buildTarget -eq 'Server'){'Win64'}else{'WebGL'}
  $log=Join-Path $repositoryRoot ("Builds/Logs/ttb-world-$($buildTarget.ToLowerInvariant())-build-"+[Guid]::NewGuid().ToString('N')+'.log')
  $arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$isolatedProject+'"'),'-buildTarget',$platform)
  if($buildTarget -eq 'Server'){$arguments+=@('-standaloneBuildSubtarget','Server')}
  if($buildTarget -eq 'Client' -and $null -ne $pairedFingerprint){$arguments+=@('-ttbPreparedWorldFingerprint',$pairedFingerprint)}
  $arguments+=@('-executeMethod',"Mangos.Ttb.Commerce.Editor.TtbWorldBuild.Build${buildTarget}Local",'-logFile',('"'+$log+'"'))
  $process=Start-Process -FilePath $unity -ArgumentList $arguments -WorkingDirectory $isolatedProject -WindowStyle Hidden -PassThru
  Write-Output "TTB full metaverse $buildTarget build PID $($process.Id); log: $log"
  & (Join-Path $PSScriptRoot 'wait-unity-build.ps1') -Build $process -LogPath $log
  if($process.ExitCode -ne 0){throw "TTB $buildTarget build exited $($process.ExitCode)."}
  $text=Get-Content -LiteralPath $log -Raw
  if($text -match '\berror CS\d{4}:' -or $text -notmatch "TTB_WORLD_BUILD_PASSED: $buildTarget;"){throw "Unconfirmed $buildTarget build."}
  if($buildTarget -eq 'Server'){
    $pairedScene=Join-Path $isolatedProject 'Assets/_TtbCommerce/Generated/World/BaseWorld.unity'
    $pairedFingerprint=(Get-FileHash -LiteralPath $pairedScene -Algorithm SHA256).Hash+':'+(Get-FileHash -LiteralPath ($pairedScene+'.meta') -Algorithm SHA256).Hash
  }
  $relative="Builds/TTB/LocalWorld/$buildTarget"
  & robocopy (Join-Path $isolatedProject $relative) (Join-Path $repositoryRoot $relative) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy $buildTarget output."}
  if($buildTarget -eq 'Client'){& node (Join-Path $PSScriptRoot 'package-pwa.mjs') (Join-Path $repositoryRoot $relative);if($LASTEXITCODE -ne 0){throw 'TTB PWA packaging failed.'}}
}
if((Get-FileHash -LiteralPath $originalBaseWorld -Algorithm SHA256).Hash -ne $originalBaseWorldHash){throw 'The original BaseWorld was edited during the isolated build; preserve the generated scene for review.'}
if((Get-FileHash -LiteralPath $originalWorldManager -Algorithm SHA256).Hash -ne $originalWorldManagerHash){throw 'The original WorldManager prefab changed during the isolated build; preserve owned output for review.'}
$preparedScene=Join-Path $isolatedProject 'Assets/_TtbCommerce/Generated/World/BaseWorld.unity'
if(-not(Test-Path -LiteralPath $preparedScene)){throw 'The owned prepared BaseWorld scene is missing.'}
& robocopy (Join-Path $isolatedProject 'Assets/_TtbCommerce/Generated/World') (Join-Path $repositoryRoot 'Assets/_TtbCommerce/Generated/World') /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if($LASTEXITCODE -gt 7){throw 'Cannot return the owned prepared BaseWorld scene/assets.'}
Write-Output 'TTB full metaverse local build complete. Existing Editor and shared KKU services were not changed.'
exit 0
