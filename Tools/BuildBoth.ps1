param(
    [string]$Unity = "$env:ProgramFiles\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe",
    [string]$Profile,
    [switch]$Validation
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskJobPath = Join-Path $taskProject 'Library\MangosBuildJob.json'
$taskLogFolder = Join-Path $taskProject 'Builds\Logs'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity executable not found: $Unity" }
if (Test-Path -LiteralPath $taskJobPath) { throw 'A saved build job already exists. Open the Editor to recover it before starting another.' }
New-Item -ItemType Directory -Path $taskLogFolder -Force | Out-Null
$taskMethod = if ($Validation) { 'MangosIntegrationValidation.BuildBoth' } else { 'MangosBuildPipeline.BuildBothBatch' }
$taskTarget = 'Linux64'
$taskLogId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
for ($taskPhase = 0; $taskPhase -lt 3; $taskPhase++) {
    $taskLog = Join-Path $taskLogFolder "$taskLogId-$taskPhase-$taskTarget.log"
    $taskArgs = @('-batchmode', '-nographics', '-projectPath', "`"$taskProject`"", '-buildTarget', $taskTarget, '-standaloneBuildSubtarget', 'Server', '-executeMethod', $taskMethod, '-mangosExitWhenDone', '-logFile', "`"$taskLog`"")
    if ($Profile) { $taskArgs += @('-mangosProfile', "`"$([IO.Path]::GetFullPath($Profile))`"") }
    Write-Output "Building $taskTarget; log: $taskLog"
    $taskProcess = Start-Process -FilePath $Unity -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $taskProcess.WaitForExit()
    # Unity can return zero (or the platform-restart code) after ScriptUpdater crashes.
    # Reject that phase before accepting either a restart or a successful build.
    $taskLogText = Get-Content -LiteralPath $taskLog -Raw
    if ($taskLogText -match '(?im)^Script updater for .+ failed with exitcode (?!0\b)\d+' -or $taskLogText -match '(?m)\berror CS\d{4}:') {
        throw "Unity script compilation/API Updater failed. See $taskLog"
    }
    if ($taskProcess.ExitCode -eq 2 -and (Test-Path -LiteralPath $taskJobPath)) {
        $taskJob = Get-Content -LiteralPath $taskJobPath -Raw | ConvertFrom-Json
        if ($taskJob.phase -ne 'switching') { throw "Unexpected saved build phase: $($taskJob.phase)" }
        $taskTarget = if ($taskJob.targets[$taskJob.index] -eq 'Server') { 'Linux64' } else { 'WebGL' }
        $taskMethod = 'MangosBuildPipeline.ResumeBatch'
        continue
    }
    if ($taskProcess.ExitCode -ne 0) { throw "Unity build failed ($($taskProcess.ExitCode)). See $taskLog" }
    if (Test-Path -LiteralPath $taskJobPath) { throw "Unity exited before completing its build job. See $taskLog" }
    $taskRelease = Get-Content -LiteralPath (Join-Path $taskProject 'Builds\last-successful.json') -Raw | ConvertFrom-Json
    Write-Output "Build Both succeeded: $($taskRelease.output)"
    exit 0
}
throw 'Build exceeded the expected platform restart count.'
