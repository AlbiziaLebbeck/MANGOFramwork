param([ValidateSet('Qr','Verify')][string]$Mode='Qr')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$command=if($Mode -eq 'Verify'){'--uat-bank-probe'}else{'--uat-bank-qr'}
$output=& ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=12 ntserver02 "kubectl -n ttb-uat exec deployment/ttb-commerce-api -- /app/TtbCommerce.Api $command"
if($LASTEXITCODE -ne 0){throw 'UAT diagnostic command failed. No automatic retry was made.'}
$lines=@($output | Where-Object { $_.StartsWith('{"status":"uat-bank-diagnostic"') })
if($lines.Count -ne 1){throw 'Expected one redacted diagnostic result.'}
$result=$lines[0] | ConvertFrom-Json -DateKind String
if($result.environment -ne 'Uat' -or $result.financialRecordsCreated -or $result.probe.paymentConfirmed -or $result.qr.ImageDataUrl -notmatch '^data:image/png;base64,'){throw 'Unexpected diagnostic result.'}
$directory=Join-Path $root ('.artifacts/bank-update/' + $result.runId.ToString().Replace('-',''))
New-Item -ItemType Directory -Path $directory | Out-Null
[IO.File]::WriteAllBytes((Join-Path $directory 'ttb-uat-1baht.png'),[Convert]::FromBase64String($result.qr.ImageDataUrl.Substring('data:image/png;base64,'.Length)))
$result.qr.ImageDataUrl='[see ttb-uat-1baht.png]'
$result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory 'result.json') -Encoding utf8
$expiry=[DateTimeOffset]::Parse($result.qr.ExpiresAt,[Globalization.CultureInfo]::InvariantCulture).ToOffset([TimeSpan]::FromHours(7)).ToString('yyyy-MM-dd HH:mm:ss zzz',[Globalization.CultureInfo]::InvariantCulture)
Write-Output "UAT diagnostic QR 1 THB (TTB bank test network only). Expires $expiry"
Write-Output "PNG: $directory/ttb-uat-1baht.png"
if($result.probe){$result.probe | ConvertTo-Json -Compress}
Write-Output 'This QR is a bank diagnostic, not a shopper order. A scan/payment is not claimed; any received credit is retained for unmatched-payment review.'
if($Mode -eq 'Verify' -and -not $result.probe.authenticatedBankReply){
    Write-Output 'Bank authentication/contract is NOT accepted. Preserve the result and ask TTB to inspect the Request UID; do not retry automatically.'
    exit 2
}
