param([ValidateSet('Local','Uat','Production')][string]$Environment='Local',[ValidateSet('gallery','world')][string]$Experience='gallery')
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$node=if($env:MANGOS_NODE_PATH){$env:MANGOS_NODE_PATH}else{'node'}
& $node (Join-Path $PSScriptRoot 'prepare-overlay.mjs') --environment $Environment --experience $Experience
if($LASTEXITCODE -ne 0){throw 'Overlay preparation failed; Unity build was not started.'}
