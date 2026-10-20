param()
$ErrorActionPreference='Stop'
$base='http://127.0.0.1:5286/ttb-commerce/v1'
$origin='http://127.0.0.1:5176'
$webSession=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
$config=Invoke-RestMethod ($base+'/storefront/config') -WebSession $webSession
if($config.environment -ne 'Local' -or $config.paymentMode -ne 'Mock' -or -not $config.mockSimulation){throw 'Price presets require the dedicated loopback Local+Mock API.'}
$login=Invoke-RestMethod ($base+'/session/local') -Method Post -WebSession $webSession -Headers @{Origin=$origin} -ContentType 'application/json' -Body '{"subject":"demo-admin"}'
if('admin' -notin $login.user.roles -or -not $login.csrfToken){throw 'The Local fixture administrator is unavailable.'}
$headers=@{Origin=$origin;'X-CSRF-Token'=$login.csrfToken}
$definitions=@(@{key='orbit-lamp';minor=9900L},@{key='mango-sculpture';minor=1000L},@{key='mango-star';minor=100L})
foreach($definition in $definitions){
  $product=Invoke-RestMethod ($base+'/catalog/by-model/'+$definition.key) -WebSession $webSession
  $entry=Invoke-RestMethod ($base+'/admin/catalog/'+$product.id) -WebSession $webSession
  # Only the named, single-variant demo products can be reset; issued order/receipt snapshots remain immutable.
  if($entry.product.slug -notin @('mango-orbit-lamp','mango-sculpture','mango-digital-star') -or $entry.variants.Count -ne 1){throw 'The demo product shape changed; do not overwrite custom variants.'}
  $variant=$entry.variants[0]
  if($variant.priceMinor -eq $definition.minor){continue}
  $body=@{expectedProductVersion=$entry.product.version;sku=$variant.sku;name=$variant.name;priceMinor=$definition.minor;active=$variant.active;trackStock=$variant.trackStock;options=($variant.optionsJson|ConvertFrom-Json);digitalKind=$variant.digitalKind;digitalAssetKey=$variant.digitalAssetKey}|ConvertTo-Json -Depth 5
  Invoke-RestMethod ($base+'/admin/products/'+$product.id+'/variants/'+$variant.id) -Method Put -WebSession $webSession -Headers $headers -ContentType 'application/json' -Body $body|Out-Null
}
$prices=@(foreach($definition in $definitions){$product=Invoke-RestMethod ($base+'/catalog/by-model/'+$definition.key) -WebSession $webSession;[pscustomobject]@{model=$definition.key;priceMinor=$product.variants[0].priceMinor}})
if(@($prices|Where-Object {$_.priceMinor -notin @(100,1000,9900)}).Count){throw 'The Local demo preset could not be verified.'}
$prices|ConvertTo-Json
