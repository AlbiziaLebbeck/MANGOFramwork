param([Parameter(Mandatory)][ValidateSet('Before','After','Rollback')][string]$Stage)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory=Join-Path $repositoryRoot '.artifacts/tls-qa'
[IO.Directory]::CreateDirectory($directory)|Out-Null
$handler=[Net.Http.HttpClientHandler]::new()
$handler.UseProxy=$false; $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler)
$client.Timeout=[TimeSpan]::FromSeconds(20)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('MANGOsTTB-NetworkCheck/1.0')
$rows=[Collections.Generic.List[object]]::new()
$failure=$null
function Read-PublicPage([string]$Url) {
 $script:attemptingUrl=$Url
 $response=$client.GetAsync($Url).GetAwaiter().GetResult()
 try {
  $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
  $rows.Add([ordered]@{url=$Url;status=[int]$response.StatusCode;bytes=$bytes.Length;
   sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes));
   location=if($response.Headers.Location){$response.Headers.Location.ToString()}else{$null}})
  return [Text.Encoding]::UTF8.GetString($bytes)
 } finally {$response.Dispose()}
}
try {
 $null=Read-PublicPage 'https://plus.mangosgo.com/systemtester'
 $html=Read-PublicPage 'https://plus.mangosgo.com/systemtester/'
 $assets=@([regex]::Matches($html,'(?:src|href)=["'']([^"'']+)["'']') |
  ForEach-Object {$_.Groups[1].Value} | Where-Object {$_ -match '\.(js|css)(\?|$)'} | Sort-Object -Unique)
 if($assets.Count -lt 3 -or $assets.Count -gt 15){throw 'Unexpected KKU public asset inventory.'}
 foreach($asset in $assets) {
  $uri=[Uri]::new([Uri]'https://plus.mangosgo.com/systemtester/',$asset)
  if($uri.Scheme -ne 'https' -or $uri.Host -ne 'plus.mangosgo.com' -or -not $uri.AbsolutePath.StartsWith('/systemtester/')) {
   throw 'Unexpected KKU asset origin/path; no request sent.'
  }
  $null=Read-PublicPage $uri.AbsoluteUri
 }
} catch {$failure=@{url=$attemptingUrl;kind=$_.Exception.GetType().FullName}} finally {$client.Dispose();$handler.Dispose()}
$healthy=$null -eq $failure -and $rows.Count -gt 1 -and $rows[0].status -eq 308 -and $rows[0].location -eq '/systemtester/' -and
 @($rows | Select-Object -Skip 1 | Where-Object {$_.status -ne 200}).Count -eq 0
$proof=[ordered]@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');stage=$Stage;origin='PC-direct-no-proxy';
 systemCaAndHostnameVerified=$true;healthy=$healthy;requests=$rows.ToArray();failure=$failure;
 authenticatedSessionsTested=$false;purchaseOrPaymentRequestsSent=$false}
if($Stage -ne 'Before') {
 $before=Get-Content -LiteralPath (Join-Path $directory 'kku-public-before.json') -Raw | ConvertFrom-Json -DateKind String
 $proof.matchesBefore=$before.healthy -and $before.requests.Count -eq $rows.Count
 foreach($row in $rows) {
  $old=@($before.requests | Where-Object {$_.url -eq $row.url})
  if($old.Count -ne 1 -or $old[0].status -ne $row.status -or $old[0].sha256 -ne $row.sha256 -or $old[0].location -ne $row.location) {
   $proof.matchesBefore=$false
  }
 }
}
$proof|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $directory ('kku-public-'+$Stage.ToLowerInvariant()+'.json')) -Encoding utf8
@{stage=$Stage;healthy=$healthy;requestCount=$rows.Count;matchesBefore=$proof.matchesBefore;failure=$failure;authenticatedSessionsTested=$false}|ConvertTo-Json
if(-not $healthy -or ($Stage -ne 'Before' -and -not $proof.matchesBefore)){throw 'KKU public availability/content differs from the recorded baseline; inspect and roll back the owned network change.'}
