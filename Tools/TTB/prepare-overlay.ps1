param([ValidateSet('Local','Uat','Production')][string]$Environment='Local',[ValidateSet('gallery','world')][string]$Experience='gallery')
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$webRoot=Join-Path $repositoryRoot 'WebCommerce'
Push-Location $webRoot
try{& pnpm run build;if($LASTEXITCODE -ne 0){throw 'Overlay build failed.'}}finally{Pop-Location}
$template=Join-Path $repositoryRoot 'Assets/WebGLTemplates/MANGOsTTB'
$output=Join-Path $template 'commerce';New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -Path (Join-Path $webRoot 'dist/*') -Destination $output -Recurse -Force
$apiBase=switch($Environment){'Uat'{'https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1'}'Production'{'https://ttb-api.mangosgo.com/ttb-commerce/v1'}default{'/ttb-commerce/v1'}}
$publicConfiguration=[ordered]@{pwaEnabled=$true;environment=$Environment;apiBase=$apiBase;experience=$Experience;assetBase='./commerce/'} | ConvertTo-Json -Compress
Set-Content -LiteralPath (Join-Path $template 'ttb-commerce-config.js') -Value "window.MANGOS_TTB_CONFIG=Object.freeze($publicConfiguration);" -Encoding utf8
Write-Output "Prepared TTB overlay for $Environment; no credentials included."
