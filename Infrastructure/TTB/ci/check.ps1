param(
 [Parameter(Mandatory)][ValidateSet('Backend','Frontend','Unity')][string]$Component,
 [switch]$SkipDependencyRestore
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$ciRunId=[Guid]::NewGuid().ToString('N')
$ciRoot=Join-Path $repositoryRoot '.artifacts/ci'
[IO.Directory]::CreateDirectory($ciRoot)|Out-Null
$runDirectory=Join-Path $ciRoot $ciRunId
[IO.Directory]::CreateDirectory($runDirectory)|Out-Null
$pwshExecutable=Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})
$stages=[Collections.Generic.List[object]]::new()
$report=[ordered]@{schemaVersion=1;runId=$ciRunId;component=$Component;startedAt=[DateTimeOffset]::UtcNow.ToString('o');
 dependencyRestoreRequested=(-not $SkipDependencyRestore);status='running';stages=@();artifactFiles=@();
 externalRunnerAcceptanceIncluded=$false;bankAccepted=$false;productionAccepted=$false;deployed=$false}
$stageName='preflight'

function Invoke-Check([string]$Name,[string]$Executable,[string[]]$CommandArguments,[string]$Directory) {
 $script:stageName=$Name
 Write-Output "TTB CI: $Component / $Name"
 $timer=[Diagnostics.Stopwatch]::StartNew()
 Push-Location $Directory
 try {
  & $Executable @CommandArguments
  if($LASTEXITCODE -ne 0){throw "CI stage failed: $Name. No packaging/deployment is authorized by a failed check."}
  $stages.Add(@{name=$Name;passed=$true;elapsedMilliseconds=$timer.ElapsedMilliseconds})
 } finally {Pop-Location;$timer.Stop()}
}

function Get-Inventory([string[]]$Paths) {
 $files=@(foreach($relativePath in $Paths) {
  $path=Join-Path $repositoryRoot $relativePath
  if(-not(Test-Path -LiteralPath $path)){throw "Required CI input is missing: $relativePath"}
  $item=Get-Item -LiteralPath $path
  $found=if($item.PSIsContainer){Get-ChildItem -LiteralPath $path -File -Recurse}else{@($item)}
  foreach($file in $found) {
   $relative=[IO.Path]::GetRelativePath($repositoryRoot,$file.FullName).Replace('\','/')
   if($relative -match '(^|/)(bin|obj|node_modules|dist|test-results|playwright-report|\.data)(/|$)' -or
      $relative -match '(\.local\.json|\.pem|\.key)$' -or $file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){continue}
   [pscustomobject][ordered]@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
  }
 })
 @($files|Sort-Object -Property path -Unique)
}

function Assert-InputsUnchanged([object[]]$Before,[string[]]$Paths) {
 $after=@(Get-Inventory $Paths)
 if(($Before|ConvertTo-Json -Depth 4 -Compress) -cne ($after|ConvertTo-Json -Depth 4 -Compress)) {
  throw 'CI inputs changed during the run. Preserve the result and rerun the changed inputs; do not accept an unbound artifact.'
 }
}

try {
 $sourcePaths=@('Infrastructure/TTB/ci/check.ps1','Infrastructure/TTB/ci/read-backend-evidence.ps1')
 switch($Component) {
  'Backend' {
   if([string]::IsNullOrWhiteSpace($env:TTB_TEST_DATABASE)){throw 'Backend CI requires TTB_TEST_DATABASE for a disposable PostgreSQL ttb_development admin database; skipped integration tests are not accepted.'}
   $connection=[Data.Common.DbConnectionStringBuilder]::new()
   # PowerShell's dictionary adapter turns property assignment into a keyword named ConnectionString.
   $connection.set_ConnectionString($env:TTB_TEST_DATABASE)
   if(-not $connection.ContainsKey('Database') -or $connection.get_Item('Database') -ne 'ttb_development' -or
      -not $connection.ContainsKey('Host') -or $connection.get_Item('Host') -notin @('127.0.0.1','localhost','::1')) {
    throw 'Backend CI accepts only a loopback PostgreSQL ttb_development admin database. No UAT/Production/KKU connection is permitted.'
   }
   if(-not [string]::IsNullOrEmpty($env:VSTEST_TESTCASEFILTER)){throw 'A filtered backend run cannot satisfy this CI gate.'}
   $sourcePaths+=@('Services/TtbCommerce/src','Services/TtbCommerce/tests','Services/TtbCommerce/tools','Services/TtbCommerce/global.json','Services/TtbCommerce/Directory.Build.props','Services/TtbCommerce/TtbCommerce.slnx')
  }
  'Frontend' {
   $sourcePaths+=@('WebCommerce/src','WebCommerce/tests','WebCommerce/pwa','WebCommerce/scripts','WebCommerce/e2e','WebCommerce/e2e-unity','WebCommerce/e2e-world','WebCommerce/e2e-pwa','WebCommerce/e2e-load','Tools/TTB/tests/pwa-policy.mjs','Tools/TTB/package-pwa.mjs')
   $sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'WebCommerce') -File|ForEach-Object {'WebCommerce/'+$_.Name})
  }
  'Unity' {
   if(-not $IsWindows -or -not(Test-Path -LiteralPath 'C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe')) {
    throw 'Unity CI requires the licensed Windows Unity6000.3.26f1 host and its WebGL/Windows Server modules.'
   }
   $sourcePaths+=@('Assets/_TtbCommerce','Assets/Plugins/WebGL','Tools/TTB','Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectVersion.txt')
   $protectedPaths=@('Assets/DefaultPrefabObjects.asset','Assets/_Modules/PlayerController/Scripts/InputActions/PlayerActions.cs','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json','ProjectSettings/ShaderGraphSettings.asset')
   $protectedBefore=@(Get-Inventory $protectedPaths)
   $report.protectedBefore=$protectedBefore
  }
 }
 $inputs=@(Get-Inventory $sourcePaths)
 foreach($requiredFile in $sourcePaths|Where-Object {Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf}) {
  if($requiredFile.Replace('\','/') -notin $inputs.path){throw 'The CI source inventory is incomplete; no artifact can be accepted.'}
 }
 $inputs|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $runDirectory 'inputs.json') -Encoding utf8
 $report.inputInventorySha256=(Get-FileHash -LiteralPath (Join-Path $runDirectory 'inputs.json')).Hash.ToLowerInvariant()
 switch($Component) {
  'Frontend' {
   $webRoot=Join-Path $repositoryRoot 'WebCommerce'
   $pnpmExecutable=(Get-Command pnpm -ErrorAction Stop).Source
   $nodeExecutable=(Get-Command node -ErrorAction Stop).Source
   $nodeVersion=(& $nodeExecutable --version)-join ''
   $pnpmVersion=(& $pnpmExecutable --version)-join ''
   $package=Get-Content -LiteralPath (Join-Path $webRoot 'package.json') -Raw|ConvertFrom-Json
   if($nodeVersion -notmatch '^v24\.' -or $package.packageManager -ne ('pnpm@'+$pnpmVersion)){throw 'CI Node/pnpm versions differ from the approved Node24/packageManager profile.'}
   $report.nodeVersion=$nodeVersion;$report.pnpmVersion=$pnpmVersion
   if(-not $SkipDependencyRestore){Invoke-Check 'locked-dependencies' $pnpmExecutable @('install','--frozen-lockfile') $webRoot}
   Invoke-Check 'types-app-and-e2e' $pnpmExecutable @('run','check') $webRoot
   Invoke-Check 'frontend-unit-security' $pnpmExecutable @('run','test') $webRoot
   Invoke-Check 'pwa-policies' $nodeExecutable @('--test',(Join-Path $repositoryRoot 'Tools/TTB/tests/pwa-policy.mjs')) $repositoryRoot
   Invoke-Check 'frontend-build' $pnpmExecutable @('run','build') $webRoot
   Assert-InputsUnchanged $inputs $sourcePaths
   $artifactDirectory=Join-Path $runDirectory 'frontend'
   [IO.Directory]::CreateDirectory($artifactDirectory)|Out-Null
   Copy-Item -Path (Join-Path $webRoot 'dist/*') -Destination $artifactDirectory -Recurse
   $report.inputFilesUnchanged=$true
   $report.browserAndCompiledUnityAcceptanceIncluded=$false
  }
  'Backend' {
   $dotnetHelper=Join-Path $repositoryRoot 'Services/TtbCommerce/tools/dotnet.ps1'
   $serviceRoot=Join-Path $repositoryRoot 'Services/TtbCommerce'
   function Invoke-Dotnet([string]$Name,[string[]]$DotnetArguments) {
    Invoke-Check $Name $pwshExecutable (@('-NoLogo','-NoProfile','-File',$dotnetHelper)+$DotnetArguments) $serviceRoot
   }
   if(-not $SkipDependencyRestore) {
    Invoke-Dotnet 'locked-backend-dependencies' @('restore','TtbCommerce.slnx','--locked-mode')
    Invoke-Dotnet 'locked-receipt-probe-dependencies' @('restore','tools/ReceiptProbe/ReceiptProbe.csproj','--locked-mode')
    Invoke-Dotnet 'backend-build' @('build','TtbCommerce.slnx','--no-restore','--nologo')
    Invoke-Check 'chromium-runtime' $pwshExecutable @('-NoLogo','-NoProfile','-File',(Join-Path $serviceRoot 'tests/TtbCommerce.Tests/bin/Debug/net10.0/playwright.ps1'),'install','chromium') $serviceRoot
   }
   $testDirectory=Join-Path $runDirectory 'backend-tests'
   Invoke-Dotnet 'backend-unit-and-postgres' @('test','TtbCommerce.slnx','--no-restore','--nologo','--verbosity','minimal','--logger','trx;LogFilePrefix=ci','--results-directory',$testDirectory,'--blame-hang','--blame-hang-timeout','120s','--blame-hang-dump-type','mini')
   $results=@(Get-ChildItem -LiteralPath $testDirectory -Filter '*.trx' -File)
   if($results.Count -ne 1){throw 'Expected one complete backend TRX; missing or ambiguous reports cannot pass CI.'}
   $evidence=& (Join-Path $PSScriptRoot 'read-backend-evidence.ps1') -TrxPath $results[0].FullName
   $report.backendTests=$evidence.total
   $report.backendTrxSha256=$evidence.sha256
   $artifactDirectory=Join-Path $runDirectory 'api-linux'
   Invoke-Dotnet 'publish-linux-api' @('publish','src/TtbCommerce.Api/TtbCommerce.Api.csproj','--no-restore','--nologo','-c','Release','-r','linux-x64','--self-contained','true','-o',$artifactDirectory)
   $probeDirectory=Join-Path $runDirectory 'receipt-probe'
   Invoke-Dotnet 'publish-linux-receipt-probe' @('publish','tools/ReceiptProbe/ReceiptProbe.csproj','--no-restore','--nologo','-c','Release','-r','linux-x64','--self-contained','true','-o',$probeDirectory)
   Get-ChildItem -LiteralPath $probeDirectory -File -Filter 'ReceiptProbe*'|Copy-Item -Destination $artifactDirectory
   $contract=Get-Content -LiteralPath (Join-Path $artifactDirectory 'release-compatibility.json') -Raw|ConvertFrom-Json
   if($contract.readerContract -lt 3 -or 'operations-freeze-v1' -notin $contract.capabilities){throw 'Published artifact lacks the required financial reader/freeze contract.'}
   $report.readerContract=$contract.readerContract
   Assert-InputsUnchanged $inputs $sourcePaths
   $report.inputFilesUnchanged=$true
  }
  'Unity' {
   Invoke-Check 'isolated-gallery-build' $pwshExecutable @('-NoLogo','-NoProfile','-File',(Join-Path $repositoryRoot 'Tools/TTB/build-local.ps1')) $repositoryRoot
   Invoke-Check 'isolated-world-builds' $pwshExecutable @('-NoLogo','-NoProfile','-File',(Join-Path $repositoryRoot 'Tools/TTB/build-world-local.ps1'),'-Target','Both') $repositoryRoot
   Assert-InputsUnchanged $protectedBefore $protectedPaths
   $report.protectedUserFilesUnchanged=$true
   $report.compiledBrowserAcceptanceIncluded=$false
   # These large outputs stay in their owned Build directories; their hashes bind this run.
   $report.artifactFiles=@(Get-Inventory @('Builds/TTB/LocalGallery','Builds/TTB/LocalWorld/Client','Builds/TTB/LocalWorld/Server'))
  }
 }
 if($Component -ne 'Unity') {
  $relativeArtifact=[IO.Path]::GetRelativePath($repositoryRoot,$artifactDirectory)
  $report.artifactFiles=@(Get-Inventory @($relativeArtifact))
  if(-not $report.artifactFiles.Count){throw 'A successful build must produce nonempty hashed artifacts.'}
  $requiredArtifacts=if($Component -eq 'Frontend'){@('index.html','mango-ttb.js','mango-ttb.css')}else{@('TtbCommerce.Api','ReceiptProbe','release-compatibility.json','.playwright/node/linux-x64/node')}
  foreach($name in $requiredArtifacts) {
   $expectedPath=[IO.Path]::GetRelativePath($repositoryRoot,(Join-Path $artifactDirectory $name)).Replace('\','/')
   if($expectedPath -notin $report.artifactFiles.path){throw 'The CI artifact inventory is missing a required published entry.'}
  }
 }
 $report.status='passed'
} catch {
 $report.status='failed';$report.failedStage=$stageName;$report.failureType=$_.Exception.GetType().Name
 throw
} finally {
 $report.finishedAt=[DateTimeOffset]::UtcNow.ToString('o');$report.stages=$stages.ToArray()
 $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding utf8
 Write-Output "TTB CI evidence: $runDirectory/result.json"
}
