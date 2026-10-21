# Keep UTF-8 BOM for Windows PowerShell 5.1 to read Thai messages correctly.
param(
 [ValidateSet('Tests','Api','Worker','Receipt','Order')][string]$View='Tests',
 [ValidatePattern('^T[0-9]{14}$')][string]$OrderNumber,
 [ValidatePattern('^[0-9]{1,4}(s|m|h)$')][string]$Since='30m',
 [guid]$CaseId=[Guid]::Empty,
 [guid]$Before=[Guid]::Empty,
 [switch]$AsJson,
 [switch]$Follow
)
$ErrorActionPreference='Stop'
if($View -eq 'Tests') {
 if($Follow) {
  & ssh -o BatchMode=yes ntserver02 "kubectl -n ttb-uat logs deployment/ttb-commerce-api --timestamps --since=$Since --tail=500 -f" | Select-String -SimpleMatch 'TTB_BANK_TEST'
 } else {
  $query='limit=100'
  if($CaseId -ne [Guid]::Empty){$query+='&caseId='+$CaseId.ToString('D')}
  if($Before -ne [Guid]::Empty){$query+='&before='+$Before.ToString('D')}
  $json=& ssh -o BatchMode=yes ntserver02 "kubectl -n ttb-uat exec deployment/ttb-commerce-api -- curl -fsS --max-time 15 'http://127.0.0.1:8080/internal/bank-test-console/api/events?$query'"
  if($LASTEXITCODE -ne 0){throw 'Cannot read durable test events'}
  # Windows PowerShell5.1 keeps JSON arrays as a single pipeline object.
  # Assign first, then enumerate explicitly so Select-Object sees each event.
  $events=ConvertFrom-Json -InputObject ($json -join [Environment]::NewLine)
  if($AsJson){ConvertTo-Json -InputObject @($events) -Depth 8}else{
   @($events)|Select-Object createdAt,kind,outcome,ref1,qrId,httpStatus,bankCode,requestUid|Format-Table -AutoSize -Wrap
  }
 }
} elseif($View -eq 'Order') {
 if(-not $OrderNumber){throw 'ระบุ -OrderNumber เช่น T20261021000002'}
 if($Follow){throw 'Order เป็นการอ่าน snapshot ให้เรียกใหม่เมื่ออยากอัปเดต'}
 $python=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'read-order-log.py') -Raw
 $python | & ssh -o BatchMode=yes ntserver02 "python3 - $OrderNumber"
} else {
 $deployment=switch($View){'Api'{'ttb-commerce-api'} 'Worker'{'ttb-commerce-worker'} 'Receipt'{'ttb-receipt-worker'}}
 $watch=if($Follow){' -f'}else{''}
 & ssh -o BatchMode=yes ntserver02 "kubectl -n ttb-uat logs deployment/$deployment --timestamps --since=$Since --tail=200$watch"
}
if($LASTEXITCODE -ne 0){throw 'อ่าน log ไม่สำเร็จ ตรวจสอบ SSH/สถานะ pod แล้วลองใหม่'}
