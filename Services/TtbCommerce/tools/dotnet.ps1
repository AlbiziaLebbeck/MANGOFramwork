$DotnetArguments = $args
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location (Join-Path $PSScriptRoot '..')
try { & dotnet @DotnetArguments; exit $LASTEXITCODE }
finally { Pop-Location }
