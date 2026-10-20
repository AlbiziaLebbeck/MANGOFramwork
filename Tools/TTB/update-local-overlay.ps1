param([ValidateSet('Gallery','World','Both')][string]$Target='Both')
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$targets=if($Target -eq 'Both'){@('Gallery','World')}else{@($Target)}
foreach($experience in $targets){
  $relative=if($experience -eq 'Gallery'){'Builds/TTB/LocalGallery'}else{'Builds/TTB/LocalWorld/Client'}
  $output=[IO.Path]::GetFullPath((Join-Path $repositoryRoot $relative))
  if(-not $output.StartsWith((Join-Path $repositoryRoot 'Builds/TTB')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid local output root.'}
  if(-not(Test-Path -LiteralPath (Join-Path $output 'index.html')) -or -not(Test-Path -LiteralPath (Join-Path $output 'Build'))){throw "Build the native $experience experience first."}
  & (Join-Path $PSScriptRoot 'prepare-overlay.ps1') -Environment Local -Experience ($experience.ToLowerInvariant())
  if($LASTEXITCODE -ne 0){throw 'Frontend preparation failed.'}
  Copy-Item -Path (Join-Path $repositoryRoot 'Assets/WebGLTemplates/MANGOsTTB/commerce/*') -Destination (Join-Path $output 'commerce') -Recurse -Force
  Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Assets/WebGLTemplates/MANGOsTTB/ttb-commerce-config.js') -Destination (Join-Path $output 'ttb-commerce-config.js') -Force
  Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Assets/WebGLTemplates/MANGOsTTB/ttb-viewport.css') -Destination (Join-Path $output 'ttb-viewport.css') -Force
  $indexPath=Join-Path $output 'index.html'
  $html=Get-Content -LiteralPath $indexPath -Raw
  if(-not $html.Contains('href="ttb-viewport.css"')){
    if(-not $html.Contains('</head>')){throw 'The owned local template has no head end marker.'}
    $html=$html.Replace('</head>','<link rel="stylesheet" href="ttb-viewport.css"></head>')
    Set-Content -LiteralPath $indexPath -Value $html -Encoding utf8
  }
  & node (Join-Path $PSScriptRoot 'package-pwa.mjs') $output
  if($LASTEXITCODE -ne 0){throw 'Local PWA packaging failed.'}
}
Write-Output 'Updated Local HTML/React/PWA and public WebGL compression/hash URLs; decoded native content and protocol were retained. Restart the owned local static preview to load its current .br header handling.'
