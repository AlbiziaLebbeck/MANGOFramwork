$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& (Join-Path $PSScriptRoot 'prepare-overlay.ps1') -Environment Local
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$unityExecutable='C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$logDirectory=Join-Path $repositoryRoot 'Builds/Logs';New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$log=Join-Path $logDirectory ('ttb-gallery-build-'+[Guid]::NewGuid().ToString('N')+'.log')
$isolatedProject=[IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds/TTB/BuildWorkspace'))
$expectedParent=[IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds/TTB'))+[IO.Path]::DirectorySeparatorChar
if(-not $isolatedProject.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid isolated build workspace.'}
$playerPrefabs=@('Assets/_Modules/Networking/Prefabs/NetworkPlayer.prefab','Assets/_Modules/Networking/Prefabs/NetworkPlayer_Flyable.prefab')
$playerHashes=@{};foreach($path in $playerPrefabs){$playerHashes[$path]=(Get-FileHash -LiteralPath (Join-Path $repositoryRoot $path) -Algorithm SHA256).Hash}
# Stable new-script GUIDs let the generated scene return from the isolated import without touching existing asset metadata.
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Assets/_TtbCommerce') -Filter '*.cs' -Recurse -File | ForEach-Object {
  $meta=$_.FullName+'.meta'
  if(-not(Test-Path -LiteralPath $meta)){
    $relative=[IO.Path]::GetRelativePath($repositoryRoot,$_.FullName).Replace('\','/').ToLowerInvariant()
    $guid=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('mangos-ttb-v1:'+$relative))).ToLowerInvariant().Substring(0,32)
    Set-Content -LiteralPath $meta -Encoding utf8 -Value "fileFormatVersion: 2`nguid: $guid`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: "
  }
}
New-Item -ItemType Directory -Path $isolatedProject -Force | Out-Null
foreach($folder in @('Assets','Packages','ProjectSettings')){
  & robocopy (Join-Path $repositoryRoot $folder) (Join-Path $isolatedProject $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy $folder to the isolated build workspace."}
}
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$isolatedProject+'"'),'-buildTarget','WebGL','-executeMethod','Mangos.Ttb.Commerce.Editor.TtbGalleryBuild.BuildLocal','-logFile',('"'+$log+'"'))
$build=Start-Process -FilePath $unityExecutable -ArgumentList $arguments -WorkingDirectory $isolatedProject -WindowStyle Hidden -PassThru
Write-Output "TTB Unity build process: $($build.Id); log: $log"
& (Join-Path $PSScriptRoot 'wait-unity-build.ps1') -Build $build -LogPath $log
Write-Output "Unity exit code: $($build.ExitCode)"
if($build.ExitCode -ne 0){exit $build.ExitCode}
$logText=Get-Content -LiteralPath $log -Raw
if($logText -match '\berror CS\d{4}:' -or $logText -notmatch 'TTB_WEBGL_BUILD_PASSED:'){throw 'Unity did not confirm a clean successful build.'}
foreach($relativeOutput in @('Builds/TTB/LocalGallery','Assets/_TtbCommerce/Generated','Assets/_TtbCommerce/Resources')){
  & robocopy (Join-Path $isolatedProject $relativeOutput) (Join-Path $repositoryRoot $relativeOutput) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
  if($LASTEXITCODE -gt 7){throw "Cannot copy owned output $relativeOutput."}
}
# Copy only the two reviewed player integrations, retaining their original GUIDs and the user's unrelated asset changes.
foreach($relativePrefab in $playerPrefabs){
  if((Get-FileHash -LiteralPath (Join-Path $repositoryRoot $relativePrefab) -Algorithm SHA256).Hash -ne $playerHashes[$relativePrefab]){throw "The user edited $relativePrefab during the isolated build; retain the generated copy for review instead of overwriting."}
  Copy-Item -LiteralPath (Join-Path $isolatedProject $relativePrefab) -Destination (Join-Path $repositoryRoot $relativePrefab) -Force
}
& node (Join-Path $PSScriptRoot 'package-pwa.mjs') (Join-Path $repositoryRoot 'Builds/TTB/LocalGallery')
if($LASTEXITCODE -ne 0){throw 'TTB public PWA shell packaging failed.'}
Write-Output 'TTB isolated WebGL build complete. Existing Editor was not closed and its scene list was not changed.'
exit 0
